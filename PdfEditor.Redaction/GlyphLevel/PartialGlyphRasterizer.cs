using SkiaSharp;
using PDFtoImage;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Renders partial glyph regions to high-resolution images for preservation
/// when the glyph partially overlaps a redaction area.
/// </summary>
public class PartialGlyphRasterizer : IDisposable
{
    private readonly byte[] _pdfBytes;
    private readonly int _pageIndex;
    private readonly int _dpi;
    private readonly double _pageHeight;
    private SKBitmap? _cachedPageImage;

    /// <summary>
    /// Create a rasterizer for a specific page.
    /// </summary>
    /// <param name="pdfBytes">The PDF document bytes.</param>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="pageHeight">Page height in points (for coordinate conversion).</param>
    /// <param name="dpi">Resolution for rendering (default 300).</param>
    public PartialGlyphRasterizer(byte[] pdfBytes, int pageIndex, double pageHeight, int dpi = 300)
    {
        _pdfBytes = pdfBytes ?? throw new ArgumentNullException(nameof(pdfBytes));
        _pageIndex = pageIndex;
        _pageHeight = pageHeight;
        _dpi = dpi;
    }

    /// <summary>
    /// Render a glyph region to a bitmap, clipped to exclude the redaction area.
    /// </summary>
    /// <param name="glyphBounds">The glyph bounding box in PDF coordinates.</param>
    /// <param name="redactionArea">The redaction area in PDF coordinates.</param>
    /// <returns>The rendered and clipped bitmap, or null if rendering fails.</returns>
    public SKBitmap? RenderGlyphRegion(PdfRectangle glyphBounds, PdfRectangle redactionArea)
    {
        try
        {
            // Ensure we have a cached page image
            EnsurePageImageCached();

            if (_cachedPageImage == null)
                return null;

            // Convert PDF coordinates to image coordinates
            // PDF: bottom-left origin, points
            // Image: top-left origin, pixels
            var imageRect = PdfToImageCoordinates(glyphBounds);

            // Clamp to image bounds
            int x = Math.Max(0, Math.Min(imageRect.X, _cachedPageImage.Width - 1));
            int y = Math.Max(0, Math.Min(imageRect.Y, _cachedPageImage.Height - 1));
            int width = Math.Min(imageRect.Width, _cachedPageImage.Width - x);
            int height = Math.Min(imageRect.Height, _cachedPageImage.Height - y);

            if (width <= 0 || height <= 0)
                return null;

            // Extract the glyph region
            var glyphImage = new SKBitmap(width, height);
            using (var canvas = new SKCanvas(glyphImage))
            {
                var srcRect = new SKRect(x, y, x + width, y + height);
                var dstRect = new SKRect(0, 0, width, height);
                canvas.DrawBitmap(_cachedPageImage, srcRect, dstRect);
            }

            return glyphImage;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Render a glyph region with redaction area masked out.
    /// The area inside the redaction will be transparent.
    /// </summary>
    /// <param name="glyphBounds">The glyph bounding box in PDF coordinates.</param>
    /// <param name="redactionArea">The redaction area in PDF coordinates (will be masked).</param>
    /// <returns>The rendered bitmap with redaction area masked, or null if rendering fails.</returns>
    public SKBitmap? RenderGlyphWithRedactionMask(PdfRectangle glyphBounds, PdfRectangle redactionArea)
    {
        var glyphImage = RenderGlyphRegion(glyphBounds, redactionArea);
        if (glyphImage == null)
            return null;

        try
        {
            // Calculate the intersection of glyph and redaction in image coordinates
            // relative to the glyph image
            var glyphImageRect = PdfToImageCoordinates(glyphBounds);
            var redactionImageRect = PdfToImageCoordinates(redactionArea);

            // Calculate redaction area relative to the glyph image
            int maskX = redactionImageRect.X - glyphImageRect.X;
            int maskY = redactionImageRect.Y - glyphImageRect.Y;
            int maskWidth = redactionImageRect.Width;
            int maskHeight = redactionImageRect.Height;

            // Clamp mask to glyph image bounds
            int clippedX = Math.Max(0, maskX);
            int clippedY = Math.Max(0, maskY);
            int clippedRight = Math.Min(glyphImage.Width, maskX + maskWidth);
            int clippedBottom = Math.Min(glyphImage.Height, maskY + maskHeight);
            int clippedWidth = Math.Max(0, clippedRight - clippedX);
            int clippedHeight = Math.Max(0, clippedBottom - clippedY);

            if (clippedWidth <= 0 || clippedHeight <= 0)
            {
                // No intersection, return original image
                return glyphImage;
            }

            // Create output bitmap with alpha channel
            var maskedImage = new SKBitmap(glyphImage.Width, glyphImage.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(maskedImage))
            {
                // Draw the glyph image
                canvas.DrawBitmap(glyphImage, 0, 0);

                // Mask out the redaction area (make transparent)
                var maskRect = new SKRect(clippedX, clippedY, clippedRight, clippedBottom);
                using var clearPaint = new SKPaint
                {
                    BlendMode = SKBlendMode.Clear
                };
                canvas.DrawRect(maskRect, clearPaint);
            }

            // Dispose original image since we created a new one
            glyphImage.Dispose();
            return maskedImage;
        }
        catch (Exception)
        {
            glyphImage.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Get the visible region of a glyph (the part outside the redaction area).
    /// Returns multiple rectangles if the redaction splits the visible region.
    /// </summary>
    /// <param name="glyphBounds">The glyph bounding box in PDF coordinates.</param>
    /// <param name="redactionArea">The redaction area in PDF coordinates.</param>
    /// <returns>List of visible regions in PDF coordinates.</returns>
    public static List<PdfRectangle> GetVisibleRegions(PdfRectangle glyphBounds, PdfRectangle redactionArea)
    {
        var visibleRegions = new List<PdfRectangle>();

        // If no intersection, the entire glyph is visible
        if (!glyphBounds.IntersectsWith(redactionArea))
        {
            visibleRegions.Add(glyphBounds);
            return visibleRegions;
        }

        // Calculate the intersection
        double intersectLeft = Math.Max(glyphBounds.Left, redactionArea.Left);
        double intersectRight = Math.Min(glyphBounds.Right, redactionArea.Right);
        double intersectBottom = Math.Max(glyphBounds.Bottom, redactionArea.Bottom);
        double intersectTop = Math.Min(glyphBounds.Top, redactionArea.Top);

        // Add left region if exists
        if (glyphBounds.Left < intersectLeft)
        {
            visibleRegions.Add(new PdfRectangle(
                glyphBounds.Left, glyphBounds.Bottom,
                intersectLeft, glyphBounds.Top
            ));
        }

        // Add right region if exists
        if (glyphBounds.Right > intersectRight)
        {
            visibleRegions.Add(new PdfRectangle(
                intersectRight, glyphBounds.Bottom,
                glyphBounds.Right, glyphBounds.Top
            ));
        }

        // Add bottom region if exists (excluding corners already counted)
        if (glyphBounds.Bottom < intersectBottom)
        {
            visibleRegions.Add(new PdfRectangle(
                intersectLeft, glyphBounds.Bottom,
                intersectRight, intersectBottom
            ));
        }

        // Add top region if exists (excluding corners already counted)
        if (glyphBounds.Top > intersectTop)
        {
            visibleRegions.Add(new PdfRectangle(
                intersectLeft, intersectTop,
                intersectRight, glyphBounds.Top
            ));
        }

        return visibleRegions;
    }

    private void EnsurePageImageCached()
    {
        if (_cachedPageImage != null)
            return;

        using var stream = new MemoryStream(_pdfBytes);
        var renderOptions = new RenderOptions(Dpi: _dpi);
        // PDFtoImage relies on PDFium which is only available on desktop platforms
        // This is intentional - partial glyph rasterization is a desktop feature
#pragma warning disable CA1416 // Validate platform compatibility
        _cachedPageImage = Conversion.ToImage(stream, leaveOpen: false, page: _pageIndex, options: renderOptions);
#pragma warning restore CA1416
    }

    private (int X, int Y, int Width, int Height) PdfToImageCoordinates(PdfRectangle pdfRect)
    {
        // Convert from PDF coordinates (points, bottom-left origin)
        // to image coordinates (pixels, top-left origin)
        double scale = _dpi / 72.0;  // 72 points per inch

        // PDF Y is measured from bottom, image Y is measured from top
        int x = (int)(pdfRect.Left * scale);
        int y = (int)((_pageHeight - pdfRect.Top) * scale);
        int width = (int)(pdfRect.Width * scale);
        int height = (int)(pdfRect.Height * scale);

        return (x, y, width, height);
    }

    public void Dispose()
    {
        _cachedPageImage?.Dispose();
        _cachedPageImage = null;
    }
}
