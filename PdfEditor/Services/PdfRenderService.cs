using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
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
    private int _maxCacheEntries = 20;
    private long _cacheHits;
    private long _cacheMisses;

    private record RenderCacheEntry(byte[] PngData, int Width, int Height, DateTime LastAccessUtc);
    private readonly ConcurrentDictionary<string, RenderCacheEntry> _cache = new();

    public PdfRenderService(ILogger<PdfRenderService> logger)
    {
        _logger = logger;
        _logger.LogDebug("PdfRenderService instance created");
        // Cache size is now configured through UI preferences (default: 20)
    }

    /// <summary>
    /// Maximum number of cached render entries. Defaults to 20.
    /// </summary>
    public int MaxCacheEntries
    {
        get => _maxCacheEntries;
        set => _maxCacheEntries = Math.Max(1, value);
    }

    /// <summary>
    /// Clear the render cache (force re-render on next request).
    /// </summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>
    /// Get cache statistics (current entries, max limit).
    /// </summary>
    public (int Count, int Max, long Hits, long Misses) GetCacheStats() => (_cache.Count, _maxCacheEntries, _cacheHits, _cacheMisses);

    /// <summary>
    /// Render a specific page of a PDF to a bitmap
    /// </summary>
    public async Task<Bitmap?> RenderPageAsync(string pdfPath, int pageIndex, int dpi = 150)
    {
        var cacheKey = BuildCacheKey(pdfPath, pageIndex, dpi);
        if (TryGetFromCache(cacheKey, out var cachedBitmap))
        {
            _logger.LogDebug("Cache hit for {File} page {Page} @ {Dpi} DPI", Path.GetFileName(pdfPath), pageIndex, dpi);
            return cachedBitmap;
        }

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

                return RenderPageFromStream(memoryStream, pageIndex, dpi, sw, cacheKey);
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

        return await Task.Run(() => RenderPageFromStream(pdfStream, pageIndex, dpi, sw, cacheKey: null));
    }

    private Bitmap? RenderPageFromStream(Stream pdfStream, int pageIndex, int dpi, Stopwatch sw, string? cacheKey)
    {
        try
        {
            _logger.LogDebug("Creating RenderOptions with DPI: {Dpi}", dpi);
            var options = new RenderOptions(Dpi: dpi);

            _logger.LogDebug("Converting PDF page to SKBitmap from stream");
            using var skBitmap = Conversion.ToImage(pdfStream, page: pageIndex, options: options);

            if (skBitmap == null)
            {
                _logger.LogWarning("Rendering returned null for page {PageIndex}", pageIndex);
                return null;
            }

            _logger.LogDebug("SKBitmap created: {Width}x{Height}", skBitmap.Width, skBitmap.Height);

            // Convert SkiaSharp bitmap to Avalonia bitmap
            var avaBitmap = ConvertSkBitmapToAvalonia(skBitmap, out var pngBytes);

            if (cacheKey != null && pngBytes.Length > 0)
            {
                AddToCache(cacheKey, pngBytes, skBitmap.Width, skBitmap.Height);
            }

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
        return ConvertSkBitmapToAvalonia(skBitmap, out _);
    }

    private Bitmap ConvertSkBitmapToAvalonia(SKBitmap skBitmap, out byte[] pngBytes)
    {
        _logger.LogDebug("Converting SKBitmap to Avalonia Bitmap");

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);

        pngBytes = stream.ToArray();

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

    private string BuildCacheKey(string pdfPath, int pageIndex, int dpi)
    {
        var lastWrite = File.Exists(pdfPath) ? File.GetLastWriteTimeUtc(pdfPath).Ticks : 0;
        return $"{pdfPath}|{pageIndex}|{dpi}|{lastWrite}";
    }

    private bool TryGetFromCache(string key, out Bitmap? bitmap)
    {
        bitmap = null;

        if (_cache.TryGetValue(key, out var entry))
        {
            var bytes = entry.PngData;
            try
            {
                using var ms = new MemoryStream(bytes);
                bitmap = new Bitmap(ms);
                _cache[key] = entry with { LastAccessUtc = DateTime.UtcNow };
                System.Threading.Interlocked.Increment(ref _cacheHits);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache decode failed, evicting entry");
                _cache.TryRemove(key, out _);
            }
        }

        System.Threading.Interlocked.Increment(ref _cacheMisses);
        return false;
    }

    private void AddToCache(string key, byte[] pngBytes, int width, int height)
    {
        _cache[key] = new RenderCacheEntry(pngBytes, width, height, DateTime.UtcNow);

        if (_cache.Count > _maxCacheEntries)
        {
            // Evict oldest entry
            var oldest = DateTime.MaxValue;
            string? oldestKey = null;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessUtc < oldest)
                {
                    oldest = kvp.Value.LastAccessUtc;
                    oldestKey = kvp.Key;
                }
            }

            if (oldestKey != null)
            {
                _cache.TryRemove(oldestKey, out _);
            }
        }
    }
}
