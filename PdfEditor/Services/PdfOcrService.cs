using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Tesseract;
using PdfEditor.Services;
using System.Collections.Generic;
using System.Text;
using SkiaSharp;
using Avalonia.Media.Imaging; // Still needed for the main app's UI, not removed

namespace PdfEditor.Services;

public class OcrOptions
{
    public string Languages { get; set; } = "eng";
    public int BaseDpi { get; set; } = 350;
    public int HighDpi { get; set; } = 450;
    public float LowConfidenceThreshold { get; set; } = 0.6f;
    public bool Preprocess { get; set; } = true;
    public float DenoiseRadius { get; set; } = 0.8f;
    public bool Binarize { get; set; } = true;

    public static OcrOptions Default => new OcrOptions();
}

/// <summary>
/// Service for Optical Character Recognition (OCR) using Tesseract
/// </summary>
public class PdfOcrService
{
    private readonly ILogger<PdfOcrService> _logger;
    private readonly PdfRenderService _renderService;
    private readonly string _tessDataPath;

    public PdfOcrService(ILogger<PdfOcrService> logger, PdfRenderService renderService)
    {
        _logger = logger;
        _renderService = renderService;
        
        // Set default tessdata path to application directory/tessdata
        _tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    }

    /// <summary>
/// Check if Tesseract data is available
/// </summary>
    public bool IsOcrAvailable()
    {
        var available = Directory.Exists(_tessDataPath) && Directory.GetFiles(_tessDataPath, "*.traineddata").Length > 0;
        if (!available)
        {
            _logger.LogWarning("OCR not available: tessdata directory not found or empty at {Path}", _tessDataPath);
        }
        return available;
    }

    /// <summary>
/// Download Tesseract language data files if not present
/// </summary>
    public async Task<bool> EnsureLanguageDataAsync(string language = "eng")
    {
        try
        {
            // Create tessdata directory if it doesn't exist
            if (!Directory.Exists(_tessDataPath))
            {
                _logger.LogInformation("Creating tessdata directory at {Path}", _tessDataPath);
                Directory.CreateDirectory(_tessDataPath);
            }

            var trainedDataFile = Path.Combine(_tessDataPath, $"{language}.traineddata");

            if (File.Exists(trainedDataFile))
            {
                _logger.LogDebug("Language data already exists: {File}", trainedDataFile);
                return true;
            }

            _logger.LogInformation("Downloading Tesseract language data for {Language}...", language);

            // Use fast model for smaller download size and faster loading
            var url = $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{language}.traineddata";

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // Large files may take time

            var response = await httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download {Language} language data. HTTP {StatusCode}", language, response.StatusCode);
                return false;
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(trainedDataFile, data);

            _logger.LogInformation("Successfully downloaded {Language} language data ({Size:N0} bytes)",
                language, data.Length);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading Tesseract language data for {Language}", language);
            return false;
        }
    }

    /// <summary>
/// Perform OCR on a PDF document and return the extracted text
/// </summary>
    public Task<string> PerformOcrAsync(string pdfPath, string language = "eng")
    {
        var opts = BuildDefaultOptions();
        opts.Languages = language;
        return PerformOcrAsync(pdfPath, opts);
    }

    /// <summary>
/// Perform OCR using multiple languages (comma or plus separated).
/// </summary>
    public Task<string> PerformOcrAsync(string pdfPath, IEnumerable<string> languages)
    {
        var lang = string.Join("+", languages ?? Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(lang))
            lang = "eng";
        return PerformOcrAsync(pdfPath, lang);
    }

    /// <summary>
/// Perform OCR with custom options.
/// </summary>
    public async Task<string> PerformOcrAsync(string pdfPath, OcrOptions options)
    {
        // Auto-download language data if not available
        if (!IsOcrAvailable())
        {
            _logger.LogInformation("OCR data not found. Attempting to download...");

            // Parse language string (supports "eng", "eng+deu", etc.)
            var languages = options.Languages.Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var lang in languages)
            {
                var langCode = lang.Trim();
                var success = await EnsureLanguageDataAsync(langCode);

                if (!success)
                {
                    throw new InvalidOperationException(
                        $"Failed to download language data for '{langCode}'. " +
                        $"Please check your internet connection or manually download from: " +
                        $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{langCode}.traineddata");
                }
            }
        }

        _logger.LogInformation("Starting OCR for {File} with language {Lang}", Path.GetFileName(pdfPath), options.Languages);

        var sb = new StringBuilder();

        try
        {
            using var engine = new TesseractEngine(_tessDataPath, options.Languages, EngineMode.Default);
            
            int pageCount = GetPageCount(pdfPath);
            _logger.LogInformation("Document has {Count} pages", pageCount);

            for (int i = 0; i < pageCount; i++)
            {
                _logger.LogDebug("Processing page {Page}", i + 1);

                var (text, confidence) = await OcrPageAsync(engine, pdfPath, i, options.BaseDpi, options);

                // If confidence is low, re-run at a higher DPI for that page
                if (confidence < options.LowConfidenceThreshold)
                {
                    _logger.LogInformation(
                        "Low OCR confidence ({Conf:P0}) on page {Page}, retrying at {Dpi} DPI",
                        confidence, i + 1, options.HighDpi);

                    var retry = await OcrPageAsync(engine, pdfPath, i, options.HighDpi, options);
                    if (retry.confidence > confidence)
                    {
                        text = retry.text;
                        confidence = retry.confidence;
                    }
                }

                _logger.LogInformation("Page {Page} OCR complete. Confidence: {Conf:P0}", i + 1, confidence);

                sb.AppendLine($"--- Page {i + 1} ---");
                sb.AppendLine(text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR Failed");
            throw;
        }

        return sb.ToString();
    }

    private OcrOptions BuildDefaultOptions()
    {
        // Return standard defaults - all configuration is now done through UI preferences
        return OcrOptions.Default;
    }

    private async Task<(string text, float confidence)> OcrPageAsync(TesseractEngine engine, string pdfPath, int pageIndex, int dpi, OcrOptions options)
    {
        // Now directly receive SKBitmap from render service
        using var skBitmap = await _renderService.RenderPageAsync(pdfPath, pageIndex, dpi);
        if (skBitmap == null) return (string.Empty, 0f);

        using var pix = options.Preprocess ? PreprocessForOcr(skBitmap, options) : Pix.LoadFromMemory(SKImage.FromBitmap(skBitmap).Encode(SKEncodedImageFormat.Png, 100).ToArray());
        using var page = engine.Process(pix);

        return (page.GetText(), page.GetMeanConfidence());
    }

    // This is no longer needed as PixFromBitmap is gone and we get SKBitmap directly
    // private Pix PixFromBitmap(Avalonia.Media.Imaging.Bitmap bitmap)
    // {
    //     using var stream = new MemoryStream();
    //     bitmap.Save(stream);
    //     stream.Position = 0;
    //     return Pix.LoadFromMemory(stream.ToArray());
    // }

    private Pix PreprocessForOcr(SKBitmap skBitmap, OcrOptions options)
    {
        // Preprocessing is now done directly on SKBitmap
        // Convert SKBitmap to grayscale and apply blur if needed
        var info = new SKImageInfo(skBitmap.Width, skBitmap.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var gray = new SKBitmap(info);

        using (var surface = new SKCanvas(gray))
        using (var paint = new SKPaint())
        {
            // Apply color matrix for grayscale conversion
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                0.2126f, 0.2126f, 0.2126f, 0, 0,
                0.7152f, 0.7152f, 0.7152f, 0, 0,
                0.0722f, 0.0722f, 0.0722f, 0, 0,
                0,       0,       0,       1, 0
            });
            if (options.DenoiseRadius > 0)
            {
                paint.ImageFilter = SKImageFilter.CreateBlur(options.DenoiseRadius, options.DenoiseRadius);
            }
            surface.DrawBitmap(skBitmap, 0, 0, paint);
        }

        using var img = SKImage.FromBitmap(gray);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var grayStream = new MemoryStream();
        data.SaveTo(grayStream);
        grayStream.Position = 0;

        return Pix.LoadFromMemory(grayStream.ToArray());
    }

    private int GetPageCount(string pdfPath)
    {
        try
        {
            using var doc = PdfSharp.Pdf.IO.PdfReader.Open(pdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }
        catch
        {
            return 0;
        }
    }
}
