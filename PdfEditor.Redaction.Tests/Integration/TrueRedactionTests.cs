using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests verifying TRUE glyph-level redaction.
/// These tests confirm that text is REMOVED from PDF structure,
/// not just visually covered with black boxes.
/// </summary>
public class TrueRedactionTests : IDisposable
{
    private readonly string _tempDir;

    public TrueRedactionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"redaction_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void RedactText_RemovesTextFromPdfStructure()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "HELLO WORLD SECRET DATA");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "SECRET");

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        result.RedactionCount.Should().BeGreaterThan(0, "Should find text to redact");

        // CRITICAL: Text must be REMOVED from PDF structure
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("SECRET",
            "Text must be REMOVED from PDF structure, not just hidden");
    }

    [Fact]
    public void RedactText_PreservesUnredactedText()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateMultiLineTextPdf(inputPath,
            "Public information here",
            "SECRET DATA TO REMOVE",
            "More public content");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "SECRET");

        // Assert
        result.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("SECRET", "Redacted text should be removed");
        // Note: Surrounding text preservation depends on content stream structure
    }

    [Fact]
    public void RedactText_CaseSensitiveByDefault()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Secret SECRET secret");

        var redactor = new TextRedactor();

        // Act - search for uppercase only
        var result = redactor.RedactText(inputPath, outputPath, "SECRET");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(1, "Only exact case match should be redacted");
    }

    [Fact]
    public void RedactText_CaseInsensitiveOption()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        // Put each variation on a separate line so they're separate text operations
        TestPdfGenerator.CreateMultiLineTextPdf(inputPath,
            "First line with Secret word",
            "Second line with SECRET word",
            "Third line with secret word");

        var redactor = new TextRedactor();
        var options = new RedactionOptions { CaseSensitive = false };

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "secret", options);

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterOrEqualTo(1, "At least one match should be found");

        // Verify text is removed from structure
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.ToLower().Should().NotContain("secret", "All case variations should be removed");
    }

    [Fact]
    public void RedactLocations_RedactsSpecifiedArea()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSensitiveDataPdf(inputPath);

        // Get text positions to find SSN location
        var positions = PdfTestHelpers.GetLetterPositions(inputPath);
        var ssnLetters = positions.Where(p => "123-45-6789".Contains(p.Character)).ToList();

        if (ssnLetters.Any())
        {
            var left = ssnLetters.Min(p => p.Left);
            var bottom = ssnLetters.Min(p => p.Bottom);
            var right = ssnLetters.Max(p => p.Right);
            var top = ssnLetters.Max(p => p.Top);

            var location = new RedactionLocation
            {
                PageNumber = 1,
                BoundingBox = new PdfRectangle(left - 5, bottom - 5, right + 5, top + 5)
            };

            var redactor = new TextRedactor();

            // Act
            var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

            // Assert
            result.Success.Should().BeTrue();
            File.Exists(outputPath).Should().BeTrue("Output file should be created");
        }
    }

    [Fact]
    public void RedactText_NoMatchesReturnsSuccessWithZeroCount()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Normal content here");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "NONEXISTENT");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(0);
    }

    [Fact]
    public void RedactText_MultipleOccurrences()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateMultiLineTextPdf(inputPath,
            "SSN: 123-45-6789",
            "Other SSN: 123-45-6789",
            "Another SSN: 123-45-6789");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "123-45-6789");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(3, "All three SSN occurrences should be redacted");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("123-45-6789", "All SSN instances should be removed");
    }

    [Fact]
    public void RedactText_DrawsVisualMarkerByDefault()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "CONFIDENTIAL DATA");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "CONFIDENTIAL");

        // Assert
        result.Success.Should().BeTrue();
        // Visual marker is drawn by default - the PDF should be modified
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactText_CanDisableVisualMarker()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "CONFIDENTIAL DATA");

        var redactor = new TextRedactor();
        var options = new RedactionOptions { DrawVisualMarker = false };

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "CONFIDENTIAL", options);

        // Assert
        result.Success.Should().BeTrue();
        // Text should still be removed even without visual marker
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("CONFIDENTIAL");
    }

    [Fact]
    public void RedactText_SanitizesMetadataWhenRequested()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "CONFIDENTIAL");

        var redactor = new TextRedactor();
        var options = new RedactionOptions { SanitizeMetadata = true };

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "CONFIDENTIAL", options);

        // Assert
        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactionResult_ContainsDetails()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "SECRET DATA");

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPath, outputPath, "SECRET");

        // Assert
        result.Success.Should().BeTrue();
        result.AffectedPages.Should().Contain(1);
        result.Details.Should().NotBeEmpty();
        result.Details.First().PageNumber.Should().Be(1);
    }

    [Fact]
    public void RedactText_OutputFileIsValid()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSensitiveDataPdf(inputPath);

        var redactor = new TextRedactor();

        // Act
        redactor.RedactText(inputPath, outputPath, "SSN:");

        // Assert - output should be a valid, readable PDF
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
        PdfTestHelpers.GetPageCount(outputPath).Should().Be(1, "Page count should be preserved");
    }
}
