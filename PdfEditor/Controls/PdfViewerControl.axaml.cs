using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pdfe.Core.Document;
using Pdfe.Rendering;
using PdfEditor.Imaging;
using SkiaSharp;

namespace PdfEditor.Controls;

/// <summary>
/// Reusable PDF viewer control with zoom, pan, and overlay support.
/// </summary>
public partial class PdfViewerControl : UserControl
{
    #region Dependency Properties

    /// <summary>
    /// The PDF document to display.
    /// </summary>
    public static readonly StyledProperty<PdfDocument?> DocumentProperty =
        AvaloniaProperty.Register<PdfViewerControl, PdfDocument?>(nameof(Document));

    public PdfDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public static readonly StyledProperty<int> CurrentPageProperty =
        AvaloniaProperty.Register<PdfViewerControl, int>(nameof(CurrentPage), defaultValue: 1);

    public int CurrentPage
    {
        get => GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    /// <summary>
    /// Zoom level (1.0 = 100%).
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<PdfViewerControl, double>(nameof(ZoomLevel), defaultValue: 1.0);

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    /// <summary>
    /// Interaction mode (None, Redaction, TextSelection, Pan).
    /// </summary>
    public static readonly StyledProperty<InteractionMode> InteractionModeProperty =
        AvaloniaProperty.Register<PdfViewerControl, InteractionMode>(nameof(InteractionMode));

    public InteractionMode InteractionMode
    {
        get => GetValue(InteractionModeProperty);
        set => SetValue(InteractionModeProperty, value);
    }

    /// <summary>
    /// Is page currently loading?
    /// </summary>
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<PdfViewerControl, bool>(nameof(IsLoading));

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        private set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>
    /// Does the control have an error?
    /// </summary>
    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<PdfViewerControl, bool>(nameof(HasError));

    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        private set => SetValue(HasErrorProperty, value);
    }

    /// <summary>
    /// Error message to display.
    /// </summary>
    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<PdfViewerControl, string?>(nameof(ErrorMessage));

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        private set => SetValue(ErrorMessageProperty, value);
    }

    /// <summary>
    /// Highlights for hidden-behind-overlay text to paint on top of the
    /// rendered page. Bound to a VM observable collection; whenever it
    /// changes, <see cref="RefreshHiddenTextOverlays"/> redraws them.
    /// </summary>
    public static readonly StyledProperty<System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>?> HiddenTextHighlightsProperty =
        AvaloniaProperty.Register<PdfViewerControl, System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>?>(nameof(HiddenTextHighlights));

    public System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>? HiddenTextHighlights
    {
        get => GetValue(HiddenTextHighlightsProperty);
        set => SetValue(HiddenTextHighlightsProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Fired when a redaction rectangle is drawn.
    /// </summary>
    public event EventHandler<RedactionDrawnEventArgs>? RedactionDrawn;

    /// <summary>
    /// Fired when text is selected.
    /// </summary>
    public event EventHandler<TextSelectedEventArgs>? TextSelected;

    /// <summary>
    /// Fired when the page changes.
    /// </summary>
    public event EventHandler<PageChangedEventArgs>? PageChanged;

    #endregion

    #region Fields

    private readonly SkiaRenderer _renderer;
    private Image? _pdfImage;
    private Canvas? _overlayCanvas;
    private Canvas? _interactionLayer;
    private LayoutTransformControl? _zoomHost;
    private ScaleTransform? _zoomScaleTransform;
    private ScrollViewer? _scrollViewer;
    private Grid? _loadingOverlay;
    private ProgressBar? _loadingProgressBar;
    private Grid? _errorOverlay;
    private TextBlock? _errorMessageText;
    private Point _dragStart;
    private bool _isDragging;

    // Default render DPI for the on-screen viewer. 200 DPI was overkill —
    // a US-Letter page at 200 DPI is 1700×2200 (3.7M px); at 120 DPI it's
    // 1020×1320 (1.3M px), 3× less rasterisation work, and the difference
    // is invisible at typical zoom levels.
    private const int DefaultRenderDpi = 120;

    // LRU bitmap cache so flipping back to a recently-viewed page is
    // instant. Capped small — bitmaps for a 200-page book can be ~6 MB
    // each in BGRA, so we trade a few tens of MB for snappy navigation.
    private const int PageCacheCapacity = 6;
    private readonly LinkedList<(int Page, int Dpi, WriteableBitmap Bmp)> _pageCache = new();

    // Tracks the in-flight render so rapid paging cancels stale work.
    private CancellationTokenSource? _renderCts;

    #endregion

    public PdfViewerControl()
    {
        InitializeComponent();
        _renderer = new SkiaRenderer();

        // Subscribe to property changes
        DocumentProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnDocumentChanged());
        CurrentPageProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnCurrentPageChanged());
        ZoomLevelProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnZoomLevelChanged());
        IsLoadingProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnLoadingStateChanged());
        HasErrorProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnErrorStateChanged());
        ErrorMessageProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnErrorMessageChanged());
        HiddenTextHighlightsProperty.Changed.AddClassHandler<PdfViewerControl>((control, e) =>
            control.OnHiddenTextHighlightsChanged(
                e.OldValue as System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>,
                e.NewValue as System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>));
    }

    private System.Collections.Specialized.INotifyCollectionChanged? _watchedHighlights;

    private void OnHiddenTextHighlightsChanged(
        System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>? oldValue,
        System.Collections.Generic.IEnumerable<PdfEditor.Models.HiddenTextHighlight>? newValue)
    {
        // If the bound value is an ObservableCollection, subscribe to its
        // changes so the overlay repaints when the VM adds/removes hits.
        if (_watchedHighlights != null)
        {
            _watchedHighlights.CollectionChanged -= OnHighlightsCollectionChanged;
            _watchedHighlights = null;
        }
        if (newValue is System.Collections.Specialized.INotifyCollectionChanged notify)
        {
            _watchedHighlights = notify;
            notify.CollectionChanged += OnHighlightsCollectionChanged;
        }
        RedrawHiddenTextOverlays();
    }

    private void OnHighlightsCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RedrawHiddenTextOverlays();

    private void RedrawHiddenTextOverlays()
    {
        var layer = this.FindControl<Canvas>("HiddenTextRevealLayer");
        if (layer == null) return;
        layer.Children.Clear();

        var highlights = HiddenTextHighlights;
        if (highlights == null) return;

        foreach (var h in highlights)
        {
            // Color code by source: yellow for structural (we have the
            // exact characters), orange for differential-OCR (recovered
            // from raster — confidence is OCR-typical, less certain).
            var (fill, stroke, ink) = h.Source == PdfEditor.Models.HiddenTextSource.DifferentialOcr
                ? (Color.FromArgb(220, 255, 165, 0),  // orange
                   Color.FromArgb(255, 200, 80, 0),
                   Color.FromArgb(255, 120, 40, 0))
                : (Color.FromArgb(230, 255, 255, 0),  // yellow
                   Color.FromArgb(255, 220, 20, 20),
                   Color.FromArgb(255, 180, 0, 0));

            var bg = new Rectangle
            {
                Width = Math.Max(h.ScreenBounds.Width, 8),
                Height = Math.Max(h.ScreenBounds.Height, 8),
                Fill = new SolidColorBrush(fill),
                Stroke = new SolidColorBrush(stroke),
                StrokeThickness = 2,
            };
            Canvas.SetLeft(bg, h.ScreenBounds.X);
            Canvas.SetTop(bg, h.ScreenBounds.Y);
            layer.Children.Add(bg);

            var label = new TextBlock
            {
                Text = h.Text,
                Foreground = new SolidColorBrush(ink),
                FontWeight = FontWeight.Bold,
                FontSize = Math.Max(10, h.ScreenBounds.Height * 0.75),
                TextWrapping = TextWrapping.NoWrap,
            };
            Canvas.SetLeft(label, h.ScreenBounds.X + 2);
            Canvas.SetTop(label, h.ScreenBounds.Y);
            layer.Children.Add(label);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        // Get references to named controls
        _pdfImage = this.FindControl<Image>("PdfImage");
        _overlayCanvas = this.FindControl<Canvas>("OverlayCanvas");
        _interactionLayer = this.FindControl<Canvas>("InteractionLayer");
        _zoomHost = this.FindControl<LayoutTransformControl>("ZoomHost");
        _scrollViewer = this.FindControl<ScrollViewer>("PdfScrollViewer");
        _loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
        _loadingProgressBar = this.FindControl<ProgressBar>("LoadingProgressBar");
        _errorOverlay = this.FindControl<Grid>("ErrorOverlay");
        _errorMessageText = this.FindControl<TextBlock>("ErrorMessageText");

        // Single scale transform on the LayoutTransformControl wrapper. Both
        // the Image and the OverlayCanvas live inside it, so they scale and
        // align together — no need for two parallel RenderTransforms.
        if (_zoomHost != null)
        {
            _zoomScaleTransform = _zoomHost.LayoutTransform as ScaleTransform;
        }

        // Set up interaction layer event handlers
        if (_interactionLayer != null)
        {
            _interactionLayer.PointerPressed += OnInteractionLayerPointerPressed;
            _interactionLayer.PointerMoved += OnInteractionLayerPointerMoved;
            _interactionLayer.PointerReleased += OnInteractionLayerPointerReleased;
        }

        // Surface viewport changes (scrollbars appearing/disappearing,
        // sidebars toggling, window resizes). Subscribe directly to the
        // ScrollViewer's Viewport AvaloniaProperty — it raises only on
        // actual value changes. Initial implementation used LayoutUpdated,
        // which fires on EVERY layout pass of the whole tree and created a
        // feedback loop with tooltips: hovering a button popped a tooltip,
        // tooltip layout fired LayoutUpdated, our handler pushed Viewport
        // (sometimes oscillating sub-pixel) to the VM, ReapplyFitModeIfNeeded
        // re-set ZoomLevel, triggering yet more layout. Result: button
        // tooltips flickered and the button was unclickable.
        if (_scrollViewer != null)
        {
            _viewportSubscription = _scrollViewer
                .GetObservable(ScrollViewer.ViewportProperty)
                .Subscribe(OnScrollViewerViewportChanged);
        }
    }

    /// <summary>
    /// The actual visible page area, in DIPs, *inside* the scroll bars.
    /// Use this — not the outer control's Bounds — for fit-zoom math, so
    /// the answer doesn't include the strip a vertical scrollbar steals.
    /// </summary>
    public Size GetVisibleViewportSize()
    {
        if (_scrollViewer != null)
        {
            var v = _scrollViewer.Viewport;
            if (v.Width > 0 && v.Height > 0) return v;
        }
        return Bounds.Size;
    }

    /// <summary>Raised when the inside-the-scrollbars viewport size changes.</summary>
    public event EventHandler<Size>? VisibleViewportChanged;

    private Size _lastReportedViewport;
    private IDisposable? _viewportSubscription;

    private void OnScrollViewerViewportChanged(Size newViewport)
    {
        if (newViewport.Width <= 0 || newViewport.Height <= 0) return;
        if (Math.Abs(newViewport.Width - _lastReportedViewport.Width) < 0.5 &&
            Math.Abs(newViewport.Height - _lastReportedViewport.Height) < 0.5) return;
        _lastReportedViewport = newViewport;
        VisibleViewportChanged?.Invoke(this, newViewport);
    }

    private void OnZoomLevelChanged()
    {
        if (_zoomScaleTransform != null)
        {
            _zoomScaleTransform.ScaleX = ZoomLevel;
            _zoomScaleTransform.ScaleY = ZoomLevel;
        }
    }

    private void OnLoadingStateChanged()
    {
        // Show the thin top-of-viewer progress bar while a render is in
        // flight. (The full-screen overlay is kept hidden — it was always
        // visually overpowering for sub-second renders and is replaced by
        // the indeterminate ProgressBar.)
        if (_loadingProgressBar != null)
            _loadingProgressBar.IsVisible = IsLoading;
        if (_loadingOverlay != null)
            _loadingOverlay.IsVisible = false;
    }

    private void OnErrorStateChanged()
    {
        if (_errorOverlay != null)
        {
            _errorOverlay.IsVisible = HasError;
        }
    }

    private void OnErrorMessageChanged()
    {
        if (_errorMessageText != null)
        {
            _errorMessageText.Text = ErrorMessage;
        }
    }

    #region Rendering

    private async void OnDocumentChanged()
    {
        // Drop cached bitmaps from the prior document (would render at wrong
        // pages otherwise) and cancel any render that was still finishing
        // for that document.
        InvalidatePageCache();
        _renderCts?.Cancel();

        if (Document != null)
        {
            await RenderCurrentPageAsync();
        }
        else
        {
            ClearDisplay();
        }
    }

    private async void OnCurrentPageChanged()
    {
        if (Document != null && CurrentPage >= 1 && CurrentPage <= Document.PageCount)
        {
            await RenderCurrentPageAsync();
            PageChanged?.Invoke(this, new PageChangedEventArgs(CurrentPage));
        }
    }

    private async Task RenderCurrentPageAsync()
    {
        if (Document == null || CurrentPage < 1 || CurrentPage > Document.PageCount)
            return;

        // Cache hit short-circuits the renderer entirely — this is the
        // common case for backwards-paging, undoing redactions, and
        // toggling overlays. Set Image.Source immediately so the user
        // doesn't even see a loading flicker.
        if (TryGetCached(CurrentPage, DefaultRenderDpi, out var cached))
        {
            if (_pdfImage != null) _pdfImage.Source = cached;
            HasError = false;
            ErrorMessage = null;
            return;
        }

        // Cancel any prior in-flight render. If the user is paging through
        // quickly we'd rather skip the now-stale page than make them wait.
        _renderCts?.Cancel();
        var cts = new CancellationTokenSource();
        _renderCts = cts;
        var token = cts.Token;

        var pageNumber = CurrentPage;
        var doc = Document;
        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            var skBitmap = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var page = doc.GetPage(pageNumber);
                var options = new Pdfe.Rendering.RenderOptions { Dpi = DefaultRenderDpi };
                return _renderer.RenderPage(page, options);
            }, token);

            try
            {
                // The user may have paged again while we were rendering —
                // honour the cancellation rather than overwriting the
                // freshly-rendered new page with the stale one.
                if (token.IsCancellationRequested) return;

                var bitmap = SkiaInterop.ToAvaloniaBitmap(skBitmap);
                if (bitmap != null)
                {
                    AddToCache(pageNumber, DefaultRenderDpi, bitmap);
                    if (_pdfImage != null) _pdfImage.Source = bitmap;
                }
            }
            finally
            {
                skBitmap?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when paging quickly — drop the stale render silently.
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to render page: {ex.Message}";
        }
        finally
        {
            // Only the most-recent render should clear IsLoading; older
            // races would otherwise flicker the overlay back on.
            if (_renderCts == cts)
                IsLoading = false;
        }
    }

    private bool TryGetCached(int page, int dpi, out WriteableBitmap? bmp)
    {
        for (var node = _pageCache.First; node != null; node = node.Next)
        {
            if (node.Value.Page == page && node.Value.Dpi == dpi)
            {
                // Move to front (LRU touch).
                _pageCache.Remove(node);
                _pageCache.AddFirst(node);
                bmp = node.Value.Bmp;
                return true;
            }
        }
        bmp = null;
        return false;
    }

    private void AddToCache(int page, int dpi, WriteableBitmap bmp)
    {
        // Replace existing entry for same key (e.g. re-render after edit).
        for (var node = _pageCache.First; node != null; node = node.Next)
        {
            if (node.Value.Page == page && node.Value.Dpi == dpi)
            {
                node.Value.Bmp.Dispose();
                _pageCache.Remove(node);
                break;
            }
        }
        _pageCache.AddFirst((page, dpi, bmp));
        while (_pageCache.Count > PageCacheCapacity)
        {
            var last = _pageCache.Last!;
            _pageCache.RemoveLast();
            last.Value.Bmp.Dispose();
        }
    }

    /// <summary>Drop the cached bitmaps — call when document changes or content edits invalidate prior renders.</summary>
    public void InvalidatePageCache()
    {
        foreach (var entry in _pageCache) entry.Bmp.Dispose();
        _pageCache.Clear();
    }

    private void ClearDisplay()
    {
        if (_pdfImage != null)
        {
            _pdfImage.Source = null;
        }
    }

    #endregion

    #region Interaction

    private void OnInteractionLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (InteractionMode == InteractionMode.None)
            return;

        var point = e.GetPosition(_interactionLayer);
        _dragStart = point;
        _isDragging = true;

        e.Handled = true;
    }

    private void OnInteractionLayerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || InteractionMode == InteractionMode.None)
            return;

        var currentPoint = e.GetPosition(_interactionLayer);

        if (InteractionMode == InteractionMode.Redaction)
        {
            // Draw temporary redaction rectangle
            DrawTemporaryRedactionRectangle(_dragStart, currentPoint);
        }
        else if (InteractionMode == InteractionMode.TextSelection)
        {
            // Draw temporary selection rectangle
            DrawTemporarySelectionRectangle(_dragStart, currentPoint);
        }

        e.Handled = true;
    }

    private void OnInteractionLayerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;

        var endPoint = e.GetPosition(_interactionLayer);
        var rect = CreateRect(_dragStart, endPoint);

        // Adjust for zoom level
        var adjustedRect = new Rect(
            rect.X / ZoomLevel,
            rect.Y / ZoomLevel,
            rect.Width / ZoomLevel,
            rect.Height / ZoomLevel
        );

        if (InteractionMode == InteractionMode.Redaction)
        {
            RedactionDrawn?.Invoke(this, new RedactionDrawnEventArgs(adjustedRect));
        }
        else if (InteractionMode == InteractionMode.TextSelection)
        {
            TextSelected?.Invoke(this, new TextSelectedEventArgs(adjustedRect));
        }

        // Clear temporary drawings
        ClearTemporaryDrawings();

        e.Handled = true;
    }

    private static Rect CreateRect(Point p1, Point p2)
    {
        var left = Math.Min(p1.X, p2.X);
        var top = Math.Min(p1.Y, p2.Y);
        var right = Math.Max(p1.X, p2.X);
        var bottom = Math.Max(p1.Y, p2.Y);

        return new Rect(left, top, right - left, bottom - top);
    }

    private Rectangle? _tempRedactionRect;
    private Rectangle? _tempSelectionRect;

    private void DrawTemporaryRedactionRectangle(Point start, Point end)
    {
        if (_interactionLayer == null) return;

        var rect = CreateRect(start, end);

        if (_tempRedactionRect == null)
        {
            _tempRedactionRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)), // Semi-transparent black
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                StrokeDashArray = new AvaloniaList<double> { 5, 5 }
            };
            _interactionLayer.Children.Add(_tempRedactionRect);
        }

        Canvas.SetLeft(_tempRedactionRect, rect.X);
        Canvas.SetTop(_tempRedactionRect, rect.Y);
        _tempRedactionRect.Width = rect.Width;
        _tempRedactionRect.Height = rect.Height;
        _tempRedactionRect.IsVisible = true;
    }

    private void DrawTemporarySelectionRectangle(Point start, Point end)
    {
        if (_interactionLayer == null) return;

        var rect = CreateRect(start, end);

        if (_tempSelectionRect == null)
        {
            _tempSelectionRect = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x4C, 0xAF, 0x50)), // Semi-transparent green
                Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50)),
                StrokeThickness = 2,
                StrokeDashArray = new AvaloniaList<double> { 5, 5 }
            };
            _interactionLayer.Children.Add(_tempSelectionRect);
        }

        Canvas.SetLeft(_tempSelectionRect, rect.X);
        Canvas.SetTop(_tempSelectionRect, rect.Y);
        _tempSelectionRect.Width = rect.Width;
        _tempSelectionRect.Height = rect.Height;
        _tempSelectionRect.IsVisible = true;
    }

    private void ClearTemporaryDrawings()
    {
        if (_tempRedactionRect != null)
        {
            _tempRedactionRect.IsVisible = false;
        }

        if (_tempSelectionRect != null)
        {
            _tempSelectionRect.IsVisible = false;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Navigate to the next page.
    /// </summary>
    public void NextPage()
    {
        if (Document != null && CurrentPage < Document.PageCount)
        {
            CurrentPage++;
        }
    }

    /// <summary>
    /// Navigate to the previous page.
    /// </summary>
    public void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
        }
    }

    /// <summary>
    /// Zoom in by 25%.
    /// </summary>
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.25, 5.0);
    }

    /// <summary>
    /// Zoom out by 25%.
    /// </summary>
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.25, 0.1);
    }

    /// <summary>
    /// Reset zoom to 100%.
    /// </summary>
    public void ZoomToActualSize()
    {
        ZoomLevel = 1.0;
    }

    /// <summary>
    /// Add a search highlight rectangle at the specified coordinates.
    /// </summary>
    public void AddSearchHighlight(Rect area)
    {
        var searchLayer = this.FindControl<Canvas>("SearchHighlightsLayer");
        if (searchLayer == null) return;

        var highlight = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0x00)), // Semi-transparent yellow
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x98, 0x00)), // Orange border
            StrokeThickness = 1,
            Width = area.Width,
            Height = area.Height
        };

        Canvas.SetLeft(highlight, area.X);
        Canvas.SetTop(highlight, area.Y);
        searchLayer.Children.Add(highlight);
    }

    /// <summary>
    /// Clear all search highlights.
    /// </summary>
    public void ClearSearchHighlights()
    {
        var searchLayer = this.FindControl<Canvas>("SearchHighlightsLayer");
        searchLayer?.Children.Clear();
    }

    /// <summary>
    /// Add a pending redaction overlay at the specified coordinates.
    /// </summary>
    public void AddPendingRedaction(Rect area)
    {
        var redactionLayer = this.FindControl<Canvas>("PendingRedactionsLayer");
        if (redactionLayer == null) return;

        var rect = new Rectangle
        {
            Fill = Brushes.Transparent,
            Stroke = Brushes.Red,
            StrokeThickness = 2,
            StrokeDashArray = new AvaloniaList<double> { 5, 3 },
            Width = area.Width,
            Height = area.Height
        };

        Canvas.SetLeft(rect, area.X);
        Canvas.SetTop(rect, area.Y);
        redactionLayer.Children.Add(rect);
    }

    /// <summary>
    /// Clear all pending redaction overlays.
    /// </summary>
    public void ClearPendingRedactions()
    {
        var redactionLayer = this.FindControl<Canvas>("PendingRedactionsLayer");
        redactionLayer?.Children.Clear();
    }

    /// <summary>
    /// Add an applied redaction overlay (black rectangle) at the specified coordinates.
    /// </summary>
    public void AddAppliedRedaction(Rect area)
    {
        var appliedLayer = this.FindControl<Canvas>("AppliedRedactionsLayer");
        if (appliedLayer == null) return;

        var rect = new Rectangle
        {
            Fill = Brushes.Black,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            Width = area.Width,
            Height = area.Height
        };

        Canvas.SetLeft(rect, area.X);
        Canvas.SetTop(rect, area.Y);
        appliedLayer.Children.Add(rect);
    }

    /// <summary>
    /// Clear all applied redaction overlays.
    /// </summary>
    public void ClearAppliedRedactions()
    {
        var appliedLayer = this.FindControl<Canvas>("AppliedRedactionsLayer");
        appliedLayer?.Children.Clear();
    }

    /// <summary>
    /// Clear all overlays (search, pending redactions, applied redactions).
    /// </summary>
    public void ClearAllOverlays()
    {
        ClearSearchHighlights();
        ClearPendingRedactions();
        ClearAppliedRedactions();
    }

    #endregion
}

#region Event Args

/// <summary>
/// Event arguments for redaction drawn event.
/// </summary>
public class RedactionDrawnEventArgs : EventArgs
{
    public Rect Area { get; }

    public RedactionDrawnEventArgs(Rect area)
    {
        Area = area;
    }
}

/// <summary>
/// Event arguments for text selected event.
/// </summary>
public class TextSelectedEventArgs : EventArgs
{
    public Rect Area { get; }

    public TextSelectedEventArgs(Rect area)
    {
        Area = area;
    }
}

/// <summary>
/// Event arguments for page changed event.
/// </summary>
public class PageChangedEventArgs : EventArgs
{
    public int PageNumber { get; }

    public PageChangedEventArgs(int pageNumber)
    {
        PageNumber = pageNumber;
    }
}

#endregion

#region Enums

/// <summary>
/// Interaction modes for the PDF viewer.
/// </summary>
public enum InteractionMode
{
    /// <summary>
    /// No interaction (view only).
    /// </summary>
    None,

    /// <summary>
    /// Draw redaction rectangles.
    /// </summary>
    Redaction,

    /// <summary>
    /// Select text areas.
    /// </summary>
    TextSelection,

    /// <summary>
    /// Pan/scroll the document.
    /// </summary>
    Pan
}

#endregion
