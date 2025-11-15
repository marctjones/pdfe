using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PdfEditor.Services;

/// <summary>
/// Service for rendering PDF pages to images
/// Uses PDFtoImage (MIT License) which wraps PDFium (BSD-3-Clause)
/// </summary>
public class PdfRenderService
{
    private readonly ILogger<PdfRenderService> _logger;

    public PdfRenderService(ILogger<PdfRenderService> logger)
    {
        _logger = logger;
        _logger.LogDebug("PdfRenderService instance created");
    }

    /// <summary>
    /// Render a specific page of a PDF to a bitmap
    /// </summary>
    public async Task<Bitmap?> RenderPageAsync(string pdfPath, int pageIndex, int dpi = 150)
    {
        _logger.LogInformation("Rendering page {PageIndex} from {FileName} at {Dpi} DPI",
            pageIndex, Path.GetFileName(pdfPath), dpi);

        var sw = Stopwatch.StartNew();

        return await Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Reading PDF file into memory stream");
                using var fileStream = File.OpenRead(pdfPath);
                using var memoryStream = new MemoryStream();
                fileStream.CopyTo(memoryStream);
                memoryStream.Position = 0;

                return RenderPageFromStream(memoryStream, pageIndex, dpi, sw);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Error rendering page {PageIndex} from {FileName} after {ElapsedMs}ms",
                    pageIndex, Path.GetFileName(pdfPath), sw.ElapsedMilliseconds);
                return null;
            }
        });
    }

    /// <summary>
    /// Render a specific page from a PDF stream (for in-memory documents)
    /// </summary>
    public async Task<Bitmap?> RenderPageFromStreamAsync(Stream pdfStream, int pageIndex, int dpi = 150)
    {
        _logger.LogInformation("Rendering page {PageIndex} from stream at {Dpi} DPI", pageIndex, dpi);
        var sw = Stopwatch.StartNew();

        return await Task.Run(() => RenderPageFromStream(pdfStream, pageIndex, dpi, sw));
    }

    private Bitmap? RenderPageFromStream(Stream pdfStream, int pageIndex, int dpi, Stopwatch sw)
    {
        try
        {
            _logger.LogDebug("Creating RenderOptions with DPI: {Dpi}", dpi);
            var options = new RenderOptions(Dpi: dpi);

            _logger.LogDebug("Converting PDF page to SKBitmap from stream");
            var skBitmap = Conversion.ToImage(pdfStream, page: pageIndex, options: options);

            if (skBitmap == null)
            {
                _logger.LogWarning("Rendering returned null for page {PageIndex}", pageIndex);
                return null;
            }

            _logger.LogDebug("SKBitmap created: {Width}x{Height}", skBitmap.Width, skBitmap.Height);

            // Convert SkiaSharp bitmap to Avalonia bitmap
            var avaBitmap = ConvertSkBitmapToAvalonia(skBitmap);

            sw.Stop();
            _logger.LogInformation("Page {PageIndex} rendered successfully in {ElapsedMs}ms",
                pageIndex, sw.ElapsedMilliseconds);

            return avaBitmap;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error rendering page {PageIndex} from stream after {ElapsedMs}ms",
                pageIndex, sw.ElapsedMilliseconds);
            return null;
        }
    }

    /// <summary>
    /// Render a page as a thumbnail
    /// </summary>
    public async Task<Bitmap?> RenderThumbnailAsync(string pdfPath, int pageIndex, int width = 200)
    {
        _logger.LogDebug("Rendering thumbnail for page {PageIndex}, target width: {Width}", pageIndex, width);

        // Calculate DPI for thumbnail - lower DPI for faster rendering
        int thumbnailDpi = 72; // Standard screen DPI
        return await RenderPageAsync(pdfPath, pageIndex, thumbnailDpi);
    }

    /// <summary>
    /// Convert SkiaSharp bitmap to Avalonia bitmap
    /// </summary>
    private Bitmap ConvertSkBitmapToAvalonia(SKBitmap skBitmap)
    {
        _logger.LogDebug("Converting SKBitmap to Avalonia Bitmap");

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);

        var bitmap = new Bitmap(stream);
        _logger.LogDebug("Conversion complete. Bitmap size: {Width}x{Height}",
            bitmap.PixelSize.Width, bitmap.PixelSize.Height);

        return bitmap;
    }

    /// <summary>
    /// Get page dimensions without rendering
    /// </summary>
    public (double Width, double Height) GetPageDimensions(string pdfPath, int pageIndex)
    {
        _logger.LogDebug("Getting dimensions for page {PageIndex}", pageIndex);

        try
        {
            using var fileStream = File.OpenRead(pdfPath);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            var options = new RenderOptions(Dpi: 72);
            using var bitmap = Conversion.ToImage(memoryStream, page: pageIndex, options: options);

            _logger.LogDebug("Page dimensions: {Width}x{Height}", bitmap.Width, bitmap.Height);
            return (bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get page dimensions for page {PageIndex}, using default", pageIndex);
            return (612, 792); // Default Letter size in points
        }
    }
}
