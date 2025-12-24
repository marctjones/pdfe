using FluentAssertions;
using PdfEditor.Redaction.GlyphLevel;
using PdfEditor.Redaction.Tests.Utilities;
using UglyToad.PdfPig;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class TextSegmenterTests : IDisposable
{
    private readonly TextSegmenter _segmenter = new();
    private readonly LetterFinder _letterFinder = new();
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
    public void BuildSegments_NoLetterMatches_WholeOperationNotInArea_KeepsAll()
    {
        // Arrange
        var textOp = CreateTextOperation("Hello", bbox: new PdfRectangle(100, 200, 150, 220));
        var letterMatches = new List<LetterMatch>();
        var redactionArea = new PdfRectangle(300, 400, 350, 420);  // Far away

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("Hello");
        segments[0].Keep.Should().BeTrue();
    }

    [Fact]
    public void BuildSegments_NoLetterMatches_WholeOperationInArea_RemovesAll()
    {
        // Arrange
        var textOp = CreateTextOperation("Hello", bbox: new PdfRectangle(100, 200, 150, 220));
        var letterMatches = new List<LetterMatch>();
        var redactionArea = new PdfRectangle(90, 190, 160, 230);  // Covers operation

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().BeEmpty();  // Nothing to keep
    }

    [Fact]
    public void BuildSegments_AllLettersOutsideArea_KeepsAll()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Hello");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Hello", bbox);
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

        // Redaction area far away
        var redactionArea = new PdfRectangle(500, 500, 550, 520);

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("Hello");
        segments[0].Keep.Should().BeTrue();
    }

    [Fact]
    public void BuildSegments_AllLettersInsideArea_RemovesAll()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Hello");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Hello", bbox);
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

        // Redaction area covering all letters
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left - 10,
            letters[0].GlyphRectangle.Bottom - 10,
            letters[4].GlyphRectangle.Right + 10,
            letters[0].GlyphRectangle.Top + 10);

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().BeEmpty();  // All removed
    }

    [Fact]
    public void BuildSegments_PartialRedaction_CreatesMultipleSegments()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Birth Certificate");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[16].GlyphRectangle.Right,  // "Birth Certificate" = 17 chars
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Birth Certificate", bbox);
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

        // Redaction area covering "Birth" (first 5 letters) - use exact bounds
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,  // Up to 'h'
            letters[0].GlyphRectangle.Top);

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);  // Only one kept segment
        segments[0].Text.Should().Be(" Certificate");  // "Birth" removed, space and rest kept
        segments[0].Keep.Should().BeTrue();
        segments[0].StartIndex.Should().Be(5);  // Starts after "Birth"
    }

    [Fact]
    public void BuildSegments_SetsCorrectPositions()
    {
        // Arrange
        var pdfPath = CreateTempPdf("ABC");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[2].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("ABC", bbox);
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

        // Redact first letter 'A' - use exact bounds
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[0].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("BC");
        segments[0].StartX.Should().BeApproximately(letters[1].GlyphRectangle.Left, 1.0);
        segments[0].StartY.Should().BeApproximately(letters[1].GlyphRectangle.Bottom, 1.0);
    }

    [Fact]
    public void BuildSegments_CalculatesWidth()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Test");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[3].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Test", bbox);
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

        var redactionArea = new PdfRectangle(500, 500, 550, 520);  // No redaction

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Width.Should().BeGreaterThan(0);
        // Width should be sum of all letter widths
        var expectedWidth = letters.Take(4).Sum(l => l.GlyphRectangle.Width);
        segments[0].Width.Should().BeApproximately(expectedWidth, 1.0);
    }

    [Fact]
    public void BuildSegments_RedactMiddle_CreatesTwoSegments()
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
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

        // Redact middle letter 'C' (index 2)
        // Use exact letter bounds - no margin, to avoid overlapping with adjacent letters
        var redactionArea = new PdfRectangle(
            letters[2].GlyphRectangle.Left,
            letters[2].GlyphRectangle.Bottom,
            letters[2].GlyphRectangle.Right,
            letters[2].GlyphRectangle.Top);

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(2);
        segments[0].Text.Should().Be("AB");
        segments[1].Text.Should().Be("DE");
    }

    [Fact]
    public void BuildSegments_SetsOriginalText()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Hello");

        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var textOp = CreateTextOperation("Hello", bbox);
        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);
        var redactionArea = new PdfRectangle(500, 500, 550, 520);

        // Act
        var segments = _segmenter.BuildSegments(textOp, letterMatches, redactionArea);

        // Assert
        segments.Should().AllSatisfy(s => s.OriginalText.Should().Be("Hello"));
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
