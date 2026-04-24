using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using Xunit;
using Xunit.Abstractions;
using PdfeCore = Pdfe.Core.Document;

namespace PdfEditor.Tests.Security;

/// <summary>
/// Integration tests for <see cref="RedactionService.RedactAreaViaCore"/> —
/// the #235-path replacement that drives glyph-level redaction through
/// Pdfe.Core without touching PdfSharp or PdfPig.
/// </summary>
public class RedactionServiceViaCoreTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _service;

    public RedactionServiceViaCoreTests(ITestOutputHelper output)
    {
        _output = output;
        _service = new RedactionService(
            NullLogger<RedactionService>.Instance,
            NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    private string TempPdf()
    {
        var p = Path.Combine(Path.GetTempPath(), $"pdfe-core-redact-{Guid.NewGuid():N}.pdf");
        _tempFiles.Add(p);
        return p;
    }

    [Fact]
    public void RedactAreaViaCore_RemovesTargetText_FromContentStream()
    {
        // Arrange: generate a PDF with known text, then open it via Pdfe.Core.
        var path = TestPdfGenerator.CreateTextOnlyPdf(TempPdf(), "CONFIDENTIAL DATA");
        using var doc = PdfeCore.PdfDocument.Open(path);
        var page = doc.GetPage(1);

        // Find the bounding box of "CONFIDENTIAL" in content-stream coords
        // using the letters Pdfe.Core already extracts.
        var targetLetters = page.Letters.Take("CONFIDENTIAL".Length).ToList();
        targetLetters.Select(l => l.Value).Should().Equal(
            "CONFIDENTIAL".Select(c => c.ToString()),
            "test PDF should contain the expected first word");

        var left = targetLetters.Min(l => l.GlyphRectangle.Left);
        var bottom = targetLetters.Min(l => l.GlyphRectangle.Bottom);
        var right = targetLetters.Max(l => l.GlyphRectangle.Right);
        var top = targetLetters.Max(l => l.GlyphRectangle.Top);

        // Convert back to the top-left-origin pixel coordinates the public
        // API expects, mirroring what CoordinateConverter does in reverse.
        var pageHeight = page.MediaBox.Top - page.MediaBox.Bottom;
        var areaTopLeft = new Avalonia.Rect(
            x: left,
            y: pageHeight - top,
            width: right - left,
            height: top - bottom);

        // Act: call the new method at 72 DPI (pixels-are-points), leaving
        // the conversion chain exercised end-to-end.
        _service.RedactAreaViaCore(page, areaTopLeft);

        // Save and re-open to prove the change survived round-trip.
        var savedBytes = doc.SaveToBytes();
        using var reopened = PdfeCore.PdfDocument.Open(savedBytes);
        var extractedAfter = string.Concat(reopened.GetPage(1).Letters.Select(l => l.Value));

        extractedAfter.Should().NotContain("CONFIDENTIAL",
            "redacted text must be structurally absent after RedactAreaViaCore");
        extractedAfter.Should().Contain("DATA", "non-redacted text survives");

        // The structural guarantee — bytes test.
        var rawContent = System.Text.Encoding.Latin1.GetString(
            reopened.GetPage(1).GetContentStreamBytes());
        rawContent.Should().NotContain("CONFIDENTIAL",
            "raw content stream must not contain redacted text");
    }

    [Fact]
    public void RedactAreaViaCore_TracksRedactedTerms()
    {
        var path = TestPdfGenerator.CreateTextOnlyPdf(TempPdf(), "SECRET INFO");
        using var doc = PdfeCore.PdfDocument.Open(path);
        var page = doc.GetPage(1);

        var secret = page.Letters.Take("SECRET".Length).ToList();
        var left = secret.Min(l => l.GlyphRectangle.Left);
        var bottom = secret.Min(l => l.GlyphRectangle.Bottom);
        var right = secret.Max(l => l.GlyphRectangle.Right);
        var top = secret.Max(l => l.GlyphRectangle.Top);
        var pageHeight = page.MediaBox.Top - page.MediaBox.Bottom;
        var areaTopLeft = new Avalonia.Rect(left, pageHeight - top, right - left, top - bottom);

        var priorCount = _service.RedactedTerms.Count;
        _service.RedactAreaViaCore(page, areaTopLeft);

        _service.RedactedTerms.Count.Should().Be(priorCount + 1,
            "each successful redaction should record the removed text for metadata sanitization");
        _service.RedactedTerms.Last().Should().Contain("SECRET");
    }

    [Fact]
    public void RedactAreaViaCore_NullPage_Throws()
    {
        Action act = () => _service.RedactAreaViaCore(null!, new Avalonia.Rect(0, 0, 10, 10));
        act.Should().Throw<ArgumentNullException>();
    }
}
