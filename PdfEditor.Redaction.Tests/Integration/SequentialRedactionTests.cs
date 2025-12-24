using Xunit;
using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests for sequential redactions (Fix #127).
/// Verifies that redactions work correctly when chained together,
/// including cases where middle steps find no matches.
/// </summary>
public class SequentialRedactionTests : IDisposable
{
    private readonly string _testDir;

    public SequentialRedactionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_sequential_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void SequentialRedaction_MiddleStepFindsNoMatches_StillCreatesOutputFile()
    {
        // Arrange
        var pdf1 = Path.Combine(_testDir, "step1.pdf");
        var pdf2 = Path.Combine(_testDir, "step2.pdf");
        var pdf3 = Path.Combine(_testDir, "step3.pdf");

        // Create initial PDF with "Hello World"
        TestPdfGenerator.CreateSimpleTextPdf(pdf1, "Hello World");

        var redactor = new TextRedactor();

        // Act - Sequential redactions where middle one finds no matches
        var result1 = redactor.RedactText(pdf1, pdf2, "Hello");
        var result2 = redactor.RedactText(pdf2, pdf3, "NonExistentText");  // Won't find anything

        // Assert
        result1.Success.Should().BeTrue("First redaction should succeed");
        result1.RedactionCount.Should().Be(1, "Should redact 'Hello'");

        result2.Success.Should().BeTrue("Second redaction should succeed even with no matches");
        result2.RedactionCount.Should().Be(0, "Should find no matches for 'NonExistentText'");

        // CRITICAL: Output file must exist even when no redactions occurred
        File.Exists(pdf2).Should().BeTrue("pdf2 should exist after first redaction");
        File.Exists(pdf3).Should().BeTrue("pdf3 should exist after second redaction even with 0 matches");

        // Verify content
        var text2 = PdfTestHelpers.ExtractAllText(pdf2);
        text2.Should().NotContain("Hello", "First redaction should have removed 'Hello'");
        text2.Should().Contain("World", "First redaction should preserve 'World'");

        var text3 = PdfTestHelpers.ExtractAllText(pdf3);
        text3.Should().Be(text2, "Second redaction with no matches should not change content");
    }

    [Fact]
    public void SequentialRedaction_MultipleStepsWithNoMatches_AllCreateOutputFiles()
    {
        // Arrange
        var pdf1 = Path.Combine(_testDir, "step1.pdf");
        var pdf2 = Path.Combine(_testDir, "step2.pdf");
        var pdf3 = Path.Combine(_testDir, "step3.pdf");
        var pdf4 = Path.Combine(_testDir, "step4.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdf1, "The quick brown fox");

        var redactor = new TextRedactor();

        // Act - Multiple sequential redactions, some with no matches
        var result1 = redactor.RedactText(pdf1, pdf2, "quick");
        var result2 = redactor.RedactText(pdf2, pdf3, "lazy");      // No match
        var result3 = redactor.RedactText(pdf3, pdf4, "dog");       // No match

        // Assert
        result1.Success.Should().BeTrue();
        result1.RedactionCount.Should().Be(1);

        result2.Success.Should().BeTrue();
        result2.RedactionCount.Should().Be(0);

        result3.Success.Should().BeTrue();
        result3.RedactionCount.Should().Be(0);

        // All files must exist
        File.Exists(pdf2).Should().BeTrue("pdf2 must exist");
        File.Exists(pdf3).Should().BeTrue("pdf3 must exist even with no matches in step 2");
        File.Exists(pdf4).Should().BeTrue("pdf4 must exist even with no matches in step 3");

        // Content verification
        var finalText = PdfTestHelpers.ExtractAllText(pdf4);
        finalText.Should().NotContain("quick", "Should have been redacted in step 1");
        finalText.Should().Contain("brown", "Should still be present");
        finalText.Should().Contain("fox", "Should still be present");
    }

    [Fact]
    public void SequentialRedaction_AllStepsFindMatches_WorksCorrectly()
    {
        // Arrange
        var pdf1 = Path.Combine(_testDir, "step1.pdf");
        var pdf2 = Path.Combine(_testDir, "step2.pdf");
        var pdf3 = Path.Combine(_testDir, "step3.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdf1, "First Second Third");

        var redactor = new TextRedactor();

        // Act
        var result1 = redactor.RedactText(pdf1, pdf2, "First");
        var result2 = redactor.RedactText(pdf2, pdf3, "Second");

        // Assert
        result1.Success.Should().BeTrue();
        result1.RedactionCount.Should().Be(1);

        result2.Success.Should().BeTrue();
        result2.RedactionCount.Should().Be(1);

        File.Exists(pdf2).Should().BeTrue();
        File.Exists(pdf3).Should().BeTrue();

        var finalText = PdfTestHelpers.ExtractAllText(pdf3);
        finalText.Should().NotContain("First");
        finalText.Should().NotContain("Second");
        finalText.Should().Contain("Third");
    }

    [Fact]
    public void RedactText_NoMatchesFound_CopiesInputToOutput()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "Sample text content");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPdf, outputPdf, "NonExistentText");

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed even with no matches");
        result.RedactionCount.Should().Be(0, "Should find 0 matches");

        File.Exists(outputPdf).Should().BeTrue("Output file must be created");

        var inputText = PdfTestHelpers.ExtractAllText(inputPdf);
        var outputText = PdfTestHelpers.ExtractAllText(outputPdf);

        outputText.Should().Be(inputText, "Output should be identical to input when no redactions occur");
    }

    [Fact]
    public void RedactLocations_EmptyLocationList_CopiesInputToOutput()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "Sample text");

        var redactor = new TextRedactor();
        var emptyLocations = new List<RedactionLocation>();

        // Act
        var result = redactor.RedactLocations(inputPdf, outputPdf, emptyLocations);

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(0);

        File.Exists(outputPdf).Should().BeTrue("Output file must be created");

        var inputText = PdfTestHelpers.ExtractAllText(inputPdf);
        var outputText = PdfTestHelpers.ExtractAllText(outputPdf);

        outputText.Should().Be(inputText);
    }
}
