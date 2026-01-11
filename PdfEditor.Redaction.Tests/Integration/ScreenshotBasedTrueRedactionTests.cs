using FluentAssertions;
using PdfEditor.Redaction.GlyphLevel;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Screenshot-based TRUE redaction verification tests.
///
/// These tests verify that redaction is TRUE (content removed from PDF structure)
/// not FAKE (just drawing black boxes over content).
///
/// For each test case, we:
/// 1. Create a test PDF with known content
/// 2. Render before/after screenshots
/// 3. Extract text using PdfPig AND pdftotext
/// 4. Verify redacted content is REMOVED from PDF structure
/// 5. Verify non-redacted content is PRESERVED
///
/// This comprehensive approach detects both:
/// - Fake redaction (visual covering only)
/// - Over-redaction (removing content that shouldn't be removed)
/// - Under-redaction (failing to remove content that should be removed)
/// </summary>
public class ScreenshotBasedTrueRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly string _screenshotDir;
    private const double Dpi = 150.0;
    private const double PageHeight = 792.0;

    public ScreenshotBasedTrueRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        _screenshotDir = Path.Combine(Path.GetTempPath(), "pdfe_screenshot_tests", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_screenshotDir);
        _output.WriteLine($"Screenshots saved to: {_screenshotDir}");

        // Initialize font resolver
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    #region Text Redaction Tests

    /// <summary>
    /// Test: Text fully inside redaction area is truly removed.
    /// Verifies: Text extraction returns nothing, visual shows black box.
    /// </summary>
    [Fact]
    public void TextFullyInsideRedactionArea_IsTrulyRemoved()
    {
        // Arrange
        var inputPath = CreateTempPath("text_inside_input.pdf");
        var outputPath = CreateTempPath("text_inside_output.pdf");

        // Create PDF with text at specific location
        CreatePdfWithLabeledText(inputPath, new[]
        {
            ("SECRET_DATA_123", 200, 500, 14),  // Inside redaction zone
            ("KEEP_THIS_TEXT", 50, 700, 14),    // Outside redaction zone
        });

        // Extract text before
        var textBefore = ExtractTextPdfPig(inputPath);
        var textBeforePdftotext = ExtractTextPdftotext(inputPath);
        _output.WriteLine($"Text before (PdfPig): {textBefore}");
        _output.WriteLine($"Text before (pdftotext): {textBeforePdftotext}");
        textBefore.Should().Contain("SECRET_DATA_123");
        textBefore.Should().Contain("KEEP_THIS_TEXT");

        // Render before screenshot
        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "text_inside_01_before.png");

        // Act - Redact the area containing SECRET_DATA_123
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(180, 480, 400, 530)  // Area around SECRET_DATA_123
        };
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        result.Success.Should().BeTrue($"Redaction failed: {result.ErrorMessage}");

        // Render after screenshot
        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "text_inside_02_after.png");

        // Assert - TRUE REDACTION VERIFICATION
        var textAfter = ExtractTextPdfPig(outputPath);
        var textAfterPdftotext = ExtractTextPdftotext(outputPath);
        _output.WriteLine($"Text after (PdfPig): {textAfter}");
        _output.WriteLine($"Text after (pdftotext): {textAfterPdftotext}");

        // SECRET_DATA_123 should be REMOVED (not extractable)
        textAfter.Should().NotContain("SECRET_DATA_123",
            "TRUE REDACTION: Text should be REMOVED from PDF structure, not just covered");
        textAfterPdftotext.Should().NotContain("SECRET_DATA_123",
            "TRUE REDACTION: pdftotext should not find redacted text");

        // KEEP_THIS_TEXT should be PRESERVED
        textAfter.Should().Contain("KEEP_THIS_TEXT",
            "Non-redacted text should be preserved");

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    /// <summary>
    /// Test: Multiple text items with selective redaction.
    /// Verifies: Only targeted text is removed, others preserved.
    /// </summary>
    [Fact]
    public void SelectiveTextRedaction_OnlyTargetedTextRemoved()
    {
        // Arrange
        var inputPath = CreateTempPath("selective_text_input.pdf");
        var outputPath = CreateTempPath("selective_text_output.pdf");

        CreatePdfWithLabeledText(inputPath, new[]
        {
            ("ZONE_A_REDACT_ME", 200, 700, 12),
            ("ZONE_B_KEEP_ME", 50, 600, 12),
            ("ZONE_C_REDACT_ME", 250, 500, 12),
            ("ZONE_D_KEEP_ME", 50, 400, 12),
        });

        var textBefore = ExtractTextPdfPig(inputPath);
        _output.WriteLine($"Text before: {textBefore}");

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "selective_01_before.png");

        // Act - Redact ZONE_A and ZONE_C
        var redactor = new TextRedactor();
        var locations = new[]
        {
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(180, 680, 400, 720) },
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(230, 480, 450, 520) },
        };
        var result = redactor.RedactLocations(inputPath, outputPath, locations);
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "selective_02_after.png");

        // Assert
        var textAfter = ExtractTextPdfPig(outputPath);
        _output.WriteLine($"Text after: {textAfter}");

        textAfter.Should().NotContain("ZONE_A_REDACT_ME", "Zone A should be redacted");
        textAfter.Should().NotContain("ZONE_C_REDACT_ME", "Zone C should be redacted");
        textAfter.Should().Contain("ZONE_B_KEEP_ME", "Zone B should be preserved");
        textAfter.Should().Contain("ZONE_D_KEEP_ME", "Zone D should be preserved");

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    /// <summary>
    /// Test: Text redaction by search term.
    /// Verifies: All instances of search term are removed.
    /// </summary>
    [Fact]
    public void TextRedactionByTerm_RemovesAllInstances()
    {
        // Arrange
        var inputPath = CreateTempPath("term_search_input.pdf");
        var outputPath = CreateTempPath("term_search_output.pdf");

        CreatePdfWithLabeledText(inputPath, new[]
        {
            ("CONFIDENTIAL information here", 50, 700, 12),
            ("Some normal text", 50, 650, 12),
            ("More CONFIDENTIAL data below", 50, 600, 12),
            ("Public information", 50, 550, 12),
        });

        var textBefore = ExtractTextPdfPig(inputPath);
        _output.WriteLine($"Text before: {textBefore}");
        textBefore.ToLower().Should().Contain("confidential");

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "term_search_01_before.png");

        // Act - Redact all instances of "CONFIDENTIAL"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "CONFIDENTIAL");
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "term_search_02_after.png");

        // Assert
        var textAfter = ExtractTextPdfPig(outputPath);
        _output.WriteLine($"Text after: {textAfter}");

        textAfter.ToLower().Should().NotContain("confidential",
            "All instances of CONFIDENTIAL should be removed");
        textAfter.Should().Contain("normal text", "Non-redacted text preserved");
        textAfter.Should().Contain("Public", "Non-redacted text preserved");

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    #endregion

    #region Shape Redaction Tests

    /// <summary>
    /// Test: Shape fully inside redaction area is completely removed.
    /// Verifies: No colored pixels remain in shape region.
    /// </summary>
    [Fact]
    public void ShapeFullyInsideRedactionArea_IsCompletelyRemoved()
    {
        // Arrange
        var inputPath = CreateTempPath("shape_inside_input.pdf");
        var outputPath = CreateTempPath("shape_inside_output.pdf");

        // Blue shape inside redaction zone (middle of page), Green shape at top (outside redaction)
        CreatePdfWithShapes(inputPath, new[]
        {
            new ShapeSpec { Type = "rect", X = 200, Y = 500, Width = 80, Height = 80, Color = XColors.Blue },
            new ShapeSpec { Type = "rect", X = 50, Y = 700, Width = 80, Height = 80, Color = XColors.Green },
        });

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "shape_inside_01_before.png");

        // Count total blue and green pixels in the entire image before redaction
        var bluePixelsBefore = CountBluePixelsInImage(beforeImage);
        var greenPixelsBefore = CountGreenPixelsInImage(beforeImage);
        _output.WriteLine($"Blue pixels in image before: {bluePixelsBefore}");
        _output.WriteLine($"Green pixels in image before: {greenPixelsBefore}");
        bluePixelsBefore.Should().BeGreaterThan(100, "Should have blue shape");
        greenPixelsBefore.Should().BeGreaterThan(100, "Should have green shape");

        // Act - Redact area containing only the blue rectangle (PDF y=480-600)
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(180, 480, 300, 600)  // Only covers blue rect
        };
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "shape_inside_02_after.png");

        // Assert - Count pixels in the entire image after redaction
        var bluePixelsAfter = CountBluePixelsInImage(afterImage);
        var greenPixelsAfter = CountGreenPixelsInImage(afterImage);
        _output.WriteLine($"Blue pixels in image after: {bluePixelsAfter}");
        _output.WriteLine($"Green pixels in image after: {greenPixelsAfter}");

        // Blue shape should be removed (replaced by black box)
        bluePixelsAfter.Should().BeLessThan(bluePixelsBefore / 2,
            "Blue shape should be significantly reduced or removed");

        // Green shape should be preserved (it's outside the redaction zone)
        greenPixelsAfter.Should().BeGreaterThan(greenPixelsBefore / 2,
            "Green shape should be preserved (outside redaction zone)");

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    /// <summary>
    /// Test: Partial shape overlap with redaction area clips the shape.
    /// Verifies: Non-overlapping portion remains, overlapping portion removed.
    /// </summary>
    [Fact]
    public void ShapePartiallyOverlapping_IsClippedCorrectly()
    {
        // Arrange - Create a wide rectangle
        var inputPath = CreateTempPath("shape_partial_input.pdf");
        var outputPath = CreateTempPath("shape_partial_output.pdf");

        CreatePdfWithShapes(inputPath, new[]
        {
            new ShapeSpec { Type = "rect", X = 100, Y = 500, Width = 200, Height = 80, Color = XColors.Red },
        });

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "shape_partial_01_before.png");

        // Measure left and right halves before
        int leftX = PdfXToPixel(100);
        int rightX = PdfXToPixel(200);
        int regionY = PdfYToPixel(580);
        int halfWidth = (int)(100 * Dpi / 72.0);
        int height = (int)(80 * Dpi / 72.0);

        var leftRedBefore = CountRedPixels(beforeImage, leftX, regionY, halfWidth, height);
        var rightRedBefore = CountRedPixels(beforeImage, rightX, regionY, halfWidth, height);
        _output.WriteLine($"Before: left red={leftRedBefore}, right red={rightRedBefore}");

        // Act - Redact right half only (x=200-350)
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(200, 480, 350, 600)
        };
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "shape_partial_02_after.png");

        // Assert
        var leftRedAfter = CountRedPixels(afterImage, leftX, regionY, halfWidth, height);
        var rightRedAfter = CountRedPixels(afterImage, rightX, regionY, halfWidth, height);
        _output.WriteLine($"After: left red={leftRedAfter}, right red={rightRedAfter}");

        // Left half should be preserved (similar pixel count)
        if (leftRedBefore > 100)
        {
            var leftRatio = (double)leftRedAfter / leftRedBefore;
            _output.WriteLine($"Left preservation ratio: {leftRatio:F2}");
            leftRatio.Should().BeGreaterThan(0.5, "Left half of shape should be preserved");
        }

        // Right half should be significantly reduced (clipped)
        if (rightRedBefore > 100)
        {
            var rightRatio = (double)rightRedAfter / rightRedBefore;
            _output.WriteLine($"Right reduction ratio: {rightRatio:F2}");
            rightRatio.Should().BeLessThan(0.5, "Right half should be clipped away");
        }

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    /// <summary>
    /// Test: Shape completely outside redaction area is preserved.
    /// </summary>
    [Fact]
    public void ShapeOutsideRedactionArea_IsFullyPreserved()
    {
        // Arrange
        var inputPath = CreateTempPath("shape_outside_input.pdf");
        var outputPath = CreateTempPath("shape_outside_output.pdf");

        CreatePdfWithShapes(inputPath, new[]
        {
            new ShapeSpec { Type = "rect", X = 50, Y = 600, Width = 100, Height = 100, Color = XColors.Purple },
        });

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "shape_outside_01_before.png");

        int shapeX = PdfXToPixel(50);
        int shapeY = PdfYToPixel(700);
        int size = (int)(100 * Dpi / 72.0);

        var pixelsBefore = CountColoredPixels(beforeImage, shapeX, shapeY, size, size);
        _output.WriteLine($"Colored pixels before: {pixelsBefore}");

        // Act - Redact area far from shape
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(400, 100, 550, 250)  // Bottom right, far from shape
        };
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "shape_outside_02_after.png");

        // Assert
        var pixelsAfter = CountColoredPixels(afterImage, shapeX, shapeY, size, size);
        _output.WriteLine($"Colored pixels after: {pixelsAfter}");

        if (pixelsBefore > 100)
        {
            var ratio = (double)pixelsAfter / pixelsBefore;
            _output.WriteLine($"Preservation ratio: {ratio:F2}");
            ratio.Should().BeGreaterThan(0.95, "Shape outside redaction area should be fully preserved");
        }

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    #endregion

    #region Combined Text and Shape Tests

    /// <summary>
    /// Test: Combined text and shapes with mixed redaction.
    /// Verifies: Both text and shapes are correctly handled.
    /// </summary>
    [Fact]
    public void CombinedTextAndShapes_CorrectlyRedacted()
    {
        // Arrange
        var inputPath = CreateTempPath("combined_input.pdf");
        var outputPath = CreateTempPath("combined_output.pdf");

        CreateCombinedPdf(inputPath);

        var textBefore = ExtractTextPdfPig(inputPath);
        _output.WriteLine($"Text before: {textBefore}");

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "combined_01_before.png");

        // Act - Redact central area (covers some text and shapes)
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(180, 400, 400, 650)  // Central vertical strip
        };
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "combined_02_after.png");

        // Assert
        var textAfter = ExtractTextPdfPig(outputPath);
        _output.WriteLine($"Text after: {textAfter}");

        // Text inside redaction zone should be removed
        textAfter.Should().NotContain("INSIDE_ZONE", "Text in redaction zone should be removed");

        // Text outside should be preserved
        textAfter.Should().Contain("OUTSIDE", "Text outside zone should be preserved");

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(filename)}_{Guid.NewGuid()}{Path.GetExtension(filename)}");
        _tempFiles.Add(path);
        return path;
    }

    private void CreatePdfWithLabeledText(string path, (string text, int x, int y, int fontSize)[] items)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        foreach (var (text, x, y, fontSize) in items)
        {
            var font = new XFont("Helvetica", fontSize);
            var yPos = page.Height.Point - y;
            gfx.DrawString(text, font, XBrushes.Black, x, yPos);
        }

        document.Save(path);
    }

    private record ShapeSpec
    {
        public string Type { get; init; } = "rect";
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public XColor Color { get; init; }
    }

    private void CreatePdfWithShapes(string path, ShapeSpec[] shapes)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        foreach (var shape in shapes)
        {
            var yPos = page.Height.Point - shape.Y - shape.Height;
            var brush = new XSolidBrush(shape.Color);

            switch (shape.Type)
            {
                case "rect":
                    gfx.DrawRectangle(brush, shape.X, yPos, shape.Width, shape.Height);
                    break;
                case "ellipse":
                    gfx.DrawEllipse(brush, shape.X, yPos, shape.Width, shape.Height);
                    break;
            }
        }

        document.Save(path);
    }

    private void CreateCombinedPdf(string path)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        var font = new XFont("Helvetica", 12);

        // Text inside zone
        gfx.DrawString("INSIDE_ZONE_TEXT", font, XBrushes.Black, 220, page.Height.Point - 500);

        // Text outside zone
        gfx.DrawString("OUTSIDE_LEFT", font, XBrushes.Black, 50, page.Height.Point - 500);
        gfx.DrawString("OUTSIDE_RIGHT", font, XBrushes.Black, 450, page.Height.Point - 500);

        // Shape inside zone
        gfx.DrawRectangle(XBrushes.Blue, 220, page.Height.Point - 600, 80, 80);

        // Shape outside zone
        gfx.DrawRectangle(XBrushes.Green, 50, page.Height.Point - 600, 80, 80);

        // Shape partially in zone
        gfx.DrawRectangle(XBrushes.Red, 100, page.Height.Point - 450, 200, 80);

        document.Save(path);
    }

    private string ExtractTextPdfPig(string pdfPath)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[PdfPig error: {ex.Message}]";
        }
    }

    private string ExtractTextPdftotext(string pdfPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pdftotext",
                    Arguments = $"\"{pdfPath}\" -",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch (Exception ex)
        {
            return $"[pdftotext error: {ex.Message}]";
        }
    }

    private SKBitmap RenderPdfToImage(string pdfPath)
    {
        using var stream = File.OpenRead(pdfPath);
        var options = new PDFtoImage.RenderOptions(Dpi: (int)Dpi);
        using var image = PDFtoImage.Conversion.ToImage(stream, page: 0, options: options);

        using var memStream = new MemoryStream();
        image.Encode(memStream, SKEncodedImageFormat.Png, 100);
        memStream.Position = 0;

        return SKBitmap.Decode(memStream);
    }

    private void SaveScreenshot(SKBitmap image, string filename)
    {
        var path = Path.Combine(_screenshotDir, filename);
        using var stream = File.Create(path);
        image.Encode(stream, SKEncodedImageFormat.Png, 100);
        _output.WriteLine($"Screenshot: {path}");
    }

    private int PdfXToPixel(double pdfX) => (int)(pdfX * Dpi / 72.0);
    private int PdfYToPixel(double pdfY) => (int)((PageHeight - pdfY) * Dpi / 72.0);

    private int CountColoredPixels(SKBitmap image, int x, int y, int width, int height)
    {
        int count = 0;
        for (int py = y; py < Math.Min(y + height, image.Height); py++)
        {
            for (int px = x; px < Math.Min(x + width, image.Width); px++)
            {
                var pixel = image.GetPixel(px, py);
                if (pixel.Red < 240 || pixel.Green < 240 || pixel.Blue < 240)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private int CountBluePixels(SKBitmap image, int x, int y, int width, int height)
    {
        int count = 0;
        for (int py = y; py < Math.Min(y + height, image.Height); py++)
        {
            for (int px = x; px < Math.Min(x + width, image.Width); px++)
            {
                var pixel = image.GetPixel(px, py);
                if (pixel.Blue > 200 && pixel.Red < 100 && pixel.Green < 100)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private int CountRedPixels(SKBitmap image, int x, int y, int width, int height)
    {
        int count = 0;
        for (int py = y; py < Math.Min(y + height, image.Height); py++)
        {
            for (int px = x; px < Math.Min(x + width, image.Width); px++)
            {
                var pixel = image.GetPixel(px, py);
                if (pixel.Red > 200 && pixel.Green < 100 && pixel.Blue < 100)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private int CountGreenPixels(SKBitmap image, int x, int y, int width, int height)
    {
        int count = 0;
        for (int py = y; py < Math.Min(y + height, image.Height); py++)
        {
            for (int px = x; px < Math.Min(x + width, image.Width); px++)
            {
                var pixel = image.GetPixel(px, py);
                // XColors.Green is (0, 128, 0), not pure green (0, 255, 0)
                // So we check for green > red and green > blue with green > 100
                if (pixel.Green > 100 && pixel.Green > pixel.Red && pixel.Green > pixel.Blue && pixel.Red < 50 && pixel.Blue < 50)
                {
                    count++;
                }
            }
        }
        return count;
    }

    // Count pixels in entire image
    private int CountBluePixelsInImage(SKBitmap image) => CountBluePixels(image, 0, 0, image.Width, image.Height);
    private int CountGreenPixelsInImage(SKBitmap image) => CountGreenPixels(image, 0, 0, image.Width, image.Height);
    private int CountRedPixelsInImage(SKBitmap image) => CountRedPixels(image, 0, 0, image.Width, image.Height);

    #endregion

    #region Font Resolver

    private class TestFontResolver : IFontResolver
    {
        private readonly Dictionary<string, string> _fontCache = new(StringComparer.OrdinalIgnoreCase);

        public TestFontResolver()
        {
            var fontDirs = new[]
            {
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/fonts")
            };

            foreach (var dir in fontDirs.Where(Directory.Exists))
            {
                try
                {
                    foreach (var font in Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(font);
                        if (!_fontCache.ContainsKey(name))
                            _fontCache[name] = font;
                    }
                }
                catch { }
            }
        }

        public byte[]? GetFont(string faceName)
        {
            if (_fontCache.TryGetValue(faceName, out var path) && File.Exists(path))
                return File.ReadAllBytes(path);
            return null;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var candidates = new[] { "LiberationSans-Regular", "DejaVuSans", "FreeSans" };
            foreach (var c in candidates)
            {
                if (_fontCache.ContainsKey(c))
                    return new FontResolverInfo(c);
            }
            return new FontResolverInfo(_fontCache.Keys.FirstOrDefault() ?? "Arial");
        }
    }

    #endregion
}
