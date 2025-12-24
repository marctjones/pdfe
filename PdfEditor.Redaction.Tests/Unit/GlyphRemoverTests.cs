using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Building;
using PdfEditor.Redaction.GlyphLevel;
using PdfEditor.Redaction.Tests.Utilities;
using UglyToad.PdfPig;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class GlyphRemoverTests : IDisposable
{
    private readonly GlyphRemover _glyphRemover;
    private readonly List<string> _tempFiles = new();

    public GlyphRemoverTests()
    {
        var builder = new ContentStreamBuilder();
        _glyphRemover = new GlyphRemover(builder);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void ProcessOperations_NoTextOperations_ReturnsUnchanged()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Dummy");
        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var operations = new List<PdfOperation>
        {
            new StateOperation
            {
                Operator = "q",
                Operands = new List<object>(),
                StreamPosition = 0
            },
            new StateOperation
            {
                Operator = "Q",
                Operands = new List<object>(),
                StreamPosition = 1
            }
        };

        var redactionArea = new PdfRectangle(100, 200, 150, 220);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        result.Should().HaveCount(2);
        result[0].Operator.Should().Be("q");
        result[1].Operator.Should().Be("Q");
    }

    [Fact]
    public void ProcessOperations_TextOperationOutsideArea_KeepsUnchanged()
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

        var operations = new List<PdfOperation>
        {
            CreateTextOperation("Hello", bbox)
        };

        // Redaction area far away
        var redactionArea = new PdfRectangle(500, 500, 550, 550);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        result.Should().HaveCount(1);
        var textOp = result[0] as TextOperation;
        textOp.Should().NotBeNull();
        textOp!.Text.Should().Be("Hello");
    }

    [Fact]
    public void ProcessOperations_TextOperationCompletelyInArea_RemovesAll()
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

        // NEW BEHAVIOR: TextOperations must be inside BT...ET block
        var operations = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = new List<object>(), StreamPosition = 0, InsideTextBlock = false },
            CreateTextOperation("Hello", bbox, streamPosition: 1),
            new TextStateOperation { Operator = "ET", Operands = new List<object>(), StreamPosition = 2, InsideTextBlock = true }
        };

        // Redaction area covering all letters
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left - 10,
            letters[0].GlyphRectangle.Bottom - 10,
            letters[4].GlyphRectangle.Right + 10,
            letters[0].GlyphRectangle.Top + 10);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        // NEW BEHAVIOR: All text removed, BT/ET filtered out from reconstructed block
        result.Should().BeEmpty();
    }

    [Fact]
    public void ProcessOperations_PartialRedaction_CreatesMultipleOperations()
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

        // NEW BEHAVIOR: TextOperations must be inside BT...ET block
        var operations = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = new List<object>(), StreamPosition = 0, InsideTextBlock = false },
            CreateTextOperation("ABCDE", bbox, streamPosition: 1),
            new TextStateOperation { Operator = "ET", Operands = new List<object>(), StreamPosition = 2, InsideTextBlock = true }
        };

        // Redact middle letter 'C' only (exact bounds, no margin)
        var redactionArea = new PdfRectangle(
            letters[2].GlyphRectangle.Left,
            letters[2].GlyphRectangle.Bottom,
            letters[2].GlyphRectangle.Right,
            letters[2].GlyphRectangle.Top);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        // NEW BEHAVIOR: Should get reconstructed block: BT, Tf, Tm, Tj("AB"), Tm, Tj("DE"), ET = 7 operations
        result.Should().HaveCount(7);

        result[0].Operator.Should().Be("BT");
        result[1].Operator.Should().Be("Tf");
        result[2].Operator.Should().Be("Tm");
        result[3].Operator.Should().Be("Tj");
        ((TextOperation)result[3]).Text.Should().Be("AB");

        result[4].Operator.Should().Be("Tm");
        result[5].Operator.Should().Be("Tj");
        ((TextOperation)result[5]).Text.Should().Be("DE");
        result[6].Operator.Should().Be("ET");
    }

    [Fact]
    public void ProcessOperations_PreservesNonTextOperations()
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

        // NEW BEHAVIOR: TextOperations must be inside BT...ET block
        var operations = new List<PdfOperation>
        {
            new StateOperation { Operator = "q", Operands = new List<object>(), StreamPosition = 0, InsideTextBlock = false },
            new TextStateOperation { Operator = "BT", Operands = new List<object>(), StreamPosition = 1, InsideTextBlock = false },
            CreateTextOperation("Test", bbox, streamPosition: 2),
            new TextStateOperation { Operator = "ET", Operands = new List<object>(), StreamPosition = 3, InsideTextBlock = true },
            new StateOperation { Operator = "Q", Operands = new List<object>(), StreamPosition = 4, InsideTextBlock = false }
        };

        // Redact all text
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left - 10,
            letters[0].GlyphRectangle.Bottom - 10,
            letters[3].GlyphRectangle.Right + 10,
            letters[0].GlyphRectangle.Top + 10);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        // NEW BEHAVIOR: q/Q preserved, BT/ET filtered from reconstructed block (all text removed)
        result.Should().HaveCount(2);
        result[0].Operator.Should().Be("q");
        result[1].Operator.Should().Be("Q");
    }

    [Fact]
    public void ProcessOperations_RedactFirstPart_KeepsRest()
    {
        // Arrange
        var pdfPath = CreateTempPdf("Birth Certificate");
        using var document = PdfDocument.Open(pdfPath);
        var letters = document.GetPage(1).Letters;

        var bbox = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[16].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        var operations = new List<PdfOperation>
        {
            CreateTextOperation("Birth Certificate", bbox)
        };

        // Redact "Birth" (first 5 letters) - exact bounds, no margin
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left,
            letters[0].GlyphRectangle.Bottom,
            letters[4].GlyphRectangle.Right,
            letters[0].GlyphRectangle.Top);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        // Should keep " Certificate" (space + rest)
        var textOps = result.OfType<TextOperation>().ToList();
        textOps.Should().Contain(op => op.Text.Contains("Certificate"));
    }

    [Fact]
    public void ProcessOperations_MultipleTextOperations_ProcessesAll()
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

        // NEW BEHAVIOR: TextOperations must be inside BT...ET blocks
        var operations = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = new List<object>(), StreamPosition = 0, InsideTextBlock = false },
            CreateTextOperation("ABC", bbox, streamPosition: 1),  // Will be redacted
            new TextStateOperation { Operator = "ET", Operands = new List<object>(), StreamPosition = 2, InsideTextBlock = true },
            new StateOperation { Operator = "q", Operands = new List<object>(), StreamPosition = 3, InsideTextBlock = false },
            new TextStateOperation { Operator = "BT", Operands = new List<object>(), StreamPosition = 4, InsideTextBlock = false },
            CreateTextOperation("ABC", bbox, streamPosition: 5),   // Will be redacted
            new TextStateOperation { Operator = "ET", Operands = new List<object>(), StreamPosition = 6, InsideTextBlock = true }
        };

        // Redact all text
        var redactionArea = new PdfRectangle(
            letters[0].GlyphRectangle.Left - 10,
            letters[0].GlyphRectangle.Bottom - 10,
            letters[2].GlyphRectangle.Right + 10,
            letters[0].GlyphRectangle.Top + 10);

        // Act
        var result = _glyphRemover.ProcessOperations(operations, letters, redactionArea);

        // Assert
        // NEW BEHAVIOR: Both text blocks completely redacted (BT/ET filtered), only 'q' remains
        result.Should().HaveCount(1);
        result[0].Operator.Should().Be("q");
    }

    // Helper Methods

    private string CreateTempPdf(string text)
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        TestPdfGenerator.CreateSimpleTextPdf(path, text);
        return path;
    }

    private static TextOperation CreateTextOperation(string text, PdfRectangle bbox, int streamPosition = 1)
    {
        return new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { text },
            BoundingBox = bbox,
            StreamPosition = streamPosition,
            Text = text,
            Glyphs = new List<GlyphPosition>(),
            FontSize = 12,
            InsideTextBlock = true  // TextOperations are inside BT...ET
        };
    }
}
