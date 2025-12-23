using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextPositioning;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for T* (move to start of next text line) operator handler.
/// T* is equivalent to: 0 -TL Td
/// </summary>
public class TStarOperatorTests
{
    [Fact]
    public void TStarHandler_MovesDownByTextLeading()
    {
        // Arrange
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 12.0;
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 100, 500);

        // Act
        handler.Handle(Array.Empty<object>(), state);

        // Assert - should move down by text leading (y -= TL)
        state.TextLineMatrix.E.Should().Be(100); // x unchanged
        state.TextLineMatrix.F.Should().Be(488); // 500 - 12
    }

    [Fact]
    public void TStarHandler_SetsTextMatrixToTextLineMatrix()
    {
        // Arrange
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 14.0;
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 50, 400);

        // Act
        handler.Handle(Array.Empty<object>(), state);

        // Assert - text matrix should equal text line matrix
        state.TextMatrix.E.Should().Be(state.TextLineMatrix.E);
        state.TextMatrix.F.Should().Be(state.TextLineMatrix.F);
    }

    [Fact]
    public void TStarHandler_UsesNegativeLeading()
    {
        // Arrange - negative leading moves up instead of down
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = -10.0;
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 0, 200);

        // Act
        handler.Handle(Array.Empty<object>(), state);

        // Assert - y should increase (move up)
        state.TextLineMatrix.F.Should().Be(210); // 200 - (-10) = 210
    }

    [Fact]
    public void TStarHandler_ZeroLeading_NoMovement()
    {
        // Arrange
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 0.0;
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 72, 720);

        // Act
        handler.Handle(Array.Empty<object>(), state);

        // Assert - no vertical movement
        state.TextLineMatrix.E.Should().Be(72);
        state.TextLineMatrix.F.Should().Be(720);
    }

    [Fact]
    public void TStarHandler_AccumulatesWithPreviousMoves()
    {
        // Arrange
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 12.0;
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 50, 600);

        // Act - call twice
        handler.Handle(Array.Empty<object>(), state);
        handler.Handle(Array.Empty<object>(), state);

        // Assert - should have moved down twice
        state.TextLineMatrix.E.Should().Be(50); // x unchanged
        state.TextLineMatrix.F.Should().Be(576); // 600 - 12 - 12
    }

    [Fact]
    public void TStarHandler_ReturnsTextStateOperation()
    {
        // Arrange
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        result!.Operator.Should().Be("T*");
    }

    [Fact]
    public void TStarHandler_IgnoresAnyOperands()
    {
        // Arrange - T* takes no operands, but should handle gracefully if given
        var handler = new TStarOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 12.0;
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 0, 100);
        var operands = new object[] { 99.0, 88.0 }; // Should be ignored

        // Act
        var result = handler.Handle(operands, state);

        // Assert - should still use TextLeading, not operands
        result.Should().NotBeNull();
        state.TextLineMatrix.F.Should().Be(88); // 100 - 12
    }
}
