using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PdfEditor.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace PdfEditor.Views;

public partial class MainWindow : Window
{
    private Point _selectionStartPoint;
    private bool _isSelecting;
    private Point _textSelectionStartPoint;
    private bool _isSelectingText;
    private Canvas? _searchHighlightsCanvas;

    public MainWindow()
    {
        InitializeComponent();

        // Wire up file dialog for Open command
        this.Opened += MainWindow_Opened;

        // Add keyboard handler for Ctrl+C
        this.KeyDown += MainWindow_KeyDown;

        // Subscribe to search highlights changes
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            // Subscribe to search highlights collection changes
            viewModel.CurrentPageSearchHighlights.CollectionChanged += OnSearchHighlightsChanged;
        }
    }

    private void OnSearchHighlightsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSearchHighlightsCanvas();
    }

    private void UpdateSearchHighlightsCanvas()
    {
        _searchHighlightsCanvas ??= this.FindControl<Canvas>("SearchHighlightsCanvas");

        if (_searchHighlightsCanvas == null)
            return;

        _searchHighlightsCanvas.Children.Clear();

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        foreach (var rect in viewModel.CurrentPageSearchHighlights)
        {
            var highlight = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0x00)), // Semi-transparent yellow
                Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x98, 0x00)), // Orange border
                StrokeThickness = 1,
                Width = rect.Width,
                Height = rect.Height
            };

            Canvas.SetLeft(highlight, rect.X);
            Canvas.SetTop(highlight, rect.Y);
            _searchHighlightsCanvas.Children.Add(highlight);
        }
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Ctrl+O: Open file
        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.OpenFileCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+S: Save file
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.SaveFileCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+Shift+S: Save As
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.SaveAsCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+W: Close document
        if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.CloseDocumentCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+P: Print
        if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.PrintCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // F1: Show keyboard shortcuts
        if (e.Key == Key.F1)
        {
            viewModel.ShowShortcutsCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+F: Toggle search
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ToggleSearchCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // F3: Find next
        if (e.Key == Key.F3 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.FindNextCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Shift+F3: Find previous
        if (e.Key == Key.F3 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            viewModel.FindPreviousCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Escape: Close search if visible
        if (e.Key == Key.Escape && viewModel.IsSearchVisible)
        {
            viewModel.CloseSearchCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+L: Rotate page left
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.RotatePageLeftCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+R: Rotate page right
        if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.RotatePageRightCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+0: Actual size (100%)
        if (e.Key == Key.D0 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ZoomActualSizeCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+1: Fit width
        if (e.Key == Key.D1 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ZoomFitWidthCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+2: Fit page
        if (e.Key == Key.D2 && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ZoomFitPageCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl++: Zoom in
        if ((e.Key == Key.OemPlus || e.Key == Key.Add) && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ZoomInCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Ctrl+-: Zoom out
        if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ZoomOutCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Page Down / Down Arrow: Next page
        if (e.Key == Key.PageDown || (e.Key == Key.Down && !e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            viewModel.NextPageCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Page Up / Up Arrow: Previous page
        if (e.Key == Key.PageUp || (e.Key == Key.Up && !e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            viewModel.PreviousPageCommand?.Execute().Subscribe();
            e.Handled = true;
            return;
        }

        // Home: First page
        if (e.Key == Key.Home)
        {
            viewModel.GoToPageCommand?.Execute(0).Subscribe();
            e.Handled = true;
            return;
        }

        // End: Last page
        if (e.Key == Key.End)
        {
            var lastPage = viewModel.TotalPages - 1;
            if (lastPage >= 0)
            {
                viewModel.GoToPageCommand?.Execute(lastPage).Subscribe();
            }
            e.Handled = true;
            return;
        }

        // Ctrl+C: Copy text
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (viewModel.IsTextSelectionMode)
            {
                viewModel.CopyTextCommand.Execute().Subscribe();
                e.Handled = true;
            }
        }
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        var viewModel = DataContext as MainWindowViewModel;
        if (viewModel != null)
        {
            viewModel.OpenFileCommand.Subscribe(async _ => await OpenFileDialog());
            viewModel.AddPagesCommand.Subscribe(async _ => await AddPagesDialog());
            viewModel.SaveAsCommand.Subscribe(async _ => await SaveAsDialog());
            viewModel.ExportPagesCommand.Subscribe(async _ => await ExportPagesDialog());
        }
    }

    /// <summary>
    /// Update viewport dimensions when the PDF scroll viewer size changes.
    /// This enables accurate zoom fit calculations.
    /// </summary>
    private void PdfScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ViewportWidth = e.NewSize.Width;
            viewModel.ViewportHeight = e.NewSize.Height;
        }
    }

    private async System.Threading.Tasks.Task OpenFileDialog()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

        if (files.Count >= 1 && DataContext is MainWindowViewModel viewModel)
        {
            var filePath = files[0].Path.LocalPath;
            await viewModel.LoadDocumentAsync(filePath);
        }
    }

    private async System.Threading.Tasks.Task AddPagesDialog()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

        if (files.Count >= 1 && DataContext is MainWindowViewModel viewModel)
        {
            var filePath = files[0].Path.LocalPath;
            await viewModel.AddPagesFromFileAsync(filePath);
        }
    }

    private async System.Threading.Tasks.Task SaveAsDialog()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save PDF As",
            DefaultExtension = "pdf",
            SuggestedFileName = "document.pdf",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF Files")
                {
                    Patterns = new[] { "*.pdf" }
                }
            }
        });

        if (file != null && DataContext is MainWindowViewModel viewModel)
        {
            var filePath = file.Path.LocalPath;
            await viewModel.SaveFileAsAsync(filePath);
        }
    }

    private async System.Threading.Tasks.Task ExportPagesDialog()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder for Exported Images",
            AllowMultiple = false
        });

        if (folder.Count >= 1 && DataContext is MainWindowViewModel viewModel)
        {
            var folderPath = folder[0].Path.LocalPath;
            await viewModel.ExportPagesToImagesAsync(folderPath, "png", 150);
        }
    }

    // ==================================================================================
    // REDACTION SELECTION HANDLERS
    // ==================================================================================
    // COORDINATE SYSTEM DOCUMENTATION:
    //
    // The Canvas has a ScaleTransform(ZoomLevel, ZoomLevel) applied via XAML.
    // When GetPosition(canvas) is called, Avalonia returns coordinates in the Canvas's
    // LOCAL coordinate space (before the transform is applied to rendering).
    //
    // This means:
    // - If ZoomLevel = 2.0 and user clicks at screen position (400, 300)
    // - The Canvas visually appears 2x larger
    // - GetPosition returns (200, 150) - automatically inverse-transformed!
    //
    // Result: Coordinates from GetPosition are already in "image pixel" space (150 DPI),
    // NOT in "zoomed screen pixel" space. We do NOT need to divide by zoom here.
    //
    // The coordinates stored in CurrentRedactionArea are in rendered image pixels (150 DPI).
    // The ViewModel/Service layer will convert these to PDF points (72 DPI) when applying
    // the redaction: pdfPoints = imagePixels * (72 / 150)
    // ==================================================================================

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (!viewModel.IsRedactionMode)
        {
            Console.WriteLine($"[Selection] PointerPressed but NOT in redaction mode");
            return;
        }

        // GetPosition returns coordinates in Canvas local space (image pixels at 150 DPI)
        // because the Canvas has ScaleTransform that inverse-transforms input coordinates
        _selectionStartPoint = e.GetPosition(sender as Control);
        _isSelecting = true;
        Console.WriteLine($"[Selection] Started at ({_selectionStartPoint.X:F1},{_selectionStartPoint.Y:F1}) [image pixels]");
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSelecting || DataContext is not MainWindowViewModel viewModel)
            return;

        // GetPosition returns image-space coordinates (zoom already handled by Canvas transform)
        var currentPoint = e.GetPosition(sender as Control);

        // Calculate selection rectangle in image pixel coordinates (150 DPI, top-left origin)
        // NO zoom division needed - the Canvas ScaleTransform handles coordinate transformation
        var x = Math.Min(_selectionStartPoint.X, currentPoint.X);
        var y = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - _selectionStartPoint.X);
        var height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y);

        viewModel.CurrentRedactionArea = new Rect(x, y, width, height);
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isSelecting && DataContext is MainWindowViewModel viewModel)
        {
            Console.WriteLine($"[Selection] Completed: ({viewModel.CurrentRedactionArea.X:F1},{viewModel.CurrentRedactionArea.Y:F1},{viewModel.CurrentRedactionArea.Width:F1}x{viewModel.CurrentRedactionArea.Height:F1}) [image pixels]");
        }
        _isSelecting = false;
    }

    // ==================================================================================
    // TEXT SELECTION HANDLERS
    // ==================================================================================
    // Same coordinate system as redaction - Canvas has ScaleTransform so GetPosition
    // returns image-space coordinates automatically.
    // ==================================================================================

    private void TextCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsTextSelectionMode)
            return;

        // GetPosition returns coordinates in Canvas local space (image pixels at 150 DPI)
        _textSelectionStartPoint = e.GetPosition(sender as Control);
        _isSelectingText = true;
    }

    private void TextCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSelectingText || DataContext is not MainWindowViewModel viewModel)
            return;

        // GetPosition returns image-space coordinates (zoom already handled by Canvas transform)
        var currentPoint = e.GetPosition(sender as Control);

        // Calculate selection rectangle in image pixel coordinates (150 DPI, top-left origin)
        // NO zoom division needed - the Canvas ScaleTransform handles coordinate transformation
        var x = Math.Min(_textSelectionStartPoint.X, currentPoint.X);
        var y = Math.Min(_textSelectionStartPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - _textSelectionStartPoint.X);
        var height = Math.Abs(currentPoint.Y - _textSelectionStartPoint.Y);

        viewModel.CurrentTextSelectionArea = new Rect(x, y, width, height);
    }

    private void TextCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isSelectingText = false;

        // Automatically copy text when selection is released
        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.CurrentTextSelectionArea.Width > 5 &&
            viewModel.CurrentTextSelectionArea.Height > 5)
        {
            viewModel.CopyTextCommand.Execute().Subscribe();
        }
    }
}
