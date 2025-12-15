using System.IO;
using System.Text;
using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Regression tests to ensure redaction output stays visible when consumers only
/// read the first content stream on a page.
/// </summary>
public class ContentStreamConsolidationTests
{
    [Fact]
    public void MultipleRedactions_StayInPrimaryContentStream()
    {
        var service = new RedactionService(NullLogger<RedactionService>.Instance, NullLoggerFactory.Instance);
        var tempPath = Path.Combine(Path.GetTempPath(), "consolidation_test.pdf");

        // Create a base PDF with some content to ensure multiple streams exist.
        using (var doc = new PdfDocument())
        {
            var page = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawString("Base text", new XFont("Arial", 12), XBrushes.Black, 20, 40);
            }

            // Apply two redactions; overlays must end up in the primary stream.
            service.RedactArea(page, new Rect(10, 10, 40, 20), renderDpi: 72);
            service.RedactArea(page, new Rect(70, 10, 40, 20), renderDpi: 72);

            doc.Save(tempPath);
        }

        try
        {
            using var reopened = PdfReader.Open(tempPath, PdfDocumentOpenMode.Import);
            var page = reopened.Pages[0];

            page.Contents.Elements.Count.Should().Be(1, "redaction should consolidate content streams");

            var content = Encoding.ASCII.GetString(GetFirstContentStreamBytes(page));
            var expectedY = page.Height.Point - 10 - 20; // XGraphics uses top-left, PDF stream uses bottom-left

            content.Should().Contain($"10 {expectedY:0} 40 20 re", "first redaction rectangle should be in the primary stream");
            content.Should().Contain($"70 {expectedY:0} 40 20 re", "second redaction rectangle should be in the primary stream");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static byte[] GetFirstContentStreamBytes(PdfPage page)
    {
        if (page.Contents.Elements.Count == 0)
            return [];

        var dict = page.Contents.Elements.GetDictionary(0);
        if (dict == null && page.Contents.Elements[0] is PdfReference pdfRef)
            dict = pdfRef.Value as PdfDictionary;

        return dict?.Stream?.Value ?? [];
    }
}
