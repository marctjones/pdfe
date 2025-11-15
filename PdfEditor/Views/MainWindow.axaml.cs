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

    public MainWindow()
    {
        InitializeComponent();
        
        // Wire up file dialog for Open command
        this.Opened += MainWindow_Opened;
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
}
