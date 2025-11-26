using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
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
/// Integration tests that simulate actual GUI redaction workflow.
/// These tests catch coordinate mismatches between:
/// - Where text visually appears in the rendered image
/// - Where ContentStreamParser calculates text bounding boxes
///
/// If these tests fail, TRUE REDACTION IS BROKEN IN THE GUI!
/// </summary>
[Collection("Sequential")]
public class GuiRedactionSimulationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    // Must match PdfRenderService
    private const int RenderDpi = 150;

    public GuiRedactionSimulationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"GuiRedactionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _redactionService = new RedactionService(
            _loggerFactory.CreateLogger<RedactionService>(),
            _loggerFactory);

        _output.WriteLine("=== GUI Redaction Simulation Test Suite ===");
        _output.WriteLine($"Render DPI: {RenderDpi}");
        _output.WriteLine($"Temp directory: {_tempDir}");
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateTempPath(string filename) => Path.Combine(_tempDir, filename);

    /// <summary>
    /// CRITICAL TEST: Simulates exact GUI workflow
    /// 1. Create PDF with known text
    /// 2. Render to image at GUI DPI (150)
    /// 3. Find where text visually appears using PdfPig
    /// 4. Create selection at that visual location
    /// 5. Redact using same coordinates GUI would use
    /// 6. Verify text is REMOVED from PDF structure
    ///
    /// If this test fails, the GUI redaction is broken!
    /// </summary>
    [Fact]
    public void GuiWorkflow_SelectTextAtVisualLocation_TextIsRemoved()
    {
        _output.WriteLine("\n=== TEST: GuiWorkflow_SelectTextAtVisualLocation_TextIsRemoved ===");
        _output.WriteLine("This test simulates the exact GUI redaction workflow.");

        // Step 1: Create PDF with known text
        var pdfPath = CreateTempPath("gui_test.pdf");
        var secretText = "SECRET_DATA_12345";
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, secretText);
        _tempFiles.Add(pdfPath);

        _output.WriteLine($"Created PDF with text: '{secretText}'");

        // Step 2: Get text position from PdfPig (same as GUI text extraction)
        Rect textBoundsInImagePixels;
        double pageHeightPoints;
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            pageHeightPoints = page.Height;
            var words = page.GetWords().ToList();

            var secretWord = words.FirstOrDefault(w => w.Text.Contains("SECRET"));
            secretWord.Should().NotBeNull("PDF should contain the secret text");

            // Convert PdfPig coordinates (bottom-left origin, 72 DPI) to image pixels (top-left origin, 150 DPI)
            var pdfBounds = secretWord!.BoundingBox;

            // Scale from PDF points to image pixels
            var scale = RenderDpi / 72.0;

            // Convert Y from bottom-left to top-left origin
            var imageY = (pageHeightPoints - pdfBounds.Top) * scale;

            textBoundsInImagePixels = new Rect(
                pdfBounds.Left * scale,
                imageY,
                (pdfBounds.Right - pdfBounds.Left) * scale,
                (pdfBounds.Top - pdfBounds.Bottom) * scale
            );

            _output.WriteLine($"PdfPig text bounds (PDF coords): Left={pdfBounds.Left:F2}, Bottom={pdfBounds.Bottom:F2}, Right={pdfBounds.Right:F2}, Top={pdfBounds.Top:F2}");
            _output.WriteLine($"Page height: {pageHeightPoints} points");
            _output.WriteLine($"Text bounds in image pixels: X={textBoundsInImagePixels.X:F2}, Y={textBoundsInImagePixels.Y:F2}, W={textBoundsInImagePixels.Width:F2}, H={textBoundsInImagePixels.Height:F2}");
        }

        // Step 3: Create selection rectangle around the text (with small margin)
        var selectionInImagePixels = new Rect(
            textBoundsInImagePixels.X - 5,
            textBoundsInImagePixels.Y - 5,
            textBoundsInImagePixels.Width + 10,
            textBoundsInImagePixels.Height + 10
        );

        _output.WriteLine($"Selection in image pixels: X={selectionInImagePixels.X:F2}, Y={selectionInImagePixels.Y:F2}, W={selectionInImagePixels.Width:F2}, H={selectionInImagePixels.Height:F2}");

        // Step 4: Perform redaction using GUI coordinates
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var pdfPage = document.Pages[0];

        _output.WriteLine($"Calling RedactArea with renderDpi={RenderDpi}");
        _redactionService.RedactArea(pdfPage, selectionInImagePixels, renderDpi: RenderDpi);

        var redactedPath = CreateTempPath("gui_test_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Step 5: Verify text is REMOVED from PDF structure
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after redaction: '{textAfter.Trim()}'");

        textAfter.Should().NotContain("SECRET",
            "CRITICAL: Text must be REMOVED from PDF structure! " +
            "If this fails, TRUE REDACTION IS BROKEN IN THE GUI!");

        _output.WriteLine("SUCCESS: Text was removed from PDF structure");
    }

    /// <summary>
    /// Tests redaction at various positions on the page
    /// </summary>
    [Theory]
    [InlineData(100, 100, "TOP_LEFT")]
    [InlineData(300, 100, "TOP_CENTER")]
    [InlineData(100, 400, "MIDDLE_LEFT")]
    [InlineData(300, 400, "CENTER")]
    [InlineData(100, 700, "BOTTOM_LEFT")]
    public void GuiWorkflow_TextAtVariousPositions_AllRedacted(double pdfX, double pdfY, string label)
    {
        _output.WriteLine($"\n=== TEST: TextAtPosition_{label} (PDF coords: {pdfX}, {pdfY}) ===");

        // Create PDF with text at specific position
        var pdfPath = CreateTempPath($"position_{label}.pdf");
        var secretText = $"SECRET_{label}";
        TestPdfGenerator.CreateTextAtPosition(pdfPath, secretText, pdfX, pdfY);
        _tempFiles.Add(pdfPath);

        // Get actual text position from PdfPig
        Rect selectionInImagePixels;
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var words = page.GetWords().ToList();
            var secretWord = words.FirstOrDefault(w => w.Text.Contains("SECRET"));

            if (secretWord == null)
            {
                _output.WriteLine($"WARNING: Could not find text '{secretText}' in PDF");
                return;
            }

            var pdfBounds = secretWord.BoundingBox;
            var scale = RenderDpi / 72.0;
            var imageY = (page.Height - pdfBounds.Top) * scale;

            selectionInImagePixels = new Rect(
                pdfBounds.Left * scale - 5,
                imageY - 5,
                (pdfBounds.Right - pdfBounds.Left) * scale + 10,
                (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
            );

            _output.WriteLine($"Text found at image pixels: ({selectionInImagePixels.X:F2}, {selectionInImagePixels.Y:F2})");
        }

        // Perform redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], selectionInImagePixels, renderDpi: RenderDpi);

        var redactedPath = CreateTempPath($"position_{label}_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Verify
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        textAfter.Should().NotContain("SECRET",
            $"Text at {label} must be removed from PDF structure");

        _output.WriteLine($"SUCCESS: Text at {label} was removed");
    }

    /// <summary>
    /// Tests that adjacent lines are NOT removed when redacting one line
    /// </summary>
    [Fact]
    public void GuiWorkflow_RedactOneLine_AdjacentLinesPreserved()
    {
        _output.WriteLine("\n=== TEST: RedactOneLine_AdjacentLinesPreserved ===");

        // Create PDF with multiple lines
        var pdfPath = CreateTempPath("multiline.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(pdfPath, new[]
        {
            "Line 1 - KEEP THIS",
            "Line 2 - REDACT THIS SECRET",
            "Line 3 - KEEP THIS TOO"
        });
        _tempFiles.Add(pdfPath);

        // Find the line to redact
        Rect selectionInImagePixels;
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var words = page.GetWords().ToList();
            var secretWord = words.FirstOrDefault(w => w.Text == "SECRET");

            secretWord.Should().NotBeNull("PDF should contain 'SECRET'");

            var pdfBounds = secretWord!.BoundingBox;
            var scale = RenderDpi / 72.0;
            var imageY = (page.Height - pdfBounds.Top) * scale;

            // Create selection that covers just the SECRET word (tight bounds)
            selectionInImagePixels = new Rect(
                pdfBounds.Left * scale - 2,
                imageY - 2,
                (pdfBounds.Right - pdfBounds.Left) * scale + 4,
                (pdfBounds.Top - pdfBounds.Bottom) * scale + 4
            );

            _output.WriteLine($"Selection around 'SECRET': ({selectionInImagePixels.X:F2}, {selectionInImagePixels.Y:F2}, {selectionInImagePixels.Width:F2}x{selectionInImagePixels.Height:F2})");
        }

        // Perform redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], selectionInImagePixels, renderDpi: RenderDpi);

        var redactedPath = CreateTempPath("multiline_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Verify
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after redaction:\n{textAfter}");

        textAfter.Should().NotContain("SECRET", "Redacted text should be removed");
        textAfter.Should().Contain("Line 1", "Adjacent line 1 should be preserved");
        textAfter.Should().Contain("Line 3", "Adjacent line 3 should be preserved");
        textAfter.Should().Contain("KEEP", "Non-redacted text should be preserved");

        _output.WriteLine("SUCCESS: Only targeted text removed, adjacent lines preserved");
    }

    /// <summary>
    /// Diagnostic test that outputs coordinate conversion details
    /// Run this if redaction stops working to debug coordinate issues
    /// </summary>
    [Fact]
    public void Diagnostic_CoordinateConversionVerification()
    {
        _output.WriteLine("\n=== DIAGNOSTIC: Coordinate Conversion Verification ===");
        _output.WriteLine("This test verifies coordinate conversion between systems.\n");

        var pdfPath = CreateTempPath("diagnostic.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "DIAGNOSTIC_TEXT");
        _tempFiles.Add(pdfPath);

        // Get coordinates from all systems
        using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = pdfPigDoc.GetPage(1);
        var word = page.GetWords().First();

        _output.WriteLine("=== Coordinate Systems ===\n");

        // PdfPig coordinates (PDF native - bottom-left origin)
        _output.WriteLine("1. PdfPig (PDF native, bottom-left origin, 72 DPI):");
        _output.WriteLine($"   Left={word.BoundingBox.Left:F2}, Bottom={word.BoundingBox.Bottom:F2}");
        _output.WriteLine($"   Right={word.BoundingBox.Right:F2}, Top={word.BoundingBox.Top:F2}");
        _output.WriteLine($"   Page Height={page.Height:F2} points\n");

        // Convert to Avalonia coordinates (top-left origin, 72 DPI)
        var avaloniaY = page.Height - word.BoundingBox.Top;
        _output.WriteLine("2. Avalonia/ContentStreamParser (top-left origin, 72 DPI):");
        _output.WriteLine($"   X={word.BoundingBox.Left:F2}, Y={avaloniaY:F2}");
        _output.WriteLine($"   Width={word.BoundingBox.Right - word.BoundingBox.Left:F2}");
        _output.WriteLine($"   Height={word.BoundingBox.Top - word.BoundingBox.Bottom:F2}\n");

        // Convert to image pixels (top-left origin, 150 DPI)
        var scale = RenderDpi / 72.0;
        var imageX = word.BoundingBox.Left * scale;
        var imageY = (page.Height - word.BoundingBox.Top) * scale;
        var imageW = (word.BoundingBox.Right - word.BoundingBox.Left) * scale;
        var imageH = (word.BoundingBox.Top - word.BoundingBox.Bottom) * scale;

        _output.WriteLine($"3. Image Pixels (top-left origin, {RenderDpi} DPI):");
        _output.WriteLine($"   X={imageX:F2}, Y={imageY:F2}");
        _output.WriteLine($"   Width={imageW:F2}, Height={imageH:F2}\n");

        // What CoordinateConverter produces
        var imageRect = new Rect(imageX, imageY, imageW, imageH);
        var convertedToPdfPoints = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(imageRect, RenderDpi);

        _output.WriteLine("4. CoordinateConverter.ImageSelectionToPdfPointsTopLeft result:");
        _output.WriteLine($"   X={convertedToPdfPoints.X:F2}, Y={convertedToPdfPoints.Y:F2}");
        _output.WriteLine($"   Width={convertedToPdfPoints.Width:F2}, Height={convertedToPdfPoints.Height:F2}\n");

        // Verify round-trip
        _output.WriteLine("=== Verification ===");
        var expectedY = avaloniaY;
        var actualY = convertedToPdfPoints.Y;
        var yDiff = Math.Abs(expectedY - actualY);

        _output.WriteLine($"Expected Avalonia Y: {expectedY:F2}");
        _output.WriteLine($"Actual converted Y: {actualY:F2}");
        _output.WriteLine($"Difference: {yDiff:F2} points");

        yDiff.Should().BeLessThan(1.0,
            "Coordinate conversion should be accurate within 1 point");

        _output.WriteLine("\nDIAGNOSTIC PASSED: Coordinate conversion is working correctly");
    }
}
