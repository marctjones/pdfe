using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests for substring redaction - ensuring that redacting part of a text operation
/// preserves the non-redacted portions. Issue #87.
/// </summary>
public class SubstringRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();

    public SubstringRedactionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { }
        }
    }

    private string CreateTempFile(string prefix = "test")
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void RedactingSubstring_PreservesOtherText_SameLine()
    {
        // Arrange - Create PDF with "NUMBER" and "STREET" on same line
        var inputPath = CreateTempFile("input");
        var outputPath = CreateTempFile("output");

        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "  NUMBER  STREET  ");

        // Get original text for logging
        using (var doc = PdfDocument.Open(inputPath))
        {
            var text = doc.GetPage(1).Text;
            _output.WriteLine($"Original text: '{text}'");
        }

        // Act - Redact only "STREET"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "STREET");

        _output.WriteLine($"Redaction result: Success={result.Success}, Count={result.RedactionCount}");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterOrEqualTo(1);

        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Output text: '{outputText}'");

        outputText.Should().NotContain("STREET", "STREET should be removed");
        outputText.Should().Contain("NUMBER", "NUMBER should be preserved (issue #87)");
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void RedactingFirstWord_PreservesSecondWord()
    {
        // Arrange
        var inputPath = CreateTempFile("input");
        var outputPath = CreateTempFile("output");

        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "HELLO WORLD");

        // Act - Redact only "HELLO"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "HELLO");

        // Assert
        result.Success.Should().BeTrue();

        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Output text: '{outputText}'");

        outputText.Should().NotContain("HELLO", "HELLO should be removed");
        outputText.Should().Contain("WORLD", "WORLD should be preserved");
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void RedactingMiddleWord_PreservesFirstAndLastWords()
    {
        // Arrange
        var inputPath = CreateTempFile("input");
        var outputPath = CreateTempFile("output");

        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "AAA BBB CCC");

        // Act - Redact only "BBB"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "BBB");

        // Assert
        result.Success.Should().BeTrue();

        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Output text: '{outputText}'");

        outputText.Should().NotContain("BBB", "BBB should be removed");
        outputText.Should().Contain("AAA", "AAA should be preserved");
        outputText.Should().Contain("CCC", "CCC should be preserved");
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void RedactingSubstringOfWord_PreservesRemainingCharacters()
    {
        // Arrange - Redact "DAY" from "BIRTHDAY"
        var inputPath = CreateTempFile("input");
        var outputPath = CreateTempFile("output");

        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "HAPPY BIRTHDAY");

        // Act - Redact only "DAY"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "DAY");

        // Assert
        result.Success.Should().BeTrue();

        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Output text: '{outputText}'");

        // "DAY" should be removed
        outputText.Should().NotContain("DAY", "DAY should be removed");
        // "HAPPY" should be preserved
        outputText.Should().Contain("HAPPY", "HAPPY should be preserved");
        // "BIRTH" portion should be preserved
        outputText.Should().Contain("BIRTH", "BIRTH should be preserved");
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void MultipleRedactions_PreservesNonTargetedText()
    {
        // Arrange
        var inputPath = CreateTempFile("input");
        var tempPath = CreateTempFile("temp");
        var outputPath = CreateTempFile("output");

        // Create PDF: "NAME: John DOB: 1990"
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "NAME: John DOB: 1990");

        // Act - Sequential redactions
        var redactor = new TextRedactor();

        // First: redact "John"
        var result1 = redactor.RedactText(inputPath, tempPath, "John");
        _output.WriteLine($"After redacting 'John': Success={result1.Success}");

        // Second: redact "1990"
        var result2 = redactor.RedactText(tempPath, outputPath, "1990");
        _output.WriteLine($"After redacting '1990': Success={result2.Success}");

        // Assert
        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Final text: '{outputText}'");

        outputText.Should().NotContain("John", "John should be removed");
        outputText.Should().NotContain("1990", "1990 should be removed");
        outputText.Should().Contain("NAME:", "NAME: should be preserved");
        outputText.Should().Contain("DOB:", "DOB: should be preserved");
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void RedactingFromSeparateTextOperations_WorksCorrectly()
    {
        // Arrange - Create PDF with text on separate lines (likely separate text operations)
        var inputPath = CreateTempFile("input");
        var outputPath = CreateTempFile("output");

        TestPdfGenerator.CreateMultiLineTextPdf(inputPath, "Line 1: REDACT_ME", "Line 2: KEEP_ME");

        // Act - Redact "REDACT_ME"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "REDACT_ME");

        // Assert
        result.Success.Should().BeTrue();

        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Output text: '{outputText}'");

        outputText.Should().NotContain("REDACT_ME", "REDACT_ME should be removed");
        outputText.Should().Contain("Line 1:", "Line 1: prefix should be preserved");
        outputText.Should().Contain("Line 2:", "Line 2: should be preserved");
        outputText.Should().Contain("KEEP_ME", "KEEP_ME should be preserved");
    }

    [Fact]
    [Trait("Category", "SubstringRedaction")]
    [Trait("Issue", "87")]
    public void RedactingNumber_PreservesAdjacentText()
    {
        // Arrange - Create PDF with "$50" that should not affect other numbers
        var inputPath = CreateTempFile("input");
        var outputPath = CreateTempFile("output");

        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Price: $50 (regular: $100)");

        // Act - Redact "$50"
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, outputPath, "$50");

        // Assert
        result.Success.Should().BeTrue();

        using var outputDoc = PdfDocument.Open(outputPath);
        var outputText = outputDoc.GetPage(1).Text;
        _output.WriteLine($"Output text: '{outputText}'");

        outputText.Should().NotContain("$50", "$50 should be removed");
        outputText.Should().Contain("Price:", "Price: should be preserved");
        outputText.Should().Contain("$100", "$100 should be preserved");
    }
}
