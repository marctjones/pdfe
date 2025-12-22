using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;

namespace PdfEditor.Redaction.Tests;

/// <summary>
/// Tests for basic infrastructure - verifies test utilities work correctly.
/// These tests should pass before implementing actual redaction.
/// </summary>
public class InfrastructureTests : IDisposable
{
    private readonly string _tempDir;

    public InfrastructureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RedactionTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void TestPdfGenerator_CreateSimpleTextPdf_CreatesValidPdf()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "simple.pdf");
        var text = "Hello World";

        // Act
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, text);

        // Assert
        File.Exists(pdfPath).Should().BeTrue();
        PdfTestHelpers.IsValidPdf(pdfPath).Should().BeTrue();
    }

    [Fact]
    public void TestPdfGenerator_CreateSimpleTextPdf_ContainsExpectedText()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "simple.pdf");
        var text = "Hello World";

        // Act
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, text);

        // Assert
        var extractedText = PdfTestHelpers.ExtractAllText(pdfPath);
        extractedText.Should().Contain(text);
    }

    [Fact]
    public void TestPdfGenerator_CreateMultiLineTextPdf_ContainsAllLines()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "multiline.pdf");
        var lines = new[] { "Line One", "Line Two", "Line Three" };

        // Act
        TestPdfGenerator.CreateMultiLineTextPdf(pdfPath, lines);

        // Assert
        var extractedText = PdfTestHelpers.ExtractAllText(pdfPath);
        foreach (var line in lines)
        {
            extractedText.Should().Contain(line);
        }
    }

    [Fact]
    public void PdfTestHelpers_ContainsText_ReturnsTrueForPresentText()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "SECRET PASSWORD");

        // Act & Assert
        PdfTestHelpers.ContainsText(pdfPath, "SECRET").Should().BeTrue();
        PdfTestHelpers.ContainsText(pdfPath, "PASSWORD").Should().BeTrue();
        PdfTestHelpers.ContainsText(pdfPath, "NOTHERE").Should().BeFalse();
    }

    [Fact]
    public void PdfTestHelpers_CountTextOccurrences_CountsCorrectly()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfGenerator.CreateMultiLineTextPdf(pdfPath, "AAA BBB", "AAA CCC", "AAA DDD");

        // Act
        var count = PdfTestHelpers.CountTextOccurrences(pdfPath, "AAA");

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public void PdfTestHelpers_GetPageSize_ReturnsCorrectSize()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Test");

        // Act
        var (width, height) = PdfTestHelpers.GetPageSize(pdfPath);

        // Assert
        width.Should().BeApproximately(612, 1);   // US Letter width
        height.Should().BeApproximately(792, 1);  // US Letter height
    }

    [Fact]
    public void PdfTestHelpers_GetLetterPositions_ReturnsPositions()
    {
        // Arrange
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "ABC");

        // Act
        var positions = PdfTestHelpers.GetLetterPositions(pdfPath);

        // Assert
        positions.Should().HaveCount(3);
        positions[0].Character.Should().Be("A");
        positions[1].Character.Should().Be("B");
        positions[2].Character.Should().Be("C");
    }
}
