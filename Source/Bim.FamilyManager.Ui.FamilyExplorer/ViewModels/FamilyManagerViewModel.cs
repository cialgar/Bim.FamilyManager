using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bim.FamilyManager.Abstractions;
using Bim.FamilyManager.Abstractions.ViewModels;
using Bim.FamilyManager.Base.Options;
using Bim.FamilyManager.Index;
using Bim.FamilyManager.Ui.Views.Settings;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Scotec.Events.WeakEvents;
using Scotec.Extensions.Linq;
using Scotec.Wpf.ViewModels;

namespace Bim.FamilyManager.Ui.FamilyExplorer.ViewModels;

/// <summary>
///     Represents the view model for managing Revit families within the application.
/// </summary>
/// <remarks>
///     This class serves as the primary view model for the Family Manager feature, providing
///     properties and commands to interact with Revit family data. It manages the loading,
///     filtering, and searching of family sources and families, as well as handling user interactions
///     through commands and properties.
/// </remarks>
public class FamilyManagerViewModel : ViewModel, IFamilyManagerViewModel
{
    private readonly FamilyViewModel.Factory _familyFactory;
    private readonly FamilyIndex _familyIndex;
    private readonly IFamilyManager _familyManager;
    private readonly IndexQuery _indexQuery;
    private readonly string? _logo;
    private readonly AsyncRelayCommand _reloadCommand;
    private readonly FamilySourceViewModel.Factory _sourceFactory;
    private IReadOnlyList<CategoryFilterOption>? _categoryFilters;
    private IEnumerable<IFamilySourceViewModel>? _familySources;
    private bool _favoritesOnly;
    private bool _isActiveSearch;
    private bool _isGalleryView;
    private string _searchPattern = string.Empty;
    private IList<IFamilyViewModel>? _searchResult;
    private CategoryFilterOption? _selectedCategoryFilter;
    private IFamilySourceViewModel? _selectedFamilySource;
    private TagFilterOption? _selectedTagFilter;
    private VersionFilterOption? _selectedVersionFilter;
    private IReadOnlyList<TagFilterOption>? _tagFilters;
    private IReadOnlyList<VersionFilterOption>? _versionFilters;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FamilyManagerViewModel" /> class.
    /// </summary>
    /// <param name="familyManager">
    ///     The <see cref="Bim.FamilyManager.Abstractions.IFamilyManager" /> instance responsible for managing
    ///     families.
    /// </param>
    /// <param name="sourceFactory">
    ///     A factory delegate for creating instances of <see cref="FamilySourceViewModel" />.
    /// </param>
    /// <param name="familyFactory">
    ///     A factory delegate for creating instances of <see cref="FamilyViewModel" />.
    /// </param>
    /// <param name="options">
    ///     The <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" /> instance containing configuration settings for
    ///     <see cref="FamilyManagerOptions" />.
    /// </param>
    /// <param name="settingsManagerWindowFactory">
    ///     A factory delegate for creating instances of <see cref="SettingsManagerWindow" />.
    /// </param>
    public FamilyManagerViewModel(IFamilyManager familyManager,
                                  FamilySourceViewModel.Factory sourceFactory,
                                  FamilyViewModel.Factory familyFactory,
                                  IOptions<FamilyManagerOptions> options,
                                  FamilyIndex familyIndex,
                                  SettingsManagerWindow.Factory settingsManagerWindowFactory
    )
    {
        SettingsManagerWindowFactory = settingsManagerWindowFactory;
        _familyManager = familyManager;
        _sourceFactory = sourceFactory;
        _familyFactory = familyFactory;
        _familyIndex = familyIndex;
        _indexQuery = new IndexQuery(familyIndex);

        _logo = options.Value.Logo;
        StaticWeakEventManager.AddWeakHandler(_familyManager, nameof(_familyManager.Reloaded), OnReloaded);
        _reloadCommand = new AsyncRelayCommand(async () =>
        {
            await _familyManager.ReloadAsync();
        });
    }

    /// <summary>
    ///     Gets the factory delegate used to create instances of <see cref="SettingsManagerWindow" />.
    /// </summary>
    /// <remarks>
    ///     This property provides a factory method to instantiate the <see cref="SettingsManagerWindow" />.
    ///     It is typically used to display the settings management interface within the Family Manager application.
    /// </remarks>
    public SettingsManagerWindow.Factory SettingsManagerWindowFactory { get; }

    /// <summary>
    ///     Gets or sets the currently selected family source in the Family Manager.
    /// </summary>
    /// <remarks>
    ///     This property represents the active family source selected by the user.
    ///     It is updated automatically when a family source is marked as selected.
    ///     The selected family source determines the context for operations such as
    ///     filtering and searching families.
    /// </remarks>
    public IFamilySourceViewModel? SelectedFamilySource
    {
        get => _selectedFamilySource;
        private set
        {
            if (_selectedFamilySource == value)
            {
                return;
            }

            if (_selectedFamilySource is not null)
            {
                _selectedFamilySource.IsSelected = false;
            }

            if (value is not null)
            {
                value.IsSelected = true;
            }

            SetProperty(ref _selectedFamilySource, value);
        }
    }

    /// <summary>
    ///     Gets or sets the search pattern used to filter Revit families in the Family Manager.
    /// </summary>
    /// <remarks>
    ///     This property is bound to the search input in the user interface and is updated
    ///     whenever the user modifies the search text. Changing this property triggers the
    ///     filtering of families based on the specified search pattern.
    /// </remarks>
    /// <value>
    ///     A <see cref="string" /> representing the search pattern used for filtering families.
    /// </value>
    public string SearchPattern
    {
        get => _searchPattern;
        set
        {
            SetProperty(ref _searchPattern, value);
            FilterFamilies(_searchPattern);
        }
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the search functionality is currently active.
    /// </summary>
    /// <remarks>
    ///     When set to <c>true</c>, the search functionality is enabled, and the application
    ///     displays search results based on the provided search pattern. When set to <c>false</c>,
    ///     the search functionality is disabled, and the application displays the default view.
    ///     This property is typically bound to the UI to toggle between search results and the
    ///     default view.
    /// </remarks>
    public bool IsActiveSearch
    {
        get => _isActiveSearch;
        set
        {
            SetProperty(ref _isActiveSearch, value);
            OnPropertyChanged(nameof(ShowTree));
            OnPropertyChanged(nameof(ShowGallery));
        }
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the gallery view (thumbnail grid of the selected
    ///     folder, including subfolders) is shown instead of the folder tree.
    /// </summary>
    public bool IsGalleryView
    {
        get => _isGalleryView;
        set
        {
            SetProperty(ref _isGalleryView, value);
            OnPropertyChanged(nameof(ShowTree));
            OnPropertyChanged(nameof(ShowGallery));
        }
    }

    /// <summary>Gets a value indicating whether the folder tree is visible.</summary>
    public bool ShowTree => !IsActiveSearch && !IsGalleryView;

    /// <summary>Gets a value indicating whether the gallery grid is visible.</summary>
    public bool ShowGallery => !IsActiveSearch && IsGalleryView;

    /// <summary>
    ///     Gets the category filter options: one entry per language-independent category key present in
    ///     the index (displayed in the UI language), plus unmapped localized categories as-is.
    /// </summary>
    public IReadOnlyList<CategoryFilterOption> CategoryFilters => _categoryFilters ??= BuildCategoryFilters();

    /// <summary>Gets the product version filter options.</summary>
    public IReadOnlyList<VersionFilterOption> VersionFilters =>
        _versionFilters ??= new[] { new VersionFilterOption("All versions", null) }
                            .Concat(SafeIndex(() => _indexQuery.GetProductVersions(), [])
                                        .Select(version => new VersionFilterOption(version, version)))
                            .ToList();

    /// <summary>Gets the tag filter options.</summary>
    public IReadOnlyList<TagFilterOption> TagFilters =>
        _tagFilters ??= new[] { new TagFilterOption("All tags", null) }
                        .Concat(SafeIndex(() => new IndexEditor(_familyIndex).GetAllTags(), [])
                                    .Select(tag => new TagFilterOption(tag, tag)))
                        .ToList();

    /// <summary>Gets or sets the selected category filter.</summary>
    public CategoryFilterOption? SelectedCategoryFilter
    {
        get => _selectedCategoryFilter ??= CategoryFilters.FirstOrDefault();
        set
        {
            SetProperty(ref _selectedCategoryFilter, value);
            FilterFamilies(SearchPattern);
        }
    }

    /// <summary>Gets or sets the selected product version filter.</summary>
    public VersionFilterOption? SelectedVersionFilter
    {
        get => _selectedVersionFilter ??= VersionFilters.FirstOrDefault();
        set
        {
            SetProperty(ref _selectedVersionFilter, value);
            FilterFamilies(SearchPattern);
        }
    }

    /// <summary>Gets or sets the selected tag filter.</summary>
    public TagFilterOption? SelectedTagFilter
    {
        get => _selectedTagFilter ??= TagFilters.FirstOrDefault();
        set
        {
            SetProperty(ref _selectedTagFilter, value);
            FilterFamilies(SearchPattern);
        }
    }

    /// <summary>Gets or sets a value indicating whether only favorite families are shown.</summary>
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            SetProperty(ref _favoritesOnly, value);
            FilterFamilies(SearchPattern);
        }
    }

    /// <summary>
    ///     Gets the search results for Revit families based on the current search pattern.
    /// </summary>
    /// <remarks>
    ///     This property holds the filtered list of <see cref="FamilyViewModel" /> objects that match the
    ///     search criteria specified by the <see cref="SearchPattern" /> property. The search is triggered
    ///     when the <see cref="SearchPattern" /> length is at least 3 characters. If no search is active,
    ///     this property will be <c>null</c>.
    /// </remarks>
    /// <value>
    ///     A list of <see cref="FamilyViewModel" /> objects representing the search results, or <c>null</c>
    ///     if no search is active.
    /// </value>
    public IList<IFamilyViewModel>? SearchResult
    {
        get => _searchResult;
        private set => SetProperty(ref _searchResult, value);
    }

    /// <summary>
    ///     Gets the logo image for the Family Manager.
    /// </summary>
    /// <remarks>
    ///     This property retrieves the logo image specified in the application settings.
    ///     If the logo file path is invalid or the file does not exist, it returns <c>null</c>.
    /// </remarks>
    /// <value>
    ///     An <see cref="ImageSource" /> representing the logo image, or <c>null</c> if the logo is not available.
    /// </value>
    public ImageSource? Logo
    {
        get
        {
            if (string.IsNullOrEmpty(_logo))
            {
                return null;
            }

            var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            // Specify the file path for the logo image
            var logoFilePath = Path.Combine(path, _logo);

            // Check if the file exists
            if (!File.Exists(logoFilePath))
            {
                return null;
            }

            // Load the image from the file
            return new BitmapImage(new Uri(logoFilePath, UriKind.Absolute));
        }
    }

    /// <summary>
    ///     Gets the command that reloads the family data within the Family Manager.
    /// </summary>
    /// <remarks>
    ///     This command is typically bound to a user interface element, such as a button, to allow
    ///     users to refresh the family data displayed in the application. It triggers the reloading
    ///     of family sources and updates the associated view models accordingly.
    /// </remarks>
    public ICommand ReloadCommand => _reloadCommand;

    /// <summary>
    ///     Gets the collection of family sources available in the Family Manager.
    /// </summary>
    /// <remarks>
    ///     This property initializes and retrieves the list of family sources, represented by
    ///     <see cref="FamilySourceViewModel" /> instances. It also manages the selection state of
    ///     family sources, ensuring that one source is marked as selected at any given time.
    /// </remarks>
    /// <value>
    ///     A collection of <see cref="FamilySourceViewModel" /> objects representing the available
    ///     family sources. Returns <c>null</c> if the collection has not been initialized.
    /// </value>
    public IEnumerable<IFamilySourceViewModel> FamilySources
    {
        get
        {
            if (_familySources is null)
            {
                _familySources = _familyManager.FamilySources.Select(source => (IFamilySourceViewModel)_sourceFactory(source)).ToList();

                _familySources.ForAll(g => StaticWeakEventManager.AddWeakHandler<IFamilySourceViewModel, PropertyChangedEventArgs>(g, nameof(g.PropertyChanged),
                    (source, args) =>
                    {
                        if (args.PropertyName == nameof(IFamilySourceViewModel.IsSelected))
                        {
                            if (source.IsSelected)
                            {
                                SelectedFamilySource = source;
                            }
                        }
                    }));

                var selectedSource = _familySources.FirstOrDefault(g => g.IsSelected) ?? _familySources.FirstOrDefault();
                if (selectedSource is not null)
                {
                    selectedSource.IsSelected = true;
                }
            }

            return _familySources;
        }
        private set => SetProperty(ref _familySources, value);
    }

    /// <summary>
    ///     Refreshes the state of the view model by applying the active search filter, if enabled.
    /// </summary>
    /// <remarks>
    ///     This method checks whether the active search is enabled and, if so, filters the families
    ///     based on the current search pattern. It is typically invoked to update the displayed
    ///     family data in response to user interactions or changes in the search criteria.
    /// </remarks>
    public void Refresh()
    {
        if (IsActiveSearch)
        {
            FilterFamilies(SearchPattern);
        }
    }

    /// <summary>
    ///     Handles the <see cref="IFamilyManager.Reloaded" /> event triggered when the family manager reloads its data.
    /// </summary>
    /// <param name="sender">The source of the event, typically the <see cref="IFamilyManager" /> instance.</param>
    /// <param name="e">The event data associated with the reload operation.</param>
    /// <remarks>
    ///     This method resets the <see cref="SelectedFamilySource" /> and <see cref="FamilySources" /> properties to null,
    ///     ensuring that the view model reflects the updated state of the family manager after a reload.
    /// </remarks>
    private void OnReloaded(IFamilyManager? sender, EventArgs e)
    {
        SelectedFamilySource = null;

        _familySources = null;
        OnPropertyChanged(nameof(FamilySources));

        // The index content may have changed: rebuild the filter option lists.
        _categoryFilters = null;
        _versionFilters = null;
        _tagFilters = null;
        OnPropertyChanged(nameof(CategoryFilters));
        OnPropertyChanged(nameof(VersionFilters));
        OnPropertyChanged(nameof(TagFilters));
    }

    /// <summary>
    ///     Filters the Revit families based on the specified search pattern and the currently selected folder.
    /// </summary>
    /// <param name="searchPattern">
    ///     The search pattern to filter the families. A non-empty string with a minimum length of 3 characters is required
    ///     to perform the filtering.
    /// </param>
    /// <remarks>
    ///     This method updates the <see cref="SearchResult" /> property with the filtered families that match the search
    ///     pattern within the selected folder. If the search pattern is invalid or no folder is selected, the search result
    ///     is cleared, and the <see cref="IsActiveSearch" /> property is set to <c>false</c>.
    /// </remarks>
    private void FilterFamilies(string searchPattern)
    {
        var options = BuildSearchOptions();
        var hasText = !string.IsNullOrWhiteSpace(searchPattern) && searchPattern.Trim().Length >= 3;
        var searchableSources = _familyManager.FamilySources.OfType<ISearchableFamilySource>().ToList();

        if ((hasText || options is not null) && searchableSources.Count > 0)
        {
            // Global search: aggregated over all indexed sources, independent of the selected folder.
            IsActiveSearch = true;
            var searchResult = Task.Run(async () =>
            {
                var families = new List<IRevitFamily>();
                foreach (var source in searchableSources)
                {
                    await foreach (var family in source.SearchFamiliesAsync(hasText ? searchPattern : null, options, CancellationToken.None))
                    {
                        families.Add(family);
                    }
                }

                // The family manager caches families by name, so the same name found in several
                // sources yields the same instance — keep one entry per name.
                return families.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                               .Select(group => group.First())
                               .OrderBy(f => f.Name)
                               .ToList();
            }).ConfigureAwait(true).GetAwaiter().GetResult();

            SearchResult = searchResult.Select(family => (IFamilyViewModel)_familyFactory(family))
                                       .ToList();
            return;
        }

        // Fallback for setups without indexed sources: folder-scoped text search (upstream behavior).
        var folder = SelectedFamilySource?.SelectedFolder;
        if (hasText && folder is not null)
        {
            IsActiveSearch = true;
            var searchResult = Task.Run(async () =>
            {
                var families = new List<IRevitFamily>();
                await foreach (var family in _familyManager.SearchRevitFamiliesAsync(folder.Folder, searchPattern, CancellationToken.None))
                {
                    families.Add(family);
                }

                return families.OrderBy(f => f.Name)
                               .ToList();
            }).ConfigureAwait(true).GetAwaiter().GetResult();

            SearchResult = searchResult.Select(family => (IFamilyViewModel)_familyFactory(family))
                                       .ToList();
        }
        else
        {
            SearchResult = null;
            IsActiveSearch = false;
        }
    }

    /// <summary>
    ///     Builds the search options from the current filter selections, or <c>null</c> when no filter is set.
    /// </summary>
    private FamilySearchOptions? BuildSearchOptions()
    {
        var options = new FamilySearchOptions
        {
            CategoryKey = SelectedCategoryFilter?.CategoryKey,
            Category = SelectedCategoryFilter?.CategoryText,
            ProductVersion = SelectedVersionFilter?.Version,
            Tag = SelectedTagFilter?.Tag,
            FavoritesOnly = FavoritesOnly
        };

        return options.HasAnyFilter ? options : null;
    }

    /// <summary>
    ///     Builds the category filter list: mapped categories collapse their localized variants into one
    ///     entry displayed in the UI language; unmapped categories appear with their localized text.
    /// </summary>
    private IReadOnlyList<CategoryFilterOption> BuildCategoryFilters()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var options = new List<CategoryFilterOption> { new("All categories", null, null) };
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<CategoryFilterOption>();

        foreach (var term in SafeIndex(() => _indexQuery.GetCategories(), []))
        {
            if (CategoryKeyMap.TryGetKey(term, out var key))
            {
                if (seenKeys.Add(key!))
                {
                    CategoryKeyMap.TryGetDisplayName(key!, language, out var displayName);
                    entries.Add(new CategoryFilterOption(displayName ?? term, key, null));
                }
            }
            else
            {
                entries.Add(new CategoryFilterOption(term, null, term));
            }
        }

        options.AddRange(entries.OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        return options;
    }

    /// <summary>
    ///     Runs an index query, falling back to a default when the index is unavailable.
    /// </summary>
    private static T SafeIndex<T>(Func<T> query, T fallback)
    {
        try
        {
            return query();
        }
        catch
        {
            return fallback;
        }
    }
}

/// <summary>
///     Represents a category filter entry: either a language-independent key (with localized display
///     name), an unmapped localized category, or the "all" entry when both are <c>null</c>.
/// </summary>
/// <param name="DisplayName">The text shown in the filter list.</param>
/// <param name="CategoryKey">The stable category key, or <c>null</c>.</param>
/// <param name="CategoryText">The localized category text for unmapped categories, or <c>null</c>.</param>
public sealed record CategoryFilterOption(string DisplayName, string? CategoryKey, string? CategoryText);

/// <summary>
///     Represents a product version filter entry; <paramref name="Version" /> is <c>null</c> for the "all" entry.
/// </summary>
/// <param name="DisplayName">The text shown in the filter list.</param>
/// <param name="Version">The product version, or <c>null</c>.</param>
public sealed record VersionFilterOption(string DisplayName, string? Version);

/// <summary>
///     Represents a tag filter entry; <paramref name="Tag" /> is <c>null</c> for the "all" entry.
/// </summary>
/// <param name="DisplayName">The text shown in the filter list.</param>
/// <param name="Tag">The tag name, or <c>null</c>.</param>
public sealed record TagFilterOption(string DisplayName, string? Tag);
