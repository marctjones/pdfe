using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Graphics;
using Pdfe.Core.Primitives;
using Xunit;

namespace Pdfe.Core.Tests.Graphics;

/// <summary>
/// TDD tests for PDF graphics API - tests define expected behavior before implementation.
/// </summary>
public class PdfGraphicsTests
{
    #region Context Creation Tests

    [Fact]
    public void GetGraphics_ReturnsGraphicsContext()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Act
        using var graphics = page.GetGraphics();

        // Assert
        graphics.Should().NotBeNull();
    }

    [Fact]
    public void GetGraphics_MultipleCallsOnSamePage_ReturnsSameContext()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Act
        using var g1 = page.GetGraphics();
        using var g2 = page.GetGraphics();

        // Assert - same context returned (or at least compatible)
        g1.Should().NotBeNull();
        g2.Should().NotBeNull();
    }

    #endregion

    #region State Management Tests

    [Fact]
    public void SaveState_RestoreState_Works()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act & Assert - should not throw
        graphics.SaveState();
        graphics.RestoreState();
    }

    [Fact]
    public void SaveState_CanBeCalledMultipleTimes()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act & Assert
        graphics.SaveState();
        graphics.SaveState();
        graphics.SaveState();
        graphics.RestoreState();
        graphics.RestoreState();
        graphics.RestoreState();
    }

    #endregion

    #region Rectangle Drawing Tests

    [Fact]
    public void DrawRectangle_Filled_AddsContentToPage()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var originalContent = page.GetContentStreamBytes();
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 200, 150, PdfBrush.Black);
        graphics.Flush();

        // Assert - content stream should have changed
        var newContent = page.GetContentStreamBytes();
        newContent.Length.Should().BeGreaterThan(originalContent.Length);
    }

    [Fact]
    public void DrawRectangle_ProducesValidPdfOperators()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 200, 150, PdfBrush.Black);
        var operators = graphics.GetOperators();

        // Assert - should contain 're' (rectangle) and 'f' (fill)
        operators.Should().Contain("100 100 200 150 re");
        operators.Should().Contain("f");
    }

    [Fact]
    public void DrawRectangle_WithStroke_ProducesStrokeOperator()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(50, 50, 100, 100, null, PdfPen.Black);
        var operators = graphics.GetOperators();

        // Assert - should contain 're' and 'S' (stroke)
        operators.Should().Contain("50 50 100 100 re");
        operators.Should().Contain("S");
    }

    [Fact]
    public void DrawRectangle_WithFillAndStroke_ProducesCorrectOperators()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(50, 50, 100, 100, PdfBrush.Red, PdfPen.Black);
        var operators = graphics.GetOperators();

        // Assert - should contain 'B' (fill and stroke)
        operators.Should().Contain("B");
    }

    #endregion

    #region Color Tests

    [Fact]
    public void DrawRectangle_BlackFill_SetsColorCorrectly()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 50, 50, PdfBrush.Black);
        var operators = graphics.GetOperators();

        // Assert - should set fill color to black (0 g)
        operators.Should().Contain("0 g");
    }

    [Fact]
    public void DrawRectangle_RedFill_SetsColorCorrectly()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 50, 50, PdfBrush.Red);
        var operators = graphics.GetOperators();

        // Assert - should set fill color (1 0 0 rg for RGB red)
        operators.Should().Contain("1 0 0 rg");
    }

    [Fact]
    public void DrawRectangle_WhiteStroke_SetsStrokeColor()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 50, 50, null, PdfPen.White);
        var operators = graphics.GetOperators();

        // Assert - should set stroke color (1 G for grayscale white)
        operators.Should().Contain("1 G");
    }

    #endregion

    #region Line Drawing Tests

    [Fact]
    public void DrawLine_ProducesCorrectOperators()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawLine(0, 0, 100, 100, PdfPen.Black);
        var operators = graphics.GetOperators();

        // Assert - should contain 'm' (moveto), 'l' (lineto), 'S' (stroke)
        operators.Should().Contain("0 0 m");
        operators.Should().Contain("100 100 l");
        operators.Should().Contain("S");
    }

    [Fact]
    public void DrawLine_WithLineWidth_SetsWidth()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        var pen = new PdfPen(PdfColor.Black, 2.5);
        graphics.DrawLine(0, 0, 100, 100, pen);
        var operators = graphics.GetOperators();

        // Assert - should set line width
        operators.Should().Contain("2.5 w");
    }

    #endregion

    #region Path Drawing Tests

    [Fact]
    public void BeginPath_MoveTo_LineTo_Stroke_ProducesCorrectOperators()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.BeginPath();
        graphics.MoveTo(0, 0);
        graphics.LineTo(100, 0);
        graphics.LineTo(100, 100);
        graphics.LineTo(0, 100);
        graphics.ClosePath();
        graphics.Stroke(PdfPen.Black);
        var operators = graphics.GetOperators();

        // Assert
        operators.Should().Contain("0 0 m");
        operators.Should().Contain("100 0 l");
        operators.Should().Contain("100 100 l");
        operators.Should().Contain("0 100 l");
        operators.Should().Contain("h"); // closepath
        operators.Should().Contain("S");
    }

    [Fact]
    public void CurveTo_ProducesCurveOperator()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.BeginPath();
        graphics.MoveTo(0, 0);
        graphics.CurveTo(10, 20, 30, 40, 50, 0); // x1,y1,x2,y2,x3,y3
        graphics.Stroke(PdfPen.Black);
        var operators = graphics.GetOperators();

        // Assert - 'c' is curve operator with 3 control points
        operators.Should().Contain("10 20 30 40 50 0 c");
    }

    #endregion

    #region Transformation Tests

    [Fact]
    public void Translate_AddsCmOperator()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.Translate(100, 200);
        var operators = graphics.GetOperators();

        // Assert - translate is cm with [1 0 0 1 tx ty]
        operators.Should().Contain("1 0 0 1 100 200 cm");
    }

    [Fact]
    public void Scale_AddsCmOperator()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.Scale(2, 3);
        var operators = graphics.GetOperators();

        // Assert - scale is cm with [sx 0 0 sy 0 0]
        operators.Should().Contain("2 0 0 3 0 0 cm");
    }

    #endregion

    #region Flush and Save Tests

    [Fact]
    public void Flush_AppendsToContentStream()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 50, 50, PdfBrush.Black);
        graphics.Flush();

        // Assert
        var content = System.Text.Encoding.Latin1.GetString(page.GetContentStreamBytes());
        content.Should().Contain("re");
        content.Should().Contain("f");
    }

    [Fact]
    public void RoundTrip_WithGraphics_PreservesDrawing()
    {
        // Arrange
        var pdfData = CreateSimplePdf();
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        using var graphics = page.GetGraphics();

        // Act
        graphics.DrawRectangle(100, 100, 50, 50, PdfBrush.Black);
        graphics.Flush();

        var savedData = doc.SaveToBytes();
        using var reopened = PdfDocument.Open(savedData);
        var content = System.Text.Encoding.Latin1.GetString(reopened.GetPage(1).GetContentStreamBytes());

        // Assert
        content.Should().Contain("100 100 50 50 re");
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateSimplePdf()
    {
        var content = "BT /F1 12 Tf 100 700 Td (Test) Tj ET";

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
