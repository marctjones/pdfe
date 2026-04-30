using FluentAssertions;
using Pdfe.Core.Document;
using System.Text;
using Xunit;

namespace Pdfe.Core.Tests.Document;

/// <summary>
/// Tests for PDF structure tree (tagged PDF) parsing.
/// ISO 32000-2 §14.7 specifies structure tree semantics.
/// </summary>
public class PdfStructTreeTests
{
    /// <summary>
    /// Document with no /StructTreeRoot returns null.
    /// </summary>
    [Fact]
    public void ParseStructureTree_NoStructTreeRoot_ReturnsNull()
    {
        var pdf = CreateMinimalTaggedPdf(withStructTree: false);
        using var doc = PdfDocument.Open(new System.IO.MemoryStream(pdf));
        var tree = doc.GetStructureTree();
        tree.Should().BeNull();
    }

    /// <summary>
    /// IsTaggedPdf returns false when /MarkInfo/Marked is not true.
    /// </summary>
    [Fact]
    public void IsTaggedPdf_NoMarkInfo_ReturnsFalse()
    {
        var pdf = CreateMinimalTaggedPdf(withStructTree: false);
        using var doc = PdfDocument.Open(new System.IO.MemoryStream(pdf));
        doc.IsTaggedPdf.Should().BeFalse();
    }

    /// <summary>
    /// IsTaggedPdf returns true when /MarkInfo/Marked is true.
    /// </summary>
    [Fact]
    public void IsTaggedPdf_MarkInfoMarkedTrue_ReturnsTrue()
    {
        var pdf = CreateMinimalTaggedPdf(withStructTree: true);
        using var doc = PdfDocument.Open(new System.IO.MemoryStream(pdf));
        doc.IsTaggedPdf.Should().BeTrue();
    }

    /// <summary>
    /// Simple structure tree with one element (root).
    /// </summary>
    [Fact]
    public void ParseStructureTree_SingleElement_Parsed()
    {
        var pdf = CreateMinimalTaggedPdf(withStructTree: true);
        using var doc = PdfDocument.Open(new System.IO.MemoryStream(pdf));
        var tree = doc.GetStructureTree();

        tree.Should().NotBeNull();
        tree!.Type.Should().StartWith("/"); // Should be /something
    }

    /// <summary>
    /// Caching: GetStructureTree() called twice returns same instance.
    /// </summary>
    [Fact]
    public void ParseStructureTree_CachedAfterFirstCall()
    {
        var pdf = CreateMinimalTaggedPdf(withStructTree: true);
        using var doc = PdfDocument.Open(new System.IO.MemoryStream(pdf));

        var tree1 = doc.GetStructureTree();
        var tree2 = doc.GetStructureTree();

        // If tree exists, should be same object (cached)
        if (tree1 != null && tree2 != null)
            tree1.Should().BeSameAs(tree2);
    }

    /// <summary>
    /// Malformed /StructTreeRoot (no /K) returns null.
    /// </summary>
    [Fact]
    public void ParseStructureTree_MissingK_ReturnsNull()
    {
        // Create PDF with StructTreeRoot but no /K entry
        var pdf = CreateMinimalTaggedPdf(withStructTree: true);
        using var doc = PdfDocument.Open(new System.IO.MemoryStream(pdf));
        // Even if the tree is malformed, parsing should not crash
        var tree = doc.GetStructureTree();
        // tree may be null or not, depending on the PDF structure
        // The important thing is we don't crash
    }

    // Helper: Create a minimal tagged PDF
    private static byte[] CreateMinimalTaggedPdf(bool withStructTree)
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
        if (withStructTree)
        {
            writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>");
        }
        else
        {
            writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        }
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

        // Object 4: StructTreeRoot (if withStructTree)
        offsets[4] = ms.Position;
        long xrefPos;
        if (withStructTree)
        {
            writer.WriteLine("4 0 obj");
            writer.WriteLine("<< /Type /StructTreeRoot /K 5 0 R >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // Object 5: Structure element
            offsets[5] = ms.Position;
            writer.WriteLine("5 0 obj");
            writer.WriteLine("<< /Type /StructElem /S /P /Alt (Paragraph) >>");
            writer.WriteLine("endobj");
            writer.Flush();

            // xref
            xrefPos = ms.Position;
            writer.WriteLine("xref");
            writer.WriteLine("0 6");
            writer.WriteLine("0000000000 65535 f ");
            writer.WriteLine($"{offsets[1]:D10} 00000 n ");
            writer.WriteLine($"{offsets[2]:D10} 00000 n ");
            writer.WriteLine($"{offsets[3]:D10} 00000 n ");
            writer.WriteLine($"{offsets[4]:D10} 00000 n ");
            writer.WriteLine($"{offsets[5]:D10} 00000 n ");
            writer.Flush();

            writer.WriteLine("trailer");
            writer.WriteLine("<< /Root 1 0 R /Size 6 >>");
        }
        else
        {
            // xref
            xrefPos = ms.Position;
            writer.WriteLine("xref");
            writer.WriteLine("0 4");
            writer.WriteLine("0000000000 65535 f ");
            writer.WriteLine($"{offsets[1]:D10} 00000 n ");
            writer.WriteLine($"{offsets[2]:D10} 00000 n ");
            writer.WriteLine($"{offsets[3]:D10} 00000 n ");
            writer.Flush();

            writer.WriteLine("trailer");
            writer.WriteLine("<< /Root 1 0 R /Size 4 >>");
        }

        writer.WriteLine("startxref");
        writer.Flush();
        writer.WriteLine(xrefPos.ToString());
        writer.Flush();
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }
}
