using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PdfEditor.Controls;
using PdfEditor.Models;
using PdfEditor.ViewModels;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace PdfEditor.Views;

public partial class MainWindow : Window
{
    private PdfViewerControl? _pdfViewerControl;
    private readonly WindowSettings _windowSettings;

    public MainWindow()
    {
        InitializeComponent();

        // Load and apply window settings (Issue #23)
        _windowSettings = WindowSettings.Load();
        _windowSettings.ApplyTo(this);

        // Save settings on close
        this.Closing += (s, e) =>
        {
            _windowSettings.CaptureFrom(this);
            _windowSettings.Save();
        };

        // Add keyboard handler for Ctrl+C
        this.KeyDown += MainWindow_KeyDown;

        // Subscribe to search highlights changes
        this.DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Get reference to PdfViewerControl
        _pdfViewerControl ??= this.FindControl<PdfViewerControl>("PdfViewerControl");

        if (DataContext is MainWindowViewModel viewModel)
        {
            // Subscribe to toast notifications
            viewModel.ToastService.ToastRequested += OnToastRequested;

            // Subscribe to search highlights collection changes
            viewModel.CurrentPageSearchHighlights.CollectionChanged += OnSearchHighlightsChanged;

            // Subscribe to redaction collection changes
            viewModel.RedactionWorkflow.PendingRedactions.CollectionChanged += OnRedactionsChanged;
            viewModel.RedactionWorkflow.AppliedRedactions.CollectionChanged += OnRedactionsChanged;

            // Subscribe to page changes to update redaction overlays
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(viewModel.CurrentPageIndex))
                {
                    UpdateRedactionOverlays();
                }
            };

            // Push the viewer's *visible* viewport (inside-the-scrollbars)
            // into the VM so Fit Width / Fit Page fit against what the user
            // actually sees, not the outer control bounds. Using outer
            // bounds gave a result ~16-20 DIPs too big — exactly the strip
            // a vertical scrollbar reserves — which made Fit Width pop a
            // horizontal scrollbar that then stole more space and broke
            // the fit recursively.
            if (_pdfViewerControl != null)
            {
                var initial = _pdfViewerControl.GetVisibleViewportSize();
                viewModel.ViewportWidth = initial.Width;
                viewModel.ViewportHeight = initial.Height;
                _pdfViewerControl.VisibleViewportChanged += (s, size) =>
                {
                    viewModel.ViewportWidth = size.Width;
                    viewModel.ViewportHeight = size.Height;
                };
            }
        }
    }

    private void OnSearchHighlightsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSearchHighlightsCanvas();
    }

    /// <summary>
    /// Theme-independent click handler for the outline TreeView. Walks
    /// up from the click target to find the nearest TreeViewItem and
    /// invokes JumpToOutline on its OutlineNode DataContext. Avoids the
    /// FluentAvalonia-specific chevron/content hit-test boundary that
    /// makes the SelectedItem path silently swallow clicks.
    /// </summary>
    private void OnOutlineTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var src = e.Source as Control;
        while (src != null)
        {
            if (src is Avalonia.Controls.TreeViewItem tvi &&
                tvi.DataContext is PdfEditor.Models.OutlineNode node)
            {
                vm.JumpToOutline(node);
                return;
            }
            src = src.Parent as Control;
        }
    }

    /// <summary>
    /// Fires whenever a thumbnail Image's effective visible area changes
    /// (item virtualisation, scrolling the strip, sidebar toggling). When
    /// the area is non-empty we ask the VM to ensure that page's thumbnail
    /// is loaded — VM dedupes already-loaded pages and coalesces concurrent
    /// requests, so we can call this freely.
    /// </summary>
    private void OnThumbnailViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        if (sender is not Image img) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (img.Tag is not int pageIndex) return;
        if (e.EffectiveViewport.Width <= 0 || e.EffectiveViewport.Height <= 0) return;

        // Fire-and-forget — VM handles its own dispatching to the UI thread.
        _ = vm.EnsureThumbnailLoadedAsync(pageIndex);
    }

    /// <summary>
    /// Enter inside the search box triggers an immediate search (skips
    /// the debounce). Escape closes the search bar.
    /// </summary>
    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            vm.FindCommand?.Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseSearchCommand?.Execute().Subscribe();
            e.Handled = true;
        }
    }

    private void UpdateSearchHighlightsCanvas()
    {
        if (_pdfViewerControl == null)
            return;

        _pdfViewerControl.ClearSearchHighlights();

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        foreach (var rect in viewModel.CurrentPageSearchHighlights)
        {
            _pdfViewerControl.AddSearchHighlight(rect);
        }
    }

    private void OnRedactionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRedactionOverlays();
    }

    private void UpdateRedactionOverlays()
    {
        if (_pdfViewerControl == null)
            return;

        _pdfViewerControl.ClearPendingRedactions();
        _pdfViewerControl.ClearAppliedRedactions();

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var currentPage = viewModel.CurrentPageIndex + 1; // DisplayPageNumber is 1-based

        // Draw pending redactions (red dashed border)
        foreach (var pending in viewModel.RedactionWorkflow.GetPendingForPage(currentPage))
        {
            _pdfViewerControl.AddPendingRedaction(pending.Area);
        }

        // Draw applied redactions (black solid rectangle)
        foreach (var applied in viewModel.RedactionWorkflow.GetAppliedForPage(currentPage))
        {
            _pdfViewerControl.AddAppliedRedaction(applied.Area);
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

        // Ctrl+F: Toggle search bar (and put focus in the input so the
        // user can type immediately). Without the focus hop the search
        // bar appears but keystrokes go to whatever was focused before
        // — which looks like "search doesn't do anything".
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.ToggleSearchCommand?.Execute().Subscribe();
            if (viewModel.IsSearchVisible)
            {
                var searchBox = this.FindControl<TextBox>("SearchTextBox");
                // The Border that hosts the TextBox just toggled
                // IsVisible — wait for the layout pass to finish before
                // we try to focus it.
                Dispatcher.UIThread.Post(() =>
                {
                    searchBox?.Focus();
                    searchBox?.SelectAll();
                }, DispatcherPriority.Background);
            }
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

        // R (unmodified): Toggle redaction mode (B2 keyboard shortcut)
        if (e.Key == Key.R && !e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            viewModel.ToggleRedactionModeCommand?.Execute().Subscribe();
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

    // ==================================================================================
    // PDF VIEWER CONTROL EVENT HANDLERS
    // ==================================================================================

    private void OnRedactionDrawn(object? sender, RedactionDrawnEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // The PdfViewerControl provides the area in image pixel coordinates
        viewModel.CurrentRedactionArea = e.Area;

        // Automatically apply the redaction when selection is completed
        if (e.Area.Width > 5 && e.Area.Height > 5)
        {
            viewModel.ApplyRedactionCommand.Execute().Subscribe();
        }
    }

    /// <summary>
    /// Internal link clicked in the page area. The destination page is
    /// 1-based; the VM tracks 0-based CurrentPageIndex.
    /// </summary>
    private void OnLinkClicked(object? sender, LinkClickedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var idx = e.PageNumber - 1;
        if (idx < 0 || idx >= viewModel.TotalPages) return;
        viewModel.CurrentPageIndex = idx;
    }

    private void OnTextSelected(object? sender, TextSelectedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Pre-fix this set CurrentTextSelectionArea (a 2D rect) and asked
        // the VM to re-extract text within the rect. The new text-line
        // selection path computes the actual text in the control via
        // letter hit-testing, so the event already carries the joined
        // string — feed it directly.
        viewModel.CurrentTextSelectionArea = e.Area;
        if (!string.IsNullOrEmpty(e.Text))
        {
            _ = viewModel.SetSelectedTextAndCopyAsync(e.Text);
        }
    }

    private void OnPageChanged(object? sender, PageChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Update ViewModel page index (convert from 1-based to 0-based)
        viewModel.CurrentPageIndex = e.PageNumber - 1;
    }

    /// <summary>
    /// Handle toast notifications from the ViewModel.
    /// Shows an InfoBar notification for 5 seconds, then auto-dismisses.
    /// </summary>
    private void OnToastRequested(object? sender, PdfEditor.Services.ToastService.ToastEventArgs e)
    {
        try
        {
            var infoBar = this.FindControl<FluentAvalonia.UI.Controls.InfoBar>("ToastInfoBar");
            if (infoBar == null)
                return;

            // Set severity based on toast severity level
            infoBar.Severity = e.Severity switch
            {
                PdfEditor.Services.ToastService.ToastSeverity.Error => FluentAvalonia.UI.Controls.InfoBarSeverity.Error,
                PdfEditor.Services.ToastService.ToastSeverity.Warning => FluentAvalonia.UI.Controls.InfoBarSeverity.Warning,
                PdfEditor.Services.ToastService.ToastSeverity.Success => FluentAvalonia.UI.Controls.InfoBarSeverity.Success,
                _ => FluentAvalonia.UI.Controls.InfoBarSeverity.Informational
            };

            // Set message and optional details
            infoBar.Title = e.Message;
            infoBar.Message = e.Details ?? string.Empty;

            // Show the InfoBar
            infoBar.IsOpen = true;

            // Auto-dismiss after 5 seconds
            var timer = new System.Timers.Timer(5000)
            {
                AutoReset = false
            };
            timer.Elapsed += (s, args) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    infoBar.IsOpen = false;
                    timer.Dispose();
                });
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Error displaying toast: {ex.Message}");
        }
    }
}
