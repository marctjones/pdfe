using Xunit;
using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests for text normalization (Fix #127).
/// Verifies that text matching works with Unicode apostrophes,
/// special characters, and whitespace variations.
/// </summary>
public class TextNormalizationTests : IDisposable
{
    private readonly string _testDir;

    public TextNormalizationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_normalization_test_{Guid.NewGuid()}");
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
    public void RedactText_UnicodeRightApostrophe_MatchesAsciiApostrophe()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "unicode_apostrophe.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with Unicode right single quotation mark (')
        var textWithUnicodeApostrophe = "John\u2019s Book";  // John's Book (Unicode apostrophe)
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textWithUnicodeApostrophe);

        var redactor = new TextRedactor();

        // Act - Search with ASCII apostrophe
        var result = redactor.RedactText(inputPdf, outputPdf, "John's Book");  // ASCII apostrophe

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        result.RedactionCount.Should().Be(1, "Should find match despite apostrophe difference");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("John", "Text should be redacted");
        textAfter.Should().NotContain("Book", "Text should be redacted");
    }

    [Fact]
    public void RedactText_UnicodeLeftApostrophe_MatchesAsciiApostrophe()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "left_apostrophe.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with Unicode left single quotation mark (')
        var textWithLeftApostrophe = "Mary\u2018s Lamb";  // Mary's Lamb (left quotation mark)
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textWithLeftApostrophe);

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPdf, outputPdf, "Mary's Lamb");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(1);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("Mary");
        textAfter.Should().NotContain("Lamb");
    }

    [Fact]
    public void RedactText_MultipleSpaces_MatchesSingleSpace()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "multiple_spaces.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with multiple spaces
        var textWithMultipleSpaces = "Hello    World";  // 4 spaces
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textWithMultipleSpaces);

        var redactor = new TextRedactor();

        // Act - Search with single space
        var result = redactor.RedactText(inputPdf, outputPdf, "Hello World");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(1, "Should match despite space count difference");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("Hello");
        textAfter.Should().NotContain("World");
    }

    [Fact]
    public void RedactText_EnDash_MatchesHyphen()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "endash.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with en dash (–)
        var textWithEnDash = "pages 10\u201320";  // pages 10–20
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textWithEnDash);

        var redactor = new TextRedactor();

        // Act - Search with regular hyphen
        var result = redactor.RedactText(inputPdf, outputPdf, "pages 10-20");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(1);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("pages");
        textAfter.Should().NotContain("10");
        textAfter.Should().NotContain("20");
    }

    [Fact]
    public void RedactText_EmDash_MatchesHyphen()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "emdash.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with em dash (—)
        var textWithEmDash = "Hello\u2014World";  // Hello—World
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textWithEmDash);

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactText(inputPdf, outputPdf, "Hello-World");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(1);
    }

    [Fact]
    public void RedactText_LeadingTrailingWhitespace_Normalized()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "whitespace.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "Sample Text");

        var redactor = new TextRedactor();

        // Act - Search with leading/trailing spaces
        var result = redactor.RedactText(inputPdf, outputPdf, "  Sample Text  ");

        // Assert
        result.Success.Should().BeTrue("Should match after trimming whitespace");
        result.RedactionCount.Should().Be(1);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("Sample");
        textAfter.Should().NotContain("Text");
    }

    [Fact]
    public void RedactText_MixedNormalization_FindsMatches()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "mixed.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // PDF has: "John's book—pages  10-20"
        // Unicode apostrophe, em dash, multiple spaces
        var textWithMixed = "John\u2019s book\u2014pages  10-20";
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textWithMixed);

        var redactor = new TextRedactor();

        // Act - Search with normalized version
        var result = redactor.RedactText(inputPdf, outputPdf, "John's book-pages 10-20");

        // Assert
        result.Success.Should().BeTrue("Should match after full normalization");
        result.RedactionCount.Should().Be(1);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("John");
        textAfter.Should().NotContain("book");
        textAfter.Should().NotContain("pages");
    }

    [Fact]
    public void RedactText_CaseSensitive_NormalizationPreservesCase()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "case.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // PDF has lowercase with Unicode apostrophe
        var textLower = "john\u2019s book";
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textLower);

        var redactor = new TextRedactor();

        // Act - Search with uppercase (case-sensitive by default)
        var result = redactor.RedactText(inputPdf, outputPdf, "John's Book");

        // Assert - Should NOT match (normalization preserves case)
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(0, "Case-sensitive search should not match different case");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().Contain("john", "Text should not be redacted due to case mismatch");
    }

    [Fact]
    public void RedactText_CaseInsensitive_NormalizationWorks()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "case_insensitive.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        var textLower = "john\u2019s book";
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, textLower);

        var redactor = new TextRedactor();
        var options = new RedactionOptions { CaseSensitive = false };

        // Act
        var result = redactor.RedactText(inputPdf, outputPdf, "John's Book", options);

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(1, "Case-insensitive search should match");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("john");
        textAfter.Should().NotContain("book");
    }
}
