using AwesomeAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Parsing;
using Pdfe.Core.Primitives;
using System.Text;
using Xunit;

namespace Pdfe.Core.Tests.Document;

/// <summary>
/// Unit tests targeting uncovered code paths in PdfDocument.cs.
/// Focus: encryption error handling, object stream edge cases, metadata access, XMP handling.
/// </summary>
public class PdfDocumentCoverageTests
{
    /// <summary>
    /// Helper: Create a minimal PDF with custom trailer entries.
    /// </summary>
    private static byte[] CreatePdfWithCustomTrailer(string trailerExtra = "")
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[4];

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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 4");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 3; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Root 1 0 R /Size 4 {trailerExtra} >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF where /Encrypt is not a dictionary.
    /// Line 207-208: encryptObj is not PdfDictionary
    /// </summary>
    private static byte[] CreatePdfWithInvalidEncryptDict()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: An integer (not a dict) to be referenced as /Encrypt
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("42");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 /Encrypt 4 0 R /ID [(TEST)] >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF with /Encrypt but missing or empty /ID array.
    /// Line 211-213: /ID array missing or empty
    /// </summary>
    private static byte[] CreatePdfWithMissingIdArray()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: Minimal /Encrypt dict
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("<< /Filter /Standard /V 1 /R 2 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        // Trailer with /Encrypt but empty /ID array
        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 /Encrypt 4 0 R /ID [] >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF with /Encrypt pointing to unsupported encryption version.
    /// Line 220-230: PdfEncryptionNotSupportedException caught with allowEncrypted=true
    /// </summary>
    private static byte[] CreatePdfWithUnsupportedEncryption()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: /Encrypt with unsupported V (99 is not a real version)
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("<< /Filter /Standard /V 99 /R 99 /O (owner) /U (user) /P -1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 /Encrypt 4 0 R /ID [(TEST_ID)] >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF with an object stream containing compressed objects.
    /// </summary>
    private static byte[] CreatePdfWithObjectStream()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: A small object to go into the object stream
        // (We'll manually write the stream content below)
        // Object stream format: pairs of (object_number, byte_offset) followed by objects
        string objStreamContent = "5 0 6 (Hello)";

        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Type /ObjStm /N 1 /First 6 /Length {objStreamContent.Length} >>");
        writer.WriteLine("stream");
        writer.Write(objStreamContent);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF where an object in xref is marked as free (InUse=false).
    /// Line 435-436: if (!entry.InUse) return PdfNull.Instance;
    /// </summary>
    private static byte[] CreatePdfWithFreeObject()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4 is defined but not referenced; we'll mark it as free
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("<< /Name (UnusedObject) >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        writer.WriteLine($"{offsets[1]:D10} 00000 n ");
        writer.WriteLine($"{offsets[2]:D10} 00000 n ");
        writer.WriteLine($"{offsets[3]:D10} 00000 n ");
        // Mark object 4 as free (f instead of n)
        writer.WriteLine($"{offsets[4]:D10} 00000 f ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF with AcroForm (interactive form).
    /// </summary>
    private static byte[] CreatePdfWithAcroForm()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Object 4: Minimal AcroForm (interactive form dictionary)
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("<< /SigFlags 0 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    /// <summary>
    /// Helper: Create a PDF with XMP metadata stream.
    /// </summary>
    private static byte[] CreatePdfWithXmpMetadata()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        string xmpContent = "<?xml version=\"1.0\"?><xmpmeta></xmpmeta>";

        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Type /Metadata /Subtype /XML /Length {xmpContent.Length} >>");
        writer.WriteLine("stream");
        writer.Write(xmpContent);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    // ============================================================================
    // TEST CASES
    // ============================================================================

    /// <summary>
    /// Test line 207-208: /Encrypt is not a dictionary.
    /// Expect PdfParseException with message "/Encrypt is not a dictionary".
    /// </summary>
    [Fact]
    public void Open_EncryptNotDictionary_ThrowsPdfParseException()
    {
        var pdfData = CreatePdfWithInvalidEncryptDict();

        var act = () => PdfDocument.Open(new MemoryStream(pdfData), ownsStream: false, allowEncrypted: false);

        act.Should().Throw<PdfParseException>()
            .WithMessage("*is not a dictionary*");
    }

    /// <summary>
    /// Test line 211-213: /ID array is empty.
    /// Expect PdfParseException with message about /ID array.
    /// </summary>
    [Fact]
    public void Open_IdArrayEmpty_ThrowsPdfParseException()
    {
        var pdfData = CreatePdfWithMissingIdArray();

        var act = () => PdfDocument.Open(new MemoryStream(pdfData), ownsStream: false, allowEncrypted: false);

        act.Should().Throw<PdfParseException>()
            .WithMessage("*ID array*");
    }

    /// <summary>
    /// Test line 220-230: PdfEncryptionNotSupportedException caught with allowEncrypted=true.
    /// Should open successfully even with unsupported encryption if allowEncrypted=true.
    /// </summary>
    [Fact]
    public void Open_UnsupportedEncryptionWithAllowEncrypted_OpensSuccessfully()
    {
        var pdfData = CreatePdfWithUnsupportedEncryption();

        // With allowEncrypted=false, should throw
        var act1 = () => PdfDocument.Open(new MemoryStream(pdfData), ownsStream: false, allowEncrypted: false);
        act1.Should().Throw<PdfEncryptionNotSupportedException>();

        // With allowEncrypted=true, should open (handler will be null but document opens)
        using var doc = PdfDocument.Open(new MemoryStream(pdfData), ownsStream: false, allowEncrypted: true);
        doc.Should().NotBeNull();
        doc.IsEncrypted.Should().BeTrue();
        doc.PageCount.Should().Be(1);
    }

    /// <summary>
    /// Test line 435-436: Object in xref marked as free (InUse=false).
    /// GetObject should return PdfNull.Instance for free objects.
    /// </summary>
    [Fact]
    public void GetObject_FreeObject_ReturnsPdfNull()
    {
        var pdfData = CreatePdfWithFreeObject();

        using var doc = PdfDocument.Open(new MemoryStream(pdfData));

        // Try to retrieve object 4, which is marked as free in xref
        var result = doc.GetObject(4);

        result.Should().Be(PdfNull.Instance);
    }

    /// <summary>
    /// Test line 605-606: GetAcroForm when Catalog has no AcroForm entry.
    /// Should return null gracefully.
    /// </summary>
    [Fact]
    public void GetAcroForm_NoAcroFormInCatalog_ReturnsNull()
    {
        var pdfData = CreatePdfWithCustomTrailer();

        using var doc = PdfDocument.Open(new MemoryStream(pdfData));
        var acroForm = doc.GetAcroForm();

        acroForm.Should().BeNull();
    }

    /// <summary>
    /// Test line 605-608: GetAcroForm when AcroForm exists but is not a dict.
    /// Should return null when AcroForm resolves to non-dict.
    /// </summary>
    [Fact]
    public void GetAcroForm_AcroFormNotDictionary_ReturnsNull()
    {
        // Create PDF where /AcroForm points to an integer instead of dict
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[4];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /AcroForm 42 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 4");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 3; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 4 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        var pdfData = ms.ToArray();
        using var doc = PdfDocument.Open(new MemoryStream(pdfData));
        var acroForm = doc.GetAcroForm();

        // Should return null since /AcroForm is not a dict
        acroForm.Should().BeNull();
    }

    /// <summary>
    /// Test line 616-619: GetXmpMetadata when Catalog has no Metadata entry.
    /// Should return null gracefully.
    /// </summary>
    [Fact]
    public void GetXmpMetadata_NoMetadata_ReturnsNull()
    {
        var pdfData = CreatePdfWithCustomTrailer();

        using var doc = PdfDocument.Open(new MemoryStream(pdfData));
        var xmp = doc.GetXmpMetadata();

        xmp.Should().BeNull();
    }

    /// <summary>
    /// Test line 620: GetXmpMetadata when Metadata is not a stream.
    /// Should return null gracefully.
    /// </summary>
    [Fact]
    public void GetXmpMetadata_MetadataNotStream_ReturnsNull()
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[4];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /Metadata 42 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 4");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 3; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 4 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        var pdfData = ms.ToArray();
        using var doc = PdfDocument.Open(new MemoryStream(pdfData));
        var xmp = doc.GetXmpMetadata();

        // Should return null since /Metadata is not a stream
        xmp.Should().BeNull();
    }

    /// <summary>
    /// Test line 622 (catch block): GetXmpMetadata when stream decompression fails.
    /// Should return null (exception caught and handled).
    /// </summary>
    [Fact]
    public void GetXmpMetadata_DecompressionError_ReturnsNull()
    {
        // Create a PDF with a Metadata stream that has a /FlateDecode filter but invalid compressed data
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[5];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Invalid compressed data for FlateDecode
        string invalidCompressed = "\\xFF\\xFF\\xFF";
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Type /Metadata /Subtype /XML /Filter /FlateDecode /Length {invalidCompressed.Length} >>");
        writer.WriteLine("stream");
        writer.Write(invalidCompressed);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        var pdfData = ms.ToArray();

        using var doc = PdfDocument.Open(new MemoryStream(pdfData));
        var xmp = doc.GetXmpMetadata();

        // Should return null when decompression fails (caught and handled)
        xmp.Should().BeNull();
    }

    /// <summary>
    /// Test line 483-486: Exception during stream decompression in GetObject.
    /// Should catch and continue with encoded data.
    /// </summary>
    [Fact]
    public void GetObject_StreamDecompressionError_ContinuesWithEncodedData()
    {
        // Create a PDF with a content stream that has invalid filter data
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
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
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Invalid FlateDecode stream
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Stream with invalid compressed data
        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Filter /FlateDecode /Length 10 >>");
        writer.WriteLine("stream");
        writer.Write("\\xFF\\xFF\\xFF\\xFF");
        writer.WriteLine();
        writer.WriteLine("endstream");
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

        var pdfData = ms.ToArray();

        using var doc = PdfDocument.Open(new MemoryStream(pdfData));

        // Getting the page should work even if the content stream has bad compression
        // because the exception is caught and the object is returned as-is
        var page = doc.GetPage(1);
        page.Should().NotBeNull();
    }

    /// <summary>
    /// Test line 545-549: ResolveLengthReference returns null on exception.
    /// This is an internal method but we test it indirectly by parsing a stream
    /// with an indirect /Length reference that is invalid/missing.
    /// </summary>
    [Fact]
    public void Open_IndirectLengthReferenceToMissingObject_StillParsesStream()
    {
        // Create a PDF where /Length is an indirect reference to a non-existent object
        // The parser should handle this gracefully via ResolveLengthReference returning null
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[6];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R /Pages 3 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [4 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Stream with /Length as indirect reference (999 0 R doesn't exist)
        string streamContent = "BT /F1 12 Tf (Hello) Tj ET";
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine("<< /Length 999 0 R >>");
        writer.WriteLine("stream");
        writer.Write(streamContent);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;

        writer.WriteLine("xref");
        writer.WriteLine("0 5");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 4; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 5 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        var pdfData = ms.ToArray();

        // Should open without throwing even though indirect /Length reference is missing
        using var doc = PdfDocument.Open(new MemoryStream(pdfData));
        doc.Should().NotBeNull();
    }

    /// <summary>
    /// Test line 224: ownsStream disposal when catching encryption exception.
    /// When allowEncrypted=false and encryption throws PdfEncryptionNotSupportedException,
    /// the stream should be disposed if ownsStream=true.
    /// </summary>
    [Fact]
    public void Open_UnsupportedEncryptionWithOwnsStreamFalse_ThrowsWithoutDisposing()
    {
        var pdfData = CreatePdfWithUnsupportedEncryption();
        var stream = new MemoryStream(pdfData);

        // With ownsStream=false, exception should throw but stream stays open
        var act = () => PdfDocument.Open(stream, ownsStream: false, allowEncrypted: false);

        act.Should().Throw<PdfEncryptionNotSupportedException>();

        // Stream should still be readable (not disposed)
        stream.CanRead.Should().BeTrue();
    }
}
