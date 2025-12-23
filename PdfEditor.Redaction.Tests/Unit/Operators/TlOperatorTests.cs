using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextState;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for TL (set text leading) operator handler.
/// Text leading is the vertical distance between baselines of adjacent lines.
/// </summary>
public class TlOperatorTests
{
    [Fact]
    public void TlHandler_SetsTextLeading()
    {
        // Arrange
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 14.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLeading.Should().Be(14.0);
    }

    [Fact]
    public void TlHandler_OverridesPreviousLeading()
    {
        // Arrange
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 12.0;
        var operands = new object[] { 16.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLeading.Should().Be(16.0);
    }

    [Fact]
    public void TlHandler_HandlesIntegerOperand()
    {
        // Arrange
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 12 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLeading.Should().Be(12);
    }

    [Fact]
    public void TlHandler_HandlesNegativeLeading()
    {
        // Arrange - negative leading moves text up instead of down
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { -10.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLeading.Should().Be(-10.0);
    }

    [Fact]
    public void TlHandler_HandlesZeroLeading()
    {
        // Arrange
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 12.0; // Previous value
        var operands = new object[] { 0.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLeading.Should().Be(0.0);
    }

    [Fact]
    public void TlHandler_ReturnsTextStateOperation()
    {
        // Arrange
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 12.0 };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        result!.Operator.Should().Be("TL");
    }

    [Fact]
    public void TlHandler_EmptyOperands_DoesNotCrash()
    {
        // Arrange
        var handler = new TlOperatorHandler();
        var state = new PdfParserState(792);
        var originalLeading = state.TextLeading;

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert - should return operation but not change state
        result.Should().NotBeNull();
        state.TextLeading.Should().Be(originalLeading);
    }
}
