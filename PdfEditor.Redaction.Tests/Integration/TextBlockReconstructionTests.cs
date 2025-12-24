using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests for verifying text block (BT...ET) reconstruction and filtering.
/// Ensures no nested BT operators or orphaned operators without operands.
/// </summary>
public class TextBlockReconstructionTests : IDisposable
{
    private readonly string _testDir;
    private readonly TextRedactor _redactor;

    public TextBlockReconstructionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_textblock_test_{Guid.NewGuid()}");
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

    [Fact]
    public void SequentialRedactions_DoNotCreateNestedBT()
    {
        // Arrange - Create PDF with multiple text blocks
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf,
            "Block 1: REDACT this text\nBlock 2: Keep this text\nBlock 3: REMOVE this text");

        var output1 = Path.Combine(_testDir, "output1.pdf");
        var output2 = Path.Combine(_testDir, "output2.pdf");

        // Act - Sequential redactions
        var result1 = _redactor.RedactText(inputPdf, output1, "REDACT");
        var result2 = _redactor.RedactText(output1, output2, "REMOVE");

        // Assert
        result1.Success.Should().BeTrue("First redaction should succeed");
        result2.Success.Should().BeTrue("Second redaction should succeed");

        // Verify no nested BT operators
        var contentStream = PdfTestHelpers.ExtractContentStream(output2, pageNumber: 1);
        contentStream.Should().NotContain("BT\nBT", "Content stream should not have nested BT operators");
        contentStream.Should().NotContain("BT BT", "Content stream should not have adjacent BT operators");

        // Verify balanced BT/ET
        var btCount = CountOccurrences(contentStream, "\nBT\n");
        var etCount = CountOccurrences(contentStream, "\nET\n");
        btCount.Should().Be(etCount, "Every BT should have a matching ET");

        // Verify text was redacted
        var text = PdfTestHelpers.ExtractAllText(output2);
        text.Should().NotContain("REDACT");
        text.Should().NotContain("REMOVE");
        text.Should().Contain("Keep this text");
    }

    [Fact]
    public void SequentialRedactions_ProduceValidPdf()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "This is FORMATTED TEXT with styling");

        var output1 = Path.Combine(_testDir, "output1.pdf");
        var output2 = Path.Combine(_testDir, "output2.pdf");

        // Act
        var result1 = _redactor.RedactText(inputPdf, output1, "FORMATTED");
        var result2 = _redactor.RedactText(output1, output2, "TEXT");

        // Assert
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();

        // Verify PDF is valid and can be opened
        PdfTestHelpers.IsValidPdf(output2).Should().BeTrue("Output PDF should be valid");

        // Verify text was redacted
        var text = PdfTestHelpers.ExtractAllText(output2);
        text.Should().NotContain("FORMATTED");
        text.Should().NotContain("TEXT");
        text.Should().Contain("This is");
        text.Should().Contain("with styling");
    }

    [Fact]
    public void ReconstructedBlock_PreservesAllTextInBlock()
    {
        // Arrange - Create PDF with text block containing multiple operations
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf,
            "First line REDACT middle\nSecond line in same block\nThird line REDACT here too");

        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Act - Redact word that appears in block
        var result = _redactor.RedactText(inputPdf, outputPdf, "REDACT");

        // Assert
        result.Success.Should().BeTrue();

        var text = PdfTestHelpers.ExtractAllText(outputPdf);
        text.Should().NotContain("REDACT", "Redacted text should be removed");
        text.Should().Contain("First line", "Non-redacted text from same block should remain");
        text.Should().Contain("middle", "Non-redacted text from same block should remain");
        text.Should().Contain("Second line in same block", "Other operations in block should remain");
        text.Should().Contain("Third line", "Non-redacted text from same operation should remain");
        text.Should().Contain("here too", "Non-redacted text from same operation should remain");
    }

    [Fact]
    public void NonReconstructedBlocks_RemainIntact()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf,
            "Block with REDACT word\nBlock without target word\nAnother block without target");

        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Act
        var result = _redactor.RedactText(inputPdf, outputPdf, "REDACT");

        // Assert
        result.Success.Should().BeTrue();

        var text = PdfTestHelpers.ExtractAllText(outputPdf);
        text.Should().NotContain("REDACT");
        text.Should().Contain("Block without target word",
            "Non-reconstructed blocks should remain unchanged");
        text.Should().Contain("Another block without target",
            "Non-reconstructed blocks should remain unchanged");
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

    private static List<string> ExtractOperatorLines(string contentStream, string operatorName)
    {
        var lines = contentStream.Split('\n');
        var operatorLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.EndsWith($" {operatorName}") || line == operatorName)
            {
                // Include previous line(s) for operands if operator is on its own line
                if (line == operatorName && i > 0)
                {
                    operatorLines.Add(lines[i - 1].Trim() + " " + line);
                }
                else
                {
                    operatorLines.Add(line);
                }
            }
        }

        return operatorLines;
    }
}
