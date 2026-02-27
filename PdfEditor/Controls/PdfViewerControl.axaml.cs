using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pdfe.Core.Document;
using Pdfe.Rendering;
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
    private ScaleTransform? _imageScaleTransform;
    private ScaleTransform? _overlayScaleTransform;
    private Grid? _loadingOverlay;
    private Grid? _errorOverlay;
    private TextBlock? _errorMessageText;
    private Point _dragStart;
    private bool _isDragging;

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
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        // Get references to named controls
        _pdfImage = this.FindControl<Image>("PdfImage");
        _overlayCanvas = this.FindControl<Canvas>("OverlayCanvas");
        _interactionLayer = this.FindControl<Canvas>("InteractionLayer");
        _loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
        _errorOverlay = this.FindControl<Grid>("ErrorOverlay");
        _errorMessageText = this.FindControl<TextBlock>("ErrorMessageText");

        // Get scale transforms from render transforms
        if (_pdfImage != null)
        {
            _imageScaleTransform = _pdfImage.RenderTransform as ScaleTransform;
        }

        if (_overlayCanvas != null)
        {
            _overlayScaleTransform = _overlayCanvas.RenderTransform as ScaleTransform;
        }

        // Set up interaction layer event handlers
        if (_interactionLayer != null)
        {
            _interactionLayer.PointerPressed += OnInteractionLayerPointerPressed;
            _interactionLayer.PointerMoved += OnInteractionLayerPointerMoved;
            _interactionLayer.PointerReleased += OnInteractionLayerPointerReleased;
        }
    }

    private void OnZoomLevelChanged()
    {
        if (_imageScaleTransform != null)
        {
            _imageScaleTransform.ScaleX = ZoomLevel;
            _imageScaleTransform.ScaleY = ZoomLevel;
        }

        if (_overlayScaleTransform != null)
        {
            _overlayScaleTransform.ScaleX = ZoomLevel;
            _overlayScaleTransform.ScaleY = ZoomLevel;
        }
    }

    private void OnLoadingStateChanged()
    {
        if (_loadingOverlay != null)
        {
            _loadingOverlay.IsVisible = IsLoading;
        }
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

        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = null;

            // Render page on background thread
            var page = Document.GetPage(CurrentPage);
            var skBitmap = await Task.Run(() => _renderer.RenderPage(page));

            // Convert to Avalonia bitmap on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var avaloniaBitmap = ConvertToAvaloniaBitmap(skBitmap);
                if (_pdfImage != null)
                {
                    _pdfImage.Source = avaloniaBitmap;
                }
                skBitmap.Dispose();
            });
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to render page: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static Bitmap ConvertToAvaloniaBitmap(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new Bitmap(stream);
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

    private void DrawTemporaryRedactionRectangle(Point start, Point end)
    {
        // TODO: Implement temporary rectangle drawing
        // This will be part of Phase 2
    }

    private void DrawTemporarySelectionRectangle(Point start, Point end)
    {
        // TODO: Implement temporary rectangle drawing
        // This will be part of Phase 2
    }

    private void ClearTemporaryDrawings()
    {
        // TODO: Clear temporary overlay elements
        // This will be part of Phase 2
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
