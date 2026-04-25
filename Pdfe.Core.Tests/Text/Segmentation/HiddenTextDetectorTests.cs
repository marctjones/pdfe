using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Text.Segmentation;
using Xunit;

namespace Pdfe.Core.Tests.Text.Segmentation;

/// <summary>
/// Tests for <see cref="HiddenTextDetector"/> — detection of text that
/// is present in the PDF content stream but visually occluded by a
/// later-drawn opaque object.
/// </summary>
public class HiddenTextDetectorTests
{
    [Fact]
    public void Scan_TextWithBlackRectOnTop_IsDetected()
    {
        // Classic "redaction by black box": draw text, then draw a
        // filled black rectangle covering the same area.
        var pdf = BuildPdfWithTextAndOverlay(
            "SECRET", textX: 100, textY: 700,
            overlayKind: OverlayKind.BlackFilledRectangle);

        using var doc = PdfDocument.Open(pdf);
        var hits = HiddenTextDetector.Scan(doc);

        hits.Should().HaveCount(1);
        hits[0].Text.Should().Be("SECRET");
        hits[0].PageNumber.Should().Be(1);
        hits[0].HiddenBy.Should().Contain("filled rectangle");
    }

    [Fact]
    public void Scan_TextWithNoOverlay_IsNotFlagged()
    {
        var pdf = BuildPdfWithTextAndOverlay(
            "VISIBLE", textX: 100, textY: 700,
            overlayKind: OverlayKind.None);

        using var doc = PdfDocument.Open(pdf);
        HiddenTextDetector.Scan(doc).Should().BeEmpty();
    }

    [Fact]
    public void Scan_OverlayDrawnBeforeText_IsNotFlagged()
    {
        // The overlay is drawn first, then the text on top. The text
        // is visible, not hidden. Stream order matters.
        var pdf = BuildPdfWithTextAndOverlay(
            "ON_TOP", textX: 100, textY: 700,
            overlayKind: OverlayKind.BlackFilledRectangle,
            overlayBeforeText: true);

        using var doc = PdfDocument.Open(pdf);
        HiddenTextDetector.Scan(doc).Should().BeEmpty(
            "an overlay drawn before the text is behind it — not an audit concern");
    }

    [Fact]
    public void Scan_WhiteOverlay_IsNotFlagged()
    {
        // White-on-white fills are decorative boilerplate (page
        // backgrounds, etc.) — not redaction-by-overlay.
        var pdf = BuildPdfWithTextAndOverlay(
            "VISIBLE", textX: 100, textY: 700,
            overlayKind: OverlayKind.WhiteFilledRectangle);

        using var doc = PdfDocument.Open(pdf);
        HiddenTextDetector.Scan(doc).Should().BeEmpty();
    }

    [Fact]
    public void Scan_ImageOverlay_IsDetected()
    {
        var pdf = BuildPdfWithTextAndOverlay(
            "HIDDEN_BY_IMAGE", textX: 100, textY: 700,
            overlayKind: OverlayKind.ImageXObject);

        using var doc = PdfDocument.Open(pdf);
        var hits = HiddenTextDetector.Scan(doc);

        hits.Should().HaveCount(1);
        hits[0].Text.Should().Contain("HIDDEN_BY_IMAGE");
        hits[0].HiddenBy.Should().Contain("image");
    }

    private enum OverlayKind { None, BlackFilledRectangle, WhiteFilledRectangle, ImageXObject }

    /// <summary>
    /// Build a minimal single-page PDF with one line of text at
    /// (<paramref name="textX"/>, <paramref name="textY"/>) and an
    /// optional overlay painted on top (or — if
    /// <paramref name="overlayBeforeText"/> — beneath) the text.
    /// </summary>
    private static byte[] BuildPdfWithTextAndOverlay(
        string text,
        double textX,
        double textY,
        OverlayKind overlayKind,
        bool overlayBeforeText = false)
    {
        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\n"
        };

        w.WriteLine("%PDF-1.4");
        w.Flush();
        var offsets = new long[7];

        offsets[1] = ms.Position;
        w.WriteLine("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj");
        w.Flush();

        offsets[2] = ms.Position;
        w.WriteLine("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj");
        w.Flush();

        offsets[3] = ms.Position;
        var resources = overlayKind == OverlayKind.ImageXObject
            ? "/Font << /F1 5 0 R >> /XObject << /Im0 6 0 R >>"
            : "/Font << /F1 5 0 R >>";
        w.WriteLine($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                    $"/Contents 4 0 R /Resources << {resources} >> >>\nendobj");
        w.Flush();

        // Tight overlay rectangle around the expected text bbox. Helvetica
        // 14pt "SECRET" etc. is roughly 10 pt tall; span ~ 8pt/char × len.
        double rectLeft = textX - 4;
        double rectBottom = textY - 4;
        double rectWidth = text.Length * 8;
        double rectHeight = 14;

        string textOp = $"BT /F1 14 Tf {textX} {textY} Td ({text}) Tj ET";
        string overlayOp = overlayKind switch
        {
            OverlayKind.BlackFilledRectangle =>
                $"q 0 0 0 rg {rectLeft} {rectBottom} {rectWidth} {rectHeight} re f Q",
            OverlayKind.WhiteFilledRectangle =>
                $"q 1 1 1 rg {rectLeft} {rectBottom} {rectWidth} {rectHeight} re f Q",
            OverlayKind.ImageXObject =>
                $"q {rectWidth} 0 0 {rectHeight} {rectLeft} {rectBottom} cm /Im0 Do Q",
            _ => "",
        };

        string body = overlayKind == OverlayKind.None
            ? textOp
            : overlayBeforeText
                ? $"{overlayOp}\n{textOp}"
                : $"{textOp}\n{overlayOp}";

        offsets[4] = ms.Position;
        w.WriteLine("4 0 obj");
        w.WriteLine($"<< /Length {body.Length} >>\nstream");
        w.Write(body);
        w.WriteLine();
        w.WriteLine("endstream\nendobj");
        w.Flush();

        offsets[5] = ms.Position;
        w.WriteLine("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj");
        w.Flush();

        if (overlayKind == OverlayKind.ImageXObject)
        {
            var pixels = new byte[] { 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0 }; // 2x2 red
            offsets[6] = ms.Position;
            w.WriteLine("6 0 obj");
            w.WriteLine($"<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                        $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {pixels.Length} >>");
            w.WriteLine("stream");
            w.Flush();
            ms.Write(pixels, 0, pixels.Length);
            w.WriteLine();
            w.WriteLine("endstream\nendobj");
            w.Flush();
        }

        long xrefPos = ms.Position;
        int lastObj = overlayKind == OverlayKind.ImageXObject ? 6 : 5;
        w.WriteLine("xref");
        w.WriteLine($"0 {lastObj + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= lastObj; i++)
            w.WriteLine($"{offsets[i]:D10} 00000 n ");
        w.Flush();
        w.WriteLine($"trailer\n<< /Root 1 0 R /Size {lastObj + 1} >>\nstartxref\n{xrefPos}\n%%EOF");
        w.Flush();
        return ms.ToArray();
    }
}
