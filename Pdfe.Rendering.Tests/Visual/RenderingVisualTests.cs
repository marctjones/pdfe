using FluentAssertions;
using Pdfe.Core.Document;
using SkiaSharp;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace Pdfe.Rendering.Tests.Visual;

/// <summary>
/// Visual regression tests for PDF rendering.
/// These tests verify that the renderer produces pixel-perfect output
/// by comparing against baseline images.
///
/// BASELINE GENERATION:
/// When adding new tests, run once to generate baseline, manually verify
/// the output looks correct, then commit the baseline image.
///
/// UPDATING BASELINES:
/// If rendering intentionally changes, regenerate baselines:
/// 1. Delete old baseline images
/// 2. Run tests (they will fail and generate new images)
/// 3. Manually verify new images look correct
/// 4. Rename from test-output/ to baselines/
/// 5. Commit new baselines
/// </summary>
public class RenderingVisualTests : IDisposable
{
    private readonly string _testOutputDir;
    private readonly string _baselinesDir;
    private readonly SkiaRenderer _renderer;

    public RenderingVisualTests()
    {
        // Set up directories
        var testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "Visual");
        _baselinesDir = Path.Combine(testDataDir, "baselines");
        _testOutputDir = Path.Combine(testDataDir, "test-output");

        Directory.CreateDirectory(_baselinesDir);
        Directory.CreateDirectory(_testOutputDir);

        _renderer = new SkiaRenderer();
    }

    public void Dispose()
    {
        // Cleanup is handled by test infrastructure
    }

    #region Helper Methods

    private SKBitmap RenderTestPdf(string testName, string content)
    {
        var pdfBytes = CreatePdfWithContent(content);
        var pdfPath = Path.Combine(_testOutputDir, $"{testName}.pdf");
        File.WriteAllBytes(pdfPath, pdfBytes);

        // Render
        using var doc = PdfDocument.Open(pdfBytes);
        var page = doc.GetPage(1);
        var options = new RenderOptions { Dpi = 150 }; // 150 DPI for reasonable file size
        return _renderer.RenderPage(page, options);
    }

    private void AssertVisualMatch(SKBitmap actual, string testName, double maxDifference = 0.01)
    {
        var baselinePath = Path.Combine(_baselinesDir, $"{testName}.png");
        var actualPath = Path.Combine(_testOutputDir, $"{testName}-actual.png");
        var diffPath = Path.Combine(_testOutputDir, $"{testName}-diff.png");

        // Save actual output for debugging
        VisualAssertions.SavePng(actual, actualPath);

        // If baseline doesn't exist, save actual as baseline template
        if (!File.Exists(baselinePath))
        {
            VisualAssertions.SavePng(actual, baselinePath);
            Assert.Fail($"Baseline not found. Generated baseline at: {baselinePath}\n" +
                       $"Please manually verify the image looks correct and re-run the test.");
        }

        // Compare against baseline
        actual.ShouldVisuallyMatch(baselinePath, maxDifference, diffPath);
    }

    private static byte[] CreatePdfWithContent(string content)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[6];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 5; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 6 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    #endregion

    #region Text Rendering Tests

    [Fact]
    public void SimpleText_RendersCorrectly()
    {
        // Arrange & Act
        var content = "BT /F1 24 Tf 100 700 Td (Hello, World!) Tj ET";
        var bitmap = RenderTestPdf("simple-text", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "simple-text");
        }
    }

    [Fact]
    public void MultilineText_RendersCorrectly()
    {
        // Arrange & Act
        var content = "BT /F1 14 Tf 50 700 Td (Line 1) Tj 0 -20 Td (Line 2) Tj 0 -20 Td (Line 3) Tj ET";
        var bitmap = RenderTestPdf("multiline-text", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "multiline-text");
        }
    }

    [Fact]
    public void DifferentFontSizes_RenderCorrectly()
    {
        // Arrange & Act
        var content = @"
            BT
            /F1 10 Tf 50 750 Td (10pt text) Tj
            /F1 16 Tf 0 -30 Td (16pt text) Tj
            /F1 24 Tf 0 -40 Td (24pt text) Tj
            /F1 36 Tf 0 -60 Td (36pt text) Tj
            ET
        ";
        var bitmap = RenderTestPdf("font-sizes", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "font-sizes");
        }
    }

    #endregion

    #region Graphics Rendering Tests

    [Fact]
    public void Rectangle_RendersCorrectly()
    {
        // Arrange & Act
        var content = "0 G 2 w 100 600 200 100 re S";
        var bitmap = RenderTestPdf("rectangle", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "rectangle");
        }
    }

    [Fact]
    public void FilledRectangle_RendersCorrectly()
    {
        // Arrange & Act
        var content = "0.5 g 100 600 200 100 re f";
        var bitmap = RenderTestPdf("filled-rectangle", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "filled-rectangle");
        }
    }

    [Fact]
    public void BlackRectangle_ForRedactionVerification()
    {
        // Arrange & Act
        var content = "0 g 100 650 300 50 re f";
        var bitmap = RenderTestPdf("black-rectangle-redaction", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "black-rectangle-redaction");
        }
    }

    #endregion

    #region Combined Tests

    [Fact]
    public void TextWithBackground_RendersCorrectly()
    {
        // Arrange & Act
        var content = "0.9 g 50 650 300 80 re f BT /F1 18 Tf 60 700 Td (Text on gray background) Tj ET";
        var bitmap = RenderTestPdf("text-with-background", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "text-with-background");
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EmptyPage_RendersWhite()
    {
        // Arrange & Act
        var content = "";
        var bitmap = RenderTestPdf("empty-page", content);

        // Assert
        using (bitmap)
        {
            AssertVisualMatch(bitmap, "empty-page");

            // Also verify it's actually white
            var centerPixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            centerPixel.Red.Should().BeGreaterThan(250);
            centerPixel.Green.Should().BeGreaterThan(250);
            centerPixel.Blue.Should().BeGreaterThan(250);
        }
    }

    #endregion
}
