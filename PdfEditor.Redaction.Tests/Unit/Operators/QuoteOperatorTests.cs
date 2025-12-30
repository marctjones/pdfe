using FluentAssertions;
using PdfEditor.Redaction.ContentStream;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextShowing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for ' (single quote) operator handler.
/// Issue #81: Implement ' text-showing operator for legacy PDF support.
/// </summary>
public class QuoteOperatorTests
{
    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void OperatorName_IsSingleQuote()
    {
        var handler = new QuoteOperatorHandler();
        handler.OperatorName.Should().Be("'");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void Handle_WithTextOperand_ReturnsTextOperation()
    {
        // Arrange
        var handler = new QuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.TextLeading = 14;

        // Act
        var result = handler.Handle(new List<object> { "Hello" }, state);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<TextOperation>();

        var textOp = (TextOperation)result!;
        textOp.Operator.Should().Be("'");
        textOp.Text.Should().Be("Hello");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void Handle_MovesToNextLine_BeforeShowingText()
    {
        // Arrange
        var handler = new QuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.TextLeading = 14;

        // Set initial text position
        state.TextMatrix = PdfMatrix.Translate(100, 700);
        state.TextLineMatrix = PdfMatrix.Translate(100, 700);

        // Act
        var result = handler.Handle(new List<object> { "Hello" }, state);

        // Assert
        // After T* equivalent (0, -TL Td), Y position should decrease by text leading
        var (x, y) = state.GetCurrentTextPosition();

        // Text should be at approximately Y = 700 - 14 = 686 (moved down by text leading)
        // Note: actual position depends on text matrix multiplication
        y.Should().BeLessThan(700, "text should move down by text leading");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void Handle_WithByteArray_DecodesCorrectly()
    {
        // Arrange
        var handler = new QuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;

        var bytes = System.Text.Encoding.ASCII.GetBytes("Test");

        // Act
        var result = handler.Handle(new List<object> { bytes }, state);

        // Assert
        result.Should().NotBeNull();
        var textOp = (TextOperation)result!;
        textOp.Text.Should().Be("Test");
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void Handle_WithEmptyOperands_ReturnsNull()
    {
        // Arrange
        var handler = new QuoteOperatorHandler();
        var state = CreateDefaultState();

        // Act
        var result = handler.Handle(new List<object>(), state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void Handle_WithEmptyString_ReturnsNull()
    {
        // Arrange
        var handler = new QuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.InTextObject = true;

        // Act
        var result = handler.Handle(new List<object> { "" }, state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    [Trait("Category", "Operators")]
    [Trait("Issue", "81")]
    public void Handle_CreatesGlyphPositions()
    {
        // Arrange
        var handler = new QuoteOperatorHandler();
        var state = CreateDefaultState();
        state.FontSize = 12;
        state.FontName = "/F1";
        state.InTextObject = true;
        state.TextMatrix = PdfMatrix.Translate(100, 700);
        state.TextLineMatrix = PdfMatrix.Translate(100, 700);

        // Act
        var result = handler.Handle(new List<object> { "ABC" }, state);

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
    [Trait("Issue", "81")]
    public void QuoteOperator_IsRegisteredInDefaultRegistry()
    {
        // Arrange & Act
        var registry = OperatorRegistry.CreateDefault();

        // Assert
        registry.HasHandler("'").Should().BeTrue();
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
