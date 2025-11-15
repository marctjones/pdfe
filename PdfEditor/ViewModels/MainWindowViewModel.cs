using Avalonia;
using Avalonia.Media.Imaging;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using PdfEditor.Services;
using PdfEditor.Models;

namespace PdfEditor.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly PdfDocumentService _documentService;
    private readonly PdfRenderService _renderService;
    private readonly RedactionService _redactionService;

    private string _currentFilePath = string.Empty;
    private Bitmap? _currentPageImage;
    private int _currentPageIndex;
    private double _zoomLevel = 1.0;
    private bool _isRedactionMode;
    private Rect _currentRedactionArea;

    public MainWindowViewModel()
    {
        _documentService = new PdfDocumentService();
        _renderService = new PdfRenderService();
        _redactionService = new RedactionService();

        PageThumbnails = new ObservableCollection<PageThumbnail>();

        // Commands
        OpenFileCommand = ReactiveCommand.CreateFromTask(OpenFileAsync);
        SaveFileCommand = ReactiveCommand.CreateFromTask(SaveFileAsync);
        RemoveCurrentPageCommand = ReactiveCommand.Create(RemoveCurrentPage);
        AddPagesCommand = ReactiveCommand.CreateFromTask(AddPagesAsync);
        ToggleRedactionModeCommand = ReactiveCommand.Create(ToggleRedactionMode);
        ApplyRedactionCommand = ReactiveCommand.Create(ApplyRedaction);
        ZoomInCommand = ReactiveCommand.Create(ZoomIn);
        ZoomOutCommand = ReactiveCommand.Create(ZoomOut);
        NextPageCommand = ReactiveCommand.Create(NextPage);
        PreviousPageCommand = ReactiveCommand.Create(PreviousPage);
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
        set => this.RaiseAndSetIfChanged(ref _currentPageIndex, value);
    }

    public int TotalPages => _documentService.PageCount;

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
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }
    public ReactiveCommand<Unit, Unit> NextPageCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousPageCommand { get; }

    // Command Implementations
    private async Task OpenFileAsync()
    {
        // This would be called from the View with a file path
        // For now, this is a placeholder
        await Task.CompletedTask;
    }

    public async Task LoadDocumentAsync(string filePath)
    {
        try
        {
            _currentFilePath = filePath;
            _documentService.LoadDocument(filePath);
            CurrentPageIndex = 0;
            
            await LoadPageThumbnailsAsync();
            await RenderCurrentPageAsync();
            
            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading document: {ex.Message}");
        }
    }

    private async Task SaveFileAsync()
    {
        if (!_documentService.IsDocumentLoaded)
            return;

        try
        {
            _documentService.SaveDocument();
            Console.WriteLine("Document saved successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving document: {ex.Message}");
        }
        
        await Task.CompletedTask;
    }

    private void RemoveCurrentPage()
    {
        if (!_documentService.IsDocumentLoaded || TotalPages <= 1)
            return;

        try
        {
            _documentService.RemovePage(CurrentPageIndex);
            
            // Adjust current page if needed
            if (CurrentPageIndex >= TotalPages)
                CurrentPageIndex = TotalPages - 1;

            Task.Run(async () =>
            {
                await LoadPageThumbnailsAsync();
                await RenderCurrentPageAsync();
            });

            this.RaisePropertyChanged(nameof(TotalPages));
            this.RaisePropertyChanged(nameof(StatusText));
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

            // Refresh the current page view
            Task.Run(async () => await RenderCurrentPageAsync());

            // Clear the selection
            CurrentRedactionArea = new Rect();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying redaction: {ex.Message}");
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

    private async Task LoadPageThumbnailsAsync()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return;

        PageThumbnails.Clear();

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
            _ = Task.Run(async () =>
            {
                var image = await _renderService.RenderThumbnailAsync(_currentFilePath, pageIndex);
                thumbnail.ThumbnailImage = image;
            });
        }
    }
}
