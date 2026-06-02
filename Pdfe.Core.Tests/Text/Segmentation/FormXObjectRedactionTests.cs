using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Text.Segmentation;
using Xunit;

namespace Pdfe.Core.Tests.Text.Segmentation;

/// <summary>
/// #355: redaction must reach content drawn by Form XObjects. The text/image
/// passes only see the page content stream, so a form painting over the
/// redaction area would be merely covered. RedactArea flattens overlapping
/// forms into the page (so the existing passes can remove their content) and
/// then frees the now-orphaned form objects — without that prune the writer,
/// which serializes every in-use object, would re-emit the form and leak the
/// very text the redaction removed.
/// </summary>
public class FormXObjectRedactionTests
{
    [Fact]
    public void RedactArea_OverFormText_RemovesItFromSavedPdfBytes()
    {
        var pdf = Build(
            Obj("<< /Type /Catalog /Pages 2 0 R >>"),
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                "/Resources << /XObject << /Fm0 6 0 R >> >> >>"),
            Stream("", "q /Fm0 Do Q"),
            Obj(HelveticaFont),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                   "/Resources << /Font << /F1 5 0 R >> >>",
                   "BT /F1 12 Tf 100 700 Td (SECRETFORM) Tj ET"));

        Encoding.Latin1.GetString(pdf).Should().Contain("SECRETFORM");

        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).RedactArea(new PdfRectangle(90, 695, 250, 716));
        var saved = doc.SaveToBytes();

        Encoding.Latin1.GetString(saved).Should().NotContain("SECRETFORM",
            "the form's redacted text must be absent from the saved bytes, not merely covered");

        using var re = PdfDocument.Open(saved);
        string.Concat(re.GetPage(1).Letters.Select(l => l.Value))
            .Should().NotContain("SECRETFORM");
    }

    [Fact]
    public void RedactArea_FormWithTwoStrings_RemovesInsideKeepsOutside()
    {
        // Proves flattening reproduces the form faithfully: only the string in
        // the redaction band is removed; the other survives, correctly placed.
        var pdf = Build(
            Obj("<< /Type /Catalog /Pages 2 0 R >>"),
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                "/Resources << /XObject << /Fm0 6 0 R >> >> >>"),
            Stream("", "q /Fm0 Do Q"),
            Obj(HelveticaFont),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                   "/Resources << /Font << /F1 5 0 R >> >>",
                   "BT /F1 12 Tf 100 700 Td (INSIDEBAND) Tj ET " +
                   "BT /F1 12 Tf 100 600 Td (OUTSIDEBAND) Tj ET"));

        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).RedactArea(new PdfRectangle(90, 695, 300, 716));
        var saved = Encoding.Latin1.GetString(doc.SaveToBytes());

        saved.Should().NotContain("INSIDEBAND", "the string in the redaction band must be removed");
        saved.Should().Contain("OUTSIDEBAND", "the string outside the band must survive flattening");
    }

    [Fact]
    public void RedactArea_NestedForm_RemovesInnermostText()
    {
        // Page → Fm0 (outer form) → Fm1 (inner form, draws the secret).
        var pdf = Build(
            Obj("<< /Type /Catalog /Pages 2 0 R >>"),
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                "/Resources << /XObject << /Fm0 6 0 R >> >> >>"),
            Stream("", "q /Fm0 Do Q"),
            Obj(HelveticaFont),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                   "/Resources << /XObject << /Fm1 7 0 R >> >>",
                   "q /Fm1 Do Q"),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                   "/Resources << /Font << /F1 5 0 R >> >>",
                   "BT /F1 12 Tf 100 700 Td (NESTEDSECRET) Tj ET"));

        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).RedactArea(new PdfRectangle(90, 695, 280, 716));
        var saved = Encoding.Latin1.GetString(doc.SaveToBytes());

        saved.Should().NotContain("NESTEDSECRET",
            "text drawn by a nested form must be removed and both orphaned forms pruned");
    }

    [Fact]
    public void RedactArea_FormSharedByAnotherPage_LeavesOtherPageIntact()
    {
        // Both pages invoke the same form. Redacting page 1 must not disturb
        // page 2's rendering — the shared form object stays (still reachable).
        var pdf = Build(
            Obj("<< /Type /Catalog /Pages 2 0 R >>"),
            Obj("<< /Type /Pages /Kids [3 0 R 7 0 R] /Count 2 >>"),
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                "/Resources << /XObject << /Fm0 6 0 R >> >> >>"),
            Stream("", "q /Fm0 Do Q"),
            Obj(HelveticaFont),
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                   "/Resources << /Font << /F1 5 0 R >> >>",
                   "BT /F1 12 Tf 100 700 Td (SHAREDFORM) Tj ET"),
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 8 0 R " +
                "/Resources << /XObject << /Fm0 6 0 R >> >> >>"),
            Stream("", "q /Fm0 Do Q"));

        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).RedactArea(new PdfRectangle(90, 695, 250, 716));
        var saved = doc.SaveToBytes();

        // The shared form is still reachable from page 2, so it must NOT be
        // pruned — its content legitimately remains for the untouched page.
        // (The text extractor doesn't recurse into forms, so page 2's Letters
        // are empty either way; the durable contract is "object preserved".)
        Encoding.Latin1.GetString(saved).Should().Contain("SHAREDFORM",
            "a form still referenced by another page must survive the prune");

        using var re = PdfDocument.Open(saved);
        string.Concat(re.GetPage(1).Letters.Select(l => l.Value))
            .Should().NotContain("SHAREDFORM", "page 1's inlined copy is redacted");
        re.GetPage(2).GetXObject("Fm0").Should().NotBeNull(
            "page 2 still resolves the shared form");
    }

    [Fact]
    public void RedactArea_FormResourceNameCollision_RenamesAndKeepsBothFonts()
    {
        // Page /F1 and the form's /F1 are DIFFERENT font objects. Flattening
        // must rename the form's font so both resolve; the surviving form text
        // and the page text must both extract correctly after redaction.
        var pdf = Build(
            Obj("<< /Type /Catalog /Pages 2 0 R >>"),
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                "/Resources << /Font << /F1 5 0 R >> /XObject << /Fm0 7 0 R >> >> >>"),
            Stream("", "BT /F1 12 Tf 100 500 Td (PAGEOWNTEXT) Tj ET q /Fm0 Do Q"),
            Obj(HelveticaFont),  // page's /F1
            Obj(HelveticaFont),  // form's /F1 — a distinct object (obj 6)
            Stream("/Type /XObject /Subtype /Form /BBox [0 0 612 792] " +
                   "/Resources << /Font << /F1 6 0 R >> >>",
                   "BT /F1 12 Tf 100 700 Td (FORMGONE) Tj ET " +
                   "BT /F1 12 Tf 100 600 Td (FORMKEEP) Tj ET"));

        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).RedactArea(new PdfRectangle(90, 695, 250, 716));

        using var re = PdfDocument.Open(doc.SaveToBytes());
        var text = string.Concat(re.GetPage(1).Letters.Select(l => l.Value));
        text.Should().NotContain("FORMGONE", "the redacted form string is removed");
        text.Should().Contain("FORMKEEP", "the form string outside the band survives with a resolvable font");
        text.Should().Contain("PAGEOWNTEXT", "the page's own text is untouched");
    }

    // ---- builders ----

    private const string HelveticaFont =
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    private static byte[] Obj(string dict) => Encoding.Latin1.GetBytes(dict);

    private static byte[] Stream(string dictExtra, string content)
    {
        var data = Encoding.Latin1.GetBytes(content);
        var head = Encoding.Latin1.GetBytes($"<< {dictExtra} /Length {data.Length} >>\nstream\n");
        var tail = Encoding.Latin1.GetBytes("\nendstream");
        return head.Concat(data).Concat(tail).ToArray();
    }

    /// <summary>Assemble object bodies (object 1..N) into a valid PDF file.</summary>
    private static byte[] Build(params byte[][] objects)
    {
        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }

        W("%PDF-1.4\n");
        var offsets = new long[objects.Length + 1];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = ms.Position;
            W($"{i + 1} 0 obj\n");
            ms.Write(objects[i], 0, objects[i].Length);
            W("\nendobj\n");
        }

        long xref = ms.Position;
        W($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= objects.Length; i++)
            W($"{offsets[i]:D10} 00000 n \n");
        W($"trailer\n<< /Root 1 0 R /Size {objects.Length + 1} >>\nstartxref\n{xref}\n%%EOF");

        return ms.ToArray();
    }
}
