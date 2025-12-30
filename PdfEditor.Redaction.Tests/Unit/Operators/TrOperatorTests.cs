using Xunit;
using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextState;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for the Tr (text rendering mode) operator handler.
/// </summary>
public class TrOperatorTests
{
    private readonly TrOperatorHandler _handler;
    private readonly PdfParserState _state;

    public TrOperatorTests()
    {
        _handler = new TrOperatorHandler();
        _state = new PdfParserState(792.0); // Letter height
    }

    [Fact]
    public void OperatorName_IsTr()
    {
        _handler.OperatorName.Should().Be("Tr");
    }

    [Theory]
    [InlineData(0)] // Fill text (default)
    [InlineData(1)] // Stroke text
    [InlineData(2)] // Fill then stroke
    [InlineData(3)] // Invisible - CRITICAL
    [InlineData(4)] // Fill and add to clipping path
    [InlineData(5)] // Stroke and add to clipping path
    [InlineData(6)] // Fill, stroke, and add to clipping path
    [InlineData(7)] // Add to clipping path only
    public void Handle_SetsTextRenderingMode(int mode)
    {
        // Arrange
        var operands = new List<object> { (double)mode };

        // Act
        var result = _handler.Handle(operands, _state);

        // Assert
        _state.TextRenderingMode.Should().Be(mode);
        result.Should().NotBeNull();
        result.Should().BeOfType<TextStateOperation>();
    }

    [Fact]
    public void Handle_InvisibleMode_SetsMode3()
    {
        // Arrange - Mode 3 is invisible text, critical for security
        var operands = new List<object> { 3.0 };

        // Act
        var result = _handler.Handle(operands, _state);

        // Assert
        _state.TextRenderingMode.Should().Be(3);
    }

    [Fact]
    public void Handle_IntOperand_Works()
    {
        // Arrange
        var operands = new List<object> { 3 }; // int, not double

        // Act
        var result = _handler.Handle(operands, _state);

        // Assert
        _state.TextRenderingMode.Should().Be(3);
    }

    [Fact]
    public void Handle_EmptyOperands_DoesNotChangeState()
    {
        // Arrange
        _state.TextRenderingMode = 0;
        var operands = new List<object>();

        // Act
        var result = _handler.Handle(operands, _state);

        // Assert
        _state.TextRenderingMode.Should().Be(0, "State should remain unchanged with no operands");
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(100)]
    public void Handle_InvalidMode_DoesNotChangeState(int invalidMode)
    {
        // Arrange
        _state.TextRenderingMode = 0;
        var operands = new List<object> { (double)invalidMode };

        // Act
        var result = _handler.Handle(operands, _state);

        // Assert
        _state.TextRenderingMode.Should().Be(0, "Invalid mode should not change state");
        result.Should().NotBeNull();
    }

    [Fact]
    public void Handle_ReturnsTextStateOperation()
    {
        // Arrange
        var operands = new List<object> { 3.0 };
        _state.StreamPosition = 42;
        _state.InTextObject = true;

        // Act
        var result = _handler.Handle(operands, _state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        var textStateOp = (TextStateOperation)result!;
        textStateOp.Operator.Should().Be("Tr");
        textStateOp.StreamPosition.Should().Be(42);
        textStateOp.InsideTextBlock.Should().BeTrue();
    }

    [Fact]
    public void OperatorRegistry_HasTrHandler()
    {
        // Arrange
        var registry = OperatorRegistry.CreateDefault();

        // Assert
        registry.HasHandler("Tr").Should().BeTrue("Tr operator should be registered");
    }
}
