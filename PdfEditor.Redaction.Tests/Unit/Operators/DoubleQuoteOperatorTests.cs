using FluentAssertions;
using PdfEditor.Redaction.ContentStream;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextShowing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for " (double quote) operator handler.
/// Issue #82: Implement " text-showing operator for legacy PDF support.
/// </summary>
public class DoubleQuoteOperatorTests
{
    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void OperatorName_IsDoubleQuote()
    {
        var handler = new DoubleQuoteOperatorHandler();
        handler.OperatorName.Should().Be("\"");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_WithThreeOperands_ReturnsTextOperation()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.TextLeading = 14;

        // aw=2.0, ac=0.5, (string)
        var operands = new List<object> { 2.0, 0.5, "Hello" };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<TextOperation>();

        var textOp = (TextOperation)result!;
        textOp.Operator.Should().Be("\"");
        textOp.Text.Should().Be("Hello");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_SetsWordSpacing()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.WordSpacing = 0; // Initial value

        // aw=3.5, ac=0, (string)
        var operands = new List<object> { 3.5, 0.0, "Test" };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.WordSpacing.Should().Be(3.5);
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_SetsCharacterSpacing()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.CharacterSpacing = 0; // Initial value

        // aw=0, ac=1.5, (string)
        var operands = new List<object> { 0.0, 1.5, "Test" };

        // Act
        handler.Handle(operands, state);

        // Assert
        state.CharacterSpacing.Should().Be(1.5);
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_MovesToNextLine_BeforeShowingText()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.TextLeading = 14;

        // Set initial text position
        state.TextMatrix = PdfMatrix.Translate(100, 700);
        state.TextLineMatrix = PdfMatrix.Translate(100, 700);

        var operands = new List<object> { 0.0, 0.0, "Hello" };

        // Act
        handler.Handle(operands, state);

        // Assert
        var (x, y) = state.GetCurrentTextPosition();
        y.Should().BeLessThan(700, "text should move down by text leading");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_WithByteArray_DecodesCorrectly()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;

        var bytes = System.Text.Encoding.ASCII.GetBytes("Test");
        var operands = new List<object> { 0.0, 0.0, bytes };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().NotBeNull();
        var textOp = (TextOperation)result!;
        textOp.Text.Should().Be("Test");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_WithLessThanThreeOperands_ReturnsNull()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();

        // Act
        var result = handler.Handle(new List<object> { 0.0, 0.0 }, state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_WithEmptyString_ReturnsNull()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.InTextObject = true;

        var operands = new List<object> { 0.0, 0.0, "" };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_CreatesGlyphPositions()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.TextMatrix = PdfMatrix.Translate(100, 700);
        state.TextLineMatrix = PdfMatrix.Translate(100, 700);

        var operands = new List<object> { 0.0, 0.0, "ABC" };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().NotBeNull();
        var textOp = (TextOperation)result!;
        textOp.Glyphs.Should().HaveCount(3);
        textOp.Glyphs[0].Character.Should().Be("A");
        textOp.Glyphs[1].Character.Should().Be("B");
        textOp.Glyphs[2].Character.Should().Be("C");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void Handle_WithIntegerOperands_ParsesCorrectly()
    {
        // Arrange
        var handler = new DoubleQuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;

        // Integer operands
        var operands = new List<object> { 2, 1, "Test" };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().NotBeNull();
        state.WordSpacing.Should().Be(2.0);
        state.CharacterSpacing.Should().Be(1.0);
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "82")]
    public void DoubleQuoteOperator_IsRegisteredInDefaultRegistry()
    {
        // Arrange & Act
        var registry = OperatorRegistry.CreateDefault();

        // Assert
        registry.HasHandler("\"").Should().BeTrue();
    }

    private PdfParserState CreateDefaultState()
    {
        var state = new PdfParserState(792); // US Letter height
        state.TextMatrix = PdfMatrix.Identity;
        state.TextLineMatrix = PdfMatrix.Identity;
        state.TransformationMatrix = PdfMatrix.Identity;
        state.FontSize = 12;
        state.FontName = "/F1";
        state.TextLeading = 0;
        state.CharacterSpacing = 0;
        state.WordSpacing = 0;
        state.HorizontalScaling = 100;
        return state;
    }
}
