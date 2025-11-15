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

public class MainWindowViewModel : ViewModelBase
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

    public MainWindowViewModel(
        ILogger<MainWindowViewModel> logger,
        PdfDocumentService documentService,
        PdfRenderService renderService,
        RedactionService redactionService,
        PdfTextExtractionService textExtractionService)
    {
        _logger = logger;
        _documentService = documentService;
        _renderService = renderService;
        _redactionService = redactionService;
        _textExtractionService = textExtractionService;

        _logger.LogInformation("MainWindowViewModel initialized");

        PageThumbnails = new ObservableCollection<PageThumbnail>();

        // Commands
        _logger.LogDebug("Setting up ReactiveUI commands");
        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        SaveFileCommand = ReactiveCommand.CreateFromTask(SaveFileAsync);
        RemoveCurrentPageCommand = ReactiveCommand.Create(RemoveCurrentPage);
        AddPagesCommand = ReactiveCommand.CreateFromTask(AddPagesAsync);
        ToggleRedactionModeCommand = ReactiveCommand.Create(ToggleRedactionMode);
        ApplyRedactionCommand = ReactiveCommand.Create(ApplyRedaction);
        ToggleTextSelectionModeCommand = ReactiveCommand.Create(ToggleTextSelectionMode);
        CopyTextCommand = ReactiveCommand.CreateFromTask(CopyTextAsync);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        NextPageCommand = ReactiveCommand.Create(NextPage);
        PreviousPageCommand = ReactiveCommand.Create(PreviousPage);
        GoToPageCommand = ReactiveCommand.Create<int>(GoToPage);

        _logger.LogDebug("MainWindowViewModel initialization complete");
    }

    // Properties
    public ObservableCollection<PageThumbnail> PageThumbnails { get; }

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
            _documentService.LoadDocument(filePath);
            CurrentPageIndex = 0;

            _logger.LogDebug("Loading page thumbnails");
            await LoadPageThumbnailsAsync();

            _logger.LogDebug("Rendering current page");
            await RenderCurrentPageAsync();

            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));

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

    private void RemoveCurrentPage()
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
            Task.Run(async () =>
            {
                await LoadPageThumbnailsAsync();
                await RenderCurrentPageAsync();
            });

            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));

            _logger.LogInformation("Page removed successfully. Remaining pages: {PageCount}", TotalPages);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing page: {ex.Message}");
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
            Console.WriteLine($"Error adding pages: {ex.Message}");
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

    private void ApplyRedaction()
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
            Task.Run(async () =>
            {
                using var docStream = _documentService.GetCurrentDocumentAsStream();
                if (docStream != null)
                {
                    var bitmap = await _renderService.RenderPageFromStreamAsync(docStream, CurrentPageIndex);
                    if (bitmap != null)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            CurrentPageImage = bitmap;
                        });
                    }
                }
            });

            // Clear the selection
            CurrentRedactionArea = new Rect();

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

    private void NextPage()
    {
        if (CurrentPageIndex < TotalPages - 1)
        {
            CurrentPageIndex++;
            Task.Run(async () => await RenderCurrentPageAsync());
        }
    }

    private void PreviousPage()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            Task.Run(async () => await RenderCurrentPageAsync());
        }
    }

    private void GoToPage(int pageIndex)
    {
        _logger.LogInformation("Navigating to page {PageIndex}", pageIndex);

        if (pageIndex >= 0 && pageIndex < TotalPages && pageIndex != CurrentPageIndex)
        {
            CurrentPageIndex = pageIndex;
            Task.Run(async () => await RenderCurrentPageAsync());
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
            Console.WriteLine($"Error rendering page: {ex.Message}");
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
}
