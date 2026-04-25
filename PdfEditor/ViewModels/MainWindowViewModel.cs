using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PdfEditor.Controls;
using PdfEditor.Models;
using PdfEditor.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using SkiaSharp;
using PdfCoreDocument = Pdfe.Core.Document.PdfDocument;

namespace PdfEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PdfDocumentService _documentService;
    private readonly PdfRenderService _renderService;
    private readonly RedactionService _redactionService;
    private readonly PdfTextExtractionService _textExtractionService;
    private readonly SignatureVerificationService _signatureService;
    private readonly FilenameSuggestionService _filenameSuggestionService;

    // State managers
    public DocumentStateManager FileState { get; }
    public RedactionWorkflowManager RedactionWorkflow { get; }

    private string _currentFilePath = string.Empty;
    private Bitmap? _currentPageImage;
    private PdfCoreDocument? _pdfCoreDocument;
    private int _currentPageIndex;
    private double _zoomLevel = 1.0;
    private bool _skipZoomSave; // Flag to skip zoom save during auto-reset
    private bool _isRedactionMode;
    private Rect _currentRedactionArea;
    private bool _isTextSelectionMode;
    private Rect _currentTextSelectionArea;
    private string _selectedText = string.Empty;
    private ObservableCollection<string> _recentFiles = new();
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;
    private ObservableCollection<Rect> _currentPageSearchHighlights = new();
#if DEBUG
    private bool _debugVerifyRedaction = true; // Debug mode: enabled in DEBUG builds, disabled in RELEASE
#else
    private bool _debugVerifyRedaction = false; // Debug mode: disabled in RELEASE builds
#endif
    private int _renderCacheMax = 20;
    private string _operationStatus = string.Empty;
    private bool _hasInMemoryModifications; // Tracks if document has been modified in-memory (e.g., redactions applied)

    /// <summary>
    /// Tracks whether the user is in an "auto-fit" zoom state. When set to
    /// <see cref="ZoomFitMode.FitWidth"/> or <see cref="ZoomFitMode.FitPage"/>
    /// the renderer re-fits on viewport size changes; once the user manually
    /// zooms (Ctrl++/-/0) we drop into <see cref="ZoomFitMode.Manual"/> and
    /// stop auto-recomputing.
    /// </summary>
    private ZoomFitMode _zoomFitMode = ZoomFitMode.FitWidth;
    private bool _isThumbnailsSidebarVisible = true;
    private bool _isClipboardSidebarVisible = true;

    /// <summary>
    /// Parameterless constructor for testing and scripting scenarios.
    /// Creates a ViewModel with default (NullLogger) dependencies.
    /// </summary>
    public MainWindowViewModel()
    {
        var nullLoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var renderService = new PdfRenderService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfRenderService>.Instance);

        _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindowViewModel>.Instance;
        _loggerFactory = nullLoggerFactory;
        _documentService = new PdfDocumentService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfDocumentService>.Instance);
        _renderService = renderService;
        _redactionService = new RedactionService(Microsoft.Extensions.Logging.Abstractions.NullLogger<RedactionService>.Instance, nullLoggerFactory);
        _textExtractionService = new PdfTextExtractionService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfTextExtractionService>.Instance);
        _searchService = new PdfSearchService(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfSearchService>.Instance);
        _signatureService = new SignatureVerificationService(Microsoft.Extensions.Logging.Abstractions.NullLogger<SignatureVerificationService>.Instance);
        _filenameSuggestionService = new FilenameSuggestionService();

        // Initialize state managers
        FileState = new DocumentStateManager();
        RedactionWorkflow = new RedactionWorkflowManager();

        _logger.LogInformation("MainWindowViewModel initialized (test mode)");

        PageThumbnails = new ObservableCollection<PageThumbnail>();
        ClipboardHistory = new ObservableCollection<ClipboardEntry>();

        // Commands
        _logger.LogDebug("Setting up ReactiveUI commands");
        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        SaveFileCommand = ReactiveCommand.CreateFromTask(SaveFileAsync);
        RemoveCurrentPageCommand = ReactiveCommand.CreateFromTask(RemoveCurrentPageAsync);
        AddPagesCommand = ReactiveCommand.CreateFromTask(AddPagesAsync);
        ToggleRedactionModeCommand = ReactiveCommand.Create(ToggleRedactionMode);
        ApplyRedactionCommand = ReactiveCommand.CreateFromTask(ApplyRedactionAsync);
        RemovePendingRedactionCommand = ReactiveCommand.Create<Guid>(RemovePendingRedaction);
        ClearAllRedactionsCommand = ReactiveCommand.Create(ClearAllRedactions);
        ApplyAllRedactionsCommand = ReactiveCommand.CreateFromTask(ApplyAllRedactionsAsync);

        ApplyRedactionCommand.ThrownExceptions.Subscribe(ex =>
            _logger.LogError(ex, "ApplyRedactionCommand threw exception"));

        ToggleTextSelectionModeCommand = ReactiveCommand.Create(ToggleTextSelectionMode);
        CopyTextCommand = ReactiveCommand.CreateFromTask(CopyTextAsync);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        NextPageCommand = ReactiveCommand.CreateFromTask(NextPageAsync);
        PreviousPageCommand = ReactiveCommand.CreateFromTask(PreviousPageAsync);
        GoToPageCommand = ReactiveCommand.CreateFromTask<int>(GoToPageAsync);

        RotatePageLeftCommand = ReactiveCommand.CreateFromTask(RotatePageLeftAsync);
        RotatePageRightCommand = ReactiveCommand.CreateFromTask(RotatePageRightAsync);
        RotatePage180Command = ReactiveCommand.CreateFromTask(RotatePage180Async);

        ZoomActualSizeCommand = ReactiveCommand.Create(ZoomActualSize);
        ZoomFitWidthCommand = ReactiveCommand.Create(ZoomFitWidth);
        ZoomFitPageCommand = ReactiveCommand.Create(ZoomFitPage);

        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CloseDocumentCommand = ReactiveCommand.Create(CloseDocument);
        ExitCommand = ReactiveCommand.Create(Exit);
        LoadRecentFileCommand = ReactiveCommand.CreateFromTask<string>(LoadRecentFileAsync);

        ExportCurrentPageCommand = ReactiveCommand.CreateFromTask(ExportCurrentPageAsync);
        ExportPagesCommand = ReactiveCommand.CreateFromTask(ExportPagesAsync);
        PrintCommand = ReactiveCommand.CreateFromTask(PrintAsync);

        AboutCommand = ReactiveCommand.Create(ShowAbout);
        ShowShortcutsCommand = ReactiveCommand.Create(ShowKeyboardShortcuts);
        ShowDocumentationCommand = ReactiveCommand.Create(ShowDocumentation);
        VerifySignaturesCommand = ReactiveCommand.CreateFromTask(VerifySignaturesAsync);
        ShowPreferencesCommand = ReactiveCommand.Create(ShowPreferences);

        InitializeSearchCommands();
        InitializeScriptingCommands();

        LoadRecentFiles();
        LoadZoomPreference(); // Issue #32: Persist zoom level

        _logger.LogDebug("MainWindowViewModel initialization complete (test mode)");
    }

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        ILoggerFactory loggerFactory,
        PdfDocumentService documentService,
        PdfRenderService renderService,
        RedactionService redactionService,
        PdfTextExtractionService textExtractionService,
        PdfSearchService searchService,
        SignatureVerificationService signatureService,
        FilenameSuggestionService filenameSuggestionService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _documentService = documentService;
        _renderService = renderService;
        _redactionService = redactionService;
        _textExtractionService = textExtractionService;
        _searchService = searchService;
        _signatureService = signatureService;
        _filenameSuggestionService = filenameSuggestionService;

        // Initialize state managers
        FileState = new DocumentStateManager();
        RedactionWorkflow = new RedactionWorkflowManager();

        _logger.LogInformation("MainWindowViewModel initialized");

        PageThumbnails = new ObservableCollection<PageThumbnail>();
        ClipboardHistory = new ObservableCollection<ClipboardEntry>();

        // Commands
        _logger.LogDebug("Setting up ReactiveUI commands");
        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        SaveFileCommand = ReactiveCommand.CreateFromTask(SaveFileAsync);
        RemoveCurrentPageCommand = ReactiveCommand.CreateFromTask(RemoveCurrentPageAsync);
        AddPagesCommand = ReactiveCommand.CreateFromTask(AddPagesAsync);
        ToggleRedactionModeCommand = ReactiveCommand.Create(ToggleRedactionMode);
        ApplyRedactionCommand = ReactiveCommand.CreateFromTask(ApplyRedactionAsync);
        RemovePendingRedactionCommand = ReactiveCommand.Create<Guid>(RemovePendingRedaction);
        ClearAllRedactionsCommand = ReactiveCommand.Create(ClearAllRedactions);
        ApplyAllRedactionsCommand = ReactiveCommand.CreateFromTask(ApplyAllRedactionsAsync);

        // Subscribe to ThrownExceptions to prevent command from getting stuck
        ApplyRedactionCommand.ThrownExceptions.Subscribe(ex =>
            _logger.LogError(ex, "ApplyRedactionCommand threw exception"));

        ToggleTextSelectionModeCommand = ReactiveCommand.Create(ToggleTextSelectionMode);
        CopyTextCommand = ReactiveCommand.CreateFromTask(CopyTextAsync);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        NextPageCommand = ReactiveCommand.CreateFromTask(NextPageAsync);
        PreviousPageCommand = ReactiveCommand.CreateFromTask(PreviousPageAsync);
        GoToPageCommand = ReactiveCommand.CreateFromTask<int>(GoToPageAsync);

        // Rotation commands
        RotatePageLeftCommand = ReactiveCommand.CreateFromTask(RotatePageLeftAsync);
        RotatePageRightCommand = ReactiveCommand.CreateFromTask(RotatePageRightAsync);
        RotatePage180Command = ReactiveCommand.CreateFromTask(RotatePage180Async);

        // Zoom preset commands
        ZoomActualSizeCommand = ReactiveCommand.Create(ZoomActualSize);
        ZoomFitWidthCommand = ReactiveCommand.Create(ZoomFitWidth);
        ZoomFitPageCommand = ReactiveCommand.Create(ZoomFitPage);

        // File menu commands
        SaveAsCommand = ReactiveCommand.CreateFromTask(SaveAsAsync);
        CloseDocumentCommand = ReactiveCommand.Create(CloseDocument);
        ExitCommand = ReactiveCommand.Create(Exit);
        LoadRecentFileCommand = ReactiveCommand.CreateFromTask<string>(LoadRecentFileAsync);

        // Tools menu commands
        ExportCurrentPageCommand = ReactiveCommand.CreateFromTask(ExportCurrentPageAsync);
        ExportPagesCommand = ReactiveCommand.CreateFromTask(ExportPagesAsync);
        PrintCommand = ReactiveCommand.CreateFromTask(PrintAsync);

        // Help menu commands
        AboutCommand = ReactiveCommand.Create(ShowAbout);
        ShowShortcutsCommand = ReactiveCommand.Create(ShowKeyboardShortcuts);
        ShowDocumentationCommand = ReactiveCommand.Create(ShowDocumentation);

        // Tools menu commands
        VerifySignaturesCommand = ReactiveCommand.CreateFromTask(VerifySignaturesAsync);
        ShowPreferencesCommand = ReactiveCommand.Create(ShowPreferences);

        // Initialize search commands
        InitializeSearchCommands();

        // Initialize scripting commands (for Roslyn automation)
        InitializeScriptingCommands();

        // Load recent files and preferences
        LoadRecentFiles();
        LoadZoomPreference(); // Issue #32: Persist zoom level

        _logger.LogDebug("MainWindowViewModel initialization complete");
    }

    // Properties
    public ObservableCollection<PageThumbnail> PageThumbnails { get; }
    public ObservableCollection<ClipboardEntry> ClipboardHistory { get; }

    public Bitmap? CurrentPageImage
    {
        get => _currentPageImage;
        set => this.RaiseAndSetIfChanged(ref _currentPageImage, value);
    }

    public PdfCoreDocument? PdfCoreDocument
    {
        get => _pdfCoreDocument;
        set => this.RaiseAndSetIfChanged(ref _pdfCoreDocument, value);
    }

    public InteractionMode InteractionMode
    {
        get
        {
            if (IsRedactionMode) return InteractionMode.Redaction;
            if (IsTextSelectionMode) return InteractionMode.TextSelection;
            return InteractionMode.None;
        }
    }

    public int CurrentPage => CurrentPageIndex + 1; // 1-based for PdfViewerControl

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPageIndex, value);
            this.RaisePropertyChanged(nameof(DisplayPageNumber));
            // CurrentPage is computed (CurrentPageIndex + 1) and bound to
            // PdfViewerControl.CurrentPage in MainWindow.axaml. Without this
            // notification, thumbnail clicks updated the index but the viewer
            // stayed on the previous page.
            this.RaisePropertyChanged(nameof(CurrentPage));
            UpdateThumbnailSelection();
            UpdateSearchHighlights(); // Update highlights when page changes (fixes #310)
            RefreshHiddenTextHighlights();
        }
    }

    private bool _revealHiddenText;
    /// <summary>
    /// When true, scans the current page for hidden-behind-overlay text
    /// and surfaces it through <see cref="HiddenTextHighlights"/>.
    /// </summary>
    public bool RevealHiddenText
    {
        get => _revealHiddenText;
        set
        {
            this.RaiseAndSetIfChanged(ref _revealHiddenText, value);
            RefreshHiddenTextHighlights();
        }
    }

    private bool _revealRasterizedHidden;
    /// <summary>
    /// When true (and <see cref="RevealHiddenText"/> is also on), runs
    /// differential OCR on the current page in addition to the structural
    /// scan. Slower; requires the <c>tesseract</c> CLI. Catches text
    /// that's only present inside images and visually obstructed.
    /// </summary>
    public bool RevealRasterizedHidden
    {
        get => _revealRasterizedHidden;
        set
        {
            this.RaiseAndSetIfChanged(ref _revealRasterizedHidden, value);
            RefreshHiddenTextHighlights();
        }
    }

    /// <summary>
    /// Highlights to paint on top of the current page — each entry is a
    /// piece of text that the PDF still contains but has visually hidden
    /// behind an overlay. Coords are in rendered-image pixels at the
    /// current render DPI, top-left origin.
    /// </summary>
    public ObservableCollection<Models.HiddenTextHighlight> HiddenTextHighlights { get; }
        = new();

    private void RefreshHiddenTextHighlights()
    {
        HiddenTextHighlights.Clear();
        if (!_revealHiddenText) return;
        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath)) return;

        try
        {
            // Scan fresh from disk so we see exactly what a downstream
            // extractor would see; the in-memory doc may have pending
            // GUI edits we don't want to audit against.
            byte[] bytes = File.ReadAllBytes(_currentFilePath);
            using var doc = Pdfe.Core.Document.PdfDocument.Open(bytes);
            if (CurrentPageIndex < 0 || CurrentPageIndex >= doc.PageCount) return;

            var page = doc.GetPage(CurrentPageIndex + 1);
            double pageHeight = page.Height;
            double scale = CoordinateConverter.DefaultRenderDpi / (double)CoordinateConverter.PdfPointsPerInch;

            // Pass 1: structural — fast, exact characters, never wrong.
            foreach (var h in Pdfe.Core.Text.Segmentation.HiddenTextDetector.ScanPage(
                page, CurrentPageIndex + 1))
            {
                AddHighlight(h.Text, h.BoundingBox, h.HiddenBy, scale, pageHeight,
                    Models.HiddenTextSource.Structural);
            }

            // Pass 2: differential OCR — slow, opt-in, recovers text
            // hidden inside rasters. Only runs when the user explicitly
            // asks for it AND the tesseract CLI is reachable.
            if (_revealRasterizedHidden)
            {
                var ocr = new Pdfe.Ocr.PdfOcrService();
                if (ocr.IsAvailable())
                {
                    var auditor = new Pdfe.Ocr.DifferentialOcrAuditor(ocr);
                    foreach (var h in auditor.ScanPage(bytes, CurrentPageIndex + 1))
                    {
                        AddHighlight(h.Text, h.BoundingBox,
                            $"raster (OCR conf {h.Confidence:F2})", scale, pageHeight,
                            Models.HiddenTextSource.DifferentialOcr);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "RevealRasterizedHidden requested but `tesseract` CLI is not available; skipping differential-OCR pass.");
                }
            }

            _logger.LogInformation(
                "Reveal-hidden-text: {Count} leak(s) on page {Page}",
                HiddenTextHighlights.Count, CurrentPageIndex + 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hidden-text scan failed for page {Page}", CurrentPageIndex + 1);
        }
    }

    private void AddHighlight(
        string text,
        Pdfe.Core.Document.PdfRectangle bbox,
        string source,
        double scale,
        double pageHeight,
        Models.HiddenTextSource severity)
    {
        // PDF points (bottom-left origin) → rendered-image pixels
        // (top-left origin) at the render DPI.
        double left = bbox.Left * scale;
        double top = (pageHeight - bbox.Top) * scale;
        double width = (bbox.Right - bbox.Left) * scale;
        double height = (bbox.Top - bbox.Bottom) * scale;
        HiddenTextHighlights.Add(new Models.HiddenTextHighlight(
            text, new Rect(left, top, width, height), source, severity));
    }

    public int TotalPages => _documentService.PageCount;

    public int DisplayPageNumber => CurrentPageIndex + 1;

    /// <summary>
    /// Context-aware text for Save button.
    /// Shows "Save Redacted Version" when working on original file with changes.
    /// Shows "Save" when working on redacted version or when no changes.
    /// </summary>
    public string SaveButtonText => FileState.GetSaveButtonText();

    /// <summary>
    /// Status bar text showing pending redaction count and file type.
    /// Updates dynamically as user marks/applies redactions.
    /// </summary>
    public string StatusBarText
    {
        get
        {
            if (RedactionWorkflow.PendingRedactions.Count > 0)
                return $"{RedactionWorkflow.PendingRedactions.Count} areas marked";
            if (FileState.IsOriginalFile)
                return "Ready";
            return FileState.FileType;
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            var oldValue = _zoomLevel;
            this.RaiseAndSetIfChanged(ref _zoomLevel, value);
            // Issue #32: Save zoom preference on user change (skip during auto-reset)
            if (!_skipZoomSave && Math.Abs(oldValue - value) > 0.001)
            {
                SaveZoomPreference();
            }
        }
    }

    /// <summary>Visibility of the left thumbnail strip. Toggled from View menu.</summary>
    public bool IsThumbnailsSidebarVisible
    {
        get => _isThumbnailsSidebarVisible;
        set => this.RaiseAndSetIfChanged(ref _isThumbnailsSidebarVisible, value);
    }

    /// <summary>Visibility of the right clipboard / pending-redactions sidebar.</summary>
    public bool IsClipboardSidebarVisible
    {
        get => _isClipboardSidebarVisible;
        set => this.RaiseAndSetIfChanged(ref _isClipboardSidebarVisible, value);
    }

    public void ToggleThumbnailsSidebar() =>
        IsThumbnailsSidebarVisible = !IsThumbnailsSidebarVisible;

    public void ToggleClipboardSidebar() =>
        IsClipboardSidebarVisible = !IsClipboardSidebarVisible;

    public string DocumentName => string.IsNullOrEmpty(_currentFilePath)
        ? "No document open"
        : System.IO.Path.GetFileName(_currentFilePath);

    public bool IsRedactionMode
    {
        get => _isRedactionMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _isRedactionMode, value);
            this.RaisePropertyChanged(nameof(CurrentModeText));
            this.RaisePropertyChanged(nameof(InteractionMode));
        }
    }

    public Rect CurrentRedactionArea
    {
        get => _currentRedactionArea;
        set => this.RaiseAndSetIfChanged(ref _currentRedactionArea, value);
    }

    public bool IsTextSelectionMode
    {
        get => _isTextSelectionMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _isTextSelectionMode, value);
            // Turn off redaction mode when entering text selection mode
            if (value && _isRedactionMode)
                IsRedactionMode = false;
            this.RaisePropertyChanged(nameof(CurrentModeText));
            this.RaisePropertyChanged(nameof(InteractionMode));
        }
    }

    public Rect CurrentTextSelectionArea
    {
        get => _currentTextSelectionArea;
        set => this.RaiseAndSetIfChanged(ref _currentTextSelectionArea, value);
    }

    public string SelectedText
    {
        get => _selectedText;
        set => this.RaiseAndSetIfChanged(ref _selectedText, value);
    }

    public bool DebugVerifyRedaction
    {
        get => _debugVerifyRedaction;
        set => this.RaiseAndSetIfChanged(ref _debugVerifyRedaction, value);
    }

    public int RenderCacheMax
    {
        get => _renderCacheMax;
        set
        {
            this.RaiseAndSetIfChanged(ref _renderCacheMax, value);
            _renderService.MaxCacheEntries = Math.Max(1, value);
        }
    }

    public string StatusText => _documentService.IsDocumentLoaded
        ? $"Page {CurrentPageIndex + 1} of {TotalPages} - Zoom: {ZoomLevel:P0}"
        : "No document loaded";

    public string OperationStatus
    {
        get => _operationStatus;
        set => this.RaiseAndSetIfChanged(ref _operationStatus, value);
    }

    public ObservableCollection<string> RecentFiles
    {
        get => _recentFiles;
        set => this.RaiseAndSetIfChanged(ref _recentFiles, value);
    }

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public ObservableCollection<Avalonia.Controls.MenuItem> RecentFileMenuItems
    {
        get
        {
            var items = new ObservableCollection<Avalonia.Controls.MenuItem>();

            if (RecentFiles.Count == 0)
            {
                // Show placeholder when no recent files
                var noFilesItem = new Avalonia.Controls.MenuItem
                {
                    Header = "No recent files",
                    IsEnabled = false
                };
                items.Add(noFilesItem);
                return items;
            }

            foreach (var filePath in RecentFiles)
            {
                var menuItem = new Avalonia.Controls.MenuItem
                {
                    Header = System.IO.Path.GetFileName(filePath), // Show filename only
                    Command = LoadRecentFileCommand,
                    CommandParameter = filePath
                };
                // Set tooltip to show full path
                Avalonia.Controls.ToolTip.SetTip(menuItem, filePath);
                items.Add(menuItem);
            }
            return items;
        }
    }

    // Viewport dimensions (set by View for accurate zoom calculations).
    // Re-applies the active fit mode when they change so window resizes
    // keep the page snapped to the viewport.
    public double ViewportWidth
    {
        get => _viewportWidth;
        set
        {
            if (Math.Abs(_viewportWidth - value) < 0.5) return;
            this.RaiseAndSetIfChanged(ref _viewportWidth, value);
            ReapplyFitModeIfNeeded();
        }
    }

    public double ViewportHeight
    {
        get => _viewportHeight;
        set
        {
            if (Math.Abs(_viewportHeight - value) < 0.5) return;
            this.RaiseAndSetIfChanged(ref _viewportHeight, value);
            ReapplyFitModeIfNeeded();
        }
    }

    private void ReapplyFitModeIfNeeded()
    {
        if (PdfCoreDocument == null) return;
        switch (_zoomFitMode)
        {
            case ZoomFitMode.FitWidth:
                ZoomFitWidthInternal(latch: true);
                break;
            case ZoomFitMode.FitPage:
                ZoomFitPageInternal(latch: true);
                break;
        }
    }

    // Search highlight rectangles for current page (in screen coordinates)
    public ObservableCollection<Rect> CurrentPageSearchHighlights
    {
        get => _currentPageSearchHighlights;
        set => this.RaiseAndSetIfChanged(ref _currentPageSearchHighlights, value);
    }

    // Mode indicator for status bar
    public string CurrentModeText
    {
        get
        {
            if (IsRedactionMode) return "🔴 Redaction Mode";
            if (IsTextSelectionMode) return "📝 Text Selection Mode";
            return "👆 View Mode";
        }
    }

    // Commands
    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveFileCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCurrentPageCommand { get; }
    public ReactiveCommand<Unit, Unit> AddPagesCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleRedactionModeCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyRedactionCommand { get; }
    public ReactiveCommand<Guid, Unit> RemovePendingRedactionCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearAllRedactionsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyAllRedactionsCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleTextSelectionModeCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyTextCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }
    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }
    public ReactiveCommand<int, Unit> GoToPageCommand { get; }

    // Rotation Commands
    public ReactiveCommand<Unit, Unit> RotatePageLeftCommand { get; }
    public ReactiveCommand<Unit, Unit> RotatePageRightCommand { get; }
    public ReactiveCommand<Unit, Unit> RotatePage180Command { get; }

    // Zoom Preset Commands
    public ReactiveCommand<Unit, Unit> ZoomActualSizeCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomFitWidthCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomFitPageCommand { get; }

    // File Menu Commands
    public ReactiveCommand<Unit, Unit> SaveAsCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseDocumentCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    public ReactiveCommand<string, Unit> LoadRecentFileCommand { get; } = null!;

    // Tools Menu Commands
    public ReactiveCommand<Unit, Unit> ExportCurrentPageCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportPagesCommand { get; }
    public ReactiveCommand<Unit, Unit> PrintCommand { get; }
    public ReactiveCommand<Unit, Unit> VerifySignaturesCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowPreferencesCommand { get; }

    // Help Menu Commands
    public ReactiveCommand<Unit, Unit> AboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowShortcutsCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowDocumentationCommand { get; }

    // Document status property
    public bool IsDocumentLoaded => _documentService.IsDocumentLoaded;

    // Command Implementations
    private async Task OpenFileAsync()
    {
        _logger.LogInformation("Open file command triggered");

        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            _logger.LogWarning("Storage provider unavailable, cannot show Open dialog");
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF Files")
                {
                    Patterns = new[] { "*.pdf" }
                }
            }
        });

        if (files.Count == 0)
        {
            _logger.LogInformation("Open dialog cancelled");
            return;
        }

        var filePath = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("Selected file has no local path");
            return;
        }

        await LoadDocumentAsync(filePath);
    }

    public async Task LoadDocumentAsync(string filePath)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        if (!System.IO.File.Exists(filePath))
        {
            throw new FileNotFoundException($"PDF file not found: {filePath}", filePath);
        }

        _logger.LogInformation(">>> STEP 1: LoadDocumentAsync START for: {FilePath}", filePath);

        try
        {
            _logger.LogInformation(">>> STEP 2: Clearing previous document state");
            // Clear ALL state from previous document before loading new one
            CurrentRedactionArea = new Rect();
            CurrentTextSelectionArea = new Rect();
            RedactionWorkflow.Reset();
            ClipboardHistory.Clear();
            PageThumbnails.Clear();
            _renderService.ClearCache();
            _hasInMemoryModifications = false;

            // Exit redaction mode if active
            if (IsRedactionMode)
            {
                IsRedactionMode = false;
            }

            _logger.LogInformation(">>> STEP 3: Setting _currentFilePath and FileState");
            _currentFilePath = filePath;
            FileState.SetDocument(filePath);

            _logger.LogInformation(">>> STEP 4: RaisePropertyChanged(DocumentName)");
            this.RaisePropertyChanged(nameof(DocumentName));
            this.RaisePropertyChanged(nameof(StatusBarText));

            // Stage label for the status bar — empty string clears it. Rendering
            // text + the parser parse both happen off the UI thread, but the
            // user otherwise has no signal that anything is in progress.
            OperationStatus = "Opening PDF…";

            // Open the document on a worker thread (the parser walks the
            // entire xref + catalog up front; on a 455-page book that's
            // hundreds of milliseconds we shouldn't pay on the dispatcher).
            // Both services need their own instance — they have separate
            // ownership lifecycles — but the parses run in parallel.
            _logger.LogInformation(">>> STEP 5: Loading Pdfe.Core document (parallel parse)");
            var docServiceLoad = Task.Run(() => _documentService.LoadDocument(filePath));
            var coreDocLoad = Task.Run(() => Pdfe.Core.Document.PdfDocument.Open(filePath));
            await Task.WhenAll(docServiceLoad, coreDocLoad);
            PdfCoreDocument = await coreDocLoad;
            _logger.LogInformation(">>> STEP 5: Both document instances loaded");

            _logger.LogInformation(">>> STEP 6: Setting CurrentPageIndex = 0");
            CurrentPageIndex = 0;

            // Render the current page FIRST — the user wants to see
            // something. The 455 thumbnails for the sidebar can come
            // afterwards. Pre-fix the order was reversed and on a long
            // document we'd block on N×Task.Run thumbnail jobs before the
            // user saw page 1, saturating the threadpool the main render
            // also needed.
            OperationStatus = $"Rendering page 1 of {TotalPages}…";
            _logger.LogInformation(">>> STEP 7: Rendering current page");
            await RenderCurrentPageAsync();
            _logger.LogInformation(">>> STEP 7: Current page rendered successfully");

            // Auto-fit-width on document open so the page is never wider than
            // the central pane (otherwise it scrolls behind the right sidebar
            // on default windows). The fit-mode latch in the ZoomFit* path
            // also keeps it fitted on subsequent window resizes.
            ReapplyFitModeIfNeeded();

            // Kick off thumbnail generation on the threadpool but DON'T
            // await it — the sidebar populates progressively and the user
            // can already use the document. Track the task so we don't
            // tear down state mid-flight.
            OperationStatus = $"Loading thumbnails (1/{TotalPages})…";
            _logger.LogInformation(">>> STEP 8: Starting thumbnail generation (background)");
            _ = LoadPageThumbnailsAsync().ContinueWith(_ =>
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    OperationStatus = string.Empty;
                else
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        OperationStatus = string.Empty);
            }, TaskScheduler.Default);

            _logger.LogInformation(">>> STEP 9: RaisePropertyChanged(TotalPages)");
            this.RaisePropertyChanged(nameof(TotalPages));

            _logger.LogInformation(">>> STEP 10: RaisePropertyChanged(StatusText)");
            this.RaisePropertyChanged(nameof(StatusText));

            _logger.LogInformation(">>> STEP 11: RaisePropertyChanged(IsDocumentLoaded)");
            this.RaisePropertyChanged(nameof(IsDocumentLoaded));

            _logger.LogInformation(">>> STEP 12: Adding to recent files");
            AddToRecentFiles(filePath);

            _logger.LogInformation(">>> STEP 13: LoadDocumentAsync COMPLETE. Total pages: {PageCount}", TotalPages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!!! ERROR in LoadDocumentAsync at some step: {FilePath}", filePath);
            _logger.LogError("!!! Exception Type: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("!!! Exception Message: {Message}", ex.Message);

            // Clear document state on failure
            _currentFilePath = string.Empty;
            FileState.Reset();
            OperationStatus = string.Empty;

            // Determine user-friendly error message
            string userMessage;
            if (ex.Message.Contains("owner password") || ex.Message.Contains("password is required"))
            {
                userMessage = "This PDF is password-protected and cannot be opened for editing.\n\nPlease use the original application to remove password protection, or open a different file.";
            }
            else if (ex.Message.Contains("encrypted"))
            {
                userMessage = "This PDF is encrypted and cannot be opened.\n\nPlease provide an unencrypted version of the file.";
            }
            else
            {
                userMessage = $"Failed to open PDF:\n\n{ex.Message}";
            }

            // Show error dialog to user (StatusBarText will show "Ready" from FileState.Reset())
            this.RaisePropertyChanged(nameof(StatusBarText));
            await ShowErrorDialogAsync("Cannot Open PDF", userMessage);
        }
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        try
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow != null)
            {
                var dialog = new Avalonia.Controls.Window
                {
                    Title = title,
                    Width = 450,
                    Height = 200,
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                    CanResize = false,
                    Content = new Avalonia.Controls.StackPanel
                    {
                        Margin = new Avalonia.Thickness(20),
                        Spacing = 15,
                        Children =
                        {
                            new Avalonia.Controls.TextBlock
                            {
                                Text = message,
                                TextWrapping = Avalonia.Media.TextWrapping.Wrap
                            },
                            new Avalonia.Controls.Button
                            {
                                Content = "OK",
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                Width = 80
                            }
                        }
                    }
                };

                // Wire up the OK button to close the dialog
                if (dialog.Content is Avalonia.Controls.StackPanel panel)
                {
                    var button = panel.Children.OfType<Avalonia.Controls.Button>().FirstOrDefault();
                    if (button != null)
                    {
                        button.Click += (s, e) => dialog.Close();
                    }
                }

                await dialog.ShowDialog(mainWindow);
            }
        }
        catch (Exception dialogEx)
        {
            _logger.LogError(dialogEx, "Failed to show error dialog");
        }
    }

    private async Task SaveFileAsync()
    {
        _logger.LogInformation("Save command triggered");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot save: No document loaded");
            return;
        }

        // CRITICAL: If working on original file with unsaved changes, FORCE "Save As" workflow
        // This prevents accidental overwriting of original files
        if (FileState.IsOriginalFile && FileState.HasUnsavedChanges)
        {
            _logger.LogInformation("Original file with changes detected - triggering Save As workflow");
            await ApplyAllRedactionsAsync();
            return;
        }

        // Safe to save directly - either redacted version or no changes
        try
        {
            _documentService.SaveDocument();
            _logger.LogInformation("Document saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving document");
        }

        await Task.CompletedTask;
    }

    private async Task RemoveCurrentPageAsync()
    {
        _logger.LogInformation("Remove page command triggered. Current page: {PageIndex}", CurrentPageIndex);

        if (!_documentService.IsDocumentLoaded || TotalPages <= 1)
        {
            _logger.LogWarning("Cannot remove page: No document loaded or only one page remaining");
            return;
        }

        try
        {
            _documentService.RemovePage(CurrentPageIndex);

            // Adjust current page if needed
            if (CurrentPageIndex >= TotalPages)
            {
                CurrentPageIndex = TotalPages - 1;
                _logger.LogDebug("Adjusted current page index to {PageIndex}", CurrentPageIndex);
            }

            _logger.LogDebug("Reloading thumbnails and rendering current page");
            await LoadPageThumbnailsAsync();
            await RenderCurrentPageAsync();

            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));

            _logger.LogInformation("Page removed successfully. Remaining pages: {PageCount}", TotalPages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing page");
        }
    }

    private async Task AddPagesAsync()
    {
        _logger.LogInformation("Add pages command triggered");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot add pages: No document loaded");
            return;
        }

        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            _logger.LogWarning("Storage provider unavailable, cannot show Add Pages dialog");
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select PDF to Add Pages From",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF Files")
                {
                    Patterns = new[] { "*.pdf" }
                }
            }
        });

        if (files.Count == 0)
        {
            _logger.LogInformation("Add pages dialog cancelled");
            return;
        }

        var filePath = files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("Selected file has no local path");
            return;
        }

        await AddPagesFromFileAsync(filePath);
    }

    public async Task AddPagesFromFileAsync(string sourcePdfPath)
    {
        if (!_documentService.IsDocumentLoaded)
            return;

        try
        {
            _documentService.AddPagesFromPdf(sourcePdfPath);
            await LoadPageThumbnailsAsync();
            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding pages");
        }
    }

    private void ToggleRedactionMode()
    {
        IsRedactionMode = !IsRedactionMode;
        // Turn off text selection mode when entering redaction mode
        if (IsRedactionMode && _isTextSelectionMode)
            IsTextSelectionMode = false;
    }

    private void ToggleTextSelectionMode()
    {
        _logger.LogInformation("Toggle text selection mode. Current: {Current}", IsTextSelectionMode);
        IsTextSelectionMode = !IsTextSelectionMode;

        if (!IsTextSelectionMode)
        {
            // Clear selection when exiting mode
            CurrentTextSelectionArea = new Rect();
            SelectedText = string.Empty;
        }
    }

    private async Task CopyTextAsync()
    {
        _logger.LogInformation("Copy text command triggered");

        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("Cannot copy text: No document loaded");
            return;
        }

        try
        {
            string textToCopy;

            // If there's a selection area, extract text from that area
            if (CurrentTextSelectionArea.Width > 0 && CurrentTextSelectionArea.Height > 0)
            {
                _logger.LogInformation(
                    "Extracting text from selection area on page {PageIndex}: ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                    CurrentPageIndex + 1,
                    CurrentTextSelectionArea.X, CurrentTextSelectionArea.Y,
                    CurrentTextSelectionArea.Width, CurrentTextSelectionArea.Height);

                textToCopy = _textExtractionService.ExtractTextFromArea(
                    _currentFilePath, CurrentPageIndex, CurrentTextSelectionArea);
            }
            else
            {
                // No selection, extract all text from current page
                _logger.LogInformation("Extracting all text from page {PageIndex}", CurrentPageIndex + 1);
                textToCopy = _textExtractionService.ExtractTextFromPage(_currentFilePath, CurrentPageIndex);
            }

            if (!string.IsNullOrEmpty(textToCopy))
            {
                // Log the actual text being copied (first 200 chars for preview)
                var textPreview = textToCopy.Length > 200 ? textToCopy.Substring(0, 200) + "..." : textToCopy;
                _logger.LogInformation(
                    "TEXT EXTRACTED ({Length} chars): \"{Text}\"",
                    textToCopy.Length, textPreview);

                // Copy to clipboard using TopLevel
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (topLevel?.Clipboard != null)
                {
                    await topLevel.Clipboard.SetTextAsync(textToCopy);
                    SelectedText = textToCopy;
                    _logger.LogInformation("✓ Successfully copied {Length} characters to clipboard", textToCopy.Length);

                    // Add to clipboard history
                    var clipboardEntry = new ClipboardEntry
                    {
                        Text = textToCopy,
                        Timestamp = DateTime.Now,
                        PageNumber = CurrentPageIndex + 1
                    };

                    // Add to beginning of list (most recent first)
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ClipboardHistory.Insert(0, clipboardEntry);

                        // Keep only last 20 entries
                        while (ClipboardHistory.Count > 20)
                        {
                            ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
                        }
                    });
                }
                else
                {
                    _logger.LogWarning("Clipboard not available");
                }
            }
            else
            {
                _logger.LogWarning("No text extracted from selection - area may not contain any text");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying text");
        }
    }

    /// <summary>
    /// Mark a redaction area (mark-then-apply workflow) - adds to pending list
    /// </summary>
    private void MarkRedactionArea()
    {
        _logger.LogInformation(">>> MarkRedactionArea START. Area=({X:F2},{Y:F2},{W:F2}x{H:F2})",
            CurrentRedactionArea.X, CurrentRedactionArea.Y, CurrentRedactionArea.Width, CurrentRedactionArea.Height);

        if (!IsRedactionMode || CurrentRedactionArea.Width <= 0 || CurrentRedactionArea.Height <= 0)
        {
            _logger.LogWarning("MarkRedactionArea returning early: IsRedactionMode={Mode}, Width={W}, Height={H}",
                IsRedactionMode, CurrentRedactionArea.Width, CurrentRedactionArea.Height);
            return;
        }

        // Extract preview text for the pending redaction
        string previewText = string.Empty;
        if (!string.IsNullOrEmpty(_currentFilePath))
        {
            try
            {
                previewText = _textExtractionService.ExtractTextFromArea(
                    _currentFilePath, CurrentPageIndex, CurrentRedactionArea);
                _logger.LogInformation("Preview text extracted: '{Text}'", previewText);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not extract preview text");
            }
        }

        // Add to pending redactions
        RedactionWorkflow.MarkArea(CurrentPageIndex + 1, CurrentRedactionArea, previewText);
        FileState.PendingRedactionsCount = RedactionWorkflow.PendingCount;
        this.RaisePropertyChanged(nameof(SaveButtonText));
        this.RaisePropertyChanged(nameof(StatusBarText));

        _logger.LogInformation("Redaction marked. Total pending: {Count}", RedactionWorkflow.PendingCount);
        _logger.LogInformation("DEBUG: RedactionWorkflow.PendingRedactions.Count = {Count}", RedactionWorkflow.PendingRedactions.Count);

        // Clear the current selection
        CurrentRedactionArea = default;
    }

    /// <summary>
    /// Remove a pending redaction by ID
    /// </summary>
    private void RemovePendingRedaction(Guid id)
    {
        _logger.LogInformation("Removing pending redaction: {Id}", id);

        if (RedactionWorkflow.RemovePending(id))
        {
            FileState.PendingRedactionsCount = RedactionWorkflow.PendingCount;
            this.RaisePropertyChanged(nameof(SaveButtonText));
            _logger.LogInformation("Pending redaction removed. Remaining: {Count}", RedactionWorkflow.PendingCount);
        }
        else
        {
            _logger.LogWarning("Could not find pending redaction with ID: {Id}", id);
        }
    }

    /// <summary>
    /// Clear all pending redactions
    /// </summary>
    private void ClearAllRedactions()
    {
        _logger.LogInformation("Clearing all pending redactions. Count: {Count}", RedactionWorkflow.PendingCount);

        RedactionWorkflow.ClearPending();
        FileState.PendingRedactionsCount = 0;
        this.RaisePropertyChanged(nameof(SaveButtonText));
        this.RaisePropertyChanged(nameof(StatusBarText));

        _logger.LogInformation("All pending redactions cleared");
    }

    /// <summary>
    /// Apply all pending redactions to create a redacted version of the PDF
    /// </summary>
    private async Task ApplyAllRedactionsAsync()
    {
        _logger.LogInformation("ApplyAllRedactionsAsync START. Pending count: {Count}", RedactionWorkflow.PendingCount);

        if (RedactionWorkflow.PendingCount == 0)
        {
            _logger.LogWarning("No pending redactions to apply");
            await ShowMessageDialog("No Redactions", "There are no pending redactions to apply.");
            return;
        }

        if (string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("No document loaded");
            await ShowMessageDialog("No Document", "Please open a PDF document first.");
            return;
        }

        try
        {
            // Generate suggested filename
            var suggestedPath = _filenameSuggestionService.SuggestRedactedFilename(_currentFilePath);

            // Show file picker to choose save location
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                _logger.LogError("Could not get main window for dialog");
                return;
            }

            var saveFile = await ShowSaveRedactedFileDialog(mainWindow, suggestedPath);
            if (saveFile == null)
            {
                _logger.LogInformation("User cancelled save file picker");
                return;
            }

            var saveFilePath = saveFile.Path.LocalPath;

            _logger.LogInformation("Applying {Count} redactions to create: {Path}", RedactionWorkflow.PendingCount, saveFilePath);

            // Get current document
            var document = _documentService.GetCurrentDocument();
            if (document == null)
            {
                _logger.LogError("Document is null");
                return;
            }

            // Apply each pending redaction
            foreach (var pending in RedactionWorkflow.PendingRedactions.ToList())
            {
                _logger.LogInformation("Applying redaction on page {Page}", pending.PageNumber);

                // pending.PageNumber is 1-based (for display), convert to 0-based for array access
                var pageIndex = pending.PageNumber - 1;
                var page = document.Pages[pageIndex];

                // pending.Area is in 150 DPI image pixels (screen coordinates)
                _redactionService.RedactArea(page, pending.Area, renderDpi: 150);
            }

            // Save the redacted document
            _logger.LogInformation("Saving redacted PDF to: {Path}", saveFilePath);
            document.Save(saveFilePath);

            // Document saved - clear in-memory modification flag
            _hasInMemoryModifications = false;

            // Move redactions to applied list
            RedactionWorkflow.MoveToApplied();
            FileState.PendingRedactionsCount = 0;
            this.RaisePropertyChanged(nameof(SaveButtonText));
            this.RaisePropertyChanged(nameof(StatusBarText));

            _logger.LogInformation("Redacted PDF saved successfully");

            // Exit redaction mode before reloading
            if (IsRedactionMode)
            {
                ToggleRedactionMode();
            }

            // Reload the saved document so text extraction and rendering work from the redacted file
            // This ensures the GUI shows the redacted content and text selection can't select removed text
            _logger.LogInformation("Reloading saved document: {Path}", saveFilePath);
            await LoadDocumentAsync(saveFilePath);

            await ShowMessageDialog("Success", $"Redacted PDF saved to:\n{saveFilePath}\n\nOriginal file preserved. Document reloaded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying all redactions");
            await ShowMessageDialog("Error", $"Failed to apply redactions: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply redaction immediately (legacy immediate-apply workflow)
    /// See issue #19: Implement "Apply All Redactions" button for mark-then-apply workflow
    /// </summary>
    private async Task ApplyRedactionAsync()
    {
        _logger.LogInformation(">>> ApplyRedactionAsync START. IsRedactionMode={Mode}, Area=({X:F2},{Y:F2},{W:F2}x{H:F2})",
            IsRedactionMode, CurrentRedactionArea.X, CurrentRedactionArea.Y, CurrentRedactionArea.Width, CurrentRedactionArea.Height);

        // NEW: In mark-then-apply mode, just mark the area instead of applying
        if (IsRedactionMode && CurrentRedactionArea.Width > 0 && CurrentRedactionArea.Height > 0)
        {
            MarkRedactionArea();
            return;
        }

        if (!IsRedactionMode || CurrentRedactionArea.Width <= 0 || CurrentRedactionArea.Height <= 0)
        {
            _logger.LogWarning("ApplyRedactionAsync returning early: IsRedactionMode={Mode}, Width={W}, Height={H}",
                IsRedactionMode, CurrentRedactionArea.Width, CurrentRedactionArea.Height);
            return;
        }

        // Capture the area before we clear it
        var areaToRedact = CurrentRedactionArea;

        try
        {
            var document = _documentService.GetCurrentDocument();
            if (document == null)
            {
                _logger.LogWarning("ApplyRedactionAsync: document is null");
                return;
            }

            // IMPORTANT: Extract text BEFORE redacting so we can show what was removed
            string redactedText = string.Empty;
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                try
                {
                    redactedText = _textExtractionService.ExtractTextFromArea(
                        _currentFilePath, CurrentPageIndex, areaToRedact);
                    _logger.LogInformation("Text to be redacted: '{Text}'", redactedText);
                }
                catch (Exception textEx)
                {
                    _logger.LogWarning(textEx, "Could not extract text before redaction");
                }
            }

            _logger.LogInformation("Applying redaction (selection area: {X:F2},{Y:F2},{W:F2}x{H:F2})",
                areaToRedact.X, areaToRedact.Y, areaToRedact.Width, areaToRedact.Height);

            var page = document.Pages[CurrentPageIndex];
            // UI selections are in rendered image pixels at the render DPI (150)
            _redactionService.RedactArea(page, areaToRedact, CoordinateConverter.DefaultRenderDpi);

            // Mark document as modified in-memory (render cache must use in-memory stream)
            _hasInMemoryModifications = true;

            // Add redacted text to clipboard history (so user can see what was removed)
            if (!string.IsNullOrWhiteSpace(redactedText))
            {
                var clipboardEntry = new ClipboardEntry
                {
                    Text = redactedText,
                    Timestamp = DateTime.Now,
                    PageNumber = CurrentPageIndex + 1,
                    IsRedacted = true
                };

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ClipboardHistory.Insert(0, clipboardEntry);

                    // Keep only last 20 entries
                    while (ClipboardHistory.Count > 20)
                    {
                        ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
                    }
                });

                _logger.LogInformation("Added redacted text to clipboard history: '{Text}'", redactedText);
            }

            // ===== DEBUG MODE: Verify redaction immediately =====
            // TEMPORARILY DISABLED: document.Save() in verification makes document read-only
            /*
            if (DebugVerifyRedaction)
            {
                _logger.LogInformation("━━━━━ DEBUG MODE: Verifying redaction immediately ━━━━━");
                await DebugVerifyRedactionAsync(areaToRedact, redactedText);
                _logger.LogInformation("━━━━━ DEBUG MODE: Verification complete ━━━━━");
            }
            */

            _logger.LogInformation("Redaction applied to in-memory document, now re-rendering page...");

            // Render the page from the in-memory document to show the redaction
            // Copy stream to memory to avoid disposal issues
            var docStream = _documentService.GetCurrentDocumentAsStream();
            if (docStream != null)
            {
                try
                {
                    // Copy to MemoryStream to avoid stream disposal issues
                    var memoryStream = new System.IO.MemoryStream();
                    await docStream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                    docStream.Dispose();

                    var skBitmap = await _renderService.RenderPageFromStreamAsync(memoryStream, CurrentPageIndex);
                    if (skBitmap != null)
                    {
                        CurrentPageImage = ToAvaloniaBitmap(skBitmap);
                        _logger.LogInformation("Page re-rendered successfully after redaction");
                    }
                    else
                    {
                        _logger.LogWarning("RenderPageFromStreamAsync returned null");
                    }
                }
                catch (Exception renderEx)
                {
                    _logger.LogError(renderEx, "Error rendering page after redaction");
                }
            }
            else
            {
                _logger.LogWarning("GetCurrentDocumentAsStream returned null");
            }

            _logger.LogInformation("Redaction complete - draw another selection or click 'Redact Mode' to exit.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying redaction");
        }
        finally
        {
            // Always clear the selection so user can draw another one
            // Stay in redaction mode to allow multiple redactions
            CurrentRedactionArea = new Rect();
            _logger.LogInformation("<<< ApplyRedactionAsync END. Selection cleared, ready for next redaction.");
        }
    }

    /// <summary>
    /// Debug mode: Verify that redaction actually removed text from the in-memory PDF.
    /// This saves the in-memory document to a temporary location and extracts text to verify removal.
    /// </summary>
    private Task DebugVerifyRedactionAsync(Rect redactedArea, string expectedRedactedText)
    {
        return Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("DEBUG: Starting verification of redacted area ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                    redactedArea.X, redactedArea.Y, redactedArea.Width, redactedArea.Height);

                // Save in-memory document to temporary file for verification
                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"debug_redaction_verify_{Guid.NewGuid()}.pdf");

                var document = _documentService.GetCurrentDocument();
                if (document == null)
                {
                    _logger.LogWarning("DEBUG: Cannot verify - document is null");
                    return;
                }

                _logger.LogInformation("DEBUG: Saving in-memory document to temporary file: {TempPath}", tempPath);
                document.Save(tempPath);

            // Extract text from the redacted area using the same extraction service
            _logger.LogInformation("DEBUG: Extracting text from redacted area in saved document...");
            var extractedText = _textExtractionService.ExtractTextFromArea(
                tempPath,
                CurrentPageIndex,
                redactedArea,
                CoordinateConverter.DefaultRenderDpi);

            _logger.LogInformation("DEBUG: Text extraction complete. Length: {Length} characters", extractedText.Length);
            _logger.LogInformation("DEBUG: Extracted text: '{ExtractedText}'", extractedText);

            // Check if any of the expected redacted text still exists
            bool verificationPassed = true;
            if (!string.IsNullOrWhiteSpace(expectedRedactedText))
            {
                if (extractedText.Contains(expectedRedactedText, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("DEBUG: ❌ VERIFICATION FAILED! Redacted text '{ExpectedText}' was found in extracted text!",
                        expectedRedactedText);
                    _logger.LogError("DEBUG: This means the redaction did NOT remove the text from the PDF structure!");
                    verificationPassed = false;
                }
                else
                {
                    _logger.LogInformation("DEBUG: ✓ Verification passed: Expected redacted text '{ExpectedText}' was NOT found",
                        expectedRedactedText);
                }
            }

            // Check if ANY text was extracted from the redacted area
            if (!string.IsNullOrWhiteSpace(extractedText))
            {
                _logger.LogWarning("DEBUG: ⚠ Found text in redacted area: '{Text}'", extractedText);
                _logger.LogWarning("DEBUG: This may indicate incomplete redaction or text outside the selection");
                verificationPassed = false;
            }
            else
            {
                _logger.LogInformation("DEBUG: ✓ No text found in redacted area - redaction appears successful");
            }

            // Summary
            if (verificationPassed)
            {
                _logger.LogInformation("DEBUG: ═══ VERIFICATION PASSED ═══");
                _logger.LogInformation("DEBUG: The redacted text was successfully removed from the PDF structure");
            }
            else
            {
                _logger.LogError("DEBUG: ═══ VERIFICATION FAILED ═══");
                _logger.LogError("DEBUG: Text extraction found content in the redacted area!");
                _logger.LogError("DEBUG: Expected to redact: '{Expected}'", expectedRedactedText);
                _logger.LogError("DEBUG: Actually found: '{Actual}'", extractedText);
            }

                // Cleanup temp file
                try
                {
                    System.IO.File.Delete(tempPath);
                    _logger.LogDebug("DEBUG: Deleted temporary verification file");
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DEBUG: Error during redaction verification");
            }
        });
    }

    private void ZoomIn()
    {
        // User-initiated zoom drops out of any auto-fit mode so a window
        // resize doesn't immediately undo what they just clicked.
        _zoomFitMode = ZoomFitMode.Manual;
        ZoomLevel = Math.Min(ZoomLevel * 1.25, 5.0);
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomOut()
    {
        _zoomFitMode = ZoomFitMode.Manual;
        ZoomLevel = Math.Max(ZoomLevel / 1.25, 0.25);
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomActualSize()
    {
        _logger.LogInformation("Setting zoom to actual size (100%)");
        _zoomFitMode = ZoomFitMode.Manual;
        ZoomLevel = 1.0;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomFitWidth() => ZoomFitWidthInternal(latch: true);
    private void ZoomFitPage() => ZoomFitPageInternal(latch: true);

    /// <summary>
    /// Resize zoom to fit the page width. When <paramref name="latch"/> is
    /// true the mode is recorded so subsequent viewport-size changes
    /// re-apply this fit until the user manually zooms.
    /// </summary>
    private void ZoomFitWidthInternal(bool latch)
    {
        _logger.LogInformation("Setting zoom to fit width");
        if (latch) _zoomFitMode = ZoomFitMode.FitWidth;
        if (TryGetPageDimensionsInViewerDips(out var pageW, out _) &&
            ViewportWidth > 0)
        {
            // Tiny gutter so the page edge doesn't kiss the scrollbar /
            // central-pane border. Now that the viewport measurement is
            // the *inside-the-scrollbars* width and zoom uses LayoutTransform,
            // the math doesn't need a 40-DIP fudge any more.
            const double margin = 8;
            var target = Math.Max(1.0, ViewportWidth - margin);
            ZoomLevel = Math.Clamp(target / pageW, 0.25, 5.0);
            _logger.LogDebug("Fit width: viewport={Viewport}, page={Page}, zoom={Zoom:P0}",
                ViewportWidth, pageW, ZoomLevel);
        }
        else
        {
            ZoomLevel = 1.0;
        }
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomFitPageInternal(bool latch)
    {
        _logger.LogInformation("Setting zoom to fit page");
        if (latch) _zoomFitMode = ZoomFitMode.FitPage;
        if (TryGetPageDimensionsInViewerDips(out var pageW, out var pageH) &&
            ViewportWidth > 0 && ViewportHeight > 0)
        {
            const double marginH = 8;
            const double marginV = 8;
            var targetW = Math.Max(1.0, ViewportWidth - marginH);
            var targetH = Math.Max(1.0, ViewportHeight - marginV);
            // Whichever dimension is the binding constraint wins.
            var zoom = Math.Min(targetW / pageW, targetH / pageH);
            ZoomLevel = Math.Clamp(zoom, 0.25, 5.0);
            _logger.LogDebug("Fit page: vp=({Vw}x{Vh}), pg=({Pw}x{Ph}), zoom={Zoom:P0}",
                ViewportWidth, ViewportHeight, pageW, pageH, ZoomLevel);
        }
        else
        {
            ZoomLevel = 1.0;
        }
        this.RaisePropertyChanged(nameof(StatusText));
    }

    /// <summary>
    /// Page dimensions in viewer DIPs at zoom 1.0. Reads page size from
    /// the parsed PdfCoreDocument (in PDF points) so we don't depend on
    /// the legacy <c>_currentPageImage</c> being populated, and converts
    /// to DIPs at the viewer's render DPI (the bitmap is tagged 96 DPI
    /// in WriteableBitmap so 1 bitmap-pixel = 1 DIP, and 1 page-point at
    /// our render DPI = render-DPI/72 bitmap-pixels).
    /// </summary>
    private bool TryGetPageDimensionsInViewerDips(out double widthDip, out double heightDip)
    {
        widthDip = 0; heightDip = 0;
        var doc = PdfCoreDocument;
        if (doc == null) return false;
        var pageNumber = CurrentPageIndex + 1;
        if (pageNumber < 1 || pageNumber > doc.PageCount) return false;
        var page = doc.GetPage(pageNumber);
        const double renderDpi = 120.0; // matches PdfViewerControl.DefaultRenderDpi
        widthDip  = page.Width  * (renderDpi / 72.0);
        heightDip = page.Height * (renderDpi / 72.0);
        return widthDip > 0 && heightDip > 0;
    }

    private async Task NextPageAsync()
    {
        if (CurrentPageIndex < TotalPages - 1)
        {
            CurrentPageIndex++;
            await RenderCurrentPageAsync();
        }
    }

    private async Task PreviousPageAsync()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            await RenderCurrentPageAsync();
        }
    }

    private async Task GoToPageAsync(int pageIndex)
    {
        _logger.LogInformation("Navigating to page {PageIndex}", pageIndex);

        if (pageIndex >= 0 && pageIndex < TotalPages && pageIndex != CurrentPageIndex)
        {
            CurrentPageIndex = pageIndex;
            await RenderCurrentPageAsync();
        }
    }

    private async Task RotatePageLeftAsync()
    {
        _logger.LogInformation("Rotating current page left (counter-clockwise)");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot rotate page: No document loaded");
            return;
        }

        try
        {
            _documentService.RotatePageLeft(CurrentPageIndex);
            _logger.LogInformation("Page {PageIndex} rotated left successfully", CurrentPageIndex);

            // Re-render the page to show the rotation
            await RenderCurrentPageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating page left");
        }
    }

    private async Task RotatePageRightAsync()
    {
        _logger.LogInformation("Rotating current page right (clockwise)");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot rotate page: No document loaded");
            return;
        }

        try
        {
            _documentService.RotatePageRight(CurrentPageIndex);
            _logger.LogInformation("Page {PageIndex} rotated right successfully", CurrentPageIndex);

            // Re-render the page to show the rotation
            await RenderCurrentPageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating page right");
        }
    }

    private async Task RotatePage180Async()
    {
        _logger.LogInformation("Rotating current page 180 degrees");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot rotate page: No document loaded");
            return;
        }

        try
        {
            _documentService.RotatePage180(CurrentPageIndex);
            _logger.LogInformation("Page {PageIndex} rotated 180 degrees successfully", CurrentPageIndex);

            // Re-render the page to show the rotation
            await RenderCurrentPageAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating page 180 degrees");
        }
    }

    private async Task RenderCurrentPageAsync()
    {
        _logger.LogInformation(">>> RenderCurrentPageAsync: START (hasInMemoryModifications={HasMods})", _hasInMemoryModifications);

        if (string.IsNullOrEmpty(_currentFilePath) || !_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning(">>> RenderCurrentPageAsync: Skipping (no file or document not loaded)");
            return;
        }

        // Surface a stage label in the status bar so the user has feedback
        // during the render. Keep the existing label if a thumbnail batch
        // is still in flight (it'll overwrite this anyway as it ticks).
        var existingStatus = OperationStatus;
        var renderingStatus = $"Rendering page {DisplayPageNumber} of {TotalPages}…";
        OperationStatus = renderingStatus;

        try
        {
            SkiaSharp.SKBitmap? skBitmap = null;

            // If document has in-memory modifications (e.g., applied redactions not yet saved),
            // we must render from the in-memory stream, not the file on disk.
            // This fixes the bug where redacted text was still visible until file reopen.
            if (_hasInMemoryModifications)
            {
                _logger.LogInformation(">>> RenderCurrentPageAsync: Using in-memory stream (document has unsaved modifications)");
                var docStream = _documentService.GetCurrentDocumentAsStream();
                if (docStream != null)
                {
                    try
                    {
                        var memoryStream = new System.IO.MemoryStream();
                        await docStream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;
                        docStream.Dispose();
                        skBitmap = await _renderService.RenderPageFromStreamAsync(memoryStream, CurrentPageIndex);
                    }
                    catch (Exception streamEx)
                    {
                        _logger.LogWarning(streamEx, "Failed to render from in-memory stream, falling back to file");
                    }
                }
            }

            // Fallback to file-based rendering if in-memory rendering failed or wasn't needed
            if (skBitmap == null)
            {
                _logger.LogInformation(">>> RenderCurrentPageAsync: Calling _renderService.RenderPageAsync for page {PageIndex}", CurrentPageIndex);
                skBitmap = await _renderService.RenderPageAsync(_currentFilePath, CurrentPageIndex);
            }

            using (skBitmap)
            {
                _logger.LogInformation(">>> RenderCurrentPageAsync: Converting to Avalonia bitmap");
                var avaloniaBitmap = ToAvaloniaBitmap(skBitmap);

                _logger.LogInformation(">>> RenderCurrentPageAsync: Setting CurrentPageImage");
                CurrentPageImage = avaloniaBitmap;
            }

            _logger.LogInformation(">>> RenderCurrentPageAsync: COMPLETE");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "!!! ERROR in RenderCurrentPageAsync");
            _logger.LogError("!!! Exception Type: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("!!! Exception Message: {Message}", ex.Message);
            throw;
        }
        finally
        {
            // Only clear if we set the rendering label AND nothing else
            // overwrote it during the render (the thumbnail batch keeps
            // updating as pages complete and should win).
            if (OperationStatus == renderingStatus)
                OperationStatus = existingStatus;
        }
    }

    private void UpdateThumbnailSelection()
    {
        foreach (var thumbnail in PageThumbnails)
        {
            thumbnail.IsSelected = (thumbnail.PageIndex == CurrentPageIndex);
        }
    }

    private async Task LoadPageThumbnailsAsync()
    {
        _logger.LogInformation(">>> LoadPageThumbnailsAsync: START");

        if (string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning(">>> LoadPageThumbnailsAsync: SKIP (no file)");
            return;
        }

        try
        {
            // Capture the current file path to avoid race conditions
            var filePath = _currentFilePath;

            _logger.LogInformation(">>> LoadPageThumbnailsAsync: Clearing existing thumbnails");
            PageThumbnails.Clear();

            var totalPages = TotalPages;
            _logger.LogInformation(
                ">>> LoadPageThumbnailsAsync: rendering {PageCount} thumbnails", totalPages);

            // Pre-allocate thumbnail placeholders so the sidebar shows
            // the strip immediately and individual entries fill in as
            // their renders complete.
            var placeholders = new PageThumbnail[totalPages];
            for (int i = 0; i < totalPages; i++)
            {
                placeholders[i] = new PageThumbnail { PageNumber = i + 1, PageIndex = i };
                PageThumbnails.Add(placeholders[i]);
            }

            // Pre-fix this code spawned one Task.Run per page, each of
            // which called PdfRenderService.RenderThumbnailAsync — and
            // *that* opened the PDF file from disk and parsed it from
            // scratch on every single call. On a 455-page book that
            // was 455× File.ReadAllBytes + 455× xref scan + 455× catalog
            // walk, with all those parses contending for the threadpool
            // alongside the foreground page render. UI was unusable.
            //
            // Now: open one PdfDocument per worker (capped at
            // ProcessorCount), pull page indexes from a shared queue,
            // render. Parses go from N pages to ~8.
            var concurrency = Math.Max(2, Math.Min(Environment.ProcessorCount, 8));
            var queue = new System.Collections.Concurrent.ConcurrentQueue<int>(
                Enumerable.Range(0, totalPages));
            int completed = 0;
            var workers = new List<Task>(concurrency);

            for (int w = 0; w < concurrency; w++)
            {
                workers.Add(Task.Run(async () =>
                {
                    Pdfe.Core.Document.PdfDocument? workerDoc = null;
                    Pdfe.Rendering.SkiaRenderer? renderer = null;
                    try
                    {
                        workerDoc = Pdfe.Core.Document.PdfDocument.Open(filePath);
                        renderer = new Pdfe.Rendering.SkiaRenderer();
                        var options = new Pdfe.Rendering.RenderOptions { Dpi = 36 };

                        while (queue.TryDequeue(out var pageIndex))
                        {
                            try
                            {
                                var page = workerDoc.GetPage(pageIndex + 1);
                                using var skBitmap = renderer.RenderPage(page, options);
                                if (skBitmap == null) continue;

                                // Hop to UI thread to publish. Yielding here
                                // also gives the dispatcher its turn — without
                                // a yield, a fast worker can saturate it with
                                // back-to-back InvokeAsyncs and starve the
                                // foreground render and pointer events.
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        placeholders[pageIndex].ThumbnailImage =
                                            ToAvaloniaBitmap(skBitmap);
                                        int n = System.Threading.Interlocked.Increment(
                                            ref completed);
                                        // Coarse status update — every page floods the UI.
                                        if (n == totalPages || (n & 7) == 0)
                                            OperationStatus =
                                                $"Loading thumbnails ({n}/{totalPages})…";
                                    }
                                    catch (Exception uiEx)
                                    {
                                        _logger.LogError(uiEx,
                                            "!!! ERROR setting thumbnail {PageIndex}",
                                            pageIndex);
                                    }
                                }, DispatcherPriority.Background);
                            }
                            catch (Exception renderEx)
                            {
                                _logger.LogError(renderEx,
                                    "!!! ERROR rendering thumbnail page {PageIndex}",
                                    pageIndex);
                            }
                        }
                    }
                    finally
                    {
                        workerDoc?.Dispose();
                    }
                }));
            }

            await Task.WhenAll(workers);
            _logger.LogInformation(
                ">>> LoadPageThumbnailsAsync: COMPLETE — {Count} thumbnails", totalPages);
        }
        catch (Exception ex)
        {
            // Don't rethrow — thumbnail load runs as a fire-and-forget
            // continuation in LoadDocumentAsync; an unhandled exception
            // here would crash the process via the unobserved-task hook.
            _logger.LogError(ex, "!!! ERROR in LoadPageThumbnailsAsync");
        }
    }

    // File Menu Commands

    private async Task SaveAsAsync()
    {
        _logger.LogInformation("Save As command triggered");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot save: No document loaded");
            return;
        }

        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            _logger.LogWarning("Storage provider unavailable, cannot show Save As dialog");
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save PDF As",
            DefaultExtension = "pdf",
            SuggestedFileName = string.IsNullOrWhiteSpace(DocumentName) ? "document.pdf" : DocumentName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF Files")
                {
                    Patterns = new[] { "*.pdf" }
                }
            }
        });

        if (file == null)
        {
            _logger.LogInformation("Save As dialog cancelled");
            return;
        }

        var filePath = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("Save As target has no local path");
            return;
        }

        await SaveFileAsAsync(filePath);
    }

    public async Task SaveFileAsAsync(string filePath)
    {
        _logger.LogInformation("Saving document to: {FilePath}", filePath);

        try
        {
            _documentService.SaveDocument(filePath);
            _currentFilePath = filePath;
            this.RaisePropertyChanged(nameof(DocumentName));
            _logger.LogInformation("Document saved successfully to: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving document to: {FilePath}", filePath);
        }

        await Task.CompletedTask;
    }

    private void CloseDocument()
    {
        _logger.LogInformation("Close document command triggered");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("No document to close");
            return;
        }

        try
        {
            // Close the PDF document
            _documentService.CloseDocument();

            // Clear file path
            _currentFilePath = string.Empty;

            // Clear visual state
            CurrentPageImage = null;
            PdfCoreDocument = null;
            PageThumbnails.Clear();
            _renderService.ClearCache();

            // Clear redaction state (FIX: These were persisting!)
            CurrentRedactionArea = new Rect();
            CurrentTextSelectionArea = new Rect();
            RedactionWorkflow.Reset();
            ClipboardHistory.Clear();
            _hasInMemoryModifications = false;

            // Clear search state
            SearchText = string.Empty;
            SearchMatches.Clear();
            CurrentSearchMatchIndex = -1;
            IsSearchVisible = false;

            // Exit redaction mode if active
            if (IsRedactionMode)
            {
                IsRedactionMode = false;
            }

            // Reset navigation state
            CurrentPageIndex = 0;

            // Reset zoom to default (skip saving - user's preference should persist)
            _skipZoomSave = true;
            ZoomLevel = 1.0;
            _skipZoomSave = false;

            // Notify UI of all state changes
            this.RaisePropertyChanged(nameof(DocumentName));
            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(StatusBarText));
            this.RaisePropertyChanged(nameof(IsDocumentLoaded));
            this.RaisePropertyChanged(nameof(CurrentRedactionArea));
            this.RaisePropertyChanged(nameof(CurrentTextSelectionArea));
            this.RaisePropertyChanged(nameof(IsRedactionMode));
            this.RaisePropertyChanged(nameof(SaveButtonText));

            _logger.LogInformation("Document closed successfully - all state cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing document");
        }
    }

    private void Exit()
    {
        _logger.LogInformation("Exit command triggered");

        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;

        if (lifetime != null)
        {
            lifetime.Shutdown();
        }
    }

    private async Task LoadRecentFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("Recent file path is empty");
            return;
        }

        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogWarning("Recent file not found: {FilePath}", filePath);
            // Issue #25: Remove deleted file from recent files list
            RemoveFromRecentFiles(filePath);
            return;
        }

        await LoadDocumentAsync(filePath);
    }

    // Tools Menu Commands

    private async Task ExportCurrentPageAsync()
    {
        _logger.LogInformation("Export current page command triggered (page {PageNumber})", CurrentPageIndex + 1);

        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("Cannot export: No document loaded");
            return;
        }

        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            _logger.LogWarning("Storage provider unavailable, cannot show Save dialog");
            return;
        }

        var suggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath) +
                                $"_page{CurrentPageIndex + 1}.png";

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Current Page",
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } }
            },
            DefaultExtension = "png"
        });

        if (file == null)
        {
            _logger.LogInformation("Export dialog cancelled");
            return;
        }

        var filePath = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("Export target file has no local path");
            return;
        }

        await ExportCurrentPageToImageAsync(filePath);
    }

    public async Task ExportCurrentPageToImageAsync(string outputPath, int dpi = 150)
    {
        _logger.LogInformation("Exporting current page {PageNumber} to: {Path}, DPI: {DPI}",
            CurrentPageIndex + 1, outputPath, dpi);

        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogError("Cannot export: No document loaded");
            return;
        }

        try
        {
            var bitmap = await _renderService.RenderPageAsync(_currentFilePath, CurrentPageIndex, dpi);
            if (bitmap != null)
            {
                var extension = System.IO.Path.GetExtension(outputPath).ToLowerInvariant();
                SKEncodedImageFormat imageFormat = extension switch
                {
                    ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                    _ => SKEncodedImageFormat.Png
                };

                using var image = SKImage.FromBitmap(bitmap);
                using var encodedData = image.Encode(imageFormat, 90);
                using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                encodedData.SaveTo(fileStream);

                _logger.LogInformation("Page {PageNumber} exported to: {FilePath}", CurrentPageIndex + 1, outputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting current page");
        }
    }

    private async Task ExportPagesAsync()
    {
        _logger.LogInformation("Export pages command triggered");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot export: No document loaded");
            return;
        }

        var storageProvider = GetStorageProvider();
        if (storageProvider == null)
        {
            _logger.LogWarning("Storage provider unavailable, cannot show Export dialog");
            return;
        }

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder for Exported Images",
            AllowMultiple = false
        });

        if (folder.Count == 0)
        {
            _logger.LogInformation("Export dialog cancelled");
            return;
        }

        var folderPath = folder[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _logger.LogWarning("Export target folder has no local path");
            return;
        }

        await ExportPagesToImagesAsync(folderPath, "png", 150);
    }

    public async Task ExportPagesToImagesAsync(string outputFolder, string format = "png", int dpi = 150)
    {
        _logger.LogInformation("Exporting pages to: {Folder}, Format: {Format}, DPI: {DPI}",
            outputFolder, format, dpi);

        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogError("Cannot export: No document loaded");
            return;
        }

        try
        {
            for (int i = 0; i < TotalPages; i++)
            {
                _logger.LogDebug("Exporting page {PageIndex}", i);

                var bitmap = await _renderService.RenderPageAsync(_currentFilePath, i, dpi);
                if (bitmap != null)
                {
                    var fileName = $"page_{i + 1:D3}.{format}";
                    var filePath = System.IO.Path.Combine(outputFolder, fileName);

                    // Determine the image format based on the 'format' parameter
                    SKEncodedImageFormat imageFormat = SKEncodedImageFormat.Png;
                    if (format.Equals("jpg", StringComparison.OrdinalIgnoreCase) || format.Equals("jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        imageFormat = SKEncodedImageFormat.Jpeg;
                    }

                    using var image = SKImage.FromBitmap(bitmap);
                    using var encodedData = image.Encode(imageFormat, 90); // 90% quality for JPG
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                    encodedData.SaveTo(fileStream);
                    _logger.LogDebug("Page {PageIndex} exported to: {FilePath}", i, filePath);
                }
            }

            _logger.LogInformation("All {Count} pages exported successfully", TotalPages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting pages");
        }
    }

    private async Task PrintAsync()
    {
        _logger.LogInformation("Print command triggered");

        if (!_documentService.IsDocumentLoaded)
        {
            _logger.LogWarning("Cannot print: No document loaded");
            return;
        }

        // This would show print dialog
        // For now, placeholder
        _logger.LogInformation("Print functionality not yet implemented");
        await Task.CompletedTask;
    }

    // Help Menu Commands

    private async void ShowAbout()
    {
        _logger.LogInformation("About dialog requested");

        var window = GetMainWindow();
        if (window != null)
        {
            var messageBox = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "About PDF Editor",
                Content = "PDF Editor v1.3.0-dev\n\n" +
                          "A cross-platform PDF editor with TRUE content-level redaction.\n\n" +
                          "Features:\n" +
                          "• View and navigate PDFs\n" +
                          "• Mark-then-apply redaction workflow\n" +
                          "• Text extraction and search\n" +
                          "• Page management (add/remove/rotate)\n" +
                          "• OCR support\n\n" +
                          "License: MIT\n" +
                          "Built with: .NET 8 + Avalonia UI",
                CloseButtonText = "Close",
                DefaultButton = FluentAvalonia.UI.Controls.ContentDialogButton.Close
            };

            await messageBox.ShowAsync();
        }
    }

    private async void ShowKeyboardShortcuts()
    {
        _logger.LogInformation("Keyboard shortcuts dialog requested");

        var window = GetMainWindow();
        if (window != null)
        {
            var messageBox = new FluentAvalonia.UI.Controls.ContentDialog
            {
                Title = "Keyboard Shortcuts",
                Content = "File:\n" +
                          "  Ctrl+O - Open PDF\n" +
                          "  Ctrl+S - Save\n" +
                          "  Ctrl+Shift+S - Save As\n" +
                          "  Ctrl+W - Close Document\n\n" +
                          "Edit:\n" +
                          "  Ctrl+F - Find\n" +
                          "  F3 - Find Next\n" +
                          "  Shift+F3 - Find Previous\n" +
                          "  T - Toggle Text Selection Mode\n" +
                          "  R - Toggle Redaction Mode\n\n" +
                          "View:\n" +
                          "  Ctrl++ - Zoom In\n" +
                          "  Ctrl+- - Zoom Out\n" +
                          "  Ctrl+0 - Actual Size\n\n" +
                          "Navigation:\n" +
                          "  PgUp/PgDn - Previous/Next Page",
                CloseButtonText = "Close",
                DefaultButton = FluentAvalonia.UI.Controls.ContentDialogButton.Close
            };

            await messageBox.ShowAsync();
        }
    }

    private void ShowDocumentation()
    {
        _logger.LogInformation("Documentation requested");

        try
        {
            var readmePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");

            if (System.IO.File.Exists(readmePath))
            {
                // Open README.md with default application
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = readmePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            else
            {
                // Fallback to GitHub repository
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/marctjones/pdfe",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open documentation");
        }
    }

    private Avalonia.Controls.Window? GetMainWindow()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        return lifetime?.MainWindow;
    }

    private IStorageProvider? GetStorageProvider()
    {
        return GetMainWindow()?.StorageProvider;
    }

    private async Task<IStorageFile?> ShowSaveRedactedFileDialog(Avalonia.Controls.Window mainWindow, string suggestedPath)
    {
        var storageProvider = mainWindow.StorageProvider;

        var options = new FilePickerSaveOptions
        {
            Title = $"Save Redacted PDF ({RedactionWorkflow.PendingCount} areas will be redacted)",
            DefaultExtension = "pdf",
            SuggestedFileName = System.IO.Path.GetFileName(suggestedPath),
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF Document")
                {
                    Patterns = new[] { "*.pdf" },
                    MimeTypes = new[] { "application/pdf" }
                }
            }
        };

        // Try to set the suggested directory
        try
        {
            var dir = System.IO.Path.GetDirectoryName(suggestedPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                options.SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(dir);
            }
        }
        catch
        {
            // Ignore errors, will use default location
        }

        return await storageProvider.SaveFilePickerAsync(options);
    }

    // Recent Files Management

    private void LoadRecentFiles()
    {
        _logger.LogDebug("Loading recent files");

        try
        {
            // Use AppPaths for cross-platform correct paths (Issues #265, #266, #267)
            var recentFilesPath = AppPaths.RecentFilesPath;

            if (System.IO.File.Exists(recentFilesPath))
            {
                var lines = System.IO.File.ReadAllLines(recentFilesPath);
                foreach (var line in lines.Take(10)) // Keep max 10 recent files
                {
                    if (System.IO.File.Exists(line))
                    {
                        RecentFiles.Add(line);
                    }
                }

                this.RaisePropertyChanged(nameof(HasRecentFiles));
                this.RaisePropertyChanged(nameof(RecentFileMenuItems));
                _logger.LogInformation("Loaded {Count} recent files", RecentFiles.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading recent files");
        }
    }

    private void AddToRecentFiles(string filePath)
    {
        _logger.LogDebug("Adding to recent files: {FilePath}", filePath);

        try
        {
            // Remove if already exists
            if (RecentFiles.Contains(filePath))
            {
                RecentFiles.Remove(filePath);
            }

            // Add to beginning
            RecentFiles.Insert(0, filePath);

            // Keep max 10 files
            while (RecentFiles.Count > 10)
            {
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }

            this.RaisePropertyChanged(nameof(HasRecentFiles));
            this.RaisePropertyChanged(nameof(RecentFileMenuItems));

            // Save to file
            SaveRecentFiles();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error adding to recent files");
        }
    }

    private void SaveRecentFiles()
    {
        try
        {
            // Use AppPaths for cross-platform correct paths (Issues #265, #266, #267)
            // AppPaths.DataDir ensures directory exists
            System.IO.File.WriteAllLines(AppPaths.RecentFilesPath, RecentFiles);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error saving recent files");
        }
    }

    /// <summary>
    /// Removes a file from the recent files list (Issue #25: handles deleted files).
    /// </summary>
    private void RemoveFromRecentFiles(string filePath)
    {
        try
        {
            if (RecentFiles.Contains(filePath))
            {
                RecentFiles.Remove(filePath);
                this.RaisePropertyChanged(nameof(HasRecentFiles));
                SaveRecentFiles();
                _logger.LogInformation("Removed deleted file from recent files: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing from recent files");
        }
    }

    // Zoom Level Persistence (Issue #32)

    private void LoadZoomPreference()
    {
        try
        {
            // Use AppPaths for cross-platform correct paths (Issues #265, #266, #267)
            var zoomFilePath = AppPaths.ZoomSettingsPath;

            if (System.IO.File.Exists(zoomFilePath))
            {
                var zoomStr = System.IO.File.ReadAllText(zoomFilePath).Trim();
                if (double.TryParse(zoomStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var savedZoom))
                {
                    // Validate range (25% to 500%)
                    if (savedZoom >= 0.25 && savedZoom <= 5.0)
                    {
                        _zoomLevel = savedZoom;
                        _logger.LogInformation("Loaded zoom preference: {Zoom:P0}", savedZoom);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading zoom preference");
        }
    }

    private void SaveZoomPreference()
    {
        try
        {
            // Use AppPaths for cross-platform correct paths (Issues #265, #266, #267)
            // AppPaths.ConfigDir ensures directory exists
            System.IO.File.WriteAllText(AppPaths.ZoomSettingsPath,
                ZoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error saving zoom preference");
        }
    }

    // OCR removed in the pure-Pdfe.Core migration. Reintroduce later
    // as a pdfe CLI subcommand if needed.

    // Signature Verification Command
    private async Task VerifySignaturesAsync()
    {
        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("No document loaded for signature verification");
            return;
        }

        try
        {
            _logger.LogInformation("Verifying digital signatures");

            var results = _signatureService.VerifySignatures(_currentFilePath);

            if (results == null || results.Count == 0)
            {
                _logger.LogInformation("No digital signatures found in document");
                // TODO: Show dialog: "No digital signatures found"
                return;
            }

            _logger.LogInformation("Found {SignatureCount} signatures", results.Count);

            // TODO: Show signature verification results dialog
            foreach (var result in results)
            {
                _logger.LogInformation("Signature: Valid={IsValid}, Signer={Signer}, Time={SigningTime}",
                    result.IsValid, result.SignedBy, result.SigningTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signatures");
        }

        await Task.CompletedTask;
    }

    // Preferences Command
    private void ShowPreferences()
    {
        _logger.LogInformation("Show preferences dialog");

        var preferencesViewModel = new PreferencesViewModel();
        preferencesViewModel.LoadFromMainViewModel(this);

        var window = new Views.PreferencesWindow
        {
            DataContext = preferencesViewModel
        };

        // Get the main window
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            window.ShowDialog(mainWindow).ContinueWith(task =>
            {
                if (preferencesViewModel.DialogResult)
                {
                    preferencesViewModel.SaveToMainViewModel(this);
                    _logger.LogInformation("Preferences saved");
                }
            });
        }
        else
        {
            _logger.LogWarning("Could not find main window to show preferences dialog");
        }
    }

    private async Task ShowMessageDialog(string title, string message)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            // Create a simple dialog window
            var dialog = new Window
            {
                Title = title,
                Width = 450,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 15
            };

            var messageText = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400
            };

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Padding = new Thickness(30, 5),
                Margin = new Thickness(0, 10, 0, 0)
            };

            okButton.Click += (s, e) => dialog.Close();

            panel.Children.Add(messageText);
            panel.Children.Add(okButton);
            dialog.Content = panel;

            await dialog.ShowDialog(mainWindow);
        }
        else
        {
            _logger.LogWarning("Could not show message dialog: Main window not found. Message was: {Message}", message);
        }
    }

    /// <summary>
    /// Converts an SKBitmap to an Avalonia.Media.Imaging.Bitmap.
    /// </summary>
    /// <param name="skBitmap">The SKBitmap to convert.</param>
    /// <returns>An Avalonia.Media.Imaging.Bitmap, or null if conversion fails.</returns>
    private Avalonia.Media.Imaging.Bitmap? ToAvaloniaBitmap(SKBitmap? skBitmap)
    {
        // Direct pixel copy via WriteableBitmap — replaces a per-render
        // PNG encode + decode round-trip that ate ~150-300ms on every
        // page render. See PdfEditor.Imaging.SkiaInterop for the rationale.
        try
        {
            return PdfEditor.Imaging.SkiaInterop.ToAvaloniaBitmap(skBitmap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting SKBitmap to Avalonia.Media.Imaging.Bitmap");
            return null;
        }
    }
}

/// <summary>
/// Zoom-mode latching for the viewer. PDF readers traditionally let users
/// pick a fit mode (Width / Page) that survives window resizes — once the
/// user manually changes zoom (Ctrl++/-/0 or scroll-wheel zoom) we drop
/// to <see cref="Manual"/> and stop auto-recomputing.
/// </summary>
public enum ZoomFitMode
{
    Manual,
    FitWidth,
    FitPage,
}
