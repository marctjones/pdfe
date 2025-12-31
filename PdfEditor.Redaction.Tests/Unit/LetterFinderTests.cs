using FluentAssertions;
using PdfEditor.Redaction.GlyphLevel;
using PdfEditor.Redaction.Tests.Utilities;
using UglyToad.PdfPig;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class LetterFinderTests : IDisposable
{
    private readonly LetterFinder _finder = new();
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void FindOperationLetters_NoLettersOnPage_ReturnsEmpty()
    {
        // Arrange
        var textOp = CreateTextOperation("Hello", bbox: new PdfRectangle(100, 200, 150, 220));
        var letters = new List<UglyToad.PdfPig.Content.Letter>();

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().BeEmpty();
    }

    [Fact]
    public void FindOperationLetters_EmptyBoundingBox_StillMatchesByText()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Hello");
        // Empty bounding box - but LetterFinder uses TEXT matching, not spatial!
        // Issue #151: Text matching is rotation-independent and more reliable.
        var textOp = CreateTextOperation("Hello", bbox: new PdfRectangle(0, 0, 0, 0));

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert - Text matching finds letters regardless of bounding box
        matches.Should().HaveCount(5, "LetterFinder uses text content matching, not bounding box");
        matches[0].Letter.Value.Should().Be("H");
    }

    [Fact]
    public void FindOperationLetters_MatchesByPosition_ReturnsInOrder()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Hello");

        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(1);
        var letters = page.Letters;

        // Get actual bounding box from first few letters
        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Hello", bbox);

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().HaveCount(5);
        matches[0].CharacterIndex.Should().Be(0);
        matches[0].Letter.Value.Should().Be("H");
        matches[1].CharacterIndex.Should().Be(1);
        matches[1].Letter.Value.Should().Be("e");
        matches[4].CharacterIndex.Should().Be(4);
        matches[4].Letter.Value.Should().Be("o");
    }

    [Fact]
    public void FindOperationLetters_LettersInReadingOrder_LeftToRight()
    {
        // Arrange
        var pdfPath = CreateTempPdf("abc");

        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(1);
        var letters = page.Letters;

        // Create bbox covering all letters
        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[2].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("abc", bbox);

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().HaveCount(3);
        matches[0].Letter.Value.Should().Be("a");
        matches[1].Letter.Value.Should().Be("b");
        matches[2].Letter.Value.Should().Be("c");
    }

    [Fact]
    public void FindOperationLetters_NoOverlap_StillMatchesByText()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Hello");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        // Bounding box far away from actual letters - but LetterFinder uses TEXT matching, not spatial!
        // Issue #151: Text matching is rotation-independent and more reliable than spatial matching.
        var textOp = CreateTextOperation("Hello", bbox: new PdfRectangle(500, 500, 550, 520));

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert - Text matching finds the letters regardless of bounding box position
        matches.Should().HaveCount(5, "LetterFinder uses text content matching, not spatial overlap");
        matches[0].Letter.Value.Should().Be("H");
        matches[4].Letter.Value.Should().Be("o");
    }

    [Fact]
    public void FindOperationLetters_SetsOperationText()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Test");

        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(1);
        var letters = page.Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[3].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Test", bbox);

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().AllSatisfy(m => m.OperationText.Should().Be("Test"));
    }

    [Fact]
    public void FindOperationLetters_SetsCorrectCharacterIndex()
    {
        // Arrange
        var pdfPath = CreateTempPdf("ABC");

        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(1);
        var letters = page.Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[2].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("ABC", bbox);

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().HaveCount(3);
        matches.Select(m => m.CharacterIndex).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void FindOperationLetters_ABCDE_Finds5Letters()
    {
        // Arrange
        var pdfPath = CreateTempPdf("ABCDE");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("ABCDE", bbox);

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().HaveCount(5);
        matches[0].Letter.Value.Should().Be("A");
        matches[1].Letter.Value.Should().Be("B");
        matches[2].Letter.Value.Should().Be("C");
        matches[3].Letter.Value.Should().Be("D");
        matches[4].Letter.Value.Should().Be("E");
    }

    [Fact]
    public void FindOperationLetters_InRedactionAreaInitiallyFalse()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Test");

        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(1);
        var letters = page.Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[3].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Test", bbox);

        // Act
        var matches = _finder.FindOperationLetters(textOp, letters);

        // Assert
        matches.Should().AllSatisfy(m => m.InRedactionArea.Should().BeFalse());
    }

    // Helper Methods

    private string CreateTempPdf(string text)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        TestPdfGenerator.CreateSimpleTextPdf(path, text);
        return path;
    }

    private static TextOperation CreateTextOperation(string text, PdfRectangle bbox)
    {
        return new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { text },
            BoundingBox = bbox,
            StreamPosition = 0,
            Text = text,
            Glyphs = new List<GlyphPosition>(),
            FontSize = 12
        };
    }
}
