using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Bim.FamilyManager.Abstractions;
using Bim.FamilyManager.Abstractions.ViewModels;
using Bim.FamilyManager.Base.Options;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scotec.Events.WeakEvents;
using Scotec.Revit;

namespace Bim.FamilyManager.Ui.ViewModels;

/// <summary>
///     Represents a view model for a Revit family, providing access to family data, commands, and related symbols.
/// </summary>
/// <typeparam name="TLayoutOptions">The type of layout options used for display configuration.</typeparam>
/// <remarks>
///     Encapsulates a <see cref="IRevitFamily" /> instance and exposes properties and commands for interacting with family
///     details, such as its name, preview image, and symbols.
/// </remarks>
public abstract class FamilyViewModel<TLayoutOptions> : FamilyManagerItemViewModel<TLayoutOptions>, IFamilyViewModel
    where TLayoutOptions : LayoutOptions
{
    /// <summary>
    ///     Default image used when no preview is available.
    /// </summary>
    /// <remarks>
    ///     Used as a fallback when the family does not provide a preview image.
    /// </remarks>
    private static readonly BitmapImage NoPreviewImage;

    /// <summary>
    ///     Default image used when a download image is required.
    /// </summary>
    /// <remarks>
    ///     Used as a fallback when the family is not initialized and a preview image is not available.
    /// </remarks>
    private static readonly BitmapImage DownloadImage;

    private readonly RelayCommand _addTagCommand;
    private readonly IFamilyAnnotations _annotations;
    private readonly Func<FamilyDropHandler> _dropHandlerFactory;
    private readonly RelayCommand _editFamilyCommand;
    private readonly IFamilyManager _familyManager;
    private readonly RelayCommand _loadFamilyCommand;
    private readonly ILogger<FamilyViewModel<TLayoutOptions>> _logger;
    private readonly RelayCommand _placeFamilyCommand;
    private readonly RelayCommand<IFamilySymbolViewModel> _placeSymbolCommand;
    private readonly RelayCommand _removeFamilyCommand;
    private readonly RevitTask _revitTask;
    private readonly RelayCommand<TagOptionViewModel> _toggleTagCommand;
    private IList<IFamilySymbolViewModel>? _symbols;

    /// <summary>
    ///     Initializes static resources for the <see cref="FamilyViewModel{TLayoutOptions}" /> class.
    /// </summary>
    /// <remarks>
    ///     Loads default images for use when preview or download images are required.
    /// </remarks>
    static FamilyViewModel()
    {
        NoPreviewImage = new BitmapImage(new Uri("pack://application:,,,/Bim.FamilyManager.Ui;component/Resources/Images/NoPreview_128x128.png",
            UriKind.Absolute));
        DownloadImage = new BitmapImage(new Uri("pack://application:,,,/Bim.FamilyManager.Ui;component/Resources/Images/Download_128x128.png",
            UriKind.Absolute));
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FamilyViewModel{TLayoutOptions}" /> class.
    /// </summary>
    /// <param name="family">The <see cref="IRevitFamily" /> instance to be managed.</param>
    /// <param name="familyManager">The <see cref="IFamilyManager" /> responsible for family operations.</param>
    /// <param name="dropHandlerFactory">Factory for creating <see cref="FamilyDropHandler" /> instances.</param>
    /// <param name="layoutOptions">Monitor for layout/display options.</param>
    /// <param name="revitTask">Task runner for Revit operations.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    /// <remarks>
    ///     Sets up commands, event handlers, and initializes the view model with provided dependencies.
    /// </remarks>
    protected FamilyViewModel(IRevitFamily family, IFamilyManager familyManager,
                              Func<FamilyDropHandler> dropHandlerFactory,
                              IOptionsMonitor<TLayoutOptions> layoutOptions,
                              RevitTask revitTask,
                              IFamilyAnnotations annotations,
                              ILogger<FamilyViewModel<TLayoutOptions>> logger)
        : base(layoutOptions)
    {
        _familyManager = familyManager;
        _dropHandlerFactory = dropHandlerFactory;
        _revitTask = revitTask;
        _annotations = annotations;
        _logger = logger;
        _editFamilyCommand = new RelayCommand(OpenFamily);
        _loadFamilyCommand = new RelayCommand(LoadFamily);
        _removeFamilyCommand = new RelayCommand(RemoveFamily, CanRemoveFamily);
        _placeFamilyCommand = new RelayCommand(PlaceDefaultSymbol);
        _placeSymbolCommand = new RelayCommand<IFamilySymbolViewModel>(PlaceSymbol);
        _toggleTagCommand = new RelayCommand<TagOptionViewModel>(ToggleTag);
        _addTagCommand = new RelayCommand(AddNewTag);
        Family = family;

        StaticWeakEventManager.AddWeakHandler(family, nameof(IRevitFamily.Initialized),
            (sender, args) =>
            {
                // Invalidate symbols cache on reinitialization to ensure they are recreated.
                _symbols = null;
                Application.Current.Dispatcher.Invoke(OnInitialized, DispatcherPriority.ApplicationIdle);
            });

        StaticWeakEventManager.AddWeakHandler(family, nameof(IRevitFamily.LoadedInDocumentChanged),
            (sender, args) =>
            {
                Application.Current.Dispatcher.Invoke(NotifyChanges);
            });
    }

    private void OnInitialized()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Product));
        OnPropertyChanged(nameof(ProductVersion));
        OnPropertyChanged(nameof(Updated));
        OnPropertyChanged(nameof(Symbols));
    }

    /// <summary>
    ///     Gets a handler for drag-and-drop operations involving the family.
    /// </summary>
    /// <remarks>
    ///     The handler is created using the provided factory and enables drag-and-drop functionality for the family.
    /// </remarks>
    public IControllableDropHandler DropHandler => _dropHandlerFactory();

    /// <summary>
    ///     Indicates whether the family is loaded in the active Revit document.
    /// </summary>
    /// <remarks>
    ///     Returns <c>true</c> if the family is loaded in the document; otherwise, <c>false</c>.
    /// </remarks>
    public bool IsLoadedInDocument => Family.IsLoadedInDocument;

    /// <summary>
    ///     Gets the name of the family.
    /// </summary>
    /// <remarks>
    ///     The name is retrieved from the encapsulated <see cref="IRevitFamily" /> instance.
    /// </remarks>
    public override string Name => Family.Name;

    /// <summary>
    ///     Gets the encapsulated <see cref="IRevitFamily" /> instance.
    /// </summary>
    /// <remarks>
    ///     Provides access to the underlying family data and metadata.
    /// </remarks>
    public IRevitFamily Family { get; }

    /// <summary>
    ///     Gets the preview image of the family.
    /// </summary>
    /// <remarks>
    ///     Returns the preview image if available, otherwise a default image.
    /// </remarks>
    public override ImageSource? Preview => 
        GetPreview(Family.Preview);

    /// <summary>
    ///     Command to edit the family.
    /// </summary>
    /// <remarks>
    ///     Executes logic to open the family for editing.
    /// </remarks>
    public ICommand EditFamilyCommand => _editFamilyCommand;

    /// <summary>
    ///     Command to load the family into the current document.
    /// </summary>
    /// <remarks>
    ///     Executes logic to load the family into the active Revit document.
    /// </remarks>
    public ICommand LoadFamilyCommand => _loadFamilyCommand;

    /// <summary>
    ///     Command to remove the family from the current document.
    /// </summary>
    /// <remarks>
    ///     Executes logic to remove the family from the active Revit document.
    /// </remarks>
    public ICommand RemoveFamilyCommand => _removeFamilyCommand;

    /// <summary>
    ///     Gets the collection of symbols associated with the family.
    /// </summary>
    /// <value>
    ///     A list of <see cref="IFamilySymbolViewModel" /> instances representing the symbols of the family.
    /// </value>
    /// <remarks>
    ///     The collection is lazily initialized and ordered by symbol name.
    /// </remarks>
    public IList<IFamilySymbolViewModel> Symbols => _symbols ??= Family.FamilySymbols
                                                                       .Select(CreateSymbolViewModel)
                                                                       .OrderBy(symbol => symbol.Name)
                                                                       .ToList();

    /// <summary>
    ///     Gets the product name associated with the family.
    /// </summary>
    /// <remarks>
    ///     The product name is provided by the underlying <see cref="IRevitFamily" />.
    /// </remarks>
    public string Product => Family.Product;

    /// <summary>
    ///     Gets the product version associated with the family.
    /// </summary>
    /// <remarks>
    ///     The product version is provided by the underlying <see cref="IRevitFamily" />.
    /// </remarks>
    public string ProductVersion => Family.ProductVersion;

    /// <summary>
    ///     Gets the last updated date of the family.
    /// </summary>
    /// <remarks>
    ///     The date is provided by the underlying <see cref="IRevitFamily" />.
    /// </remarks>
    public DateTime Updated => Family.Updated;

    /// <summary>
    ///     Creates a view model for a given family symbol.
    /// </summary>
    /// <param name="symbol">The <see cref="IRevitFamilySymbol" /> to wrap.</param>
    /// <returns>An <see cref="IFamilySymbolViewModel" /> instance.</returns>
    /// <remarks>
    ///     This method must be implemented by derived classes to provide the appropriate symbol view model.
    /// </remarks>
    protected abstract IFamilySymbolViewModel CreateSymbolViewModel(IRevitFamilySymbol symbol);

    /// <summary>
    ///     Determines if the family can be removed from the document.
    /// </summary>
    /// <returns><c>true</c> if the family is loaded; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     Used to control the enabled state of the remove command.
    /// </remarks>
    private bool CanRemoveFamily()
    {
        return IsLoadedInDocument;
    }

    /// <summary>
    ///     Removes the family from the active document.
    /// </summary>
    /// <remarks>
    ///     Executes the removal operation and notifies property changes. Logs errors if the operation fails.
    /// </remarks>
    private async void RemoveFamily()
    {
        try
        {
            await _revitTask.Run(uiApplication => { _familyManager.RemoveFamilyFromActiveDocument(Family); });
            NotifyChanges();
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, $"Error while removing family from the active document. Family: {Family.Name}");
        }
    }

    /// <summary>
    ///     Opens the family for editing.
    /// </summary>
    /// <remarks>
    ///     Invokes the family manager to open the family for editing.
    /// </remarks>
    private void OpenFamily()
    {
        _familyManager.EditFamily(Family);
    }

    /// <summary>
    ///     Loads the family into the active document.
    /// </summary>
    /// <remarks>
    ///     Executes the load operation and notifies property changes. Logs errors if the operation fails.
    /// </remarks>
    private async void LoadFamily()
    {
        try
        {
            var family = await _revitTask.Run(uiApplication =>
            {
                _familyManager.TryLoadFamilyIntoActiveDocument(Family, out var loadedFamily);
                return loadedFamily;
            }).ConfigureAwait(true);

            NotifyChanges();
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, $"Error while loading family into the active document. Family: {Family.Name}");
        }
    }

    /// <summary>
    ///     Notifies property changes and updates command states.
    /// </summary>
    /// <remarks>
    ///     Ensures the UI reflects the current state of the family and its commands.
    /// </remarks>
    private void NotifyChanges()
    {
        OnPropertyChanged(nameof(IsLoadedInDocument));
        _removeFamilyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    ///     Gets the command that places the default type of the family in the active view (double click).
    /// </summary>
    public ICommand PlaceFamilyCommand => _placeFamilyCommand;

    /// <summary>
    ///     Gets the command that places a specific family type in the active view (click on a type).
    /// </summary>
    public ICommand PlaceSymbolCommand => _placeSymbolCommand;

    /// <summary>
    ///     Gets the command that toggles a tag assignment from the context menu.
    /// </summary>
    public ICommand ToggleTagCommand => _toggleTagCommand;

    /// <summary>
    ///     Gets the command that prompts for a new tag and assigns it to the family.
    /// </summary>
    public ICommand AddTagCommand => _addTagCommand;

    /// <summary>
    ///     Gets or sets a value indicating whether the family is marked as favorite.
    /// </summary>
    /// <remarks>
    ///     The state persists in the family index. Families that are not indexed report <c>false</c>
    ///     and ignore changes.
    /// </remarks>
    public bool IsFavorite
    {
        get => _annotations.IsFavorite(Family.Name);
        set
        {
            _annotations.SetFavorite(Family.Name, value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Gets the tag options shown in the context menu: every known tag with its assignment state
    ///     for this family.
    /// </summary>
    /// <remarks>Evaluated when the context menu opens, so the list always reflects the current index state.</remarks>
    public IReadOnlyList<TagOptionViewModel> TagOptions
    {
        get
        {
            var assigned = _annotations.GetTags(Family.Name);
            return _annotations.GetAllTags()
                               .Select(tag => new TagOptionViewModel(tag, assigned.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                               .ToList();
        }
    }

    /// <summary>
    ///     Places the default (first) type of the family in the active view.
    /// </summary>
    private void PlaceDefaultSymbol()
    {
        PlaceSymbol(Symbols.FirstOrDefault());
    }

    /// <summary>
    ///     Loads the family type into the active document and posts a placement request for it.
    /// </summary>
    /// <param name="symbolViewModel">The type to place, or <c>null</c> when the family exposes no types.</param>
    /// <remarks>
    ///     Mirrors the drag &amp; drop flow of <see cref="FamilyDropHandler" />: load the symbol inside a
    ///     transaction, validate the active view with <see cref="UIDocument.CanPlaceElementType" /> and
    ///     post <see cref="UIDocument.PostRequestForElementTypePlacement" />. An incompatible view or a
    ///     missing document produces a clear message — never a silent failure.
    /// </remarks>
    private async void PlaceSymbol(IFamilySymbolViewModel? symbolViewModel)
    {
        try
        {
            await _revitTask.Run(uiApplication =>
            {
                var uiDocument = uiApplication.ActiveUIDocument;
                if (uiDocument is null)
                {
                    TaskDialog.Show("Family Manager", "There is no open document to place the family in.");
                    return;
                }

                var symbol = symbolViewModel ?? Symbols.FirstOrDefault();
                if (symbol is null)
                {
                    TaskDialog.Show("Family Manager", $"The family '{Family.Name}' does not contain any placeable type.");
                    return;
                }

                using (var transaction = new Autodesk.Revit.DB.Transaction(uiDocument.Document, "Load Family"))
                {
                    transaction.Start();

                    if (!_familyManager.TryLoadFamilySymbol(symbol.FamilySymbol, uiDocument.Document, out var familySymbol))
                    {
                        // The family manager has already informed the user (version gate, cancelled
                        // overwrite); nothing was loaded.
                        transaction.RollBack();
                        return;
                    }

                    transaction.Commit();

                    if (!uiDocument.CanPlaceElementType(familySymbol))
                    {
                        TaskDialog.Show("Family Manager",
                            $"The type '{symbol.Name}' of family '{Family.Name}' cannot be placed in the active view. " +
                            "Switch to a view that supports this category and try again. The family has been loaded into the project.");
                        return;
                    }

                    uiDocument.PostRequestForElementTypePlacement(familySymbol);
                }
            }).ConfigureAwait(true);

            NotifyChanges();
        }
        catch (Exception e)
        {
            _logger.LogCritical(e, $"Error while placing family type. Family: {Family.Name}");
        }
    }

    /// <summary>
    ///     Toggles a tag assignment from the context menu.
    /// </summary>
    /// <param name="option">The tag option that was clicked.</param>
    private void ToggleTag(TagOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        if (option.IsAssigned)
        {
            _annotations.RemoveTag(Family.Name, option.Name);
        }
        else
        {
            _annotations.AddTag(Family.Name, option.Name);
        }

        OnPropertyChanged(nameof(TagOptions));
    }

    /// <summary>
    ///     Prompts for a new tag name and assigns it to the family.
    /// </summary>
    private void AddNewTag()
    {
        var tag = Views.TagPromptWindow.Prompt();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        _annotations.AddTag(Family.Name, tag.Trim());
        OnPropertyChanged(nameof(TagOptions));
    }

    /// <summary>
    ///     Gets the preview image for the family, or a default image if none is available.
    /// </summary>
    /// <param name="preview">Stream containing the preview image data.</param>
    /// <returns>An <see cref="ImageSource" /> for the preview image.</returns>
    /// <remarks>
    ///     Returns a default image if the preview stream is null.
    /// </remarks>
    private ImageSource? GetPreview(Stream? preview)
    {
        return preview is null
            ? GetDefaultPreviewImage()
            : GetPreviewImage(preview, Color.FromRgb(255, 255, 255));
    }

    /// <summary>
    ///     Gets the default preview image for the family.
    /// </summary>
    /// <returns>A <see cref="BitmapImage" /> representing the default preview image.</returns>
    /// <remarks>
    ///     Returns a "No Preview" image if the family is initialized, otherwise returns a "Download" image.
    /// </remarks>
    private BitmapImage GetDefaultPreviewImage()
    {
        return Family.IsInitialized
            ? NoPreviewImage
            : DownloadImage;
    }
}
