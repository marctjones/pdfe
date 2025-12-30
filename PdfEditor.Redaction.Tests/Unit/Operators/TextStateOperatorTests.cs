using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextState;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Unit tests for text state operators (Tc, Tw, Tz, Ts).
/// </summary>
public class TextStateOperatorTests
{
    [Fact]
    public void TcOperator_SetsCharacterSpacing()
    {
        // Arrange
        var handler = new TcOperatorHandler();
        var state = new PdfParserState(792.0);
        var operands = new List<object> { 2.5 };

        // Act
        var operation = handler.Handle(operands, state);

        // Assert
        state.CharacterSpacing.Should().Be(2.5);
        operation.Should().NotBeNull();
        operation.Should().BeOfType<TextStateOperation>();
        ((TextStateOperation)operation!).Operator.Should().Be("Tc");
    }

    [Fact]
    public void TwOperator_SetsWordSpacing()
    {
        // Arrange
        var handler = new TwOperatorHandler();
        var state = new PdfParserState(792.0);
        var operands = new List<object> { 1.0 };

        // Act
        var operation = handler.Handle(operands, state);

        // Assert
        state.WordSpacing.Should().Be(1.0);
        operation.Should().NotBeNull();
        ((TextStateOperation)operation!).Operator.Should().Be("Tw");
    }

    [Fact]
    public void TzOperator_SetsHorizontalScaling()
    {
        // Arrange
        var handler = new TzOperatorHandler();
        var state = new PdfParserState(792.0);
        state.HorizontalScaling.Should().Be(100.0); // Default
        var operands = new List<object> { 50.0 };

        // Act
        var operation = handler.Handle(operands, state);

        // Assert
        state.HorizontalScaling.Should().Be(50.0);
        operation.Should().NotBeNull();
        ((TextStateOperation)operation!).Operator.Should().Be("Tz");
    }

    [Fact]
    public void TsOperator_SetsTextRise()
    {
        // Arrange
        var handler = new TsOperatorHandler();
        var state = new PdfParserState(792.0);
        var operands = new List<object> { 5.0 };

        // Act
        var operation = handler.Handle(operands, state);

        // Assert
        state.TextRise.Should().Be(5.0);
        operation.Should().NotBeNull();
        ((TextStateOperation)operation!).Operator.Should().Be("Ts");
    }

    [Fact]
    public void TextStateOperators_HandleIntegerOperands()
    {
        // Arrange
        var tcHandler = new TcOperatorHandler();
        var state = new PdfParserState(792.0);
        var operands = new List<object> { 3 }; // int, not double

        // Act
        tcHandler.Handle(operands, state);

        // Assert
        state.CharacterSpacing.Should().Be(3.0);
    }

    [Fact]
    public void TextStateOperators_HandleStringOperands()
    {
        // Arrange
        var twHandler = new TwOperatorHandler();
        var state = new PdfParserState(792.0);
        var operands = new List<object> { "1.5" }; // string

        // Act
        twHandler.Handle(operands, state);

        // Assert
        state.WordSpacing.Should().Be(1.5);
    }

    [Fact]
    public void TextStateOperators_HandleEmptyOperands()
    {
        // Arrange
        var tzHandler = new TzOperatorHandler();
        var state = new PdfParserState(792.0);
        state.HorizontalScaling = 80.0;
        var operands = new List<object>(); // Empty

        // Act
        var operation = tzHandler.Handle(operands, state);

        // Assert
        state.HorizontalScaling.Should().Be(80.0); // Unchanged
        operation.Should().NotBeNull();
    }

    [Fact]
    public void OperatorRegistry_RegistersAllTextStateOperators()
    {
        // Arrange
        var registry = OperatorRegistry.CreateDefault();

        // Assert
        registry.HasHandler("Tc").Should().BeTrue("Tc (character spacing) should be registered");
        registry.HasHandler("Tw").Should().BeTrue("Tw (word spacing) should be registered");
        registry.HasHandler("Tz").Should().BeTrue("Tz (horizontal scaling) should be registered");
        registry.HasHandler("Ts").Should().BeTrue("Ts (text rise) should be registered");
        registry.HasHandler("TL").Should().BeTrue("TL (text leading) should be registered");
        registry.HasHandler("Tf").Should().BeTrue("Tf (font) should be registered");
        registry.HasHandler("Tr").Should().BeTrue("Tr (render mode) should be registered");
    }
}
