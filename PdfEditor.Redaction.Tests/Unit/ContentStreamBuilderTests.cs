using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Building;
using System.Text;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class ContentStreamBuilderTests
{
    private readonly ContentStreamBuilder _builder = new();

    [Fact]
    public void Build_EmptyOperations_ReturnsEmptyBytes()
    {
        // Arrange
        var operations = new List<PdfOperation>();

        // Act
        var bytes = _builder.Build(operations);

        // Assert
        bytes.Should().BeEmpty();
    }

    [Fact]
    public void Build_SingleTjOperation_SerializesCorrectly()
    {
        // Arrange
        var operation = new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { "Hello" },
            BoundingBox = new PdfRectangle(100, 200, 150, 220),
            StreamPosition = 0,
            Text = "Hello",
            Glyphs = new List<GlyphPosition>(),
            FontSize = 12
        };

        var operations = new List<PdfOperation> { operation };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);

        // Assert
        result.Trim().Should().Be("(Hello) Tj");
    }

    [Fact]
    public void Build_TmOperation_SerializesCorrectly()
    {
        // Arrange
        var operation = new StateOperation
        {
            Operator = "Tm",
            Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 100.5, 200.75 },
            StreamPosition = 0
        };

        var operations = new List<PdfOperation> { operation };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);

        // Assert
        result.Trim().Should().Be("1 0 0 1 100.5 200.75 Tm");
    }

    [Fact]
    public void Build_TmFollowedByTj_PreservesOrder()
    {
        // Arrange
        var tmOp = new StateOperation
        {
            Operator = "Tm",
            Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 150.0, 250.0 },
            StreamPosition = 0
        };

        var tjOp = new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { "Test" },
            BoundingBox = new PdfRectangle(150, 250, 200, 262),
            StreamPosition = 1,
            Text = "Test",
            Glyphs = new List<GlyphPosition>(),
            FontSize = 12
        };

        var operations = new List<PdfOperation> { tmOp, tjOp };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Assert
        lines.Should().HaveCount(2);
        lines[0].Trim().Should().Be("1 0 0 1 150 250 Tm");
        lines[1].Trim().Should().Be("(Test) Tj");
    }

    [Fact]
    public void Build_MultipleTmTjPairs_SerializesAll()
    {
        // Arrange
        var operations = new List<PdfOperation>
        {
            new StateOperation
            {
                Operator = "Tm",
                Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 100.0, 200.0 },
                StreamPosition = 0
            },
            new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { "AB" },
                BoundingBox = new PdfRectangle(100, 200, 120, 210),
                StreamPosition = 1,
                Text = "AB",
                Glyphs = new List<GlyphPosition>(),
                FontSize = 12
            },
            new StateOperation
            {
                Operator = "Tm",
                Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 130.0, 200.0 },
                StreamPosition = 2
            },
            new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { "DE" },
                BoundingBox = new PdfRectangle(130, 200, 150, 210),
                StreamPosition = 3,
                Text = "DE",
                Glyphs = new List<GlyphPosition>(),
                FontSize = 12
            }
        };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Assert
        lines.Should().HaveCount(4);
        lines[0].Trim().Should().Be("1 0 0 1 100 200 Tm");
        lines[1].Trim().Should().Be("(AB) Tj");
        lines[2].Trim().Should().Be("1 0 0 1 130 200 Tm");
        lines[3].Trim().Should().Be("(DE) Tj");
    }

    [Fact]
    public void Build_PreservesDecimalPrecision()
    {
        // Arrange
        var operation = new StateOperation
        {
            Operator = "Tm",
            Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 123.456, 789.012 },
            StreamPosition = 0
        };

        var operations = new List<PdfOperation> { operation };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);

        // Assert
        result.Should().Contain("123.456");
        result.Should().Contain("789.012");
    }

    [Fact]
    public void Build_EscapesSpecialCharactersInStrings()
    {
        // Arrange
        var operation = new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { "Test (with) parens" },
            BoundingBox = new PdfRectangle(100, 200, 200, 212),
            StreamPosition = 0,
            Text = "Test (with) parens",
            Glyphs = new List<GlyphPosition>(),
            FontSize = 12
        };

        var operations = new List<PdfOperation> { operation };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);

        // Assert
        result.Should().Contain("\\(");
        result.Should().Contain("\\)");
    }

    [Fact]
    public void Build_OrdersOperationsByStreamPosition()
    {
        // Arrange - Add operations out of order
        var operations = new List<PdfOperation>
        {
            new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { "Second" },
                BoundingBox = new PdfRectangle(100, 200, 150, 212),
                StreamPosition = 10,
                Text = "Second",
                Glyphs = new List<GlyphPosition>(),
                FontSize = 12
            },
            new StateOperation
            {
                Operator = "Tm",
                Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 100.0, 200.0 },
                StreamPosition = 5
            }
        };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Assert - Should be ordered by StreamPosition
        lines[0].Should().Contain("Tm");  // StreamPosition 5
        lines[1].Should().Contain("Tj");  // StreamPosition 10
    }

    [Fact]
    public void Build_HandlesNonIntegerPosition()
    {
        // Arrange
        var operation = new StateOperation
        {
            Operator = "Tm",
            Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 125.5, 300.25 },
            StreamPosition = 0
        };

        var operations = new List<PdfOperation> { operation };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);

        // Assert
        result.Should().Contain("125.5");
        result.Should().Contain("300.25");
    }

    [Fact]
    public void Build_IntegerOperandsNoDecimal()
    {
        // Arrange
        var operation = new StateOperation
        {
            Operator = "Tm",
            Operands = new List<object> { 1.0, 0.0, 0.0, 1.0, 100.0, 200.0 },
            StreamPosition = 0
        };

        var operations = new List<PdfOperation> { operation };

        // Act
        var bytes = _builder.Build(operations);
        var result = Encoding.ASCII.GetString(bytes);

        // Assert
        // Integer values should not have decimal point
        result.Trim().Should().Be("1 0 0 1 100 200 Tm");
    }
}
