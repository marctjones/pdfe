using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Visual verification tests for PDF redaction.
/// These tests:
/// 1. Render the PDF before redaction
/// 2. Perform redaction
/// 3. Render the PDF after redaction
/// 4. Save screenshots for visual inspection
/// 5. Verify text is removed from PDF structure
/// 6. Check for font size corruption issues
///
/// Run these tests to diagnose GUI rendering/caching issues.
/// </summary>
[Collection("Sequential")]
public class VisualRedactionVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly string _screenshotDir;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    // Must match PdfRenderService
    private const int RenderDpi = 150;

    public VisualRedactionVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"VisualRedactionTests_{Guid.NewGuid()}");
        _screenshotDir = Path.Combine(_tempDir, "screenshots");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_screenshotDir);

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _redactionService = new RedactionService(
            _loggerFactory.CreateLogger<RedactionService>(),
            _loggerFactory);

        _output.WriteLine("=== Visual Redaction Verification Tests ===");
        _output.WriteLine($"Screenshots will be saved to: {_screenshotDir}");
    }

    public void Dispose()
    {
        // Don't delete screenshots - useful for debugging
        _output.WriteLine($"\nScreenshots preserved in: {_screenshotDir}");

        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private string CreateTempPath(string filename) => Path.Combine(_tempDir, filename);

    /// <summary>
    /// Tests the "wallet size" redaction scenario from the birth certificate PDF.
    /// Creates a PDF with "WALLET SIZE" text at the same approximate position as the real PDF.
    /// </summary>
    [Fact]
    public void WalletSize_Redaction_TextRemovedAndFontSizePreserved()
    {
        _output.WriteLine("\n=== TEST: WalletSize_Redaction_TextRemovedAndFontSizePreserved ===");

        // Step 1: Create a PDF that mimics the birth certificate layout
        var pdfPath = CreateTempPath("wallet_size_test.pdf");
        CreateBirthCertificateMockPdf(pdfPath);
        _tempFiles.Add(pdfPath);

        // Step 2: Render BEFORE redaction
        var beforeScreenshot = Path.Combine(_screenshotDir, "01_before_redaction.png");
        RenderPdfToImage(pdfPath, beforeScreenshot);
        _output.WriteLine($"Before screenshot: {beforeScreenshot}");

        // Step 3: Find "WALLET" text position using PdfPig
        Rect selectionInImagePixels;
        double pageHeight;
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            pageHeight = page.Height;
            var words = page.GetWords().ToList();

            _output.WriteLine($"\nWords found in PDF:");
            foreach (var w in words)
            {
                _output.WriteLine($"  '{w.Text}' at ({w.BoundingBox.Left:F1}, {w.BoundingBox.Bottom:F1})");
            }

            var walletWord = words.FirstOrDefault(w => w.Text.Contains("WALLET", StringComparison.OrdinalIgnoreCase));
            walletWord.Should().NotBeNull("PDF should contain 'WALLET' text");

            var pdfBounds = walletWord!.BoundingBox;
            var scale = RenderDpi / 72.0;
            var imageY = (pageHeight - pdfBounds.Top) * scale;

            // Create selection that covers "WALLET SIZE" (wider than just WALLET)
            selectionInImagePixels = new Rect(
                pdfBounds.Left * scale - 5,
                imageY - 5,
                200, // Wide enough for "WALLET SIZE"
                (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
            );

            _output.WriteLine($"\nSelection in image pixels: X={selectionInImagePixels.X:F1}, Y={selectionInImagePixels.Y:F1}, W={selectionInImagePixels.Width:F1}, H={selectionInImagePixels.Height:F1}");
        }

        // Step 4: Perform redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], selectionInImagePixels, pdfPath, renderDpi: RenderDpi);

        var redactedPath = CreateTempPath("wallet_size_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Step 5: Render AFTER redaction (from saved file - simulates close and reopen)
        var afterScreenshot = Path.Combine(_screenshotDir, "02_after_redaction.png");
        RenderPdfToImage(redactedPath, afterScreenshot);
        _output.WriteLine($"After screenshot: {afterScreenshot}");

        // Step 6: Verify text is removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"\nText after redaction:\n{textAfter}");

        textAfter.Should().NotContain("WALLET", "WALLET text should be removed from PDF structure");

        // Step 7: Check for font size issues - extract words and check their bounding boxes
        using (var redactedPdfPig = UglyToad.PdfPig.PdfDocument.Open(redactedPath))
        {
            var page = redactedPdfPig.GetPage(1);
            var words = page.GetWords().ToList();

            _output.WriteLine($"\nWords in redacted PDF:");
            bool foundOversizedText = false;
            foreach (var w in words)
            {
                var height = w.BoundingBox.Top - w.BoundingBox.Bottom;
                _output.WriteLine($"  '{w.Text}' at ({w.BoundingBox.Left:F1}, {w.BoundingBox.Bottom:F1}) height={height:F1}");

                // Check for abnormally large text (height > 50 points is suspicious for regular text)
                if (height > 50 && w.Text.Length > 1)
                {
                    _output.WriteLine($"  *** WARNING: Oversized text detected! '{w.Text}' has height {height:F1}");
                    foundOversizedText = true;
                }
            }

            foundOversizedText.Should().BeFalse("No text should have abnormally large font size after redaction");
        }

        _output.WriteLine("\nSUCCESS: Redaction complete, text removed, no font size issues");
    }

    /// <summary>
    /// Tests that adjacent text is NOT affected when redacting specific text.
    /// </summary>
    [Fact]
    public void AdjacentText_NotAffected_ByRedaction()
    {
        _output.WriteLine("\n=== TEST: AdjacentText_NotAffected_ByRedaction ===");

        var pdfPath = CreateTempPath("adjacent_text_test.pdf");
        CreateMultiLineTextPdf(pdfPath);
        _tempFiles.Add(pdfPath);

        // Screenshot before
        var beforeScreenshot = Path.Combine(_screenshotDir, "03_adjacent_before.png");
        RenderPdfToImage(pdfPath, beforeScreenshot);

        // Find "REDACT_ME" text
        Rect selectionInImagePixels;
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var words = page.GetWords().ToList();
            var targetWord = words.FirstOrDefault(w => w.Text.Contains("REDACT_ME"));
            targetWord.Should().NotBeNull();

            var pdfBounds = targetWord!.BoundingBox;
            var scale = RenderDpi / 72.0;
            var imageY = (page.Height - pdfBounds.Top) * scale;

            selectionInImagePixels = new Rect(
                pdfBounds.Left * scale - 2,
                imageY - 2,
                (pdfBounds.Right - pdfBounds.Left) * scale + 4,
                (pdfBounds.Top - pdfBounds.Bottom) * scale + 4
            );
        }

        // Perform redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], selectionInImagePixels, pdfPath, renderDpi: RenderDpi);

        var redactedPath = CreateTempPath("adjacent_text_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Screenshot after
        var afterScreenshot = Path.Combine(_screenshotDir, "04_adjacent_after.png");
        RenderPdfToImage(redactedPath, afterScreenshot);

        // Verify
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after: {textAfter}");

        textAfter.Should().NotContain("REDACT_ME");
        textAfter.Should().Contain("KEEP_LINE_1");
        textAfter.Should().Contain("KEEP_LINE_2");
        textAfter.Should().Contain("KEEP_LINE_3");

        // Check font sizes
        using (var redactedPdfPig = UglyToad.PdfPig.PdfDocument.Open(redactedPath))
        {
            var page = redactedPdfPig.GetPage(1);
            var words = page.GetWords().ToList();

            foreach (var w in words)
            {
                var height = w.BoundingBox.Top - w.BoundingBox.Bottom;
                height.Should().BeLessThan(20, $"Word '{w.Text}' should have normal font size, not {height:F1}");
            }
        }

        _output.WriteLine("SUCCESS: Adjacent text preserved with correct font sizes");
    }

    /// <summary>
    /// Create a mock PDF similar to birth certificate with WALLET SIZE text.
    /// </summary>
    private void CreateBirthCertificateMockPdf(string path)
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        var page = doc.AddPage();
        page.Width = 612; // Letter size
        page.Height = 792;

        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Helvetica", 10);
        var boldFont = new PdfSharp.Drawing.XFont("Helvetica-Bold", 12);

        // Header
        gfx.DrawString("CERTIFICATE OF LIVE BIRTH", boldFont, PdfSharp.Drawing.XBrushes.Black, 200, 50);

        // Form fields similar to birth certificate
        gfx.DrawString("TYPE OR PRINT IN PERMANENT BLACK INK", font, PdfSharp.Drawing.XBrushes.Black, 50, 100);
        gfx.DrawString("INFORMATION FOR MEDICAL AND HEALTH USE ONLY", font, PdfSharp.Drawing.XBrushes.Black, 50, 130);

        // The target text - WALLET SIZE
        // Position it similar to where it appears in the real birth certificate
        gfx.DrawString("_WALLET SIZE (2\" x 1.5\")", font, PdfSharp.Drawing.XBrushes.Black, 180, 320);

        // Surrounding text that should NOT be affected
        gfx.DrawString("FILING DATE", font, PdfSharp.Drawing.XBrushes.Black, 50, 350);
        gfx.DrawString("LOCAL REGISTRATION NUMBER", font, PdfSharp.Drawing.XBrushes.Black, 350, 350);

        doc.Save(path);
    }

    /// <summary>
    /// Create a PDF with multiple lines of text for testing adjacent text preservation.
    /// </summary>
    private void CreateMultiLineTextPdf(string path)
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        var page = doc.AddPage();
        page.Width = 612;
        page.Height = 792;

        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Helvetica", 12);

        gfx.DrawString("KEEP_LINE_1: This line should remain", font, PdfSharp.Drawing.XBrushes.Black, 50, 100);
        gfx.DrawString("REDACT_ME: This line should be removed", font, PdfSharp.Drawing.XBrushes.Black, 50, 130);
        gfx.DrawString("KEEP_LINE_2: This line should also remain", font, PdfSharp.Drawing.XBrushes.Black, 50, 160);
        gfx.DrawString("KEEP_LINE_3: And this one too", font, PdfSharp.Drawing.XBrushes.Black, 50, 190);

        doc.Save(path);
    }

    /// <summary>
    /// Render a PDF page to an image file using PDFtoImage.
    /// </summary>
    private void RenderPdfToImage(string pdfPath, string outputPath)
    {
        using var stream = File.OpenRead(pdfPath);
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        var options = new PDFtoImage.RenderOptions(Dpi: RenderDpi);
        using var bitmap = PDFtoImage.Conversion.ToImage(memoryStream, page: 0, options: options);

        using var fileStream = File.Create(outputPath);
        bitmap.Encode(fileStream, SkiaSharp.SKEncodedImageFormat.Png, 100);
    }
}
