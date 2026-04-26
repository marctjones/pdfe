using System.Text;
using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;
using Xunit;

namespace Pdfe.Core.Tests.Document;

/// <summary>
/// Tests for PDF interactive form (AcroForm) parsing (§12.7).
/// Synthetic in-memory PDFs are used so no fixture files are required.
/// </summary>
public class PdfAcroFormTests
{
    // ─── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a minimal single-page PDF with an AcroForm containing the given fields.
    /// </summary>
    private static byte[] MakePdfWithAcroForm(string acroFormDef)
    {
        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.7");

        long obj1Pos, obj2Pos, obj3Pos, obj4Pos, obj5Pos;

        // obj 1 — catalog with AcroForm
        obj1Pos = sb.Length;
        sb.AppendLine("1 0 obj");
        sb.AppendLine($"<< /Type /Catalog /Pages 2 0 R {acroFormDef} >>");
        sb.AppendLine("endobj");

        // obj 2 — pages node
        obj2Pos = sb.Length;
        sb.AppendLine("2 0 obj");
        sb.AppendLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        sb.AppendLine("endobj");

        // obj 3 — page
        obj3Pos = sb.Length;
        sb.AppendLine("3 0 obj");
        sb.AppendLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        sb.AppendLine("endobj");

        // obj 4 — content stream (empty)
        obj4Pos = sb.Length;
        sb.AppendLine("4 0 obj");
        sb.AppendLine("<< /Length 0 >>");
        sb.AppendLine("stream");
        sb.AppendLine("endstream");
        sb.AppendLine("endobj");

        // obj 5 — placeholder for field/widget references
        obj5Pos = sb.Length;
        sb.AppendLine("5 0 obj");
        sb.AppendLine("<< >>");
        sb.AppendLine("endobj");

        long xrefPos = sb.Length;
        sb.AppendLine("xref");
        sb.AppendLine("0 6");
        sb.AppendLine("0000000000 65535 f ");
        sb.AppendLine($"{obj1Pos:D10} 00000 n ");
        sb.AppendLine($"{obj2Pos:D10} 00000 n ");
        sb.AppendLine($"{obj3Pos:D10} 00000 n ");
        sb.AppendLine($"{obj4Pos:D10} 00000 n ");
        sb.AppendLine($"{obj5Pos:D10} 00000 n ");
        sb.AppendLine("trailer");
        sb.AppendLine("<< /Size 6 /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine(xrefPos.ToString());
        sb.AppendLine("%%EOF");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Build a more complex PDF with multiple field types and widget annotations.
    /// </summary>
    private static byte[] MakePdfWithComplexAcroForm()
    {
        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.7");

        long catalogPos = sb.Length;
        sb.AppendLine("1 0 obj");
        sb.AppendLine(@"<<
            /Type /Catalog
            /Pages 2 0 R
            /AcroForm <<
                /Fields [5 0 R 6 0 R]
                /NeedsAppearances false
            >>
        >>");
        sb.AppendLine("endobj");

        long pagesPos = sb.Length;
        sb.AppendLine("2 0 obj");
        sb.AppendLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        sb.AppendLine("endobj");

        long pagePos = sb.Length;
        sb.AppendLine("3 0 obj");
        sb.AppendLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>");
        sb.AppendLine("endobj");

        long contentPos = sb.Length;
        sb.AppendLine("4 0 obj");
        sb.AppendLine("<< /Length 0 >>");
        sb.AppendLine("stream");
        sb.AppendLine("endstream");
        sb.AppendLine("endobj");

        // Obj 5 — Text field (Tx) with widget
        long textFieldPos = sb.Length;
        sb.AppendLine("5 0 obj");
        sb.AppendLine(@"<<
            /Type /Annot
            /Subtype /Widget
            /FT /Tx
            /T (Name)
            /V (Alice)
            /DV (DefaultName)
            /Ff 0
            /Rect [72 700 300 720]
            /P 3 0 R
        >>");
        sb.AppendLine("endobj");

        // Obj 6 — Button field (Btn) with widget
        long buttonFieldPos = sb.Length;
        sb.AppendLine("6 0 obj");
        sb.AppendLine(@"<<
            /Type /Annot
            /Subtype /Widget
            /FT /Btn
            /T (Accept)
            /V /Yes
            /Ff 0
            /Rect [72 680 100 700]
            /P 3 0 R
        >>");
        sb.AppendLine("endobj");

        long xrefPos = sb.Length;
        sb.AppendLine("xref");
        sb.AppendLine("0 7");
        sb.AppendLine("0000000000 65535 f ");
        sb.AppendLine($"{catalogPos:D10} 00000 n ");
        sb.AppendLine($"{pagesPos:D10} 00000 n ");
        sb.AppendLine($"{pagePos:D10} 00000 n ");
        sb.AppendLine($"{contentPos:D10} 00000 n ");
        sb.AppendLine($"{textFieldPos:D10} 00000 n ");
        sb.AppendLine($"{buttonFieldPos:D10} 00000 n ");
        sb.AppendLine("trailer");
        sb.AppendLine("<< /Size 7 /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine(xrefPos.ToString());
        sb.AppendLine("%%EOF");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    // ─── basic parsing ─────────────────────────────────────────────────────

    [Fact]
    public void GetAcroForm_NullDocument_ReturnsNull()
    {
        // PDF without /AcroForm in catalog
        byte[] pdf = MakePdfWithAcroForm("");
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        doc.GetAcroForm().Should().BeNull();
    }

    [Fact]
    public void GetAcroForm_WithTextAndButtonFields_ParsesBoth()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();
        form!.Fields.Should().HaveCount(2);

        // Check types
        var types = form.Fields.Select(f => f.FieldType).ToList();
        types.Should().Contain(PdfFieldType.Text);
        types.Should().Contain(PdfFieldType.Button);
    }

    [Fact]
    public void GetAcroForm_TextFieldValue_ParsedCorrectly()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();

        var nameField = form!.Fields.FirstOrDefault(f => f.PartialName == "Name");
        nameField.Should().NotBeNull();
        nameField!.Value.Should().Be("Alice");
        nameField.DefaultValue.Should().Be("DefaultName");
    }

    [Fact]
    public void GetAcroForm_WidgetRect_ParsedCorrectly()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();

        var nameField = form!.Fields.FirstOrDefault(f => f.PartialName == "Name");
        nameField.Should().NotBeNull();
        nameField!.Rect.Should().NotBeNull();
        nameField.Rect!.Value.Left.Should().Be(72);
        nameField.Rect!.Value.Bottom.Should().Be(700);
        nameField.Rect!.Value.Right.Should().Be(300);
        nameField.Rect!.Value.Top.Should().Be(720);
    }

    [Fact]
    public void GetAcroForm_NeedsAppearances_ParsedCorrectly()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();
        form!.NeedsAppearances.Should().BeFalse();
    }

    [Fact]
    public void GetAcroForm_ButtonFieldValue_ParsedAsName()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        var acceptField = form!.Fields.FirstOrDefault(f => f.PartialName == "Accept");
        acceptField.Should().NotBeNull();
        acceptField!.Value.Should().Be("Yes");
    }

    [Fact]
    public void GetAcroForm_MultipleFields_AllParsed()
    {
        // Test that multiple fields at the top level are all parsed
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();
        form!.Fields.Should().HaveCount(2);

        // Verify we can find both fields
        var textField = form.FindField("Name");
        var buttonField = form.FindField("Accept");

        textField.Should().NotBeNull();
        buttonField.Should().NotBeNull();
        textField!.FieldType.Should().Be(PdfFieldType.Text);
        buttonField!.FieldType.Should().Be(PdfFieldType.Button);
    }

    [Fact]
    public void GetAcroForm_GetFields_FiltersByType()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();

        form!.GetTextFields().Should().HaveCount(1);
        form.GetButtonFields().Should().HaveCount(1);
        form.GetChoiceFields().Should().HaveCount(0);
        form.GetSignatureFields().Should().HaveCount(0);
    }

    [Fact]
    public void GetAcroForm_FindField_ByFullName()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        form.Should().NotBeNull();

        var field = form!.FindField("Name");
        field.Should().NotBeNull();
        field!.Value.Should().Be("Alice");
    }

    [Fact]
    public void GetAcroForm_FindField_ReturnsNull_WhenNotFound()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        var field = form!.FindField("NonExistent");
        field.Should().BeNull();
    }

    [Fact]
    public void GetAcroForm_ToString_FormatsCorrectly()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        var nameField = form!.Fields.First(f => f.PartialName == "Name");

        var str = nameField.ToString();
        str.Should().Contain("Text");
        str.Should().Contain("Name");
        str.Should().Contain("Alice");
    }

    [Fact]
    public void GetAcroForm_RawDictionary_Accessible()
    {
        byte[] pdf = MakePdfWithComplexAcroForm();
        using var doc = PdfDocument.Open(new MemoryStream(pdf), ownsStream: false);

        var form = doc.GetAcroForm();
        var nameField = form!.Fields.First(f => f.PartialName == "Name");

        nameField.RawDictionary.Should().NotBeNull();
        nameField.RawDictionary.GetStringOrNull("T").Should().Be("Name");
    }
}
