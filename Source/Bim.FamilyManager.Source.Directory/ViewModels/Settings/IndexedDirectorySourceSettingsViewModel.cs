using System.Windows.Input;
using Bim.FamilyManager.Index;
using Bim.FamilyManager.Source.Directory.Options;
using Bim.FamilyManager.Ui.ViewModels.Settings;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Bim.FamilyManager.Source.Directory.ViewModels.Settings;

/// <summary>
///     Represents the view model for managing settings related to an index-backed directory family source.
/// </summary>
/// <remarks>
///     In addition to the name and path settings, this view model exposes a re-index command that runs
///     an incremental <see cref="IndexScanner" /> pass for the configured path on demand. The panel's
///     refresh button triggers the same incremental scan for the running source; this command exists so
///     the index can also be (re)built from the settings dialog, e.g. right after adding a large source.
/// </remarks>
public class IndexedDirectorySourceSettingsViewModel : FamilySourceSettingsViewModel<IndexedDirectorySourceOptions>
{
    private readonly FamilyIndex _familyIndex;
    private readonly ILogger<IndexedDirectorySourceSettingsViewModel> _logger;
    private readonly AsyncRelayCommand _reindexCommand;
    private string _path;
    private string _reindexSummary = string.Empty;

    public IndexedDirectorySourceSettingsViewModel(IndexedDirectorySourceOptions options, FamilyIndex familyIndex,
                                                   ILogger<IndexedDirectorySourceSettingsViewModel> logger)
        : base(options)
    {
        _familyIndex = familyIndex;
        _logger = logger;
        _path = options.Path;
        _reindexCommand = new AsyncRelayCommand(ReindexAsync, CanReindex);
    }

    /// <summary>
    ///     Gets the source identifier for the index-backed directory family source.
    /// </summary>
    public override string Source => Path;

    /// <summary>
    ///     Gets or sets the directory path associated with the family source.
    /// </summary>
    public string Path
    {
        get => _path;
        set
        {
            SetProperty(ref _path, value);
            IsModified = true;
            OnPropertyChanged(nameof(Source));
            _reindexCommand.NotifyCanExecuteChanged();
        }
    }

    public override string TypeName => "Indexed Directory";

    /// <summary>
    ///     Gets the command that runs an incremental index scan for the configured path.
    /// </summary>
    public ICommand ReindexCommand => _reindexCommand;

    /// <summary>
    ///     Gets a human-readable summary of the last re-index run, for display below the button.
    /// </summary>
    public string ReindexSummary
    {
        get => _reindexSummary;
        private set => SetProperty(ref _reindexSummary, value);
    }

    /// <summary>
    ///     Determines whether the settings for the family source can be applied.
    /// </summary>
    /// <returns><c>true</c> if the settings can be applied; otherwise, <c>false</c>.</returns>
    public override bool CanApply()
    {
        return base.CanApply() && !string.IsNullOrWhiteSpace(Path) && System.IO.Directory.Exists(Path);
    }

    /// <summary>
    ///     Applies the current settings of the family source.
    /// </summary>
    protected override void OnApply()
    {
        base.OnApply();
        Options.Path = Path;
    }

    /// <summary>
    ///     Handles the cancellation of changes made to the family source settings.
    /// </summary>
    protected override void OnReset()
    {
        base.OnReset();
        Path = Options.Path;
    }

    private bool CanReindex()
    {
        return !_reindexCommand.IsRunning && !string.IsNullOrWhiteSpace(Path) && System.IO.Directory.Exists(Path);
    }

    private async Task ReindexAsync()
    {
        var path = Path;
        ReindexSummary = "Indexing…";

        try
        {
            var result = await Task.Run(() => new IndexScanner(_familyIndex).Scan(path)).ConfigureAwait(true);

            ReindexSummary =
                $"Indexed in {result.Duration.TotalSeconds:F1} s — {result.Added} added, {result.Updated} updated, " +
                $"{result.Removed} removed, {result.Unchanged} unchanged" +
                (result.FailedExtractions.Count > 0 ? $", {result.FailedExtractions.Count} unreadable" : string.Empty) + ".";
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Re-index failed. Path: {Path}", path);
            ReindexSummary = $"Re-index failed: {e.Message}";
        }
        finally
        {
            _reindexCommand.NotifyCanExecuteChanged();
        }
    }
}
