using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services.Verification;
using PdfEditor.Tests.Utilities;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Tests.Services;

public class RedactionVerifierTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly RedactionVerifier _verifier;

    public RedactionVerifierTests()
    {
        _verifier = new RedactionVerifier(new NullLogger<RedactionVerifier>(), NullLoggerFactory.Instance);
    }

    [Fact]
    public void Verify_WhenTextOutsideBlackBox_Passes()
    {
        using var doc = CreatePdf(gfx =>
        {
            var font = new XFont("Arial", 12);
            gfx.DrawRectangle(XBrushes.Black, new XRect(100, 100, 150, 60));
            gfx.DrawString("visible text", font, XBrushes.Black, new XPoint(100, 220));
        });

        var result = _verifier.Verify(doc);

        result.Passed.Should().BeTrue();
        result.Leaks.Should().BeEmpty();
    }

    [Fact]
    public void Verify_WhenTextOverlapsBlackBox_FindsLeak()
    {
        using var doc = CreatePdf(gfx =>
        {
            var font = new XFont("Arial", 12);
            gfx.DrawRectangle(XBrushes.Black, new XRect(100, 100, 150, 60));
            gfx.DrawString("SECRET", font, XBrushes.Black, new XPoint(110, 120));
        });

        var result = _verifier.Verify(doc);

        result.Passed.Should().BeFalse();
        result.Leaks.Should().ContainSingle();
        result.Leaks[0].PageIndex.Should().Be(0);
        result.Leaks[0].Text.Should().Contain("SECRET");
    }

    private PdfDocument CreatePdf(Action<XGraphics> draw)
    {
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }

        var path = Path.Combine(Path.GetTempPath(), $"redaction_verifier_{Guid.NewGuid():N}.pdf");
        _tempFiles.Add(path);

        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            draw(gfx);
        }

        document.Save(path);
        document.Dispose();

        return PdfReader.Open(path, PdfDocumentOpenMode.Import);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* ignore */ }
        }
    }
}
