using FluentAssertions;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Integration tests for the mark-then-apply redaction workflow introduced in v1.3.0.
///
/// See Issue #28: Add integration test for complete mark-then-apply workflow
/// </summary>
[Collection("Sequential")]
public class MarkThenApplyWorkflowTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    private const int RenderDpi = 150;

    public MarkThenApplyWorkflowTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"MarkThenApply_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _redactionService = new RedactionService(
            _loggerFactory.CreateLogger<RedactionService>(),
            _loggerFactory);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateTempPath(string filename)
    {
        var path = Path.Combine(_tempDir, filename);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Complete mark-then-apply workflow test.
    /// Simulates: Open PDF → Mark 3 areas → Apply all → Save → Verify text removed.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "MarkThenApply")]
    public void CompleteWorkflow_MarkThreeAreas_AllRedacted()
    {
        _output.WriteLine("\n=== TEST: Complete Mark-Then-Apply Workflow ===");

        // Step 1: Create multi-line PDF with known content
        var inputPath = CreateTempPath("input.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(inputPath, new[]
        {
            "SECRET1 - First secret to redact",
            "PUBLIC - This line should remain",
            "SECRET2 - Second secret to redact",
            "PUBLIC - Another public line",
            "SECRET3 - Third secret to redact"
        });

        _output.WriteLine($"Created PDF with 3 secrets and 2 public lines");

        // Step 2: Get text positions using PdfPig (same as GUI)
        var secretAreas = new List<(int pageNum, Avalonia.Rect screenArea, string text)>();
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var words = page.GetWords().ToList();

            foreach (var word in words)
            {
                if (word.Text.StartsWith("SECRET"))
                {
                    var pdfBounds = word.BoundingBox;
                    var scale = RenderDpi / 72.0;
                    var imageY = (page.Height - pdfBounds.Top) * scale;

                    var screenRect = new Avalonia.Rect(
                        pdfBounds.Left * scale - 5,
                        imageY - 5,
                        (pdfBounds.Right - pdfBounds.Left) * scale + 10,
                        (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
                    );

                    secretAreas.Add((1, screenRect, word.Text));
                    _output.WriteLine($"Marked area for '{word.Text}' at ({screenRect.X:F1}, {screenRect.Y:F1})");
                }
            }
        }

        secretAreas.Should().HaveCount(3, "Should find 3 SECRET words");

        // Step 3: Open document and mark all areas (simulate pending redactions)
        var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        var pdfPage = document.Pages[0];

        // Step 4: Apply all redactions
        foreach (var (pageNum, screenArea, text) in secretAreas)
        {
            _output.WriteLine($"Applying redaction for '{text}'...");
            _redactionService.RedactArea(pdfPage, screenArea, inputPath, renderDpi: RenderDpi);
        }

        // Step 5: Save the document
        var outputPath = CreateTempPath("output_redacted.pdf");
        document.Save(outputPath);
        document.Dispose();

        _output.WriteLine($"Saved redacted PDF to: {outputPath}");

        // Step 6: Verify TRUE redaction - text must be removed from PDF structure
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        _output.WriteLine($"\nText after redaction:\n{textAfter}");

        // Verify secrets are removed
        textAfter.Should().NotContain("SECRET1", "SECRET1 should be removed");
        textAfter.Should().NotContain("SECRET2", "SECRET2 should be removed");
        textAfter.Should().NotContain("SECRET3", "SECRET3 should be removed");

        // Verify public text is preserved
        textAfter.Should().Contain("PUBLIC", "PUBLIC lines should be preserved");

        _output.WriteLine("\n✓ SUCCESS: All 3 secrets removed, public lines preserved");
    }

    /// <summary>
    /// Multi-page mark-then-apply workflow.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "MarkThenApply")]
    public void MultiPageWorkflow_MarkAcrossPages_AllRedacted()
    {
        _output.WriteLine("\n=== TEST: Multi-Page Mark-Then-Apply Workflow ===");

        // Create 3-page PDF (uses default content with "Secret on Page N")
        var inputPath = CreateTempPath("multipage.pdf");
        TestPdfGenerator.CreateMultiPagePdf(inputPath, 3);

        _output.WriteLine("Created 3-page PDF with one secret per page");

        // Mark redactions on each page
        var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

        for (int i = 0; i < 3; i++)
        {
            using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath);
            var pigPage = pdfPigDoc.GetPage(i + 1);
            var secretWord = pigPage.GetWords().FirstOrDefault(w => w.Text.Contains("Secret"));

            if (secretWord != null)
            {
                var pdfBounds = secretWord.BoundingBox;
                var scale = RenderDpi / 72.0;
                var imageY = (pigPage.Height - pdfBounds.Top) * scale;

                var screenRect = new Avalonia.Rect(
                    pdfBounds.Left * scale - 5,
                    imageY - 5,
                    (pdfBounds.Right - pdfBounds.Left) * scale + 10,
                    (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
                );

                _output.WriteLine($"Applying redaction on page {i + 1} for '{secretWord.Text}'");
                _redactionService.RedactArea(document.Pages[i], screenRect, inputPath, renderDpi: RenderDpi);
            }
        }

        var outputPath = CreateTempPath("multipage_redacted.pdf");
        document.Save(outputPath);
        document.Dispose();

        // Verify all secrets removed
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        _output.WriteLine($"\nText after redaction:\n{textAfter}");

        textAfter.Should().NotContain("Secret", "Secret text should be removed from all pages");

        // Verify page structure preserved
        textAfter.Should().Contain("Page 1", "Page 1 text should remain");
        textAfter.Should().Contain("Page 2", "Page 2 text should remain");
        textAfter.Should().Contain("Page 3", "Page 3 text should remain");

        _output.WriteLine("\n✓ SUCCESS: All 3 page secrets removed");
    }

    /// <summary>
    /// Verifies that redactions are TRUE glyph-level removal.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "TrueRedaction")]
    public void VerifyTrueRedaction_GlyphsRemoved_NotJustCovered()
    {
        _output.WriteLine("\n=== TEST: Verify TRUE Glyph-Level Redaction ===");

        var inputPath = CreateTempPath("glyph_test.pdf");
        var secretText = "SENSITIVE_DATA_XYZ123";
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, secretText);

        // Get text position
        Avalonia.Rect screenRect;
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var word = page.GetWords().First();

            var pdfBounds = word.BoundingBox;
            var scale = RenderDpi / 72.0;
            var imageY = (page.Height - pdfBounds.Top) * scale;

            screenRect = new Avalonia.Rect(
                pdfBounds.Left * scale - 5,
                imageY - 5,
                (pdfBounds.Right - pdfBounds.Left) * scale + 300, // Wide to cover all
                (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
            );
        }

        // Apply redaction
        var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], screenRect, inputPath, renderDpi: RenderDpi);

        var outputPath = CreateTempPath("glyph_redacted.pdf");
        document.Save(outputPath);
        document.Dispose();

        // Verify using multiple extraction methods
        var textPdfSharp = PdfTestHelpers.ExtractAllText(outputPath);
        _output.WriteLine($"PDFsharp extraction: '{textPdfSharp.Trim()}'");

        // Text must NOT be found by any extraction method
        textPdfSharp.Should().NotContain("SENSITIVE",
            "Text must be removed from PDF structure (glyph-level), not just visually covered");

        _output.WriteLine("\n✓ SUCCESS: TRUE glyph-level redaction verified");
    }

    /// <summary>
    /// Batch redaction workflow - mark multiple then apply all at once.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "BatchRedaction")]
    public void BatchWorkflow_CollectMultipleMarks_ApplyAllAtOnce()
    {
        _output.WriteLine("\n=== TEST: Batch Redaction Workflow ===");

        var inputPath = CreateTempPath("batch.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(inputPath, new[]
        {
            "Name: John Smith - REDACT",
            "SSN: 123-45-6789 - REDACT",
            "DOB: 01/15/1980 - REDACT",
            "Address: 123 Main Street - KEEP",
            "Phone: 555-1234 - KEEP"
        });

        // Collect all areas to redact first (simulating pending list)
        var pendingAreas = new List<Avalonia.Rect>();

        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(inputPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var words = page.GetWords().ToList();

            // Find words ending in REDACT line
            var redactWords = words.Where(w =>
                w.Text == "Name:" || w.Text == "SSN:" || w.Text == "DOB:").ToList();

            foreach (var word in redactWords)
            {
                var pdfBounds = word.BoundingBox;
                var scale = RenderDpi / 72.0;
                var imageY = (page.Height - pdfBounds.Top) * scale;

                // Extend width to cover the whole line
                var screenRect = new Avalonia.Rect(
                    pdfBounds.Left * scale - 5,
                    imageY - 5,
                    400 * scale, // Wide enough for whole line
                    (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
                );

                pendingAreas.Add(screenRect);
                _output.WriteLine($"Marked: {word.Text} line");
            }
        }

        pendingAreas.Should().HaveCount(3, "Should mark 3 lines for redaction");
        _output.WriteLine($"Pending redactions: {pendingAreas.Count}");

        // Apply all at once
        var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        foreach (var area in pendingAreas)
        {
            _redactionService.RedactArea(document.Pages[0], area, inputPath, renderDpi: RenderDpi);
        }

        var outputPath = CreateTempPath("batch_redacted.pdf");
        document.Save(outputPath);
        document.Dispose();

        // Verify
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        _output.WriteLine($"\nText after batch redaction:\n{textAfter}");

        // Redacted content should be gone
        textAfter.Should().NotContain("John Smith", "Name should be redacted");
        textAfter.Should().NotContain("123-45-6789", "SSN should be redacted");
        textAfter.Should().NotContain("01/15/1980", "DOB should be redacted");

        // Kept content should remain
        textAfter.Should().Contain("Address", "Address line should be preserved");
        textAfter.Should().Contain("Phone", "Phone line should be preserved");
        textAfter.Should().Contain("KEEP", "KEEP text should be preserved");

        _output.WriteLine("\n✓ SUCCESS: Batch redaction completed");
    }

    /// <summary>
    /// Error handling - verify graceful handling of empty areas.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Feature", "ErrorHandling")]
    public void ErrorHandling_EmptyRedactionArea_NoException()
    {
        _output.WriteLine("\n=== TEST: Error Handling - Empty Area ===");

        var inputPath = CreateTempPath("empty_area.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Test content");

        var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

        // Redact an empty area (should not throw)
        var emptyArea = new Avalonia.Rect(1000, 1000, 10, 10); // Far from any content
        Action act = () => _redactionService.RedactArea(document.Pages[0], emptyArea, inputPath, renderDpi: RenderDpi);

        act.Should().NotThrow("Redacting empty area should not throw");

        var outputPath = CreateTempPath("empty_area_redacted.pdf");
        document.Save(outputPath);
        document.Dispose();

        // Original content should still be present
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().Contain("Test content", "Original content should remain");

        _output.WriteLine("\n✓ SUCCESS: Empty area handled gracefully");
    }
}
