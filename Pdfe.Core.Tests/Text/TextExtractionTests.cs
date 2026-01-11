using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Text;
using System.Text;
using Xunit;

namespace Pdfe.Core.Tests.Text;

/// <summary>
/// TDD tests for text extraction - these define the expected API behavior.
/// Tests are written FIRST, then implementation follows.
/// </summary>
public class TextExtractionTests
{
    #region API Tests - Define the expected interface

    [Fact]
    public void Page_Text_ReturnsExtractedText()
    {
        // Arrange - Create a PDF with known text content
        var pdfData = CreatePdfWithText("Hello World");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert - Page should have a Text property
        page.Text.Should().Contain("Hello World");
    }

    [Fact]
    public void Page_Letters_ReturnsLetterCollection()
    {
        // Arrange
        var pdfData = CreatePdfWithText("ABC");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert - Page should have a Letters collection
        page.Letters.Should().NotBeEmpty();
        page.Letters.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void Letter_HasExpectedProperties()
    {
        // Arrange
        var pdfData = CreatePdfWithText("A");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var letter = page.Letters.First();

        // Assert - Letter should have position and character info
        letter.Value.Should().Be("A");
        letter.GlyphRectangle.Width.Should().BeGreaterThan(0);
        letter.GlyphRectangle.Height.Should().BeGreaterThan(0);
        letter.FontSize.Should().BeGreaterThan(0);
        letter.FontName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Letters_HaveCorrectOrder()
    {
        // Arrange
        var pdfData = CreatePdfWithText("ABC");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var text = string.Join("", page.Letters.Select(l => l.Value));

        // Assert - Letters should be in reading order
        text.Should().Contain("ABC");
    }

    [Fact]
    public void Letter_Position_IsAccurate()
    {
        // Arrange - Text at position 100, 700
        var pdfData = CreatePdfWithTextAtPosition("X", 100, 700);

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var letter = page.Letters.First();

        // Assert - Letter position should be near expected location
        letter.GlyphRectangle.Left.Should().BeApproximately(100, 5);
        // PDF coordinates are bottom-up, so Y at 700 is near top
        letter.GlyphRectangle.Bottom.Should().BeApproximately(700, 20);
    }

    #endregion

    #region Encoding Tests

    [Fact]
    public void TextExtraction_WinAnsiEncoding_DecodesCorrectly()
    {
        // Arrange - PDF with WinAnsiEncoding
        var pdfData = CreatePdfWithEncodedText("Hello", "WinAnsiEncoding");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert
        page.Text.Should().Contain("Hello");
    }

    [Fact]
    public void TextExtraction_StandardType1Font_ExtractsText()
    {
        // Arrange - PDF with standard Type1 font (Helvetica)
        var pdfData = CreatePdfWithStandardFont("Test", "Helvetica");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert
        page.Text.Should().Contain("Test");
    }

    #endregion

    #region Text Operator Tests

    [Fact]
    public void TextExtraction_TjOperator_ExtractsText()
    {
        // Arrange - PDF using Tj operator: (Hello) Tj
        var pdfData = CreatePdfWithContentStream("BT /F1 12 Tf 100 700 Td (Hello) Tj ET");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert
        page.Text.Should().Contain("Hello");
    }

    [Fact]
    public void TextExtraction_TJOperator_ExtractsText()
    {
        // Arrange - PDF using TJ operator with array: [(H) 10 (ello)] TJ
        var pdfData = CreatePdfWithContentStream("BT /F1 12 Tf 100 700 Td [(H) 10 (ello)] TJ ET");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert
        page.Text.Should().Contain("Hello");
    }

    [Fact]
    public void TextExtraction_MultipleTdMoves_TracksPosition()
    {
        // Arrange - Multiple Td moves
        var content = "BT /F1 12 Tf 100 700 Td (Line1) Tj 0 -20 Td (Line2) Tj ET";
        var pdfData = CreatePdfWithContentStream(content);

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert
        page.Text.Should().Contain("Line1");
        page.Text.Should().Contain("Line2");
    }

    [Fact]
    public void TextExtraction_TmOperator_SetsTextMatrix()
    {
        // Arrange - Tm sets absolute text matrix
        var content = "BT /F1 12 Tf 1 0 0 1 200 500 Tm (Positioned) Tj ET";
        var pdfData = CreatePdfWithContentStream(content);

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var letter = page.Letters.FirstOrDefault();

        // Assert
        page.Text.Should().Contain("Positioned");
        letter?.GlyphRectangle.Left.Should().BeApproximately(200, 5);
    }

    #endregion

    #region ToUnicode CMap Tests

    [Fact]
    public void TextExtraction_ToUnicodeMap_DecodesCorrectly()
    {
        // Arrange - PDF with ToUnicode CMap that maps codes to Unicode
        var pdfData = CreatePdfWithToUnicode("ABC");

        // Act
        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        // Assert - Should decode using ToUnicode map
        page.Text.Should().Contain("ABC");
    }

    [Fact]
    public void TextExtraction_ToUnicode_HandlesBfRange()
    {
        // ToUnicode CMaps can use bfrange to map ranges
        // e.g., <0041> <005A> <0041> maps A-Z directly
        var pdfData = CreatePdfWithToUnicode("XYZ");

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        page.Text.Should().Contain("XYZ");
    }

    #endregion

    #region Helper Methods - Create test PDFs

    private static byte[] CreatePdfWithText(string text)
    {
        return CreatePdfWithContentStream($"BT /F1 12 Tf 100 700 Td ({EscapePdfString(text)}) Tj ET");
    }

    private static byte[] CreatePdfWithTextAtPosition(string text, double x, double y)
    {
        return CreatePdfWithContentStream($"BT /F1 12 Tf {x} {y} Td ({EscapePdfString(text)}) Tj ET");
    }

    private static byte[] CreatePdfWithEncodedText(string text, string encoding)
    {
        // For now, same as basic - encoding is set in font dictionary
        return CreatePdfWithText(text);
    }

    private static byte[] CreatePdfWithToUnicode(string text)
    {
        // Create a PDF with a ToUnicode CMap
        // The CMap maps character codes to Unicode values
        var toUnicodeStream = @"/CIDInit /ProcSet findresource begin
12 dict begin
begincmap
/CIDSystemInfo <<
  /Registry (Adobe)
  /Ordering (UCS)
  /Supplement 0
>> def
/CMapName /Adobe-Identity-UCS def
/CMapType 2 def
1 begincodespacerange
<00> <FF>
endcodespacerange
1 beginbfrange
<41> <5A> <0041>
endcmap
CMapName currentdict /CMap defineresource pop
end
end";
        return CreatePdfWithToUnicodeStream($"BT /F1 12 Tf 100 700 Td ({EscapePdfString(text)}) Tj ET", toUnicodeStream);
    }

    private static byte[] CreatePdfWithToUnicodeStream(string content, string toUnicodeContent)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        // Header
        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[7];

        // Object 1: Catalog
        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 2: Pages
        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 3: Page
        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: Content stream
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 5: Font with ToUnicode
        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /ToUnicode 6 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 6: ToUnicode CMap stream
        offsets[6] = ms.Position;
        writer.WriteLine("6 0 obj");
        writer.WriteLine($"<< /Length {toUnicodeContent.Length} >>");
        writer.WriteLine("stream");
        writer.Write(toUnicodeContent);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // xref
        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 7");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 6; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        // trailer
        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 7 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    private static byte[] CreatePdfWithStandardFont(string text, string fontName)
    {
        return CreatePdfWithContentStream($"BT /F1 12 Tf 100 700 Td ({EscapePdfString(text)}) Tj ET", fontName);
    }

    private static byte[] CreatePdfWithContentStream(string content, string fontName = "Helvetica")
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        // Header
        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[6];

        // Object 1: Catalog
        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 2: Pages
        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 3: Page
        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: Content stream
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 5: Font
        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine($"<< /Type /Font /Subtype /Type1 /BaseFont /{fontName} /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // xref
        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 5; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        // trailer
        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 6 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    private static string EscapePdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    #endregion
}
