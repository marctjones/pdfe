using FluentAssertions;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Test the exact scenario from Issue #137: Simple PDF redaction via GUI
/// </summary>
[Collection("Sequential")]
public class SimplePdfGuiTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public SimplePdfGuiTest(ITestOutputHelper _output)
    {
        this._output = _output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"SimplePdfGuiTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _redactionService = new RedactionService(
            _loggerFactory.CreateLogger<RedactionService>(),
            _loggerFactory);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void GuiRedactsSimplePdf_CONFIDENTIAL_TextIsRemoved()
    {
        _output.WriteLine("\n=== TESTING ISSUE #137: Simple PDF GUI Redaction ===");

        // Use the actual demo PDF (path relative to test binary: bin/Debug/net8.0)
        var demoPdf = Path.GetFullPath("../../../../PdfEditor.Demo/RedactionDemo/01_simple_original.pdf");
        if (!File.Exists(demoPdf))
        {
            _output.WriteLine($"SKIP: Demo PDF not found at {demoPdf}");
            return;
        }

        _output.WriteLine($"Input: {demoPdf}");

        // Verify it contains "CONFIDENTIAL"
        var textBefore = PdfTestHelpers.ExtractAllText(demoPdf);
        _output.WriteLine($"Text before redaction: {textBefore}");
        textBefore.Should().Contain("CONFIDENTIAL", "Demo PDF should contain CONFIDENTIAL");

        // Get text positions using PdfPig and redact
        string outputPath;
        using (var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(demoPdf))
        {
            var pdfPage = pdfDoc.GetPage(1);
            var words = pdfPage.GetWords();
            var confidentialWord = words.FirstOrDefault(w => w.Text.Contains("CONFIDENTIAL"));
            confidentialWord.Should().NotBeNull("Should find CONFIDENTIAL in PDF");

            var bbox = confidentialWord!.BoundingBox;
            _output.WriteLine($"Found CONFIDENTIAL at PDF coords (bottom-left): ({bbox.Left:F2}, {bbox.Bottom:F2}, {bbox.Width:F2}x{bbox.Height:F2})");

            // Load PDF in memory (like GUI does)
            var doc = PdfReader.Open(demoPdf, PdfDocumentOpenMode.Modify);
            var page = doc.Pages[0];
            var pageHeight = page.Height.Point;

            // Convert PdfPig coordinates (bottom-left) to top-left (Avalonia convention)
            // PdfPig Bottom is the baseline, Top is Bottom + Height
            var topLeftY = pageHeight - (bbox.Bottom + bbox.Height);

            var avaloniaArea = new Avalonia.Rect(
                bbox.Left,
                topLeftY,
                bbox.Width,
                bbox.Height);

            _output.WriteLine($"Converted to top-left coords: ({avaloniaArea.X:F2}, {avaloniaArea.Y:F2}, {avaloniaArea.Width:F2}x{avaloniaArea.Height:F2})");
            _output.WriteLine($"Page height: {pageHeight:F2}");

            // Redact using RedactionService (simulates GUI workflow)
            // Pass as PDF points (72 DPI) with default renderDpi=72
            _redactionService.RedactArea(page, avaloniaArea, demoPdf);

            // Save the redacted document
            outputPath = Path.Combine(_tempDir, "simple_redacted_gui.pdf");
            doc.Save(outputPath);
            doc.Dispose();
        } // end using pdfDoc

        _output.WriteLine($"Saved to: {outputPath}");

        // CRITICAL: Verify text is REMOVED from PDF structure
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        _output.WriteLine($"Text after redaction: {textAfter}");

        // THIS IS THE TEST FROM ISSUE #137
        textAfter.Should().NotContain("CONFIDENTIAL",
            "CONFIDENTIAL must be REMOVED from PDF structure (Issue #137)");

        // Also verify with external tool simulation
        var stillContains = textAfter.Contains("CONFIDENTIAL", StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"\nVERIFICATION: pdftotext would find CONFIDENTIAL: {stillContains}");

        if (stillContains)
        {
            _output.WriteLine("❌ ISSUE #137 REPRODUCED: Text NOT removed from PDF!");
        }
        else
        {
            _output.WriteLine("✅ SUCCESS: Text successfully removed from PDF");
        }

        stillContains.Should().BeFalse("External tool (pdftotext) must not find redacted text");
    }
}
