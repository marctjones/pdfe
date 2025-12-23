using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextPositioning;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for Tm (set text matrix) operator handler.
/// </summary>
public class TmOperatorTests
{
    [Fact]
    public void TmHandler_SetsTextMatrix()
    {
        // Arrange
        var handler = new TmOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 1.0, 0.0, 0.0, 1.0, 100.0, 200.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextMatrix.A.Should().Be(1.0);
        state.TextMatrix.B.Should().Be(0.0);
        state.TextMatrix.C.Should().Be(0.0);
        state.TextMatrix.D.Should().Be(1.0);
        state.TextMatrix.E.Should().Be(100.0);
        state.TextMatrix.F.Should().Be(200.0);
    }

    [Fact]
    public void TmHandler_SetsTextLineMatrixToSameValue()
    {
        // Arrange
        var handler = new TmOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 2.0, 0.0, 0.0, 2.0, 50.0, 100.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLineMatrix.A.Should().Be(state.TextMatrix.A);
        state.TextLineMatrix.B.Should().Be(state.TextMatrix.B);
        state.TextLineMatrix.C.Should().Be(state.TextMatrix.C);
        state.TextLineMatrix.D.Should().Be(state.TextMatrix.D);
        state.TextLineMatrix.E.Should().Be(state.TextMatrix.E);
        state.TextLineMatrix.F.Should().Be(state.TextMatrix.F);
    }

    [Fact]
    public void TmHandler_HandlesIntegerOperands()
    {
        // Arrange
        var handler = new TmOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 1, 0, 0, 1, 72, 720 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextMatrix.E.Should().Be(72);
        state.TextMatrix.F.Should().Be(720);
    }

    [Fact]
    public void TmHandler_HandlesScalingMatrix()
    {
        // Arrange: 2x scaling
        var handler = new TmOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 2.0, 0.0, 0.0, 2.0, 0.0, 0.0 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextMatrix.A.Should().Be(2.0);
        state.TextMatrix.D.Should().Be(2.0);
    }

    [Fact]
    public void TmHandler_ReturnsTextStateOperation()
    {
        // Arrange
        var handler = new TmOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 1.0, 0.0, 0.0, 1.0, 0.0, 0.0 };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        result!.Operator.Should().Be("Tm");
    }

    [Fact]
    public void TmHandler_EmptyOperands_DoesNotCrash()
    {
        // Arrange
        var handler = new TmOperatorHandler();
        var state = new PdfParserState(792);

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert - should return operation but not change state
        result.Should().NotBeNull();
    }
}
