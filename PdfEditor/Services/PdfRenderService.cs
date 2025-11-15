using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PDFtoImage;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PdfEditor.Services;

/// <summary>
/// Service for rendering PDF pages to images
/// Uses PDFtoImage (MIT License) which wraps PDFium (BSD-3-Clause)
/// </summary>
public class PdfRenderService
{
    /// <summary>
    /// Render a specific page of a PDF to a bitmap
    /// </summary>
    public async Task<Bitmap?> RenderPageAsync(string pdfPath, int pageIndex, int dpi = 150)
    {
        return await Task.Run(() =>
        {
            try
            {
                // PDFtoImage uses 1-based page indexing, we use 0-based
                var skBitmap = Conversion.ToImage(pdfPath, page: pageIndex, dpi: dpi);
                
                if (skBitmap == null)
                    return null;

                // Convert SkiaSharp bitmap to Avalonia bitmap
                return ConvertSkBitmapToAvalonia(skBitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rendering page {pageIndex}: {ex.Message}");
                return null;
            }
        });
    }

    /// <summary>
    /// Render a page as a thumbnail
    /// </summary>
    public async Task<Bitmap?> RenderThumbnailAsync(string pdfPath, int pageIndex, int width = 200)
    {
        // Calculate DPI for thumbnail - lower DPI for faster rendering
        int thumbnailDpi = 72; // Standard screen DPI
        return await RenderPageAsync(pdfPath, pageIndex, thumbnailDpi);
    }

    /// <summary>
    /// Convert SkiaSharp bitmap to Avalonia bitmap
    /// </summary>
    private Bitmap ConvertSkBitmapToAvalonia(SKBitmap skBitmap)
    {
        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(stream);
    }

    /// <summary>
    /// Get page dimensions without rendering
    /// </summary>
    public (double Width, double Height) GetPageDimensions(string pdfPath, int pageIndex)
    {
        try
        {
            using var bitmap = Conversion.ToImage(pdfPath, page: pageIndex, dpi: 72);
            return (bitmap.Width, bitmap.Height);
        }
        catch
        {
            return (612, 792); // Default Letter size in points
        }
    }
}
