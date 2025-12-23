using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextShowing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for Tj (show text) operator handler.
/// </summary>
public class TjOperatorTests
{
    [Fact]
    public void TjHandler_ReturnsTextOperation()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var operands = new object[] { "Hello" };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeOfType<TextOperation>();
        result!.Operator.Should().Be("Tj");
    }

    [Fact]
    public void TjHandler_ExtractsText()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var operands = new object[] { "Hello World" };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Hello World");
    }

    [Fact]
    public void TjHandler_CreatesGlyphsForEachCharacter()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var operands = new object[] { "ABC" };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Glyphs.Should().HaveCount(3);
        result.Glyphs[0].Character.Should().Be("A");
        result.Glyphs[1].Character.Should().Be("B");
        result.Glyphs[2].Character.Should().Be("C");
    }

    [Fact]
    public void TjHandler_UsesFontSize()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 24; // Large font
        var operands = new object[] { "X" };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.FontSize.Should().Be(24);

        // Height should be approximately font size
        var height = result.Glyphs[0].BoundingBox.Top - result.Glyphs[0].BoundingBox.Bottom;
        height.Should().BeApproximately(24, 1);
    }

    [Fact]
    public void TjHandler_UsesFontName()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.FontName = "/F1";
        var operands = new object[] { "Test" };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.FontName.Should().Be("/F1");
    }

    [Fact]
    public void TjHandler_CalculatesCorrectBoundingBox()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.TextMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 100, 200);
        var operands = new object[] { "Hi" };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.BoundingBox.Left.Should().Be(100);
        result.BoundingBox.Bottom.Should().Be(200);
        result.BoundingBox.Right.Should().BeGreaterThan(100);
        result.BoundingBox.Top.Should().BeGreaterThan(200);
    }

    [Fact]
    public void TjHandler_AdvancesTextMatrix()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.TextMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 0, 0);
        var operands = new object[] { "ABC" };

        // Act
        handler.Handle(operands, state);

        // Assert - text matrix E (x position) should have advanced
        state.TextMatrix.E.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TjHandler_HandlesByteArray()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var bytes = new byte[] { 0x48, 0x69 }; // "Hi" in ASCII
        var operands = new object[] { bytes };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Hi");
    }

    [Fact]
    public void TjHandler_EmptyString_ReturnsNull()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { "" };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TjHandler_NoOperands_ReturnsNull()
    {
        // Arrange
        var handler = new TjOperatorHandler();
        var state = new PdfParserState(792);

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert
        result.Should().BeNull();
    }
}
