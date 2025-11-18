using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PdfEditor.ViewModels;
using System;
using System.Linq;

namespace PdfEditor.Views;

public partial class MainWindow : Window
{
    private Point _selectionStartPoint;
    private bool _isSelecting;
    private Point _textSelectionStartPoint;
    private bool _isSelectingText;

    public MainWindow()
    {
        InitializeComponent();

        // Wire up file dialog for Open command
        this.Opened += MainWindow_Opened;

        // Add keyboard handler for Ctrl+C
        this.KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

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

    // Redaction selection handlers
    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsRedactionMode)
            return;

        _selectionStartPoint = e.GetPosition(sender as Control);
        _isSelecting = true;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSelecting || DataContext is not MainWindowViewModel viewModel)
            return;

        var currentPoint = e.GetPosition(sender as Control);
        
        // Calculate selection rectangle
        var x = Math.Min(_selectionStartPoint.X, currentPoint.X);
        var y = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - _selectionStartPoint.X);
        var height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y);

        viewModel.CurrentRedactionArea = new Rect(x, y, width, height);
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isSelecting = false;
    }

    // Text selection handlers
    private void TextCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsTextSelectionMode)
            return;

        _textSelectionStartPoint = e.GetPosition(sender as Control);
        _isSelectingText = true;
    }

    private void TextCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSelectingText || DataContext is not MainWindowViewModel viewModel)
            return;

        var currentPoint = e.GetPosition(sender as Control);

        // Calculate selection rectangle
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
