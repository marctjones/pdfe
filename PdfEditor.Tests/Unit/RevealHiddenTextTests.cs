using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using PdfEditor.ViewModels;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Integration test for the "Reveal Hidden Text" audit feature:
/// given a PDF with text occluded by a black rectangle (the classic
/// bad-redaction pattern), verify the ViewModel surfaces it through
/// <see cref="MainWindowViewModel.HiddenTextHighlights"/> when the
/// <see cref="MainWindowViewModel.RevealHiddenText"/> toggle is on.
/// </summary>
public class RevealHiddenTextTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) try { File.Delete(f); } catch { }
    }

    [Fact]
    public void RevealToggle_FlushesHighlightsForHiddenText()
    {
        // Arrange: build a PDF with "SECRET INFO 12345" covered by a
        // black rectangle drawn on top (classic bad redaction).
        var path = Path.Combine(Path.GetTempPath(), $"reveal-test-{Guid.NewGuid():N}.pdf");
        _tempFiles.Add(path);
        File.WriteAllBytes(path, BuildBadRedactionPdf("SECRET INFO 12345"));

        var vm = new MainWindowViewModel();

        // Act: simulate the user's workflow — load the PDF, flip the toggle.
        vm.LoadDocumentCommand(path).GetAwaiter().GetResult();
        vm.CurrentPageIndex = 0;
        vm.RevealHiddenText.Should().BeFalse("default off");
        vm.HiddenTextHighlights.Should().BeEmpty("nothing populated until revealed");

        vm.RevealHiddenText = true;

        // Assert: the hidden text surfaces, with a non-zero on-screen
        // bounding box so the overlay can actually draw it.
        vm.HiddenTextHighlights.Should().HaveCount(1);
        var h = vm.HiddenTextHighlights[0];
        h.Text.Should().Be("SECRET INFO 12345");
        h.HiddenBy.Should().Contain("filled rectangle");
        h.ScreenBounds.Width.Should().BeGreaterThan(0);
        h.ScreenBounds.Height.Should().BeGreaterThan(0);

        // And: flipping off clears the overlay.
        vm.RevealHiddenText = false;
        vm.HiddenTextHighlights.Should().BeEmpty();
    }

    /// <summary>
    /// Minimal PDF with a line of text plus a black rectangle drawn
    /// on top covering the text bbox. Serves as a deterministic bad-
    /// redaction fixture for the reveal test.
    /// </summary>
    private static byte[] BuildBadRedactionPdf(string secretText)
    {
        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };

        w.WriteLine("%PDF-1.4");
        w.Flush();

        var offsets = new long[6];
        offsets[1] = ms.Position;
        w.WriteLine("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj");
        w.Flush();

        offsets[2] = ms.Position;
        w.WriteLine("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj");
        w.Flush();

        offsets[3] = ms.Position;
        w.WriteLine("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                    "/Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj");
        w.Flush();

        double textX = 100, textY = 700;
        double rectLeft = textX - 4;
        double rectBottom = textY - 4;
        double rectWidth = secretText.Length * 8;
        double rectHeight = 14;

        var body =
            $"BT /F1 14 Tf {textX} {textY} Td ({secretText}) Tj ET\n" +
            $"q 0 0 0 rg {rectLeft} {rectBottom} {rectWidth} {rectHeight} re f Q";

        offsets[4] = ms.Position;
        w.WriteLine($"4 0 obj\n<< /Length {body.Length} >>\nstream");
        w.Write(body);
        w.WriteLine();
        w.WriteLine("endstream\nendobj");
        w.Flush();

        offsets[5] = ms.Position;
        w.WriteLine("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica " +
                    "/Encoding /WinAnsiEncoding >>\nendobj");
        w.Flush();

        long xref = ms.Position;
        w.WriteLine("xref");
        w.WriteLine("0 6");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 5; i++)
            w.WriteLine($"{offsets[i]:D10} 00000 n ");
        w.Flush();
        w.WriteLine($"trailer\n<< /Root 1 0 R /Size 6 >>\nstartxref\n{xref}\n%%EOF");
        w.Flush();

        return ms.ToArray();
    }
}
