using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Avalonia;
using System.IO;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Comprehensive redaction tests that verify:
/// 1. Content under black boxes is removed from the PDF structure
/// 2. Content outside black boxes is preserved
/// 3. Random redaction locations work correctly
/// 4. Various content types (text, graphics) are properly redacted
/// </summary>
public class ComprehensiveRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly Random _random;

    public ComprehensiveRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, loggerFactory);
        _random = new Random(42); // Fixed seed for reproducible tests
    }

    [Fact]
    public void RedactMappedContent_ShouldRemoveOnlyTargetedItems()
    {
        // Arrange
        _output.WriteLine("Test: Comprehensive content removal with mapped positions");

        var testPdf = CreateTempPath("mapped_content_test.pdf");
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        _output.WriteLine($"Created PDF with {contentMap.Count} mapped content items");

        // Verify all content exists before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before redaction:\n{textBefore}");

        textBefore.Should().Contain("CONFIDENTIAL");
        textBefore.Should().Contain("PUBLIC");
        textBefore.Should().Contain("SECRET");
        textBefore.Should().Contain("PRIVATE");
        textBefore.Should().Contain("INTERNAL");

        // Act - Redact only "CONFIDENTIAL" and "SECRET"
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact CONFIDENTIAL
        var confidentialPos = contentMap["CONFIDENTIAL"];
        var redactionArea1 = new Rect(
            confidentialPos.x - 5,
            confidentialPos.y - 5,
            confidentialPos.width + 10,
            confidentialPos.height + 10
        );
        _output.WriteLine($"Redacting CONFIDENTIAL at: {redactionArea1}");
        _redactionService.RedactArea(page, redactionArea1, renderDpi: 72);

        // Redact SECRET
        var secretPos = contentMap["SECRET"];
        var redactionArea2 = new Rect(
            secretPos.x - 5,
            secretPos.y - 5,
            secretPos.width + 10,
            secretPos.height + 10
        );
        _output.WriteLine($"Redacting SECRET at: {redactionArea2}");
        _redactionService.RedactArea(page, redactionArea2, renderDpi: 72);

        var redactedPdf = CreateTempPath("mapped_content_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Verify targeted content is removed, others preserved
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after redaction:\n{textAfter}");

        // Content under black boxes should be REMOVED
        textAfter.Should().NotContain("CONFIDENTIAL",
            "CONFIDENTIAL should be removed from PDF structure");
        textAfter.Should().NotContain("SECRET",
            "SECRET should be removed from PDF structure");

        // Content NOT under black boxes should be PRESERVED
        textAfter.Should().Contain("PUBLIC",
            "PUBLIC should be preserved");
        textAfter.Should().Contain("PRIVATE",
            "PRIVATE should be preserved");
        textAfter.Should().Contain("INTERNAL",
            "INTERNAL should be preserved");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF should remain valid after redaction");

        _output.WriteLine("✓ Test passed: Targeted content removed, other content preserved");
    }

    [Fact]
    public void RedactRandomAreas_ShouldOnlyRemoveIntersectingContent()
    {
        // Arrange
        _output.WriteLine("Test: Random area redaction with content preservation");

        var testPdf = CreateTempPath("random_redaction_test.pdf");
        TestPdfGenerator.CreateGridContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before redaction (excerpt):\n{textBefore.Substring(0, Math.Min(200, textBefore.Length))}...");

        // Act - Redact 3 random areas
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var redactionAreas = new List<Rect>
        {
            // Random area 1: Around position (200, 200)
            new Rect(180, 180, 80, 60),

            // Random area 2: Around position (400, 400)
            new Rect(380, 380, 100, 80),

            // Random area 3: Around position (300, 600)
            new Rect(280, 580, 90, 70)
        };

        foreach (var area in redactionAreas)
        {
            _output.WriteLine($"Redacting random area: {area}");
            _redactionService.RedactArea(page, area, renderDpi: 72);
        }

        var redactedPdf = CreateTempPath("random_redaction_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Verify some content was removed, but not all
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after redaction (excerpt):\n{textAfter.Substring(0, Math.Min(200, textAfter.Length))}...");

        // The text should be different (some removed)
        textAfter.Should().NotBe(textBefore,
            "some content should be removed");

        // But not everything should be removed
        textAfter.Length.Should().BeGreaterThan(0,
            "some content should remain");

        // Content outside redacted areas should still be present
        // We know Cell(100,100) is outside all redaction areas
        textAfter.Should().Contain("Cell(100,100)",
            "content outside redaction areas should be preserved");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF should remain valid");

        _output.WriteLine("✓ Test passed: Random area redaction working correctly");
    }

    [Fact]
    public void RedactComplexDocument_ShouldRemoveSensitiveDataOnly()
    {
        // Arrange
        _output.WriteLine("Test: Complex document redaction");

        var testPdf = CreateTempPath("complex_doc_test.pdf");
        TestPdfGenerator.CreateComplexContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before redaction:\n{textBefore}");

        // Verify sensitive data exists
        textBefore.Should().Contain("1234-5678-9012-3456");
        textBefore.Should().Contain("123-45-6789");
        textBefore.Should().Contain("SuperSecret123!");
        textBefore.Should().Contain("ACME Corporation");

        // Act - Redact the sensitive data section (account, SSN, password)
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact the sensitive data box (approximately x=50-550, y=240-360)
        var sensitiveDataArea = new Rect(50, 280, 500, 90);
        _output.WriteLine($"Redacting sensitive data area: {sensitiveDataArea}");
        _redactionService.RedactArea(page, sensitiveDataArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("complex_doc_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after redaction:\n{textAfter}");

        // Sensitive data should be REMOVED
        textAfter.Should().NotContain("1234-5678-9012-3456",
            "account number should be removed");
        textAfter.Should().NotContain("123-45-6789",
            "SSN should be removed");
        textAfter.Should().NotContain("SuperSecret123!",
            "password should be removed");

        // Public information should be PRESERVED
        textAfter.Should().Contain("ACME Corporation",
            "public company name should be preserved");
        textAfter.Should().Contain("PUBLIC INFORMATION",
            "public section should be preserved");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine("✓ Test passed: Sensitive data removed, public info preserved");
    }

    [Fact]
    public void RedactMultipleRandomAreas_ShouldMaintainDocumentIntegrity()
    {
        // Arrange
        _output.WriteLine("Test: Multiple random redactions maintain document integrity");

        var testPdf = CreateTempPath("multiple_random_test.pdf");
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        var wordsBefore = textBefore.Split(new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;
        _output.WriteLine($"Words before redaction: {wordsBefore}");

        // Act - Apply multiple random redactions
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Generate random redaction areas
        var numRedactions = 3;
        var redactionAreas = new List<Rect>();

        for (int i = 0; i < numRedactions; i++)
        {
            var x = _random.Next(50, 400);
            var y = _random.Next(50, 600);
            var width = _random.Next(50, 150);
            var height = _random.Next(30, 80);

            var area = new Rect(x, y, width, height);
            redactionAreas.Add(area);

            _output.WriteLine($"Random redaction {i + 1}: {area}");
            _redactionService.RedactArea(page, area, renderDpi: 72);
        }

        var redactedPdf = CreateTempPath("multiple_random_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        var wordsAfter = textAfter.Split(new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries).Length;
        _output.WriteLine($"Words after redaction: {wordsAfter}");

        // Some content should be removed
        wordsAfter.Should().BeLessThan(wordsBefore,
            "some content should be removed by redaction");

        // But document should still be valid
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "document should remain valid after multiple redactions");

        // Document should still have some content
        wordsAfter.Should().BeGreaterThan(0,
            "document should still contain some content");

        _output.WriteLine($"✓ Test passed: {wordsBefore - wordsAfter} words removed, document integrity maintained");
    }

    [Fact]
    public void RedactEntireContent_ShouldRemoveAllTextButMaintainStructure()
    {
        // Arrange
        _output.WriteLine("Test: Redacting entire page content");

        var testPdf = CreateTempPath("full_redaction_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "This should be completely redacted");
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before: {textBefore}");
        textBefore.Should().Contain("completely redacted");

        // Act - Redact entire page
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Large area covering entire page
        var fullPageArea = new Rect(0, 0, page.Width.Point, page.Height.Point);
        _output.WriteLine($"Redacting entire page: {fullPageArea}");
        _redactionService.RedactArea(page, fullPageArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("full_redaction_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after: '{textAfter.Trim()}'");

        // All text should be removed
        textAfter.Should().NotContain("completely redacted",
            "all text should be removed");

        // PDF structure should be maintained
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF structure should be valid");

        PdfTestHelpers.GetPageCount(redactedPdf).Should().Be(1,
            "page should still exist");

        _output.WriteLine("✓ Test passed: All content removed, structure maintained");
    }

    [Fact]
    public void RedactWithPreciseCoordinates_ShouldRemoveExactContent()
    {
        // Arrange
        _output.WriteLine("Test: Precise coordinate-based redaction");

        var testPdf = CreateTempPath("precise_redaction_test.pdf");
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Redact each item precisely using its mapped position
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var itemsToRedact = new[] { "CONFIDENTIAL", "SECRET", "PRIVATE" };

        foreach (var item in itemsToRedact)
        {
            var pos = contentMap[item];
            var area = new Rect(pos.x - 2, pos.y - 2, pos.width + 4, pos.height + 4);
            _output.WriteLine($"Precisely redacting '{item}' at {area}");
            _redactionService.RedactArea(page, area, renderDpi: 72);
        }

        var redactedPdf = CreateTempPath("precise_redaction_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after precise redaction:\n{textAfter}");

        // Precisely redacted items should be REMOVED
        foreach (var item in itemsToRedact)
        {
            textAfter.Should().NotContain(item,
                $"{item} should be removed by precise redaction");
        }

        // Non-redacted items should be PRESERVED
        textAfter.Should().Contain("PUBLIC", "PUBLIC should be preserved");
        textAfter.Should().Contain("INTERNAL", "INTERNAL should be preserved");

        _output.WriteLine("✓ Test passed: Precise redaction removes exact content");
    }

    [Theory]
    [InlineData(72)]   // Standard PDF DPI
    [InlineData(150)]  // Common rendering DPI
    [InlineData(300)]  // High quality DPI
    public void RedactAtVariousDPI_ShouldWorkCorrectly(int renderDpi)
    {
        // Arrange
        _output.WriteLine($"Test: Redaction at {renderDpi} DPI");

        var testPdf = CreateTempPath($"dpi_test_{renderDpi}.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "DPI TEST CONTENT");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact at specified DPI
        var redactionArea = new Rect(90, 90, 150, 30);
        _redactionService.RedactArea(page, redactionArea, renderDpi: renderDpi);

        var redactedPdf = CreateTempPath($"dpi_test_{renderDpi}_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after redaction at {renderDpi} DPI: '{textAfter}'");

        textAfter.Should().NotContain("DPI TEST CONTENT",
            $"content should be removed at {renderDpi} DPI");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine($"✓ Test passed: Redaction works at {renderDpi} DPI");
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PdfEditorTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    public void Dispose()
    {
        // Cleanup temp files
        foreach (var file in _tempFiles)
        {
            TestPdfGenerator.CleanupTestFile(file);
        }
    }
}
