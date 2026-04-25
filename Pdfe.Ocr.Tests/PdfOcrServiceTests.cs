using System.Linq;
using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Graphics;
using Pdfe.Ocr;
using Xunit;

namespace Pdfe.Ocr.Tests;

/// <summary>
/// End-to-end OCR tests. Skip automatically when the <c>tesseract</c>
/// CLI isn't on PATH (local-dev fallback — CI should install it).
/// </summary>
public class PdfOcrServiceTests
{
    // Use the repo's committed tessdata if the test environment doesn't
    // have TESSDATA_PREFIX set externally.
    private static readonly string TessdataPrefix =
        Environment.GetEnvironmentVariable("TESSDATA_PREFIX")
        ?? ResolveRepoTessdata();

    private static string ResolveRepoTessdata()
    {
        // Walk up from the test bin/ directory until we find tessdata/.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var probe = System.IO.Path.Combine(dir.FullName, "tessdata", "eng.traineddata");
            if (System.IO.File.Exists(probe))
                return System.IO.Path.Combine(dir.FullName, "tessdata");
            dir = dir.Parent;
        }
        return string.Empty;
    }

    private static bool TesseractAvailable => new PdfOcrService().IsAvailable();

    private static PdfOcrService NewService(int dpi = 300) =>
        new(dpi: dpi, tessdataPrefix: TessdataPrefix);

    [SkippableFact]
    public void RecognizePage_ReadsTextFromRenderedPage()
    {
        Skip.IfNot(TesseractAvailable, "tesseract CLI not installed");

        using var doc = PdfDocument.CreateNew();
        var page = doc.Pages.AddBlank(612, 792);
        using (var g = page.GetGraphics())
        {
            // Big 32pt text — keeps the OCR pass reliable at 300 DPI
            // without needing fine fidelity tuning.
            g.DrawString("HELLO PDFE WORLD", PdfFont.Helvetica(32),
                PdfBrush.Black, 100, 600);
            g.Flush();
        }

        var ocr = NewService(dpi: 300);
        var result = ocr.RecognizePage(page);

        result.Text.Should().Contain("HELLO");
        result.Text.Should().Contain("PDFE");
        result.Text.Should().Contain("WORLD");
        result.Words.Should().NotBeEmpty();
    }

    [SkippableFact]
    public void RecognizePage_BlankPage_ReturnsEmptyText()
    {
        Skip.IfNot(TesseractAvailable, "tesseract CLI not installed");

        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank(612, 792);

        var ocr = NewService(dpi: 200);
        var result = ocr.RecognizePage(doc.GetPage(1));

        result.Text.Trim().Should().BeEmpty();
        result.Words.Should().BeEmpty();
    }

    [SkippableFact]
    public void OcrWord_BoundingBox_IsInPdfPointsBottomLeft()
    {
        Skip.IfNot(TesseractAvailable, "tesseract CLI not installed");

        using var doc = PdfDocument.CreateNew();
        var page = doc.Pages.AddBlank(612, 792);
        using (var g = page.GetGraphics())
        {
            g.DrawString("ANCHOR", PdfFont.Helvetica(40),
                PdfBrush.Black, 200, 400);
            g.Flush();
        }

        var ocr = NewService(dpi: 300);
        var result = ocr.RecognizePage(page);

        var anchor = result.Words.FirstOrDefault(w => w.Text.Contains("ANCHOR"));
        anchor.Should().NotBeNull();
        // Drawn near x=200, baseline y=400 (PDF bottom-left). Word box
        // should sit in that general region — 60-point slack per axis.
        anchor!.BoundingBox.Left.Should().BeInRange(140, 260);
        anchor.BoundingBox.Bottom.Should().BeInRange(360, 440);
    }
}
