using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests to verify that specific GitHub issues have been resolved.
/// These tests validate that bugs are fixed and don't regress.
/// </summary>
public class IssueVerificationTests : IDisposable
{
    private readonly string _testDir;
    private readonly TextRedactor _redactor;

    public IssueVerificationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_issue_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _redactor = new TextRedactor();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    /// <summary>
    /// Issue #102: CRITICAL - Redaction corrupts PDF content stream - text becomes scrambled garbage
    ///
    /// Symptoms:
    /// - Text becomes unreadable garbage after redaction
    /// - Content stream has invalid structure
    /// - Sequential redactions compound the problem
    ///
    /// Root Cause:
    /// - Nested BT operators (invalid PDF)
    /// - Orphaned operators without operands
    /// - Mixed original and reconstructed operations
    ///
    /// Fix:
    /// - Block-aware filtering in GlyphRemover
    /// - Reconstruct entire text blocks when any text is redacted
    /// - Filter out all operations from reconstructed blocks
    /// </summary>
    [Fact]
    public void Issue102_RedactionDoesNotCorruptPdfContentStream()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(inputPdf,
            "This document contains SENSITIVE information that must be redacted.\n" +
            "The remaining text should stay readable and not become scrambled.\n" +
            "Additional content here to verify preservation.");

        var originalText = PdfTestHelpers.ExtractAllText(inputPdf);
        originalText.Should().Contain("SENSITIVE");
        originalText.Should().Contain("remaining text");
        originalText.Should().Contain("Additional content");

        // Act - Redact sensitive information
        var result = _redactor.RedactText(inputPdf, outputPdf, "SENSITIVE");

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPdf).Should().BeTrue("Output PDF should be valid, not corrupted");

        var redactedText = PdfTestHelpers.ExtractAllText(outputPdf);

        // Verify redaction worked
        redactedText.Should().NotContain("SENSITIVE", "Redacted text should be removed");

        // Verify text is NOT corrupted - original readable text should remain readable
        redactedText.Should().Contain("This document contains");
        redactedText.Should().Contain("information that must be redacted");
        redactedText.Should().Contain("The remaining text should stay readable");
        redactedText.Should().Contain("Additional content here to verify preservation",
            "Non-redacted text should remain intact and readable");

        // Verify we didn't get garbage characters
        var hasGarbageChars = redactedText.Any(c => c < 32 && c != '\n' && c != '\r' && c != '\t');
        hasGarbageChars.Should().BeFalse("Text should not contain garbage control characters");

        // Verify content stream structure is valid (no nested BT)
        var contentStream = PdfTestHelpers.ExtractContentStream(outputPdf, pageNumber: 1);
        contentStream.Should().NotContain("BT\nBT", "Should not have nested BT operators");
        contentStream.Should().NotContain("BT BT", "Should not have adjacent BT operators");
    }

    /// <summary>
    /// Issue #103: Text scrambling/doubling after redaction - letters duplicated in content stream
    ///
    /// Symptoms:
    /// - Characters appear multiple times in the content stream
    /// - Text shows duplicated/doubled letters when viewed
    /// - PDF structure is corrupted
    ///
    /// Root Cause:
    /// - Original TextStateOperations (Tf, Tm) kept alongside reconstructed operations
    /// - Content stream has duplicate text-showing operators (Tj)
    /// - Nested BT...ET blocks cause text to render multiple times
    ///
    /// Fix:
    /// - Block-aware filtering removes all operations from reconstructed blocks
    /// - Only reconstructed operations are added to output
    /// - No duplication of BT, Tf, Tm, or Tj operators
    /// </summary>
    [Fact]
    public void Issue103_RedactionDoesNotDuplicateOrScrambleText()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var output1 = Path.Combine(_testDir, "output1.pdf");
        var output2 = Path.Combine(_testDir, "output2.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(inputPdf,
            "First REDACT operation here\n" +
            "Second REMOVE operation here\n" +
            "Third line should remain intact");

        // Act - Perform sequential redactions (this exposed the bug)
        var result1 = _redactor.RedactText(inputPdf, output1, "REDACT");
        var result2 = _redactor.RedactText(output1, output2, "REMOVE");

        // Assert
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        var finalText = PdfTestHelpers.ExtractAllText(output2);

        // Verify redactions worked
        finalText.Should().NotContain("REDACT");
        finalText.Should().NotContain("REMOVE");

        // Verify NO text duplication/doubling
        finalText.Should().Contain("First");
        finalText.Should().Contain("operation here");
        finalText.Should().Contain("Second");
        finalText.Should().Contain("Third line should remain intact");

        // Check for character doubling (the bug symptom)
        finalText.Should().NotContain("FFiirrsstt", "Characters should not be doubled");
        finalText.Should().NotContain("SSeeccoonndd", "Characters should not be doubled");

        // Verify each word appears only once (not duplicated)
        var wordCounts = new Dictionary<string, int>
        {
            ["First"] = CountOccurrences(finalText, "First"),
            ["Second"] = CountOccurrences(finalText, "Second"),
            ["Third"] = CountOccurrences(finalText, "Third"),
            ["operation"] = CountOccurrences(finalText, "operation"),
            ["intact"] = CountOccurrences(finalText, "intact")
        };

        foreach (var (word, count) in wordCounts)
        {
            count.Should().BeLessOrEqualTo(2,
                $"Word '{word}' should appear at most twice (once per line), but appeared {count} times - this indicates duplication bug");
        }

        // Verify content stream doesn't have duplicate operators
        var contentStream = PdfTestHelpers.ExtractContentStream(output2, pageNumber: 1);

        // Count BT/ET pairs - should be balanced and minimal
        var btCount = CountOccurrences(contentStream, "BT");
        var etCount = CountOccurrences(contentStream, "ET");
        btCount.Should().Be(etCount, "BT and ET should be balanced");
        btCount.Should().BeLessOrEqualTo(10,
            "Should have reasonable number of text blocks, not excessive duplication");
    }

    /// <summary>
    /// Issue #106: ContentStreamBuilder corruption - serialization duplicates operations
    ///
    /// Symptoms:
    /// - Operations appear multiple times in serialized content stream
    /// - Content stream size is larger than expected
    /// - Text rendering shows artifacts from duplicate operations
    ///
    /// Root Cause:
    /// - GlyphRemover was keeping both original and reconstructed operations
    /// - ContentStreamBuilder serialized all of them
    /// - Result: duplicate BT/Tf/Tm/Tj operators in output
    ///
    /// Fix:
    /// - Block-aware filtering ensures only one set of operations per text block
    /// - Either all original (non-reconstructed blocks) or all reconstructed
    /// - No mixing or duplication
    /// </summary>
    [Fact]
    public void Issue106_ContentStreamBuilderDoesNotDuplicateOperations()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with known content
        var testText = "Line 1: REDACT this word\nLine 2: Keep this text\nLine 3: REDACT again";
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, testText);

        var originalSize = new FileInfo(inputPdf).Length;

        // Act
        var result = _redactor.RedactText(inputPdf, outputPdf, "REDACT");

        // Assert
        result.Success.Should().BeTrue();

        var outputSize = new FileInfo(outputPdf).Length;

        // Output should not be significantly larger than input
        // (Small increase is OK due to redaction rectangles, but duplication would make it much larger)
        var sizeRatio = (double)outputSize / originalSize;
        sizeRatio.Should().BeLessThan(2.0,
            "Output PDF should not be more than 2x input size - excessive size indicates operation duplication");

        // Verify text appears correct number of times
        var finalText = PdfTestHelpers.ExtractAllText(outputPdf);

        finalText.Should().NotContain("REDACT");

        // These phrases should appear exactly once each
        CountOccurrences(finalText, "Line 1:").Should().BeLessOrEqualTo(1,
            "Line 1 should not be duplicated");
        CountOccurrences(finalText, "Line 2:").Should().BeLessOrEqualTo(1,
            "Line 2 should not be duplicated");
        CountOccurrences(finalText, "Keep this text").Should().BeLessOrEqualTo(1,
            "Text should not be duplicated");

        // Verify content stream doesn't have excessive operators
        var contentStream = PdfTestHelpers.ExtractContentStream(outputPdf, pageNumber: 1);

        // Should have reasonable number of Tf operators (1-5 for simple doc)
        var tfCount = CountOccurrences(contentStream, "Tf");
        tfCount.Should().BeLessThan(15,
            $"Should have reasonable number of Tf operators, but found {tfCount} - indicates duplication");

        // Should have reasonable number of text blocks
        var btCount = CountOccurrences(contentStream, "BT");
        btCount.Should().BeLessThan(15,
            $"Should have reasonable number of text blocks, but found {btCount} - indicates duplication");
    }

    /// <summary>
    /// Comprehensive test: Verify all three issues are resolved together.
    /// This test combines scenarios that would trigger all three bugs.
    /// </summary>
    [Fact]
    public void Issues102_103_106_AllResolvedTogether()
    {
        // Arrange - Complex scenario that would trigger all bugs
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var output1 = Path.Combine(_testDir, "output1.pdf");
        var output2 = Path.Combine(_testDir, "output2.pdf");
        var output3 = Path.Combine(_testDir, "output3.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(inputPdf,
            "FIRST redaction target in line one\n" +
            "SECOND redaction target in line two\n" +
            "THIRD redaction target in line three\n" +
            "Normal text that should be preserved\n" +
            "More normal text here");

        // Act - Multiple sequential redactions (maximum stress test)
        var result1 = _redactor.RedactText(inputPdf, output1, "FIRST");
        var result2 = _redactor.RedactText(output1, output2, "SECOND");
        var result3 = _redactor.RedactText(output2, output3, "THIRD");

        // Assert - All should succeed
        result1.Success.Should().BeTrue("First redaction should succeed");
        result2.Success.Should().BeTrue("Second redaction should succeed");
        result3.Success.Should().BeTrue("Third redaction should succeed");

        // Verify #102 - No corruption
        PdfTestHelpers.IsValidPdf(output3).Should().BeTrue("PDF should be valid, not corrupted");
        var finalText = PdfTestHelpers.ExtractAllText(output3);
        finalText.Should().Contain("Normal text");
        finalText.Should().Contain("preserved");

        // Verify #103 - No text duplication/scrambling
        finalText.Should().NotContain("FIRST");
        finalText.Should().NotContain("SECOND");
        finalText.Should().NotContain("THIRD");
        CountOccurrences(finalText, "Normal text").Should().BeLessOrEqualTo(1,
            "Text should not be duplicated");

        // Verify #106 - No operation duplication in content stream
        var contentStream = PdfTestHelpers.ExtractContentStream(output3, pageNumber: 1);
        contentStream.Should().NotContain("BT\nBT", "No nested BT operators");

        var btCount = CountOccurrences(contentStream, "BT");
        btCount.Should().BeLessThan(20, "Should have reasonable number of text blocks");

        // Final size check - shouldn't be bloated
        var finalSize = new FileInfo(output3).Length;
        var inputSize = new FileInfo(inputPdf).Length;
        var sizeRatio = (double)finalSize / inputSize;
        sizeRatio.Should().BeLessThan(3.0,
            "After 3 redactions, size should not be excessively larger - indicates operation duplication");
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
