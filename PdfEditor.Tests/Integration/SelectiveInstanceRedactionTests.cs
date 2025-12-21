using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// CRITICAL TESTS: Verify that redaction removes ONLY the specific instance of text
/// in the selected area, NOT all instances of that text throughout the PDF.
///
/// These tests ensure that when verifying "text is removed", we're actually checking
/// the correct area - preventing false positives where we check the wrong coordinates.
/// </summary>
[Collection("Sequential")]
public class SelectiveInstanceRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;

    public SelectiveInstanceRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"SelectiveRedactionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _redactionService = new RedactionService(
            NullLogger<RedactionService>.Instance,
            NullLoggerFactory.Instance);
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
    /// CRITICAL: When the same word appears TWICE, redacting ONE instance should
    /// remove that instance but PRESERVE the other instance.
    ///
    /// This test ensures we're checking the RIGHT coordinates, not just searching
    /// the whole file for the text string.
    /// </summary>
    [Fact]
    public void RedactOneInstance_SameTextAppearsTwice_OnlySelectedInstanceRemoved()
    {
        _output.WriteLine("\n=== CRITICAL TEST: Selective Instance Redaction ===");
        _output.WriteLine("Same word appears TWICE - redact only ONE instance");

        // Arrange: Create PDF with "SECRET" appearing at TWO different locations
        var pdfPath = CreateTempPath("duplicate_text.pdf");
        _tempFiles.Add(pdfPath);

        using (var doc = new PdfSharp.Pdf.PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = new XUnit(612);
            page.Height = new XUnit(792);

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var font = new XFont("Arial", 14);
                // First instance at top of page
                gfx.DrawString("SECRET data at top", font, XBrushes.Black, 100, 100);
                // Second instance at middle of page
                gfx.DrawString("SECRET data at middle", font, XBrushes.Black, 100, 400);
            }

            doc.Save(pdfPath);
        }

        _output.WriteLine("Created PDF with 'SECRET' appearing TWICE at different positions");

        // Verify both instances exist before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(pdfPath);
        var secretCount = System.Text.RegularExpressions.Regex.Matches(textBefore, "SECRET").Count;
        secretCount.Should().Be(2, "PDF should contain 'SECRET' exactly TWICE before redaction");
        _output.WriteLine($"Confirmed: 'SECRET' appears {secretCount} times before redaction");

        // Act: Redact ONLY the first instance (at top of page, Avalonia Y~100)
        using (var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
        {
            var page = doc.Pages[0];

            // Redaction area covering first instance only (Y=85 to 115 in Avalonia coords)
            var redactionArea = new Rect(90, 85, 100, 20);
            _output.WriteLine($"Redacting area: ({redactionArea.X}, {redactionArea.Y}, {redactionArea.Width}x{redactionArea.Height})");
            _output.WriteLine("This should cover ONLY the first 'SECRET' (at top), NOT the second one (at middle)");

            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

            var redactedPath = CreateTempPath("duplicate_text_redacted.pdf");
            _tempFiles.Add(redactedPath);
            doc.Save(redactedPath);
        }

        // Assert: Verify specific instance removal using PdfPig positional extraction
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(CreateTempPath("duplicate_text_redacted.pdf")))
        {
            var page = pdfPigDoc.GetPage(1);
            var words = page.GetWords().ToList();

            _output.WriteLine($"\nWords found after redaction:");
            foreach (var word in words)
            {
                var avaloniaY = page.Height - word.BoundingBox.Top;
                _output.WriteLine($"  '{word.Text}' at Avalonia Y={avaloniaY:F2}");
            }

            // Check for "SECRET" instances
            var secretWords = words.Where(w => w.Text.Contains("SECRET")).ToList();

            _output.WriteLine($"\n'SECRET' instances found: {secretWords.Count}");

            // CRITICAL ASSERTION: Should find exactly ONE "SECRET" remaining
            secretWords.Count.Should().Be(1,
                "CRITICAL: Exactly ONE 'SECRET' should remain (the one NOT in the redaction area). " +
                "If this fails, either (1) we redacted the wrong instance due to coordinate bug, or " +
                "(2) we redacted both instances when we should have only redacted one!");

            if (secretWords.Count == 1)
            {
                var remainingSecret = secretWords[0];
                var remainingAvaloniaY = page.Height - remainingSecret.BoundingBox.Top;

                _output.WriteLine($"Remaining 'SECRET' is at Avalonia Y={remainingAvaloniaY:F2}");

                // The remaining instance should be the one at middle (Avalonia Y~400)
                remainingAvaloniaY.Should().BeInRange(380, 420,
                    "The remaining 'SECRET' should be the one at MIDDLE of page (Y~400), " +
                    "NOT the one at top (Y~100) that we redacted");

                _output.WriteLine("✓ VERIFIED: Correct instance was redacted, other instance preserved");
            }
        }

        // Double-check with whole-file text extraction
        var textAfter = PdfTestHelpers.ExtractAllText(CreateTempPath("duplicate_text_redacted.pdf"));
        var secretCountAfter = System.Text.RegularExpressions.Regex.Matches(textAfter, "SECRET").Count;
        secretCountAfter.Should().Be(1,
            "Whole-file extraction should find exactly ONE 'SECRET' remaining");

        _output.WriteLine("\n✓ SUCCESS: Selective instance redaction works correctly");
        _output.WriteLine("  - Started with 2 instances of 'SECRET'");
        _output.WriteLine("  - Redacted 1 specific instance");
        _output.WriteLine("  - 1 instance remains (the correct one)");
    }

    /// <summary>
    /// Test with THREE instances of the same text, redact the MIDDLE one only.
    /// This tests coordinate accuracy even more rigorously.
    /// </summary>
    [Fact]
    public void RedactMiddleInstance_ThreeInstances_OnlyMiddleRemoved()
    {
        _output.WriteLine("\n=== TEST: Redact Middle Instance of Three ===");

        // Arrange
        var pdfPath = CreateTempPath("three_instances.pdf");
        _tempFiles.Add(pdfPath);

        using (var doc = new PdfSharp.Pdf.PdfDocument())
        {
            var page = doc.AddPage();
            page.Width = new XUnit(612);
            page.Height = new XUnit(792);

            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var font = new XFont("Arial", 14);
                gfx.DrawString("REDACT at top", font, XBrushes.Black, 100, 100);     // Y~100
                gfx.DrawString("REDACT at middle", font, XBrushes.Black, 100, 400);  // Y~400
                gfx.DrawString("REDACT at bottom", font, XBrushes.Black, 100, 700);  // Y~700
            }

            doc.Save(pdfPath);
        }

        // Verify all three exist
        var textBefore = PdfTestHelpers.ExtractAllText(pdfPath);
        var countBefore = System.Text.RegularExpressions.Regex.Matches(textBefore, "REDACT").Count;
        countBefore.Should().Be(3, "Should have three instances before redaction");

        // Act: Redact ONLY the middle instance (Y~400)
        using (var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
        {
            var redactionArea = new Rect(90, 385, 110, 20);  // Covers Y=385-405
            _output.WriteLine($"Redacting middle instance at Y~400");
            _redactionService.RedactArea(doc.Pages[0], redactionArea, renderDpi: 72);

            var redactedPath = CreateTempPath("three_instances_redacted.pdf");
            _tempFiles.Add(redactedPath);
            doc.Save(redactedPath);
        }

        // Assert: Should have exactly TWO instances remaining
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(CreateTempPath("three_instances_redacted.pdf")))
        {
            var page = pdfPigDoc.GetPage(1);
            var redactWords = page.GetWords().Where(w => w.Text.Contains("REDACT")).ToList();

            redactWords.Count.Should().Be(2,
                "Should have exactly TWO 'REDACT' instances remaining (top and bottom)");

            // Verify the remaining instances are at correct positions
            var positions = redactWords.Select(w => page.Height - w.BoundingBox.Top).OrderBy(y => y).ToList();

            positions[0].Should().BeInRange(80, 120, "First remaining instance should be at top (Y~100)");
            positions[1].Should().BeInRange(680, 720, "Second remaining instance should be at bottom (Y~700)");

            _output.WriteLine($"✓ Remaining instances at Y={positions[0]:F2} and Y={positions[1]:F2}");
            _output.WriteLine("✓ Middle instance (Y~400) was correctly removed");
        }
    }

    /// <summary>
    /// Test with the word appearing on DIFFERENT PAGES - redact on one page only.
    /// </summary>
    [Fact]
    public void RedactOnePage_SameTextOnMultiplePages_OtherPagesPreserved()
    {
        _output.WriteLine("\n=== TEST: Multi-Page Selective Redaction ===");

        // Arrange
        var pdfPath = CreateTempPath("multipage_duplicate.pdf");
        _tempFiles.Add(pdfPath);

        using (var doc = new PdfSharp.Pdf.PdfDocument())
        {
            // Page 1: Has "CONFIDENTIAL"
            var page1 = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page1))
            {
                gfx.DrawString("Page 1 CONFIDENTIAL data", new XFont("Arial", 14), XBrushes.Black, 100, 100);
            }

            // Page 2: Also has "CONFIDENTIAL"
            var page2 = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page2))
            {
                gfx.DrawString("Page 2 CONFIDENTIAL info", new XFont("Arial", 14), XBrushes.Black, 100, 100);
            }

            // Page 3: Also has "CONFIDENTIAL"
            var page3 = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page3))
            {
                gfx.DrawString("Page 3 CONFIDENTIAL record", new XFont("Arial", 14), XBrushes.Black, 100, 100);
            }

            doc.Save(pdfPath);
        }

        // Verify all three pages have the text
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            for (int i = 1; i <= 3; i++)
            {
                var pageText = pdfPigDoc.GetPage(i).Text;
                pageText.Should().Contain("CONFIDENTIAL", $"Page {i} should contain CONFIDENTIAL before redaction");
            }
        }

        // Act: Redact ONLY on page 2
        using (var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify))
        {
            var redactionArea = new Rect(90, 85, 150, 20);
            _output.WriteLine("Redacting CONFIDENTIAL on PAGE 2 ONLY");
            _redactionService.RedactArea(doc.Pages[1], redactionArea, renderDpi: 72);  // Page 2 (index 1)

            var redactedPath = CreateTempPath("multipage_duplicate_redacted.pdf");
            _tempFiles.Add(redactedPath);
            doc.Save(redactedPath);
        }

        // Assert: Pages 1 and 3 should still have "CONFIDENTIAL", page 2 should not
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(CreateTempPath("multipage_duplicate_redacted.pdf")))
        {
            var page1Text = pdfPigDoc.GetPage(1).Text;
            page1Text.Should().Contain("CONFIDENTIAL", "Page 1 should PRESERVE 'CONFIDENTIAL' (not redacted)");

            var page2Text = pdfPigDoc.GetPage(2).Text;
            page2Text.Should().NotContain("CONFIDENTIAL", "Page 2 should REMOVE 'CONFIDENTIAL' (redacted)");

            var page3Text = pdfPigDoc.GetPage(3).Text;
            page3Text.Should().Contain("CONFIDENTIAL", "Page 3 should PRESERVE 'CONFIDENTIAL' (not redacted)");

            _output.WriteLine("✓ Page 1: CONFIDENTIAL preserved");
            _output.WriteLine("✓ Page 2: CONFIDENTIAL removed");
            _output.WriteLine("✓ Page 3: CONFIDENTIAL preserved");
        }
    }

}
