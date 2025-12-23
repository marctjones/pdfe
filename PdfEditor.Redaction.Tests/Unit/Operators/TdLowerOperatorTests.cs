using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextPositioning;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for Td (move to next line with offset) operator handler.
/// Unlike TD, Td does NOT set the text leading.
/// </summary>
public class TdLowerOperatorTests
{
    [Fact]
    public void TdHandler_UpdatesTextLineMatrix()
    {
        // Arrange
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLineMatrix = PdfMatrix.Identity;
        var operands = new object[] { 100.0, 50.0 };

        // Act
        handler.Handle(operands, state);

        // Assert - should translate by (tx, ty)
        state.TextLineMatrix.E.Should().Be(100.0);
        state.TextLineMatrix.F.Should().Be(50.0);
    }

    [Fact]
    public void TdHandler_SetsTextMatrixToTextLineMatrix()
    {
        // Arrange
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLineMatrix = PdfMatrix.Identity;
        var operands = new object[] { 200.0, 100.0 };

        // Act
        handler.Handle(operands, state);

        // Assert - text matrix should equal text line matrix
        state.TextMatrix.E.Should().Be(state.TextLineMatrix.E);
        state.TextMatrix.F.Should().Be(state.TextLineMatrix.F);
    }

    [Fact]
    public void TdHandler_DoesNotSetTextLeading()
    {
        // Arrange - this is the key difference from TD
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLeading = 12.0; // Set initial leading
        var operands = new object[] { 0.0, -15.0 };

        // Act
        handler.Handle(operands, state);

        // Assert - leading should NOT change (unlike TD)
        state.TextLeading.Should().Be(12.0);
    }

    [Fact]
    public void TdHandler_AccumulatesTranslation()
    {
        // Arrange
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLineMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 50, 100);
        var operands = new object[] { 25.0, 30.0 };

        // Act
        handler.Handle(operands, state);

        // Assert - translation should be cumulative
        state.TextLineMatrix.E.Should().Be(75.0); // 50 + 25
        state.TextLineMatrix.F.Should().Be(130.0); // 100 + 30
    }

    [Fact]
    public void TdHandler_HandlesIntegerOperands()
    {
        // Arrange
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        state.TextLineMatrix = PdfMatrix.Identity;
        var operands = new object[] { 72, -12 };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.TextLineMatrix.E.Should().Be(72);
        state.TextLineMatrix.F.Should().Be(-12);
    }

    [Fact]
    public void TdHandler_ReturnsTextStateOperation()
    {
        // Arrange
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        var operands = new object[] { 0.0, 0.0 };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        result!.Operator.Should().Be("Td");
    }

    [Fact]
    public void TdHandler_EmptyOperands_DoesNotCrash()
    {
        // Arrange
        var handler = new TdLowerOperatorHandler();
        var state = new PdfParserState(792);
        var originalE = state.TextLineMatrix.E;
        var originalF = state.TextLineMatrix.F;

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert - should return operation but not change state
        result.Should().NotBeNull();
        state.TextLineMatrix.E.Should().Be(originalE);
        state.TextLineMatrix.F.Should().Be(originalF);
    }
}
