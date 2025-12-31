using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Parsing;
using PdfEditor.Redaction.GlyphLevel;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Visual regression tests for redaction that use screenshots to verify
/// the PDF renders correctly after redaction.
///
/// These tests specifically detect:
/// - Font size corruption (text becoming too small/large)
/// - Text position shifts
/// - Missing content
/// - Visual artifacts
///
/// Issue: Font scaling corruption where text was rendered at 1pt instead of 9pt
/// because the Tm matrix scaling wasn't preserved during redaction.
/// </summary>
public class VisualRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly string _screenshotDir;

    public VisualRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        _screenshotDir = Path.Combine(Path.GetTempPath(), "pdfe_visual_tests");
        Directory.CreateDirectory(_screenshotDir);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Test that redaction preserves font sizes visually.
    /// Renders the PDF before and after redaction and compares
    /// the visual appearance of non-redacted text.
    /// This test uses separate lines to ensure text not being redacted stays visually identical.
    /// </summary>
    [Fact]
    public void Redaction_PreservesFontSizes_VisualVerification()
    {
        // Arrange - Create a PDF with known text at different sizes on SEPARATE lines
        var inputPath = Path.Combine(Path.GetTempPath(), $"font_size_test_{Guid.NewGuid()}.pdf");
        var outputPath = Path.Combine(Path.GetTempPath(), $"font_size_test_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(inputPath);
        _tempFiles.Add(outputPath);

        // Create PDF with specific text at known sizes - on separate lines!
        // Title is at top (y=720), Redactable is in middle (y=680), Footer is at bottom (y=640)
        CreatePdfWithTextAtSizes(inputPath, new[]
        {
            ("KEEP THIS TITLE", 72, 720, 14),      // 14pt title - should be preserved
            ("REDACT_ME", 72, 680, 10),            // 10pt - will be fully redacted (simpler for test)
            ("KEEP THIS FOOTER", 72, 640, 8),      // 8pt footer - should be preserved
        });

        // Render original PDF
        var originalImage = RenderPdfToImage(inputPath);
        SaveScreenshot(originalImage, "01_original.png");
        _output.WriteLine($"Original image: {originalImage.Width}x{originalImage.Height}");

        // Act - Redact the middle text completely
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "REDACT_ME");

        result.Success.Should().BeTrue("Redaction should succeed");

        // Render redacted PDF
        var redactedImage = RenderPdfToImage(outputPath);
        SaveScreenshot(redactedImage, "02_redacted.png");
        _output.WriteLine($"Redacted image: {redactedImage.Width}x{redactedImage.Height}");

        // Assert - Compare specific regions
        // The TITLE region should look identical (not affected by redaction)
        var titleRegion = ExtractRegion(originalImage, 50, 50, 300, 40);
        var titleRegionRedacted = ExtractRegion(redactedImage, 50, 50, 300, 40);

        // Calculate visual differences
        var titleDiff = CalculateImageDifference(titleRegion, titleRegionRedacted);
        _output.WriteLine($"Title region difference: {titleDiff:F2}%");

        // Title should be nearly identical (allowing for minor antialiasing)
        // If font sizes were corrupted, the difference would be much higher (> 50%)
        titleDiff.Should().BeLessThan(5.0,
            "Title text should appear the same size before and after redaction");

        // Measure text heights to verify font sizes are preserved
        var titleHeight = MeasureTextHeight(originalImage, 50, 50, 300, 40);
        var titleHeightRedacted = MeasureTextHeight(redactedImage, 50, 50, 300, 40);
        _output.WriteLine($"Title height: original={titleHeight}px, redacted={titleHeightRedacted}px");

        // Heights should be the same (font size preserved)
        if (titleHeight > 0 && titleHeightRedacted > 0)
        {
            var ratio = (double)titleHeightRedacted / titleHeight;
            ratio.Should().BeInRange(0.8, 1.2, "Title text height should be preserved");
        }

        originalImage.Dispose();
        redactedImage.Dispose();
        titleRegion.Dispose();
        titleRegionRedacted.Dispose();
    }

    /// <summary>
    /// Test that specifically catches the Tm scaling bug.
    /// Uses a PDF with Tf=1 and Tm scaling, which was the broken pattern.
    /// </summary>
    [Fact]
    public void Redaction_WithTmScaling_PreservesEffectiveFontSize()
    {
        // Arrange - Create content stream with Tf=1 and Tm scaling
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "/F1 1 Tf\n" +                  // Font size 1 in Tf
            "10 0 0 10 72 720 Tm\n" +       // 10x scaling in Tm -> effective 10pt
            "(Text at 10pt effective) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act - Parse and check that effective font size is calculated
        var operations = parser.Parse(contentBytes, 792);
        var textOp = operations.OfType<TextOperation>().FirstOrDefault();

        // Assert - The font size should be the EFFECTIVE size (10pt), not Tf value (1pt)
        textOp.Should().NotBeNull();
        textOp!.FontSize.Should().BeApproximately(10.0, 0.1,
            "FontSize should be effective size (Tf x Tm scale = 1 x 10 = 10pt)");

        // Now verify reconstruction preserves this
        var reconstructor = new OperationReconstructor();
        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 5,
            Keep = true,
            StartX = 72,
            StartY = 720,
            Width = 50,
            Height = 10,
            OriginalText = "Text at 10pt effective"
        };

        var reconstructed = reconstructor.ReconstructWithPositioning(
            new List<TextSegment> { segment },
            textOp);

        // Find the Tm operator in reconstructed operations
        var tmOp = reconstructed
            .OfType<TextStateOperation>()
            .FirstOrDefault(op => op.Operator == "Tm");

        tmOp.Should().NotBeNull("Reconstructed operations should include Tm");

        // The Tm should have the font size as scaling factor
        var scaleA = Convert.ToDouble(tmOp!.Operands[0]);
        var scaleD = Convert.ToDouble(tmOp.Operands[3]);

        scaleA.Should().BeApproximately(10.0, 0.1,
            "Tm horizontal scale should be font size (10pt)");
        scaleD.Should().BeApproximately(10.0, 0.1,
            "Tm vertical scale should be font size (10pt)");

        _output.WriteLine($"Tm matrix: [{scaleA} 0 0 {scaleD} x y]");
    }

    /// <summary>
    /// Visual test that renders a PDF before/after and measures text height.
    /// If font scaling is wrong, text height will be dramatically different.
    /// </summary>
    [Fact]
    public void Redaction_TextHeight_RemainsConsistent()
    {
        // Arrange
        var inputPath = Path.Combine(Path.GetTempPath(), $"text_height_test_{Guid.NewGuid()}.pdf");
        var outputPath = Path.Combine(Path.GetTempPath(), $"text_height_test_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(inputPath);
        _tempFiles.Add(outputPath);

        // Create PDF with text
        CreatePdfWithTextAtSizes(inputPath, new[]
        {
            ("AAAAA BBBBB CCCCC", 72, 720, 12),  // Line with text to partially redact
        });

        // Render original
        var originalImage = RenderPdfToImage(inputPath);
        var originalTextHeight = MeasureTextHeight(originalImage, 50, 40, 300, 50);
        _output.WriteLine($"Original text height: {originalTextHeight} pixels");

        // Redact middle word
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "BBBBB");
        result.Success.Should().BeTrue();

        // Render redacted
        var redactedImage = RenderPdfToImage(outputPath);
        var redactedTextHeight = MeasureTextHeight(redactedImage, 50, 40, 300, 50);
        _output.WriteLine($"Redacted text height: {redactedTextHeight} pixels");

        SaveScreenshot(originalImage, "height_original.png");
        SaveScreenshot(redactedImage, "height_redacted.png");

        // Text height should be similar (within 20% tolerance for minor variations)
        // If font scaling was broken (1pt vs 12pt), height would be ~12x different
        if (originalTextHeight > 0 && redactedTextHeight > 0)
        {
            var heightRatio = (double)redactedTextHeight / originalTextHeight;
            _output.WriteLine($"Height ratio: {heightRatio:F2}");

            heightRatio.Should().BeInRange(0.7, 1.3,
                "Text height should remain consistent after redaction (font scaling preserved)");
        }

        originalImage.Dispose();
        redactedImage.Dispose();
    }

    /// <summary>
    /// Test that verifies the Tm matrix contains correct font scaling after redaction.
    /// This is a direct test of the fix for the font corruption bug.
    /// </summary>
    [Fact]
    public void ReconstructedTmMatrix_ContainsFontSize_NotIdentityMatrix()
    {
        // Arrange - Simulate what parsing produces after reading a PDF with Tm scaling
        var textOp = new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { "Sample Text" },
            Text = "Sample Text",
            FontName = "/F1",
            FontSize = 9.0,  // This should be the effective size (Tf * Tm scale)
            BoundingBox = new PdfRectangle { Left = 100, Bottom = 700, Right = 200, Top = 712 },
            Glyphs = new List<GlyphPosition>()
        };

        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 6,
            Keep = true,
            StartX = 100,
            StartY = 700,
            Width = 50,
            Height = 12,
            OriginalText = "Sample Text"
        };

        // Act
        var reconstructor = new OperationReconstructor();
        var operations = reconstructor.ReconstructWithPositioning(
            new List<TextSegment> { segment },
            textOp);

        // Assert - Find the Tm operator
        var tmOp = operations
            .OfType<TextStateOperation>()
            .FirstOrDefault(op => op.Operator == "Tm");

        tmOp.Should().NotBeNull("Reconstruction must include Tm operator");

        // The Tm matrix should NOT be identity [1 0 0 1 x y]
        // It should be [fontSize 0 0 fontSize x y]
        var a = Convert.ToDouble(tmOp!.Operands[0]);
        var d = Convert.ToDouble(tmOp.Operands[3]);

        _output.WriteLine($"Tm matrix: [{a} 0 0 {d} {tmOp.Operands[4]} {tmOp.Operands[5]}]");

        // The scaling factors should match the font size
        a.Should().BeApproximately(9.0, 0.1,
            "Tm[0] (a) should be font size, not 1");
        d.Should().BeApproximately(9.0, 0.1,
            "Tm[3] (d) should be font size, not 1");

        // Verify Tf uses size 1 (actual size is in Tm)
        var tfOp = operations
            .OfType<TextStateOperation>()
            .FirstOrDefault(op => op.Operator == "Tf");

        tfOp.Should().NotBeNull("Reconstruction must include Tf operator");
        Convert.ToDouble(tfOp!.Operands[1]).Should().Be(1.0,
            "Tf should use size 1, with actual size encoded in Tm matrix");
    }

    /// <summary>
    /// Test that various font sizes are all preserved correctly through redaction.
    /// </summary>
    [Theory]
    [InlineData(6.0)]
    [InlineData(9.0)]
    [InlineData(10.02)]
    [InlineData(12.0)]
    [InlineData(14.0)]
    [InlineData(18.0)]
    [InlineData(24.0)]
    public void ReconstructedTmMatrix_PreservesVariousFontSizes(double fontSize)
    {
        // Arrange
        var textOp = new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { "Test" },
            Text = "Test",
            FontName = "/F1",
            FontSize = fontSize,
            BoundingBox = new PdfRectangle { Left = 100, Bottom = 700, Right = 150, Top = 700 + fontSize },
            Glyphs = new List<GlyphPosition>()
        };

        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 4,
            Keep = true,
            StartX = 100,
            StartY = 700,
            Width = 50,
            Height = fontSize,
            OriginalText = "Test"
        };

        // Act
        var reconstructor = new OperationReconstructor();
        var operations = reconstructor.ReconstructWithPositioning(
            new List<TextSegment> { segment },
            textOp);

        // Assert
        var tmOp = operations.OfType<TextStateOperation>().First(op => op.Operator == "Tm");
        var scale = Convert.ToDouble(tmOp.Operands[0]);

        scale.Should().BeApproximately(fontSize, 0.01,
            $"Tm scale should preserve font size {fontSize}pt");

        _output.WriteLine($"Font size {fontSize}pt -> Tm scale {scale}");
    }

    /// <summary>
    /// CRITICAL TEST: Visual regression test for the actual birth certificate PDF that surfaced
    /// the font scaling corruption bug. This test renders the PDF before and after redaction
    /// to verify text remains at the correct size (not shrunk to 1pt).
    ///
    /// The bug was: PDFs using Tf=1 with scaling in Tm matrix (e.g., "9 0 0 9 x y Tm")
    /// were being reconstructed with identity matrix (1 0 0 1), making text nearly invisible.
    /// </summary>
    [Fact]
    public void BirthCertificate_VisualRegression_FontSizesPreserved()
    {
        // Arrange - Use the actual birth certificate PDF
        var birthCertPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(birthCertPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {birthCertPath}");
            return;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"birth_cert_visual_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(outputPath);

        // Render original PDF
        var originalImage = RenderPdfToImage(birthCertPath);
        SaveScreenshot(originalImage, "birth_cert_original.png");
        _output.WriteLine($"Original birth certificate: {originalImage.Width}x{originalImage.Height}");

        // Measure text heights in specific regions of the original
        // Header region (top of form) - should contain "BIRTH CERTIFICATE" in larger font
        var headerHeight = MeasureTextHeight(originalImage, 100, 50, 400, 100);
        _output.WriteLine($"Original header text height: {headerHeight}px");

        // Body region (middle of form) - contains form labels
        var bodyHeight = MeasureTextHeight(originalImage, 50, 200, 500, 100);
        _output.WriteLine($"Original body text height: {bodyHeight}px");

        // Act - Redact something that won't affect the measured regions
        var redactor = new TextRedactor();
        var result = redactor.RedactText(birthCertPath, outputPath, "TORRINGTON");
        result.Success.Should().BeTrue($"Redaction should succeed. Error: {result.ErrorMessage}");

        // Render redacted PDF
        var redactedImage = RenderPdfToImage(outputPath);
        SaveScreenshot(redactedImage, "birth_cert_redacted.png");
        _output.WriteLine($"Redacted birth certificate: {redactedImage.Width}x{redactedImage.Height}");

        // Measure text heights in the redacted version
        var headerHeightRedacted = MeasureTextHeight(redactedImage, 100, 50, 400, 100);
        var bodyHeightRedacted = MeasureTextHeight(redactedImage, 50, 200, 500, 100);

        _output.WriteLine($"Redacted header text height: {headerHeightRedacted}px");
        _output.WriteLine($"Redacted body text height: {bodyHeightRedacted}px");

        // Assert - Text heights should be similar (not shrunk to near-zero)
        // The font scaling bug would cause text to shrink dramatically (e.g., from 20px to 2px)
        if (headerHeight > 0 && headerHeightRedacted > 0)
        {
            var headerRatio = (double)headerHeightRedacted / headerHeight;
            _output.WriteLine($"Header height ratio: {headerRatio:F2}");

            // If ratio is < 0.5, text has shrunk dramatically (bug is present)
            headerRatio.Should().BeGreaterThan(0.5,
                "Header text should not shrink dramatically after redaction (font scaling bug)");
        }

        if (bodyHeight > 0 && bodyHeightRedacted > 0)
        {
            var bodyRatio = (double)bodyHeightRedacted / bodyHeight;
            _output.WriteLine($"Body height ratio: {bodyRatio:F2}");

            bodyRatio.Should().BeGreaterThan(0.5,
                "Body text should not shrink dramatically after redaction (font scaling bug)");
        }

        // Also check overall image similarity - if text shrunk, image would look very different
        var overallDiff = CalculateImageDifference(originalImage, redactedImage);
        _output.WriteLine($"Overall image difference: {overallDiff:F2}%");

        // The overall difference should be moderate (text removed, but rest preserved)
        // If font scaling bug was present, difference would be > 50% (nearly blank page)
        overallDiff.Should().BeLessThan(30.0,
            "Overall image should be similar after redaction (not nearly blank from font scaling bug)");

        originalImage.Dispose();
        redactedImage.Dispose();
    }

    /// <summary>
    /// Test sequential redactions on birth certificate to verify font scaling is preserved
    /// across multiple redaction operations (the original bug scenario).
    /// </summary>
    [Fact]
    public void BirthCertificate_SequentialRedactions_FontSizesPreserved()
    {
        // Arrange - Use the actual birth certificate PDF
        var birthCertPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(birthCertPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {birthCertPath}");
            return;
        }

        var temp1 = Path.Combine(Path.GetTempPath(), $"birth_cert_seq1_{Guid.NewGuid()}.pdf");
        var temp2 = Path.Combine(Path.GetTempPath(), $"birth_cert_seq2_{Guid.NewGuid()}.pdf");
        var temp3 = Path.Combine(Path.GetTempPath(), $"birth_cert_seq3_{Guid.NewGuid()}.pdf");
        var finalOutput = Path.Combine(Path.GetTempPath(), $"birth_cert_seq_final_{Guid.NewGuid()}.pdf");
        _tempFiles.AddRange(new[] { temp1, temp2, temp3, finalOutput });

        // Render original for comparison
        var originalImage = RenderPdfToImage(birthCertPath);
        var originalTextHeight = MeasureTextHeight(originalImage, 50, 50, 500, 200);
        _output.WriteLine($"Original text height (top 200px): {originalTextHeight}px");
        SaveScreenshot(originalImage, "birth_cert_seq_original.png");

        // Act - Perform multiple sequential redactions (the original bug scenario)
        var redactor = new TextRedactor();

        var result1 = redactor.RedactText(birthCertPath, temp1, "TORRINGTON");
        result1.Success.Should().BeTrue();
        _output.WriteLine("Redaction 1 (TORRINGTON) complete");

        var result2 = redactor.RedactText(temp1, temp2, "CERTIFICATE");
        result2.Success.Should().BeTrue();
        _output.WriteLine("Redaction 2 (CERTIFICATE) complete");

        var result3 = redactor.RedactText(temp2, temp3, "$20.00");
        result3.Success.Should().BeTrue();
        _output.WriteLine("Redaction 3 ($20.00) complete");

        var result4 = redactor.RedactText(temp3, finalOutput, "$15.00");
        result4.Success.Should().BeTrue();
        _output.WriteLine("Redaction 4 ($15.00) complete");

        // Render final result
        var finalImage = RenderPdfToImage(finalOutput);
        var finalTextHeight = MeasureTextHeight(finalImage, 50, 50, 500, 200);
        _output.WriteLine($"Final text height (top 200px): {finalTextHeight}px");
        SaveScreenshot(finalImage, "birth_cert_seq_final.png");

        // Assert - Text height should be preserved through all redactions
        if (originalTextHeight > 5 && finalTextHeight > 0)
        {
            var ratio = (double)finalTextHeight / originalTextHeight;
            _output.WriteLine($"Height ratio after 4 redactions: {ratio:F2}");

            // If the font scaling bug was present, text would shrink dramatically
            ratio.Should().BeGreaterThan(0.3,
                "Text height should be preserved through sequential redactions");
        }

        originalImage.Dispose();
        finalImage.Dispose();
    }

    #region Helper Methods

    private void CreatePdfWithTextAtSizes(string path, (string text, int x, int y, int fontSize)[] textItems)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        foreach (var (text, x, y, fontSize) in textItems)
        {
            var font = new XFont("Helvetica", fontSize);
            // Convert from PDF coordinates (bottom-left) to PdfSharp (top-left)
            var yPos = page.Height.Point - y;
            gfx.DrawString(text, font, XBrushes.Black, x, yPos);
        }

        document.Save(path);
    }

    private SKBitmap RenderPdfToImage(string pdfPath)
    {
        // Use PDFtoImage to render with RenderOptions
        using var stream = File.OpenRead(pdfPath);
        var options = new PDFtoImage.RenderOptions(Dpi: 150);
        using var image = PDFtoImage.Conversion.ToImage(stream, page: 0, options: options);

        // Convert to SKBitmap
        using var memStream = new MemoryStream();
        image.Encode(memStream, SKEncodedImageFormat.Png, 100);
        memStream.Position = 0;

        return SKBitmap.Decode(memStream);
    }

    private SKBitmap ExtractRegion(SKBitmap source, int x, int y, int width, int height)
    {
        // Clamp to image bounds
        x = Math.Max(0, Math.Min(x, source.Width - 1));
        y = Math.Max(0, Math.Min(y, source.Height - 1));
        width = Math.Min(width, source.Width - x);
        height = Math.Min(height, source.Height - y);

        var region = new SKBitmap(width, height);
        using var canvas = new SKCanvas(region);
        canvas.DrawBitmap(source, new SKRect(x, y, x + width, y + height),
            new SKRect(0, 0, width, height));
        return region;
    }

    private double CalculateImageDifference(SKBitmap img1, SKBitmap img2)
    {
        if (img1.Width != img2.Width || img1.Height != img2.Height)
            return 100.0;

        long differentPixels = 0;
        long totalPixels = img1.Width * img1.Height;

        for (int y = 0; y < img1.Height; y++)
        {
            for (int x = 0; x < img1.Width; x++)
            {
                var p1 = img1.GetPixel(x, y);
                var p2 = img2.GetPixel(x, y);

                // Allow tolerance for antialiasing
                if (Math.Abs(p1.Red - p2.Red) > 10 ||
                    Math.Abs(p1.Green - p2.Green) > 10 ||
                    Math.Abs(p1.Blue - p2.Blue) > 10)
                {
                    differentPixels++;
                }
            }
        }

        return (double)differentPixels / totalPixels * 100.0;
    }

    private int MeasureTextHeight(SKBitmap image, int regionX, int regionY, int regionWidth, int regionHeight)
    {
        // Measure the vertical extent of non-white pixels in the region
        int topMost = -1;
        int bottomMost = -1;

        for (int y = regionY; y < Math.Min(regionY + regionHeight, image.Height); y++)
        {
            for (int x = regionX; x < Math.Min(regionX + regionWidth, image.Width); x++)
            {
                var pixel = image.GetPixel(x, y);
                // Check if pixel is not white (text or graphics)
                if (pixel.Red < 240 || pixel.Green < 240 || pixel.Blue < 240)
                {
                    if (topMost < 0) topMost = y;
                    bottomMost = y;
                }
            }
        }

        return topMost >= 0 ? bottomMost - topMost + 1 : 0;
    }

    private void SaveScreenshot(SKBitmap image, string filename)
    {
        var path = Path.Combine(_screenshotDir, filename);
        using var stream = File.Create(path);
        image.Encode(stream, SKEncodedImageFormat.Png, 100);
        _output.WriteLine($"Screenshot saved: {path}");
    }

    #endregion
}
