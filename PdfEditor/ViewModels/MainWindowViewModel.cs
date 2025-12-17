using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PdfEditor.Models;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Threading.Tasks;

namespace PdfEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PdfDocumentService _documentService;
    private readonly PdfRenderService _renderService;
    private readonly RedactionService _redactionService;
    private readonly PdfTextExtractionService _textExtractionService;
    private readonly PdfOcrService _ocrService;
    private readonly SignatureVerificationService _signatureService;
    private readonly RedactionVerifier _verifier;

    private string _currentFilePath = string.Empty;
    private Bitmap? _currentPageImage;
    private int _currentPageIndex;
    private double _zoomLevel = 1.0;
    private bool _isRedactionMode;
    private Rect _currentRedactionArea;
    private bool _isTextSelectionMode;
    private Rect _currentTextSelectionArea;
    private string _selectedText = string.Empty;
    private ObservableCollection<string> _recentFiles = new();
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;
    private ObservableCollection<Rect> _currentPageSearchHighlights = new();
    private bool _runVerifyAfterSave;
    private string _ocrLanguages = "eng";
    private int _ocrBaseDpi = 350;
    private int _ocrHighDpi = 450;
    private double _ocrLowConfidence = 0.6;
    private int _renderCacheMax = 20;
    private string _lastVerifyStatus = string.Empty;
    private bool _lastVerifyFailed;

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        ILoggerFactory loggerFactory,
        PdfDocumentService documentService,
        PdfRenderService renderService,
        RedactionService redactionService,
        PdfTextExtractionService textExtractionService,
        PdfSearchService searchService,
        PdfOcrService ocrService,
        SignatureVerificationService signatureService,
        RedactionVerifier verifier)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _documentService = documentService;
        _renderService = renderService;
        _redactionService = redactionService;
        _textExtractionService = textExtractionService;
        _searchService = searchService;
        _ocrService = ocrService;
        _signatureService = signatureService;
        _verifier = verifier;

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

        // Tools menu commands
        ExportPagesCommand = ReactiveCommand.CreateFromTask(ExportPagesAsync);
        PrintCommand = ReactiveCommand.CreateFromTask(PrintAsync);

        // Help menu commands
        AboutCommand = ReactiveCommand.Create(ShowAbout);
        ShowShortcutsCommand = ReactiveCommand.Create(ShowKeyboardShortcuts);
        RunVerifyNowCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                await RunVerifyAsync(_currentFilePath);
            }
        });

        // Tools menu commands
        RunOcrCommand = ReactiveCommand.CreateFromTask(RunOcrOnCurrentPageAsync);
        RunOcrAllPagesCommand = ReactiveCommand.CreateFromTask(RunOcrOnAllPagesAsync);
        VerifySignaturesCommand = ReactiveCommand.CreateFromTask(VerifySignaturesAsync);
        ShowPreferencesCommand = ReactiveCommand.Create(ShowPreferences);

        // Initialize search commands
        InitializeSearchCommands();

        // Load recent files
        LoadRecentFiles();

        _logger.LogDebug("MainWindowViewModel initialization complete");
    }

    // Properties
    public ObservableCollection<PageThumbnail> PageThumbnails { get; }
    public ObservableCollection<ClipboardEntry> ClipboardHistory { get; }
    public ReactiveCommand<Unit, Unit> RunVerifyNowCommand { get; }

    public Bitmap? CurrentPageImage
    {
        get => _currentPageImage;
        set => this.RaiseAndSetIfChanged(ref _currentPageImage, value);
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPageIndex, value);
            this.RaisePropertyChanged(nameof(DisplayPageNumber));
            UpdateThumbnailSelection();
        }
    }

    public int TotalPages => _documentService.PageCount;

    public int DisplayPageNumber => CurrentPageIndex + 1;

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => this.RaiseAndSetIfChanged(ref _zoomLevel, value);
    }

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

    public bool RunVerifyAfterSave
    {
        get => _runVerifyAfterSave;
        set => this.RaiseAndSetIfChanged(ref _runVerifyAfterSave, value);
    }

    public string OcrLanguages
    {
        get => _ocrLanguages;
        set => this.RaiseAndSetIfChanged(ref _ocrLanguages, value);
    }

    public int OcrBaseDpi
    {
        get => _ocrBaseDpi;
        set => this.RaiseAndSetIfChanged(ref _ocrBaseDpi, value);
    }

    public int OcrHighDpi
    {
        get => _ocrHighDpi;
        set => this.RaiseAndSetIfChanged(ref _ocrHighDpi, value);
    }

    public double OcrLowConfidence
    {
        get => _ocrLowConfidence;
        set => this.RaiseAndSetIfChanged(ref _ocrLowConfidence, value);
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

    public string LastVerifyStatus
    {
        get => _lastVerifyStatus;
        set => this.RaiseAndSetIfChanged(ref _lastVerifyStatus, value);
    }

    public bool LastVerifyFailed
    {
        get => _lastVerifyFailed;
        set => this.RaiseAndSetIfChanged(ref _lastVerifyFailed, value);
    }

    public string StatusText => _documentService.IsDocumentLoaded
        ? $"Page {CurrentPageIndex + 1} of {TotalPages} - Zoom: {ZoomLevel:P0}"
        : "No document loaded";

    public ObservableCollection<string> RecentFiles
    {
        get => _recentFiles;
        set => this.RaiseAndSetIfChanged(ref _recentFiles, value);
    }

    public bool HasRecentFiles => RecentFiles.Count > 0;

    // Viewport dimensions (set by View for accurate zoom calculations)
    public double ViewportWidth
    {
        get => _viewportWidth;
        set => this.RaiseAndSetIfChanged(ref _viewportWidth, value);
    }

    public double ViewportHeight
    {
        get => _viewportHeight;
        set => this.RaiseAndSetIfChanged(ref _viewportHeight, value);
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

    // Tools Menu Commands
    public ReactiveCommand<Unit, Unit> ExportPagesCommand { get; }
    public ReactiveCommand<Unit, Unit> PrintCommand { get; }
    public ReactiveCommand<Unit, Unit> RunOcrCommand { get; }
    public ReactiveCommand<Unit, Unit> RunOcrAllPagesCommand { get; }
    public ReactiveCommand<Unit, Unit> VerifySignaturesCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowPreferencesCommand { get; }

    // Help Menu Commands
    public ReactiveCommand<Unit, Unit> AboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowShortcutsCommand { get; }

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
        _logger.LogInformation("Loading document: {FilePath}", filePath);

        try
        {
            _currentFilePath = filePath;
            this.RaisePropertyChanged(nameof(DocumentName));
            _documentService.LoadDocument(filePath);
            CurrentPageIndex = 0;

            _logger.LogDebug("Loading page thumbnails");
            await LoadPageThumbnailsAsync();

            _logger.LogDebug("Rendering current page");
            await RenderCurrentPageAsync();

            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));

            // Add to recent files
            AddToRecentFiles(filePath);

            _logger.LogInformation("Document loaded successfully. Total pages: {PageCount}", TotalPages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading document: {FilePath}", filePath);
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

        try
        {
            _documentService.SaveDocument();
            _logger.LogInformation("Document saved successfully");

            if (RunVerifyAfterSave && !string.IsNullOrEmpty(_currentFilePath))
            {
                await RunVerifyAsync(_currentFilePath);
            }
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

    private async Task ApplyRedactionAsync()
    {
        _logger.LogInformation(">>> ApplyRedactionAsync START. IsRedactionMode={Mode}, Area=({X:F2},{Y:F2},{W:F2}x{H:F2})",
            IsRedactionMode, CurrentRedactionArea.X, CurrentRedactionArea.Y, CurrentRedactionArea.Width, CurrentRedactionArea.Height);

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

                    var bitmap = await _renderService.RenderPageFromStreamAsync(memoryStream, CurrentPageIndex);
                    if (bitmap != null)
                    {
                        CurrentPageImage = bitmap;
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

    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.25, 5.0);
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.25, 0.25);
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomActualSize()
    {
        _logger.LogInformation("Setting zoom to actual size (100%)");
        ZoomLevel = 1.0;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomFitWidth()
    {
        _logger.LogInformation("Setting zoom to fit width");

        if (_currentPageImage == null || ViewportWidth <= 0)
        {
            // Fallback to default if no image loaded
            ZoomLevel = 1.0;
            this.RaisePropertyChanged(nameof(StatusText));
            return;
        }

        // Calculate zoom to fit page width in viewport (with small margin)
        var pageWidth = _currentPageImage.Size.Width;
        var margin = 40; // Leave some margin on sides
        var targetWidth = ViewportWidth - margin;

        if (pageWidth > 0)
        {
            ZoomLevel = Math.Max(0.25, Math.Min(5.0, targetWidth / pageWidth));
            _logger.LogDebug("Fit width: viewport={Viewport}, page={Page}, zoom={Zoom:P0}",
                ViewportWidth, pageWidth, ZoomLevel);
        }

        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomFitPage()
    {
        _logger.LogInformation("Setting zoom to fit page");

        if (_currentPageImage == null || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            // Fallback to default if no image loaded
            ZoomLevel = 1.0;
            this.RaisePropertyChanged(nameof(StatusText));
            return;
        }

        // Calculate zoom to fit entire page in viewport (with margins)
        var pageWidth = _currentPageImage.Size.Width;
        var pageHeight = _currentPageImage.Size.Height;
        var marginH = 40;
        var marginV = 40;
        var targetWidth = ViewportWidth - marginH;
        var targetHeight = ViewportHeight - marginV;

        if (pageWidth > 0 && pageHeight > 0)
        {
            // Use the smaller ratio to fit both dimensions
            var zoomW = targetWidth / pageWidth;
            var zoomH = targetHeight / pageHeight;
            ZoomLevel = Math.Max(0.25, Math.Min(5.0, Math.Min(zoomW, zoomH)));
            _logger.LogDebug("Fit page: viewport=({VW}x{VH}), page=({PW}x{PH}), zoom={Zoom:P0}",
                ViewportWidth, ViewportHeight, pageWidth, pageHeight, ZoomLevel);
        }

        this.RaisePropertyChanged(nameof(StatusText));
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
        if (string.IsNullOrEmpty(_currentFilePath) || !_documentService.IsDocumentLoaded)
            return;

        try
        {
            var bitmap = await _renderService.RenderPageAsync(_currentFilePath, CurrentPageIndex);
            CurrentPageImage = bitmap;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering page");
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
        if (string.IsNullOrEmpty(_currentFilePath))
            return;

        _logger.LogDebug("Clearing existing thumbnails");
        PageThumbnails.Clear();

        var loadTasks = new List<Task>();

        _logger.LogDebug("Creating thumbnail placeholders for {PageCount} pages", TotalPages);

        for (int i = 0; i < TotalPages; i++)
        {
            var thumbnail = new PageThumbnail
            {
                PageNumber = i + 1,
                PageIndex = i
            };

            PageThumbnails.Add(thumbnail);

            // Load thumbnail asynchronously
            int pageIndex = i;
            var task = Task.Run(async () =>
            {
                _logger.LogDebug("Loading thumbnail for page {PageIndex}", pageIndex);
                var image = await _renderService.RenderThumbnailAsync(_currentFilePath, pageIndex);

                // Update UI on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    thumbnail.ThumbnailImage = image;
                    _logger.LogDebug("Thumbnail {PageIndex} loaded and set", pageIndex);
                });
            });

            loadTasks.Add(task);
        }

        // Wait for all thumbnails to load
        _logger.LogDebug("Waiting for all thumbnails to load");
        await Task.WhenAll(loadTasks);
        _logger.LogInformation("All {Count} thumbnails loaded successfully", TotalPages);
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

            if (RunVerifyAfterSave)
            {
                await RunVerifyAsync(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving document to: {FilePath}", filePath);
        }

        await Task.CompletedTask;
    }

    private Task RunVerifyAsync(string filePath)
    {
        return Task.Run(() =>
        {
            try
            {
                using var doc = PdfSharp.Pdf.IO.PdfReader.Open(filePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                var result = _verifier.Verify(doc);

                if (result.Passed)
                {
                    _logger.LogInformation("Verification passed for {File}", filePath);
                    LastVerifyFailed = false;
                    LastVerifyStatus = "Verification passed";
                }
                else
                {
                    _logger.LogWarning("Verification FAILED for {File}. Leaks: {Count}", filePath, result.Leaks.Count);
                    foreach (var leak in result.Leaks.Take(5))
                    {
                        _logger.LogWarning("Leak on page {Page}: '{Text}' at ({X:F1},{Y:F1})",
                            leak.PageIndex + 1, leak.Text, leak.BoundingBox.X, leak.BoundingBox.Y);
                    }
                    LastVerifyFailed = true;
                    LastVerifyStatus = $"Verification failed ({result.Leaks.Count} leaks)";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Verification failed for {FilePath}", filePath);
                LastVerifyFailed = true;
                LastVerifyStatus = "Verification failed";
            }
        });
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
            _documentService.CloseDocument();
            _currentFilePath = string.Empty;
            CurrentPageImage = null;
            PageThumbnails.Clear();
            _renderService.ClearCache();
            this.RaisePropertyChanged(nameof(DocumentName));
            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));
            _logger.LogInformation("Document closed successfully");
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

    // Tools Menu Commands

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

                    bitmap.Save(filePath);
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

    private void ShowAbout()
    {
        _logger.LogInformation("About dialog requested");
        // This would show an about dialog
    }

    private void ShowKeyboardShortcuts()
    {
        _logger.LogInformation("Keyboard shortcuts dialog requested");
        // This would show keyboard shortcuts help
    }

    private IStorageProvider? GetStorageProvider()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        return lifetime?.MainWindow?.StorageProvider;
    }

    // Recent Files Management

    private void LoadRecentFiles()
    {
        _logger.LogDebug("Loading recent files");

        try
        {
            var recentFilesPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PdfEditor",
                "recent.txt");

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
            var appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PdfEditor");

            System.IO.Directory.CreateDirectory(appDataPath);

            var recentFilesPath = System.IO.Path.Combine(appDataPath, "recent.txt");
            System.IO.File.WriteAllLines(recentFilesPath, RecentFiles);

            _logger.LogDebug("Recent files saved");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error saving recent files");
        }
    }

    public async Task LoadRecentFileAsync(string filePath)
    {
        _logger.LogInformation("Loading recent file: {FilePath}", filePath);
        await LoadDocumentAsync(filePath);
    }

    // OCR Commands
    private async Task RunOcrOnCurrentPageAsync()
    {
        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("No document loaded for OCR");
            return;
        }

        try
        {
            _logger.LogInformation("Running OCR on current page {PageNumber}", CurrentPageIndex + 1);

            if (!_ocrService.IsOcrAvailable())
            {
                _logger.LogWarning("OCR not available - tessdata files missing");
                // TODO: Show dialog to user: "OCR requires Tesseract data files. Please install tessdata."
                return;
            }

            var options = new OcrOptions
            {
                Languages = OcrLanguages,
                BaseDpi = OcrBaseDpi,
                HighDpi = OcrHighDpi,
                LowConfidenceThreshold = (float)OcrLowConfidence
            };

            // For now, OCR the entire document and show only current page result
            // TODO: Implement single-page OCR extraction
            var result = await _ocrService.PerformOcrAsync(_currentFilePath, options);

            if (!string.IsNullOrWhiteSpace(result))
            {
                _logger.LogInformation("OCR completed. Extracted {CharCount} characters", result.Length);

                // Add to clipboard history
                ClipboardHistory.Insert(0, new ClipboardEntry
                {
                    Text = result,
                    PageNumber = CurrentPageIndex + 1,
                    Timestamp = DateTime.Now,
                    IsRedacted = false
                });

                _logger.LogInformation("OCR text added to clipboard history");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running OCR");
        }
    }

    private async Task RunOcrOnAllPagesAsync()
    {
        if (!_documentService.IsDocumentLoaded || string.IsNullOrEmpty(_currentFilePath))
        {
            _logger.LogWarning("No document loaded for OCR");
            return;
        }

        try
        {
            _logger.LogInformation("Running OCR on all {PageCount} pages", TotalPages);

            if (!_ocrService.IsOcrAvailable())
            {
                _logger.LogWarning("OCR not available - tessdata files missing");
                // TODO: Show dialog to user
                return;
            }

            var options = new OcrOptions
            {
                Languages = OcrLanguages,
                BaseDpi = OcrBaseDpi,
                HighDpi = OcrHighDpi,
                LowConfidenceThreshold = (float)OcrLowConfidence
            };

            var result = await _ocrService.PerformOcrAsync(_currentFilePath, options);

            if (!string.IsNullOrWhiteSpace(result))
            {
                // Add to clipboard history
                ClipboardHistory.Insert(0, new ClipboardEntry
                {
                    Text = result,
                    PageNumber = 0, // All pages
                    Timestamp = DateTime.Now,
                    IsRedacted = false
                });

                _logger.LogInformation("OCR completed for all pages. Total characters: {CharCount}", result.Length);
                // TODO: Show completion dialog
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running OCR on all pages");
        }
    }

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

    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
