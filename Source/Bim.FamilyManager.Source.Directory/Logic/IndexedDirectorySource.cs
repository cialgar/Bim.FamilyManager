using System.IO;
using System.Runtime.CompilerServices;
using Autodesk.Internal.InfoCenter;
using Autodesk.Windows;
using Bim.FamilyManager.Abstractions;
using Bim.FamilyManager.Base.Logic;
using Bim.FamilyManager.Index;
using Bim.FamilyManager.Rfa;
using Bim.FamilyManager.Source.Directory.Options;
using Bim.FamilyManager.Ui.Resources;
using Microsoft.Extensions.Logging;
using Scotec.Revit.RevitFamily;
using Folder = Bim.FamilyManager.Base.Logic.Folder;

namespace Bim.FamilyManager.Source.Directory.Logic;

/// <summary>
///     Represents a family source that browses the persistent SQLite family index instead of scanning
///     the directory tree on every session.
/// </summary>
/// <remarks>
///     This source coexists with <see cref="DirectorySource" /> as a separate source type intended for
///     large libraries (see ARCHITECTURE.md for the decision). Folders and families are served from the
///     index, so opening the panel is instant regardless of the library size. An incremental
///     <see cref="IndexScanner" /> run is started in the background on the first folder enumeration of
///     each session (the "light re-scan on open") and on every manual <see cref="FamilySource{TOptions}.Reload" />
///     — the panel's refresh button therefore doubles as the re-index command. When a scan detects
///     changes, the source reloads itself so the UI reflects the new state.
/// </remarks>
public sealed class IndexedDirectorySource : FamilySource<IndexedDirectorySourceOptions>
{
    /// <summary>
    ///     A factory delegate for creating instances of <see cref="IndexedDirectorySource" />.
    /// </summary>
    /// <param name="options">The <see cref="IndexedDirectorySourceOptions" /> used to configure the created instance.</param>
    /// <returns>A new instance of <see cref="IndexedDirectorySource" /> configured with the specified options.</returns>
    public delegate IndexedDirectorySource Factory(IndexedDirectorySourceOptions options);

    private static readonly Stream PreviewStream;

    private readonly FamilyInfoCache _familyInfoCache;
    private readonly ILogger<IndexedDirectorySource> _logger;
    private readonly IndexQuery _query;
    private readonly string _rootPath;
    private readonly object _scanGate = new();
    private readonly IndexScanner _scanner;
    private Task? _runningScan;
    private bool _suppressRescan;

    static IndexedDirectorySource()
    {
        const string packUri =
            "pack://application:,,,/Bim.FamilyManager.Source.Directory;component/Resources/Images/FamilySourceStorageNetwork_128x128.png";

        PreviewStream = LoadResourceAsStream(packUri);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="IndexedDirectorySource" /> class.
    /// </summary>
    /// <param name="options">The options specifying the configuration for the indexed directory source.</param>
    /// <param name="familyManager">An implementation of the <see cref="IFamilyManager" /> interface used to manage Revit families.</param>
    /// <param name="familyFactory">A factory delegate for creating instances of <see cref="RevitFamily" />.</param>
    /// <param name="familyIndex">The persistent family index backing this source.</param>
    /// <param name="familyInfoCache">The persistent cache used to serve family display information without reading the family files.</param>
    /// <param name="logger">An instance of <see cref="ILogger{IndexedDirectorySource}" /> used for logging.</param>
    public IndexedDirectorySource(IndexedDirectorySourceOptions options, IFamilyManager familyManager,
                                  IRevitFamily.Factory familyFactory, FamilyIndex familyIndex,
                                  FamilyInfoCache familyInfoCache, ILogger<IndexedDirectorySource> logger)
        : base(options, familyManager, familyFactory)
    {
        _familyInfoCache = familyInfoCache;
        _logger = logger;
        _rootPath = Options.Path;
        _scanner = new IndexScanner(familyIndex);
        _query = new IndexQuery(familyIndex);
    }

    /// <summary>
    ///     Gets a stream that provides a preview of the family source.
    /// </summary>
    /// <returns>A <see cref="Stream" /> object containing the preview data for the family source.</returns>
    public override Stream Preview
    {
        get
        {
            PreviewStream.Position = 0;
            return PreviewStream;
        }
    }

    /// <summary>
    ///     Gets the collection of top-level folders of the source, served from the index.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the operation to complete.</param>
    /// <returns>An asynchronous stream of <see cref="IFolder" /> representing the folders containing Revit families.</returns>
    /// <remarks>
    ///     The first enumeration of a session also starts the incremental background scan that keeps the
    ///     index in sync with the disk.
    /// </remarks>
    public override async IAsyncEnumerable<IFolder> GetFoldersAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StartScan();

        var folders = await Task.Run(() => _query.GetChildFolders(_rootPath, string.Empty), cancellationToken);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateFolder(folder);
        }
    }

    /// <summary>
    ///     Releases the resources used by the <see cref="IndexedDirectorySource" /> instance.
    /// </summary>
    /// <param name="disposing">A boolean value indicating whether the method is being called explicitly (true) or by the garbage collector (false).</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // No managed resources to dispose.
        }
    }

    /// <summary>
    ///     Handles the reload operation for the <see cref="IndexedDirectorySource" /> instance.
    /// </summary>
    /// <remarks>
    ///     A manual reload (the panel's refresh button) triggers an incremental re-scan, which makes the
    ///     refresh button double as the re-index command. Reloads raised by a completed scan do not
    ///     trigger another scan.
    /// </remarks>
    protected override void OnReload()
    {
        if (!_suppressRescan)
        {
            StartScan();
        }
    }

    /// <summary>
    ///     Starts an incremental background scan unless one is already running.
    /// </summary>
    private void StartScan()
    {
        lock (_scanGate)
        {
            if (_runningScan is { IsCompleted: false })
            {
                return;
            }

            _runningScan = Task.Run(RunScan);
        }
    }

    /// <summary>
    ///     Runs the incremental scan and reloads the source when the index changed.
    /// </summary>
    private void RunScan()
    {
        try
        {
            var result = _scanner.Scan(_rootPath);
            _logger.LogInformation(
                "Incremental index scan completed. Source: {Source}, Added: {Added}, Updated: {Updated}, Removed: {Removed}, Unchanged: {Unchanged}, Failed: {Failed}, Duration: {Duration} ms",
                Name, result.Added, result.Updated, result.Removed, result.Unchanged, result.FailedExtractions.Count,
                (long)result.Duration.TotalMilliseconds);

            if (result.Added + result.Updated + result.Removed > 0)
            {
                _suppressRescan = true;
                try
                {
                    Reload();
                }
                finally
                {
                    _suppressRescan = false;
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Incremental index scan failed. Source: {Source}", Name);
            RaiseError(e);
        }
    }

    /// <summary>
    ///     Creates an <see cref="IFolder" /> for the specified source-relative folder, backed by the index.
    /// </summary>
    /// <param name="relativeFolder">The source-relative folder path.</param>
    /// <returns>The folder abstraction consumed by the UI.</returns>
    private IFolder CreateFolder(string relativeFolder)
    {
        return new Folder(
            Path.GetFileName(relativeFolder),
            cancellationToken => GetSubfoldersFromIndex(relativeFolder, cancellationToken),
            (includeSubfolders, cancellationToken) => GetFamiliesFromIndex(relativeFolder, includeSubfolders, cancellationToken));
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private async IAsyncEnumerable<IFolder> GetSubfoldersFromIndex(string relativeFolder,
#pragma warning restore CS1998
                                                                   [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var folder in _query.GetChildFolders(_rootPath, relativeFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateFolder(folder);
        }
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    private async IAsyncEnumerable<IRevitFamily> GetFamiliesFromIndex(string relativeFolder, bool includeSubfolders,
#pragma warning restore CS1998
                                                                      [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var record in _query.GetFamilies(_rootPath, relativeFolder, includeSubfolders))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A single unreadable family file must not abort the enumeration (see DirectorySource).
            IRevitFamily? family;
            try
            {
                if (!FamilyManager.TryGetRevitFamily(record.Name, out family))
                {
                    var familyFile = record.Path;
                    family = CreateRevitFamily(record.Name, CreateFamilyInfo(familyFile),
                        (revitFamily, stream) => SaveFamily(revitFamily, stream, familyFile));

                    ApplyOrCaptureCachedInfo(family, familyFile);
                }
            }
            catch (Exception e)
            {
                family = null;
                _logger.LogError(e, "Failed to create the family entry. Family file: {FamilyFile}", record.Path);
            }

            if (family is not null)
            {
                yield return family;
            }
        }
    }

    /// <summary>
    ///     Creates a new Revit family instance and registers it with the family manager.
    /// </summary>
    private IRevitFamily CreateRevitFamily(string familyName, RevitFamilyInfo familyInfo, Action<IRevitFamily, Stream> saveAction)
    {
        var family = FamilyFactory(familyName, familyInfo, saveAction);
        FamilyManager.RegisterRevitFamily(family);

        return family;
    }

    /// <summary>
    ///     Creates a <see cref="RevitFamilyInfo" /> whose stream loader reads the specified file.
    /// </summary>
    private RevitFamilyInfo CreateFamilyInfo(string path)
    {
        return CreateFamilyInfo(LoadFileStream);

        Stream LoadFileStream()
        {
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);

            memoryStream.Position = 0;
            return memoryStream;
        }
    }

    /// <summary>
    ///     Applies cached display information to a newly created family, or arranges for it to be captured.
    /// </summary>
    /// <remarks>Mirrors <see cref="DirectorySource" />; see there for the full rationale.</remarks>
    private void ApplyOrCaptureCachedInfo(IRevitFamily family, string familyFile)
    {
        if (family is not RevitFamily revitFamily)
        {
            return;
        }

        try
        {
            if (_familyInfoCache.TryGet(familyFile, out var cachedInfo))
            {
                var preview = cachedInfo!.Thumbnail is null ? null : new MemoryStream(cachedInfo.Thumbnail);
                revitFamily.ApplyCachedInfo(preview, cachedInfo.Product, cachedInfo.ProductVersion, cachedInfo.Updated);

                _logger.LogInformation("Family info served from cache; the family file was not read. Family: {Family}", family.Name);
                return;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to read the family info cache. Family file: {FamilyFile}", familyFile);
        }

        revitFamily.Initialized += (_, _) => StoreFamilyInfoInCache(revitFamily, familyFile);
    }

    /// <summary>
    ///     Stores the extracted display information of an initialized family in the persistent cache.
    /// </summary>
    private void StoreFamilyInfoInCache(RevitFamily family, string familyFile)
    {
        try
        {
            byte[]? thumbnail = null;
            using (var preview = family.Preview)
            {
                if (preview is not null)
                {
                    using var buffer = new MemoryStream();
                    preview.CopyTo(buffer);
                    thumbnail = buffer.ToArray();
                }
            }

            _familyInfoCache.Store(familyFile, new CachedFamilyInfo
            {
                Product = family.Product,
                ProductVersion = family.ProductVersion,
                Updated = family.Updated,
                Thumbnail = thumbnail
            });

            _logger.LogInformation("Extracted family info stored in the cache. Family: {Family}", family.Name);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to store the extracted family info in the cache. Family: {Family}", family.Name);
        }
    }

    /// <summary>
    ///     Saves the specified Revit family to the provided file path using the given stream.
    /// </summary>
    /// <remarks>Mirrors <see cref="DirectorySource.SaveFamily" />; the next incremental scan picks up the change.</remarks>
    private void SaveFamily(IRevitFamily family, Stream stream, string path)
    {
        _logger.LogInformation($"Save family to the family source. Source: {Name}, Family: {family.Name}, Path: {path}");

        var notification = string.Empty;
        try
        {
            if (!FileBackupHelper.CanWriteToFolder(path))
            {
                notification = StringResources.DirectorySource_Message_Save_Error;

                _logger.LogWarning("Failed to save the family to the specified family source. The current user does not have the necessary access rights.");
                return;
            }

            FileBackupHelper.CreateBackup(path);
            using (var file = File.OpenWrite(path))
            {
                stream.CopyTo(file);
            }

            family.ApplyUpdate(CreateFamilyInfo(path));
            notification = StringResources.DirectorySource_Message_Save_Success;

            _logger.LogInformation("The family was successfully saved to the family source.");
        }
        catch (Exception e)
        {
            notification = StringResources.DirectorySource_Message_Save_Error;

            _logger.LogError(e, "Failed to save the family to the specified family source.");
        }
        finally
        {
            var result = new ResultItem
            {
                Title = notification,
                Category = StringResources.FamilyManager_Name,
                IsNew = true,
                Timestamp = DateTime.Now,
                Type = ResultType.Unknown
            };

            ComponentManager.InfoCenterPaletteManager.ShowBalloon(result);
        }
    }
}
