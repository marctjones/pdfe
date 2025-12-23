using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextObject;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for BT (begin text) and ET (end text) operator handlers.
/// </summary>
public class BtEtOperatorTests
{
    [Fact]
    public void BtHandler_SetsInTextObjectTrue()
    {
        // Arrange
        var handler = new BtOperatorHandler();
        var state = new PdfParserState(792); // Letter page height

        // Act
        handler.Handle(Array.Empty<object>(), state);

        // Assert
        state.InTextObject.Should().BeTrue();
    }

    [Fact]
    public void BtHandler_ResetsTextMatricesToIdentity()
    {
        // Arrange
        var handler = new BtOperatorHandler();
        var state = new PdfParserState(792);

        // Modify matrices first
        state.TextMatrix = PdfMatrix.FromOperands(2, 0, 0, 2, 100, 200);
        state.TextLineMatrix = PdfMatrix.FromOperands(2, 0, 0, 2, 100, 200);

        // Act
        handler.Handle(Array.Empty<object>(), state);

        // Assert
        state.TextMatrix.A.Should().Be(1);
        state.TextMatrix.D.Should().Be(1);
        state.TextMatrix.E.Should().Be(0);
        state.TextMatrix.F.Should().Be(0);

        state.TextLineMatrix.A.Should().Be(1);
        state.TextLineMatrix.D.Should().Be(1);
    }

    [Fact]
    public void BtHandler_ReturnsTextStateOperation()
    {
        // Arrange
        var handler = new BtOperatorHandler();
        var state = new PdfParserState(792);

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        result!.Operator.Should().Be("BT");
    }

    [Fact]
    public void EtHandler_SetsInTextObjectFalse()
    {
        // Arrange
        var btHandler = new BtOperatorHandler();
        var etHandler = new EtOperatorHandler();
        var state = new PdfParserState(792);

        // Enter text object first
        btHandler.Handle(Array.Empty<object>(), state);
        state.InTextObject.Should().BeTrue();

        // Act
        etHandler.Handle(Array.Empty<object>(), state);

        // Assert
        state.InTextObject.Should().BeFalse();
    }

    [Fact]
    public void EtHandler_ReturnsTextStateOperation()
    {
        // Arrange
        var handler = new EtOperatorHandler();
        var state = new PdfParserState(792);

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert
        result.Should().BeOfType<TextStateOperation>();
        result!.Operator.Should().Be("ET");
    }

    [Fact]
    public void MultipleBtEtBlocks_WorkCorrectly()
    {
        // Arrange
        var btHandler = new BtOperatorHandler();
        var etHandler = new EtOperatorHandler();
        var state = new PdfParserState(792);

        // First block
        btHandler.Handle(Array.Empty<object>(), state);
        state.InTextObject.Should().BeTrue();
        etHandler.Handle(Array.Empty<object>(), state);
        state.InTextObject.Should().BeFalse();

        // Second block
        btHandler.Handle(Array.Empty<object>(), state);
        state.InTextObject.Should().BeTrue();
        etHandler.Handle(Array.Empty<object>(), state);
        state.InTextObject.Should().BeFalse();
    }
}
