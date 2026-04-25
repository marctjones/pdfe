using FluentAssertions;
using Pdfe.Core.Document;
using System.Text;
using Xunit;

namespace Pdfe.Core.Tests.Document;

public class PdfDocumentTests
{
    /// <summary>
    /// Creates a minimal valid PDF for testing with correct byte offsets.
    /// </summary>
    private static byte[] CreateMinimalPdf()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        // Header
        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        // Track object positions
        var offsets = new long[4];

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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // xref position
        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 4");
        writer.WriteLine("0000000000 65535 f ");
        writer.WriteLine($"{offsets[1]:D10} 00000 n ");
        writer.WriteLine($"{offsets[2]:D10} 00000 n ");
        writer.WriteLine($"{offsets[3]:D10} 00000 n ");
        writer.Flush();

        // trailer
        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 4 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal PDF with content stream.
    /// </summary>
    private static byte[] CreatePdfWithContent()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        var content = "BT /F1 12 Tf 100 700 Td (Hello World) Tj ET";

        // Header
        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        // Track object positions
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

        // Object 5: Font (simplified)
        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // xref position
        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        writer.WriteLine($"{offsets[1]:D10} 00000 n ");
        writer.WriteLine($"{offsets[2]:D10} 00000 n ");
        writer.WriteLine($"{offsets[3]:D10} 00000 n ");
        writer.WriteLine($"{offsets[4]:D10} 00000 n ");
        writer.WriteLine($"{offsets[5]:D10} 00000 n ");
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

    [Fact]
    public void Open_MinimalPdf_ReturnsDocument()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);

        doc.Should().NotBeNull();
        doc.Version.Should().Be("1.4");
        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public void GetPage_FirstPage_ReturnsPage()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        page.Should().NotBeNull();
        page.PageNumber.Should().Be(1);
        page.Width.Should().Be(612);
        page.Height.Should().Be(792);
    }

    [Fact]
    public void GetPage_InvalidPageNumber_ThrowsException()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);

        var act1 = () => doc.GetPage(0);
        var act2 = () => doc.GetPage(2);

        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetPages_EnumeratesAllPages()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);
        var pages = doc.GetPages().ToList();

        pages.Count.Should().Be(1);
        pages[0].PageNumber.Should().Be(1);
    }

    [Fact]
    public void Page_MediaBox_ReturnsCorrectRectangle()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        page.MediaBox.Left.Should().Be(0);
        page.MediaBox.Bottom.Should().Be(0);
        page.MediaBox.Right.Should().Be(612);
        page.MediaBox.Top.Should().Be(792);
    }

    [Fact]
    public void Page_CropBox_FallsBackToMediaBox()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        page.CropBox.Should().Be(page.MediaBox);
    }

    [Fact]
    public void Page_GetContentStreamBytes_ReturnsContent()
    {
        var pdfData = CreatePdfWithContent();

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var content = page.GetContentStreamBytes();

        content.Should().NotBeEmpty();
        var contentStr = Encoding.ASCII.GetString(content);
        contentStr.Should().Contain("Hello World");
    }

    [Fact]
    public void Page_Resources_ReturnsResourcesDictionary()
    {
        var pdfData = CreatePdfWithContent();

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);

        page.Resources.Should().NotBeNull();
        page.Resources!.ContainsKey("Font").Should().BeTrue();
    }

    [Fact]
    public void Page_GetFont_ReturnsFont()
    {
        var pdfData = CreatePdfWithContent();

        using var doc = PdfDocument.Open(pdfData);
        var page = doc.GetPage(1);
        var font = page.GetFont("F1");

        font.Should().NotBeNull();
        font!.GetName("BaseFont").Should().Be("Helvetica");
    }

    [Fact]
    public void Catalog_HasExpectedEntries()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);

        doc.Catalog.Should().NotBeNull();
        doc.Catalog.GetName("Type").Should().Be("Catalog");
        doc.Catalog.ContainsKey("Pages").Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_UnencryptedPdf_ReturnsFalse()
    {
        var pdfData = CreateMinimalPdf();

        using var doc = PdfDocument.Open(pdfData);

        doc.IsEncrypted.Should().BeFalse();
    }
}
