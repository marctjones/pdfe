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
/// Black Box Redaction Tests
///
/// These tests validate the core requirement:
/// 1. Generate a PDF with visual objects and text
/// 2. Add black boxes over random locations
/// 3. Remove content from the document underneath the black boxes
/// 4. Verify that the underlying content including text is removed
/// 5. Verify that content NOT under black boxes is preserved
///
/// All tests are UI-independent and verify actual content removal from the PDF structure.
/// </summary>
public class BlackBoxRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly Random _random;

    public BlackBoxRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, loggerFactory);
        _random = new Random(12345); // Fixed seed for reproducibility
    }

    [Fact]
    public void GeneratePDF_ApplyBlackBox_VerifyContentRemoval()
    {
        // STEP 1: Generate a PDF with visual objects and text
        _output.WriteLine("STEP 1: Generating PDF with visual objects and text");

        var testPdf = CreateTempPath("blackbox_test_original.pdf");
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        _output.WriteLine($"Generated PDF at: {pdfPath}");
        _output.WriteLine($"Content items: {string.Join(", ", contentMap.Keys)}");

        // STEP 2: Verify initial content exists
        _output.WriteLine("\nSTEP 2: Verifying initial content");

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        var wordsBefore = PdfTestHelpers.GetAllUniqueWords(testPdf);

        _output.WriteLine($"Total unique words before: {wordsBefore.Count}");
        _output.WriteLine($"Content before redaction:\n{textBefore}");

        // Verify all expected content exists
        PdfTestHelpers.ContainsAllText(testPdf, "CONFIDENTIAL", "PUBLIC", "SECRET", "PRIVATE", "INTERNAL")
            .Should().BeTrue("all content should exist before redaction");

        // STEP 3: Add black boxes over random locations
        _output.WriteLine("\nSTEP 3: Adding black boxes over random locations");

        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Select random content items to redact
        var itemsToRedact = SelectRandomItems(contentMap.Keys.ToList(), 3);
        var redactionAreas = new List<Rect>();

        _output.WriteLine($"Randomly selected items to redact: {string.Join(", ", itemsToRedact)}");

        foreach (var item in itemsToRedact)
        {
            var pos = contentMap[item];
            // Add some padding around the content
            var redactionArea = new Rect(
                pos.x - 5,
                pos.y - 5,
                pos.width + 10,
                pos.height + 10
            );
            redactionAreas.Add(redactionArea);

            _output.WriteLine($"  Black box for '{item}': X={redactionArea.X:F1}, Y={redactionArea.Y:F1}, " +
                            $"W={redactionArea.Width:F1}, H={redactionArea.Height:F1}");

            // STEP 4: Remove content underneath the black box
            _output.WriteLine($"  Removing content under black box for '{item}'");
            _redactionService.RedactArea(page, redactionArea, testPdf, renderDpi: 72);
        }

        // Save the redacted PDF
        var redactedPdf = CreateTempPath("blackbox_test_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        _output.WriteLine($"\nRedacted PDF saved to: {redactedPdf}");

        // STEP 5: Verify that content under black boxes is REMOVED
        _output.WriteLine("\nSTEP 5: Verifying content under black boxes is REMOVED");

        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        var wordsAfter = PdfTestHelpers.GetAllUniqueWords(redactedPdf);

        _output.WriteLine($"Total unique words after: {wordsAfter.Count}");
        _output.WriteLine($"Content after redaction:\n{textAfter}");

        // Verify redacted content is removed from PDF structure
        foreach (var item in itemsToRedact)
        {
            if (!item.Contains("BOX")) // Skip graphics items, check only text
            {
                textAfter.Should().NotContain(item,
                    $"'{item}' should be removed from PDF structure (not just visually hidden)");
                _output.WriteLine($"  ✓ Verified '{item}' is REMOVED from document");
            }
        }

        // STEP 6: Verify that content NOT under black boxes is PRESERVED
        _output.WriteLine("\nSTEP 6: Verifying content NOT under black boxes is PRESERVED");

        var itemsNotRedacted = contentMap.Keys.Except(itemsToRedact).Where(k => !k.Contains("BOX")).ToList();

        foreach (var item in itemsNotRedacted)
        {
            textAfter.Should().Contain(item,
                $"'{item}' should be preserved (it was NOT under a black box)");
            _output.WriteLine($"  ✓ Verified '{item}' is PRESERVED");
        }

        // Additional verification: text content should be shorter after redaction
        textAfter.Length.Should().BeLessThan(textBefore.Length,
            "total text length should decrease after redaction");

        _output.WriteLine($"\n  Characters removed: {textBefore.Length - textAfter.Length}");

        // PDF should remain valid
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF should remain structurally valid after redaction");

        _output.WriteLine("\n✓ TEST PASSED: Content under black boxes removed, other content preserved");
    }

    [Fact]
    public void MultipleBlackBoxes_RandomPositions_VerifySelectiveRemoval()
    {
        // Generate PDF with grid content
        _output.WriteLine("Generating PDF with grid content");
        var testPdf = CreateTempPath("multi_blackbox_test.pdf");
        TestPdfGenerator.CreateGridContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Content before:\n{textBefore.Substring(0, Math.Min(300, textBefore.Length))}...");

        // Apply multiple random black boxes
        _output.WriteLine("\nApplying multiple random black boxes");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var numBlackBoxes = 5;
        var blackBoxes = new List<Rect>();

        for (int i = 0; i < numBlackBoxes; i++)
        {
            // Generate random position and size
            var x = _random.Next(50, 450);
            var y = _random.Next(50, 650);
            var width = _random.Next(60, 120);
            var height = _random.Next(40, 80);

            var blackBox = new Rect(x, y, width, height);
            blackBoxes.Add(blackBox);

            _output.WriteLine($"Black box {i + 1}: X={x}, Y={y}, W={width}, H={height}");

            // Apply redaction (removes content + draws black box)
            _redactionService.RedactArea(page, blackBox, testPdf, renderDpi: 72);
        }

        var redactedPdf = CreateTempPath("multi_blackbox_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Verify results
        _output.WriteLine("\nVerifying redaction results");
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        _output.WriteLine($"Content after:\n{textAfter.Substring(0, Math.Min(300, textAfter.Length))}...");

        // Some content should be removed
        textAfter.Length.Should().BeLessThan(textBefore.Length,
            "some content should be removed");

        // But not all content
        textAfter.Length.Should().BeGreaterThan(0,
            "some content should remain (not everything is under black boxes)");

        // PDF should be valid
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine($"\nContent reduction: {textBefore.Length} → {textAfter.Length} characters");
        _output.WriteLine("✓ TEST PASSED: Multiple black boxes applied, selective content removal verified");
    }

    [Fact]
    public void ComplexDocument_TargetedBlackBoxes_OnlySensitiveDataRemoved()
    {
        // Generate complex document
        _output.WriteLine("Generating complex document with sensitive and public data");
        var testPdf = CreateTempPath("complex_blackbox_test.pdf");
        TestPdfGenerator.CreateComplexContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Document contains {textBefore.Length} characters");

        // Verify sensitive data exists before redaction
        var sensitiveItems = new[] { "1234-5678-9012-3456", "123-45-6789", "SuperSecret123!" };
        var publicItems = new[] { "ACME Corporation", "PUBLIC INFORMATION" };

        _output.WriteLine("\nVerifying initial content:");
        _output.WriteLine("  Sensitive data: " + string.Join(", ", sensitiveItems));
        _output.WriteLine("  Public data: " + string.Join(", ", publicItems));

        PdfTestHelpers.ContainsAllText(testPdf, sensitiveItems).Should().BeTrue(
            "sensitive data should exist before redaction");
        PdfTestHelpers.ContainsAllText(testPdf, publicItems).Should().BeTrue(
            "public data should exist before redaction");

        // Apply black boxes ONLY over sensitive data
        _output.WriteLine("\nApplying black boxes ONLY over sensitive data");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Black box over account number area (y ≈ 280-320)
        _output.WriteLine("  Black box 1: Over account number");
        _redactionService.RedactArea(page, new Rect(50, 285, 300, 25), testPdf, renderDpi: 72);

        // Black box over SSN area (y ≈ 315-335)
        _output.WriteLine("  Black box 2: Over SSN");
        _redactionService.RedactArea(page, new Rect(50, 315, 200, 25), testPdf, renderDpi: 72);

        // Black box over password area (y ≈ 345-365)
        _output.WriteLine("  Black box 3: Over password");
        _redactionService.RedactArea(page, new Rect(50, 345, 250, 25), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("complex_blackbox_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Verify: Sensitive data REMOVED, public data PRESERVED
        _output.WriteLine("\nVerifying selective redaction:");

        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        _output.WriteLine("\nSensitive data removal verification:");
        foreach (var item in sensitiveItems)
        {
            textAfter.Should().NotContain(item,
                $"sensitive data '{item}' should be REMOVED");
            _output.WriteLine($"  ✓ '{item}' REMOVED");
        }

        _output.WriteLine("\nPublic data preservation verification:");
        foreach (var item in publicItems)
        {
            textAfter.Should().Contain(item,
                $"public data '{item}' should be PRESERVED");
            _output.WriteLine($"  ✓ '{item}' PRESERVED");
        }

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine("\n✓ TEST PASSED: Only sensitive data removed, public data intact");
    }

    [Fact]
    public void RandomBlackBoxes_VerifyContentIntegrity_NoCrosstalk()
    {
        _output.WriteLine("Testing that black boxes don't affect non-overlapping content");

        var testPdf = CreateTempPath("integrity_test.pdf");
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Apply black box over ONLY "CONFIDENTIAL" (top-left)
        var confidentialPos = contentMap["CONFIDENTIAL"];
        var blackBox = new Rect(
            confidentialPos.x - 3,
            confidentialPos.y - 3,
            confidentialPos.width + 6,
            confidentialPos.height + 6
        );

        _output.WriteLine($"Applying single black box at: {blackBox}");
        _output.WriteLine($"  This should cover ONLY 'CONFIDENTIAL'");
        _redactionService.RedactArea(page, blackBox, testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("integrity_test_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Verify ONLY CONFIDENTIAL is removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        _output.WriteLine("\nVerification:");
        textAfter.Should().NotContain("CONFIDENTIAL", "should be under black box");
        _output.WriteLine("  ✓ 'CONFIDENTIAL' removed");

        // All other text should be intact
        var otherItems = new[] { "PUBLIC", "SECRET", "PRIVATE", "INTERNAL" };
        foreach (var item in otherItems)
        {
            textAfter.Should().Contain(item, $"'{item}' should NOT be affected");
            _output.WriteLine($"  ✓ '{item}' preserved");
        }

        _output.WriteLine("\n✓ TEST PASSED: Black box affects ONLY the targeted content, no crosstalk");
    }

    [Fact]
    public void SaveAndReload_VerifyPermanentRemoval()
    {
        _output.WriteLine("Testing that content removal is permanent (survives save/reload)");

        var testPdf = CreateTempPath("permanent_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "PERMANENT REMOVAL TEST");
        _tempFiles.Add(testPdf);

        // Apply black box
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("permanent_test_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        _output.WriteLine("Saved redacted PDF");

        // Reload and verify content is still removed
        _output.WriteLine("Reloading PDF to verify permanent removal");

        var textAfterReload = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfterReload.Should().NotContain("PERMANENT REMOVAL TEST",
            "content should be permanently removed (not just hidden)");

        // Verify at content stream level
        var contentStream = PdfTestHelpers.GetPageContentStream(redactedPdf, 0);
        var streamText = System.Text.Encoding.ASCII.GetString(contentStream);

        _output.WriteLine($"\nContent stream size: {contentStream.Length} bytes");

        // The original text should not appear in the content stream
        streamText.Should().NotContain("PERMANENT REMOVAL TEST",
            "text should not exist in content stream");

        _output.WriteLine("✓ TEST PASSED: Content is permanently removed from PDF structure");
    }

    /// <summary>
    /// Randomly selects N items from a list
    /// </summary>
    private List<T> SelectRandomItems<T>(List<T> items, int count)
    {
        var shuffled = items.OrderBy(x => _random.Next()).ToList();
        return shuffled.Take(Math.Min(count, items.Count)).ToList();
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PdfEditorTests", "BlackBoxTests");
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
}
