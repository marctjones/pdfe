using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PdfEditor.Models;
using PdfEditor.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;

namespace PdfEditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly PdfDocumentService _documentService;
    private readonly PdfRenderService _renderService;
    private readonly RedactionService _redactionService;
    private readonly PdfTextExtractionService _textExtractionService;

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

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        PdfDocumentService documentService,
        PdfRenderService renderService,
        RedactionService redactionService,
        PdfTextExtractionService textExtractionService,
        PdfSearchService searchService)
    {
        _logger = logger;
        _documentService = documentService;
        _renderService = renderService;
        _redactionService = redactionService;
        _textExtractionService = textExtractionService;
        _searchService = searchService;

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

        // Initialize search commands
        InitializeSearchCommands();

        // Load recent files
        LoadRecentFiles();

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
        set => this.RaiseAndSetIfChanged(ref _isRedactionMode, value);
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

    public string StatusText => _documentService.IsDocumentLoaded
        ? $"Page {CurrentPageIndex + 1} of {TotalPages} - Zoom: {ZoomLevel:P0}"
        : "No document loaded";

    public ObservableCollection<string> RecentFiles
    {
        get => _recentFiles;
        set => this.RaiseAndSetIfChanged(ref _recentFiles, value);
    }

    public bool HasRecentFiles => RecentFiles.Count > 0;

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

    // Help Menu Commands
    public ReactiveCommand<Unit, Unit> AboutCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowShortcutsCommand { get; }

    // Command Implementations
    private async Task OpenFileAsync()
    {
        // This would be called from the View with a file path
        // For now, this is a placeholder
        await Task.CompletedTask;
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
        // This would open a file picker and add pages
        // Placeholder for now
        await Task.CompletedTask;
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
        if (!IsRedactionMode || CurrentRedactionArea.Width <= 0 || CurrentRedactionArea.Height <= 0)
            return;

        try
        {
            var document = _documentService.GetCurrentDocument();
            if (document == null)
                return;

            var page = document.Pages[CurrentPageIndex];
            _redactionService.RedactArea(page, CurrentRedactionArea);

            // NOTE: Redaction is applied to the in-memory document only
            // User must click Save to persist changes to disk
            _logger.LogInformation("Redaction applied to in-memory document (not saved to disk yet)");

            // Render the page from the in-memory document to show the redaction
            using var docStream = _documentService.GetCurrentDocumentAsStream();
            if (docStream != null)
            {
                var bitmap = await _renderService.RenderPageFromStreamAsync(docStream, CurrentPageIndex);
                if (bitmap != null)
                {
                    CurrentPageImage = bitmap;
                }
            }

            // Clear the selection
            CurrentRedactionArea = new Rect();

            // Exit redaction mode after applying
            IsRedactionMode = false;

            _logger.LogInformation("Redaction complete - use Save button to persist changes to disk");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying redaction");
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
        // In a production app, this would calculate based on page width and viewport width
        // For now, use a reasonable default that typically fits width well
        ZoomLevel = 1.3;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void ZoomFitPage()
    {
        _logger.LogInformation("Setting zoom to fit page");
        // In a production app, this would calculate based on page dimensions and viewport dimensions
        // For now, use a reasonable default that typically fits the page
        ZoomLevel = 0.9;
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

        // This would show a save file dialog
        // For now, placeholder
        await Task.CompletedTask;
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

        // This would show a folder picker and export options dialog
        // For now, placeholder
        await Task.CompletedTask;
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
}
