using FluentAssertions;
using PdfEditor.Redaction.GlyphLevel;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class OperationReconstructorTests
{
    private readonly OperationReconstructor _reconstructor = new();

    [Fact]
    public void ReconstructOperations_NoSegments_ReturnsEmpty()
    {
        // Arrange
        var segments = new List<TextSegment>();
        var originalOp = CreateTextOperation("Hello", new PdfRectangle(100, 200, 150, 220));

        // Act
        var operations = _reconstructor.ReconstructOperations(segments, originalOp);

        // Assert
        operations.Should().BeEmpty();
    }

    [Fact]
    public void ReconstructOperations_SingleSegment_CreatesSingleOperation()
    {
        // Arrange
        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 5,
            Keep = true,
            StartX = 100,
            StartY = 200,
            Width = 50,
            Height = 20,
            OriginalText = "Hello"
        };

        var segments = new List<TextSegment> { segment };
        var originalOp = CreateTextOperation("Hello", new PdfRectangle(100, 200, 150, 220));

        // Act
        var operations = _reconstructor.ReconstructOperations(segments, originalOp);

        // Assert
        operations.Should().HaveCount(1);
        operations[0].Operator.Should().Be("Tj");
        operations[0].Text.Should().Be("Hello");
        operations[0].BoundingBox.Left.Should().Be(100);
        operations[0].BoundingBox.Bottom.Should().Be(200);
    }

    [Fact]
    public void ReconstructOperations_MultipleSegments_CreatesMultipleOperations()
    {
        // Arrange
        var segment1 = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 2,
            Keep = true,
            StartX = 100,
            StartY = 200,
            Width = 20,
            Height = 10,
            OriginalText = "ABCDE"
        };

        var segment2 = new TextSegment
        {
            StartIndex = 3,
            EndIndex = 5,
            Keep = true,
            StartX = 130,
            StartY = 200,
            Width = 20,
            Height = 10,
            OriginalText = "ABCDE"
        };

        var segments = new List<TextSegment> { segment1, segment2 };
        var originalOp = CreateTextOperation("ABCDE", new PdfRectangle(100, 200, 150, 210));

        // Act
        var operations = _reconstructor.ReconstructOperations(segments, originalOp);

        // Assert
        operations.Should().HaveCount(2);
        operations[0].Text.Should().Be("AB");
        operations[1].Text.Should().Be("DE");
    }

    [Fact]
    public void ReconstructOperations_PreservesFontSize()
    {
        // Arrange
        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 4,
            Keep = true,
            StartX = 100,
            StartY = 200,
            Width = 40,
            Height = 15,
            OriginalText = "Test"
        };

        var segments = new List<TextSegment> { segment };
        var originalOp = CreateTextOperation("Test", new PdfRectangle(100, 200, 140, 215), fontSize: 14.5);

        // Act
        var operations = _reconstructor.ReconstructOperations(segments, originalOp);

        // Assert
        operations[0].FontSize.Should().Be(14.5);
    }

    [Fact]
    public void ReconstructOperations_SetsBoundingBoxFromSegment()
    {
        // Arrange
        var segment = new TextSegment
        {
            StartIndex = 5,
            EndIndex = 16,
            Keep = true,
            StartX = 125.5,
            StartY = 300.25,
            Width = 100.75,
            Height = 12.5,
            OriginalText = "Birth Certificate"
        };

        var segments = new List<TextSegment> { segment };
        var originalOp = CreateTextOperation("Birth Certificate", new PdfRectangle(100, 300, 250, 312.5));

        // Act
        var operations = _reconstructor.ReconstructOperations(segments, originalOp);

        // Assert
        var bbox = operations[0].BoundingBox;
        bbox.Left.Should().Be(125.5);
        bbox.Bottom.Should().Be(300.25);
        bbox.Right.Should().Be(125.5 + 100.75);
        bbox.Top.Should().Be(300.25 + 12.5);
    }

    [Fact]
    public void CreatePositioningOperation_GeneratesTmOperator()
    {
        // Arrange
        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 3,
            Keep = true,
            StartX = 150.5,
            StartY = 250.75,
            Width = 30,
            Height = 12,
            OriginalText = "ABC"
        };

        // Act - pass font size (12pt) which will be used as scale factor in Tm
        var operation = _reconstructor.CreatePositioningOperation(segment, 12.0);

        // Assert
        operation.Operator.Should().Be("Tm");
        operation.Operands.Should().HaveCount(6);
        operation.Operands[0].Should().Be(12.0);  // a - horizontal scaling (font size)
        operation.Operands[1].Should().Be(0.0);   // b - vertical skew
        operation.Operands[2].Should().Be(0.0);   // c - horizontal skew
        operation.Operands[3].Should().Be(12.0);  // d - vertical scaling (font size)
        operation.Operands[4].Should().Be(150.5);  // e - x position
        operation.Operands[5].Should().Be(250.75);  // f - y position
    }

    [Fact]
    public void ReconstructWithPositioning_AlternatesTmAndTj()
    {
        // Arrange
        var segment1 = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 2,
            Keep = true,
            StartX = 100,
            StartY = 200,
            Width = 20,
            Height = 10,
            OriginalText = "ABCDE"
        };

        var segment2 = new TextSegment
        {
            StartIndex = 3,
            EndIndex = 5,
            Keep = true,
            StartX = 130,
            StartY = 200,
            Width = 20,
            Height = 10,
            OriginalText = "ABCDE"
        };

        var segments = new List<TextSegment> { segment1, segment2 };
        var originalOp = CreateTextOperation("ABCDE", new PdfRectangle(100, 200, 150, 210));

        // Act
        var operations = _reconstructor.ReconstructWithPositioning(segments, originalOp);

        // Assert
        operations.Should().HaveCount(7);  // BT, Tf, Tm, Tj, Tm, Tj, ET
        operations[0].Operator.Should().Be("BT");
        operations[1].Operator.Should().Be("Tf");
        operations[2].Operator.Should().Be("Tm");
        operations[3].Operator.Should().Be("Tj");
        operations[4].Operator.Should().Be("Tm");
        operations[5].Operator.Should().Be("Tj");
        operations[6].Operator.Should().Be("ET");
    }

    [Fact]
    public void ReconstructWithPositioning_TmPositionMatchesTjPosition()
    {
        // Arrange
        var segment = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 5,
            Keep = true,
            StartX = 123.45,
            StartY = 678.90,
            Width = 50,
            Height = 12,
            OriginalText = "Hello"
        };

        var segments = new List<TextSegment> { segment };
        var originalOp = CreateTextOperation("Hello", new PdfRectangle(100, 600, 200, 700));

        // Act
        var operations = _reconstructor.ReconstructWithPositioning(segments, originalOp);

        // Assert
        operations.Should().HaveCount(5);  // BT, Tf, Tm, Tj, ET

        operations[0].Operator.Should().Be("BT");
        operations[1].Operator.Should().Be("Tf");

        var tmOp = operations[2];
        tmOp.Operands[4].Should().Be(123.45);  // Tm x position
        tmOp.Operands[5].Should().Be(678.90);  // Tm y position

        var tjOp = (TextOperation)operations[3];
        tjOp.BoundingBox.Left.Should().Be(123.45);
        tjOp.BoundingBox.Bottom.Should().Be(678.90);

        operations[4].Operator.Should().Be("ET");
    }

    [Fact]
    public void ReconstructWithPositioning_PreservesTextContent()
    {
        // Arrange
        var segment1 = new TextSegment
        {
            StartIndex = 0,
            EndIndex = 5,
            Keep = true,
            StartX = 100,
            StartY = 200,
            Width = 50,
            Height = 12,
            OriginalText = "Birth Certificate"
        };

        var segment2 = new TextSegment
        {
            StartIndex = 6,
            EndIndex = 17,
            Keep = true,
            StartX = 160,
            StartY = 200,
            Width = 110,
            Height = 12,
            OriginalText = "Birth Certificate"
        };

        var segments = new List<TextSegment> { segment1, segment2 };
        var originalOp = CreateTextOperation("Birth Certificate", new PdfRectangle(100, 200, 270, 212));

        // Act
        var operations = _reconstructor.ReconstructWithPositioning(segments, originalOp);

        // Assert
        // Should be: BT, Tf, Tm, Tj("Birth"), Tm, Tj("Certificate"), ET
        var textOps = operations.Where(op => op.Operator == "Tj").Cast<TextOperation>().ToList();
        textOps.Should().HaveCount(2);
        textOps[0].Text.Should().Be("Birth");
        textOps[1].Text.Should().Be("Certificate");
    }

    // Helper Methods

    private static TextOperation CreateTextOperation(string text, PdfRectangle bbox, double fontSize = 12.0)
    {
        return new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { text },
            BoundingBox = bbox,
            StreamPosition = 0,
            Text = text,
            Glyphs = new List<GlyphPosition>(),
            FontSize = fontSize
        };
    }
}
