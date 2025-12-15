using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using Tesseract;
using PdfEditor.Services;
using System.Collections.Generic;
using System.Text;
using SkiaSharp;

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
        if (!IsOcrAvailable())
        {
            throw new InvalidOperationException($"OCR data not found at {_tessDataPath}. Please install Tesseract training data.");
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
        var opts = OcrOptions.Default;

        var envLang = Environment.GetEnvironmentVariable("PDFEDITOR_OCR_LANGS");
        if (!string.IsNullOrWhiteSpace(envLang))
            opts.Languages = envLang;

        if (int.TryParse(Environment.GetEnvironmentVariable("PDFEDITOR_OCR_BASE_DPI"), out var baseDpi) && baseDpi > 0)
            opts.BaseDpi = baseDpi;

        if (int.TryParse(Environment.GetEnvironmentVariable("PDFEDITOR_OCR_HIGH_DPI"), out var highDpi) && highDpi > 0)
            opts.HighDpi = highDpi;

        if (float.TryParse(Environment.GetEnvironmentVariable("PDFEDITOR_OCR_LOW_CONFIDENCE"), out var lowConf) && lowConf > 0 && lowConf < 1)
            opts.LowConfidenceThreshold = lowConf;

        var envPre = Environment.GetEnvironmentVariable("PDFEDITOR_OCR_PREPROCESS");
        if (!string.IsNullOrWhiteSpace(envPre) && bool.TryParse(envPre, out var pre))
            opts.Preprocess = pre;

        if (float.TryParse(Environment.GetEnvironmentVariable("PDFEDITOR_OCR_DENOISE_RADIUS"), out var radius) && radius >= 0)
            opts.DenoiseRadius = radius;

        var envBin = Environment.GetEnvironmentVariable("PDFEDITOR_OCR_BINARIZE");
        if (!string.IsNullOrWhiteSpace(envBin) && bool.TryParse(envBin, out var bin))
            opts.Binarize = bin;

        return opts;
    }

    private async Task<(string text, float confidence)> OcrPageAsync(TesseractEngine engine, string pdfPath, int pageIndex, int dpi, OcrOptions options)
    {
        using var bitmap = await _renderService.RenderPageAsync(pdfPath, pageIndex, dpi);
        if (bitmap == null) return (string.Empty, 0f);

        using var pix = options.Preprocess ? PreprocessForOcr(bitmap, options) : PixFromBitmap(bitmap);
        using var page = engine.Process(pix);

        return (page.GetText(), page.GetMeanConfidence());
    }

    private Pix PixFromBitmap(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return Pix.LoadFromMemory(stream.ToArray());
    }

    private Pix PreprocessForOcr(Avalonia.Media.Imaging.Bitmap bitmap, OcrOptions options)
    {
        // Convert Avalonia Bitmap -> SKBitmap -> grayscale to reduce noise for OCR
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;

        using var src = SKBitmap.Decode(stream);
        if (src == null)
            throw new InvalidOperationException("Failed to decode bitmap for OCR preprocessing.");

        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        var gray = new SKBitmap(info);

        using (var surface = new SKCanvas(gray))
        using (var paint = new SKPaint())
        {
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
            surface.DrawBitmap(src, 0, 0, paint);
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
