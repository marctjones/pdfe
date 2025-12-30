using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using Avalonia;
using System.IO;
using System.Text;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests that verify precise redaction behavior:
/// 1. Content beneath black box is REMOVED
/// 2. Content outside black box is PRESERVED
/// 3. No false positives (content incorrectly removed)
/// 4. No false negatives (content that should be removed but isn't)
/// </summary>
public class PreciseRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;

    public PreciseRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, loggerFactory);
    }

    #region Core "Beneath the Black Box" Tests

    /// <summary>
    /// PRIMARY TEST: Verify that ONLY content visually blocked by the black box is removed.
    /// Creates a grid of text items and redacts specific cells, verifying:
    /// - All text in redacted cells is removed
    /// - All text outside redacted cells is preserved
    /// </summary>
    [Fact]
    public void RedactArea_ShouldOnlyRemoveContentBeneathBlackBox()
    {
        _output.WriteLine("=== TEST: RedactArea_ShouldOnlyRemoveContentBeneathBlackBox ===");

        // Arrange - Create PDF with text at known grid positions
        var testPdf = CreateTempPath("grid_test.pdf");
        var contentMap = CreateGridTestPdf(testPdf);
        _tempFiles.Add(testPdf);

        _output.WriteLine("Created PDF with content at positions:");
        foreach (var item in contentMap)
        {
            _output.WriteLine($"  '{item.Key}' at ({item.Value.x}, {item.Value.y})");
        }

        // Verify all content exists before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        foreach (var item in contentMap)
        {
            textBefore.Should().Contain(item.Key, $"'{item.Key}' should exist before redaction");
        }

        // Act - Redact specific area (Row 2, covering "Row2_A" and "Row2_B")
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redaction area covers Row2 items at Y=200
        // Text is drawn with baseline at Y=200, use redaction at Y=190
        var redactionArea = new Rect(90, 190, 300, 30);
        _output.WriteLine($"\nApplying redaction at: ({redactionArea.X}, {redactionArea.Y}, {redactionArea.Width}x{redactionArea.Height})");

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("grid_test_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Check what was removed and what was preserved
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine("\nVerification results:");

        // Items that SHOULD be removed (beneath black box)
        var shouldBeRemoved = new[] { "Row2_A", "Row2_B" };
        foreach (var item in shouldBeRemoved)
        {
            var wasRemoved = !textAfter.Contains(item);
            _output.WriteLine($"  '{item}' removed: {wasRemoved}");
            textAfter.Should().NotContain(item,
                $"'{item}' is beneath black box and must be REMOVED");
        }

        // Items that SHOULD be preserved (outside black box)
        var shouldBePreserved = new[] { "Row1_A", "Row1_B", "Row3_A", "Row3_B" };
        foreach (var item in shouldBePreserved)
        {
            var wasPreserved = textAfter.Contains(item);
            _output.WriteLine($"  '{item}' preserved: {wasPreserved}");
            textAfter.Should().Contain(item,
                $"'{item}' is outside black box and must be PRESERVED");
        }

        _output.WriteLine("\n=== PASSED: Only content beneath black box was removed ===");
    }

    /// <summary>
    /// Test that verifies NO false positives - content near but outside redaction is preserved
    /// </summary>
    [Fact]
    public void RedactArea_ShouldNotRemoveContentOutsideArea()
    {
        _output.WriteLine("=== TEST: RedactArea_ShouldNotRemoveContentOutsideArea ===");

        // Arrange - Create PDF with text very close to redaction boundaries
        var testPdf = CreateTempPath("boundary_precision_test.pdf");
        CreateBoundaryTestPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Redact the center area only
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Very precise redaction in the center - text at Y=220, so use Y=210
        var redactionArea = new Rect(200, 210, 150, 30);
        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("boundary_precision_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        // Center content should be removed
        textAfter.Should().NotContain("CENTER_TARGET",
            "Content in redaction area must be removed");

        // Boundary content should be preserved
        textAfter.Should().Contain("TOP_OUTSIDE",
            "Content above redaction must be preserved");
        textAfter.Should().Contain("BOTTOM_OUTSIDE",
            "Content below redaction must be preserved");
        textAfter.Should().Contain("LEFT_OUTSIDE",
            "Content left of redaction must be preserved");
        textAfter.Should().Contain("RIGHT_OUTSIDE",
            "Content right of redaction must be preserved");

        _output.WriteLine("=== PASSED: No false positives ===");
    }

    /// <summary>
    /// Test that content partially overlapping with redaction area IS removed
    /// (any overlap = removal, not just complete containment)
    /// </summary>
    [Fact]
    public void RedactArea_ShouldRemovePartiallyOverlappingContent()
    {
        _output.WriteLine("=== TEST: RedactArea_ShouldRemovePartiallyOverlappingContent ===");

        // Arrange
        var testPdf = CreateTempPath("partial_overlap_test.pdf");
        CreateOverlapTestPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Redact area that partially overlaps with text
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // This area partially overlaps with "PARTIALLY_COVERED" at Y=100
        var redactionArea = new Rect(150, 90, 100, 30);
        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("partial_overlap_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Partially overlapping content should be removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("PARTIALLY_COVERED",
            "Partially overlapping content must be removed for security");

        _output.WriteLine("=== PASSED: Partially overlapping content removed ===");
    }

    #endregion

    #region Multi-Page Tests

    /// <summary>
    /// Verify redaction on one page doesn't affect other pages
    /// </summary>
    [Fact]
    public void RedactOnPage_ShouldNotAffectOtherPages()
    {
        _output.WriteLine("=== TEST: RedactOnPage_ShouldNotAffectOtherPages ===");

        // Arrange - Create multi-page PDF
        var testPdf = CreateTempPath("multipage_test.pdf");
        TestPdfGenerator.CreateMultiPagePdf(testPdf, pageCount: 3);
        _tempFiles.Add(testPdf);

        // Verify content on all pages before
        for (int i = 0; i < 3; i++)
        {
            var pageText = PdfTestHelpers.ExtractTextFromPage(testPdf, i);
            pageText.Should().Contain($"Page {i + 1} Content");
        }

        // Act - Redact only on page 2 - text at Y=100
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page2 = document.Pages[1];
        _redactionService.RedactArea(page2, new Rect(90, 90, 200, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("multipage_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Only page 2 should be affected
        var page1Text = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 0);
        var page2Text = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 1);
        var page3Text = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 2);

        page1Text.Should().Contain("Page 1 Content",
            "Page 1 should be unaffected");
        page2Text.Should().NotContain("Page 2 Content",
            "Page 2 content should be redacted");
        page3Text.Should().Contain("Page 3 Content",
            "Page 3 should be unaffected");

        _output.WriteLine("=== PASSED: Other pages unaffected ===");
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Redacting empty area should not corrupt PDF
    /// </summary>
    [Fact]
    public void RedactEmptyArea_ShouldNotCorruptPdf()
    {
        // Arrange
        var testPdf = CreateTempPath("empty_area_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "PRESERVE_THIS");
        _tempFiles.Add(testPdf);

        // Act - Redact area with no content
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(400, 400, 100, 50));

        var redactedPdf = CreateTempPath("empty_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().Contain("PRESERVE_THIS",
            "Content outside empty redaction area must be preserved");
    }

    /// <summary>
    /// Very small redaction area should still work
    /// </summary>
    [Fact]
    public void RedactSmallArea_ShouldStillRemoveContent()
    {
        // Arrange
        var testPdf = CreateTempPath("small_area_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "TINY");
        _tempFiles.Add(testPdf);

        // Act - Very small redaction
        // Text at Y=100, use redaction at Y=90
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(95, 90, 60, 25), renderDpi: 72); // Small area covering text body

        var redactedPdf = CreateTempPath("small_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("TINY",
            "Even small redaction areas should remove content");
    }

    /// <summary>
    /// Large redaction area (covering most of page) should work
    /// </summary>
    [Fact]
    public void RedactLargeArea_ShouldRemoveAllContent()
    {
        // Arrange
        var testPdf = CreateTempPath("large_area_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Large redaction covering most of page
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(0, 0, 600, 500), renderDpi: 72);

        var redactedPdf = CreateTempPath("large_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("CONFIDENTIAL");
        textAfter.Should().NotContain("Public Information");
        textAfter.Should().NotContain("Secret Data");
        textAfter.Should().NotContain("Normal Text");
    }

    #endregion

    #region Helper Methods - PDF Generation

    private Dictionary<string, (double x, double y)> CreateGridTestPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        var contentMap = new Dictionary<string, (double, double)>();

        // Row 1 - y = 100
        contentMap["Row1_A"] = (100, 100);
        contentMap["Row1_B"] = (250, 100);

        // Row 2 - y = 200 (this row will be redacted)
        contentMap["Row2_A"] = (100, 200);
        contentMap["Row2_B"] = (250, 200);

        // Row 3 - y = 300
        contentMap["Row3_A"] = (100, 300);
        contentMap["Row3_B"] = (250, 300);

        foreach (var item in contentMap)
        {
            gfx.DrawString(item.Key, font, XBrushes.Black,
                new XPoint(item.Value.Item1, item.Value.Item2));
        }

        document.Save(outputPath);
        document.Dispose();

        return contentMap;
    }

    private void CreateBoundaryTestPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);

        // Center target (will be redacted) - at Y=220, baseline is at 220, text extends to ~210
        gfx.DrawString("CENTER_TARGET", font, XBrushes.Black, new XPoint(210, 220));

        // Surrounding content (should be preserved) - further apart to avoid overlap
        gfx.DrawString("TOP_OUTSIDE", font, XBrushes.Black, new XPoint(210, 160));
        gfx.DrawString("BOTTOM_OUTSIDE", font, XBrushes.Black, new XPoint(210, 300));
        gfx.DrawString("LEFT_OUTSIDE", font, XBrushes.Black, new XPoint(50, 220));
        gfx.DrawString("RIGHT_OUTSIDE", font, XBrushes.Black, new XPoint(400, 220));

        document.Save(outputPath);
        document.Dispose();
    }

    private void CreateOverlapTestPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        // Text that will be partially covered by redaction
        gfx.DrawString("PARTIALLY_COVERED", font, XBrushes.Black, new XPoint(100, 100));

        // Text that won't be covered
        gfx.DrawString("NOT_COVERED", font, XBrushes.Black, new XPoint(100, 200));

        document.Save(outputPath);
        document.Dispose();
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PreciseRedactionTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            TestPdfGenerator.CleanupTestFile(file);
        }
    }

    #endregion
}
