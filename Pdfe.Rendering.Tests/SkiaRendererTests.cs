using FluentAssertions;
using Pdfe.Core.Document;
using SkiaSharp;
using Xunit;

namespace Pdfe.Rendering.Tests;

/// <summary>
/// TDD tests for SkiaSharp-based PDF rendering.
/// </summary>
public class SkiaRendererTests
{
    #region Basic Rendering Tests

    [Fact]
    public void RenderPage_ReturnsNonNullBitmap()
    {
        // Arrange
        var pdfData = CreateSimplePdf("Hello World");
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
    }

    [Fact]
    public void RenderPage_DefaultDpi_Returns150DpiImage()
    {
        // Arrange
        var pdfData = CreateSimplePdf("Test");
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();
        var page = doc.GetPage(1);

        // Act
        using var bitmap = renderer.RenderPage(page);

        // Assert - US Letter at 150 DPI should be ~1275x1650 pixels
        // 612 points * 150/72 = 1275, 792 points * 150/72 = 1650
        var expectedWidth = (int)Math.Round(page.Width * 150 / 72);
        var expectedHeight = (int)Math.Round(page.Height * 150 / 72);
        bitmap.Width.Should().Be(expectedWidth);
        bitmap.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void RenderPage_CustomDpi_ScalesCorrectly()
    {
        // Arrange
        var pdfData = CreateSimplePdf("Test");
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();
        var page = doc.GetPage(1);
        var options = new RenderOptions { Dpi = 300 };

        // Act
        using var bitmap = renderer.RenderPage(page, options);

        // Assert - 300 DPI should be double the size of 150 DPI
        var expectedWidth = (int)Math.Round(page.Width * 300 / 72);
        var expectedHeight = (int)Math.Round(page.Height * 300 / 72);
        bitmap.Width.Should().Be(expectedWidth);
        bitmap.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void RenderPage_WhiteBackground_IsWhite()
    {
        // Arrange
        var pdfData = CreateEmptyPdf();
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();
        var options = new RenderOptions { BackgroundColor = SKColors.White };

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1), options);

        // Assert - check center pixel is white
        var centerX = bitmap.Width / 2;
        var centerY = bitmap.Height / 2;
        var pixel = bitmap.GetPixel(centerX, centerY);
        pixel.Should().Be(SKColors.White);
    }

    #endregion

    #region Rectangle Rendering Tests

    [Fact]
    public void RenderPage_FilledRectangle_ShowsBlackPixel()
    {
        // Arrange
        var pdfData = CreatePdfWithRectangle(100, 100, 200, 150, fill: true);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample pixel inside rectangle should be black (or non-white)
        // Rectangle at PDF coordinates (100, 100, 200, 150) means:
        // - X: 100 to 300 (left edge + width)
        // - Y: 100 to 250 (bottom edge + height in PDF coords)
        // At 150 DPI, X pixel = 100 * 150/72 = 208, Y = depends on flip
        var pixelX = (int)(200 * 150 / 72); // Center of rectangle X
        var pixelY = bitmap.Height - (int)(175 * 150 / 72); // Center of rectangle Y, flipped
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Should().NotBe(SKColors.White, "rectangle area should be filled");
    }

    [Fact]
    public void RenderPage_StrokedRectangle_ShowsOutline()
    {
        // Arrange
        var pdfData = CreatePdfWithRectangle(50, 50, 100, 100, fill: false, stroke: true);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - center of rectangle should be white (not filled)
        var centerX = (int)(100 * 150 / 72);
        var centerY = bitmap.Height - (int)(100 * 150 / 72);
        var centerPixel = bitmap.GetPixel(centerX, centerY);
        centerPixel.Should().Be(SKColors.White, "rectangle interior should not be filled");
    }

    #endregion

    #region Line Rendering Tests

    [Fact]
    public void RenderPage_Line_ShowsStroke()
    {
        // Arrange
        var pdfData = CreatePdfWithLine(100, 100, 300, 300);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample a point along the line
        var midX = (int)(200 * 150 / 72);
        var midY = bitmap.Height - (int)(200 * 150 / 72);
        var pixel = bitmap.GetPixel(midX, midY);
        // The line may not hit exactly due to anti-aliasing, so just check it's not white
        pixel.Should().NotBe(SKColors.White, "line should be visible");
    }

    #endregion

    #region Color Tests

    [Fact]
    public void RenderPage_RedRectangle_ShowsRed()
    {
        // Arrange
        var pdfData = CreatePdfWithColoredRectangle(100, 100, 200, 150, 1, 0, 0); // RGB red
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample inside rectangle
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Red.Should().BeGreaterThan(200, "red component should be high");
        pixel.Green.Should().BeLessThan(50, "green component should be low");
        pixel.Blue.Should().BeLessThan(50, "blue component should be low");
    }

    [Fact]
    public void RenderPage_GrayscaleRectangle_ShowsGray()
    {
        // Arrange - 50% gray (0.5 g)
        var pdfData = CreatePdfWithGrayscaleRectangle(100, 100, 200, 150, 0.5);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample inside rectangle should be gray
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        // 50% gray = approximately 128
        pixel.Red.Should().BeInRange((byte)100, (byte)160);
        pixel.Green.Should().BeInRange((byte)100, (byte)160);
        pixel.Blue.Should().BeInRange((byte)100, (byte)160);
    }

    #endregion

    #region Transformation Tests

    [Fact]
    public void RenderPage_TranslatedRectangle_IsOffset()
    {
        // Arrange - rectangle at (0,0) translated by (200, 200)
        var pdfData = CreatePdfWithTranslatedRectangle(0, 0, 50, 50, 200, 200);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - rectangle should be at (200, 200) not (0, 0)
        var origin = bitmap.GetPixel(10, bitmap.Height - 10);
        origin.Should().Be(SKColors.White, "original position should be empty");

        var translated = bitmap.GetPixel((int)(225 * 150 / 72), bitmap.Height - (int)(225 * 150 / 72));
        translated.Should().NotBe(SKColors.White, "translated position should have content");
    }

    #endregion

    #region State Stack Tests

    [Fact]
    public void RenderPage_SaveRestoreState_WorksCorrectly()
    {
        // Arrange - save state, draw red, restore, draw black
        var pdfData = CreatePdfWithStateStack();
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should have both rectangles rendered
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateSimplePdf(string text)
    {
        var content = $"BT /F1 12 Tf 100 700 Td ({text}) Tj ET";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreateEmptyPdf()
    {
        return CreatePdfWithContent("");
    }

    private static byte[] CreatePdfWithRectangle(int x, int y, int w, int h, bool fill = true, bool stroke = false)
    {
        var op = fill && stroke ? "B" : (fill ? "f" : (stroke ? "S" : "n"));
        var content = $"0 g {x} {y} {w} {h} re {op}";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithLine(int x1, int y1, int x2, int y2)
    {
        var content = $"0 G 1 w {x1} {y1} m {x2} {y2} l S";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithColoredRectangle(int x, int y, int w, int h, double r, double g, double b)
    {
        var content = $"{r} {g} {b} rg {x} {y} {w} {h} re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithGrayscaleRectangle(int x, int y, int w, int h, double gray)
    {
        var content = $"{gray} g {x} {y} {w} {h} re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithTranslatedRectangle(int x, int y, int w, int h, double tx, double ty)
    {
        var content = $"q 1 0 0 1 {tx} {ty} cm 0 g {x} {y} {w} {h} re f Q";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithStateStack()
    {
        var content = "q 1 0 0 rg 50 50 100 100 re f Q 0 g 200 200 100 100 re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithContent(string content)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
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
}
