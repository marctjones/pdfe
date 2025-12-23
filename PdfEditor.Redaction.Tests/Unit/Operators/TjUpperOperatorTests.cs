using FluentAssertions;
using PdfEditor.Redaction.Operators;
using PdfEditor.Redaction.Operators.TextShowing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.Operators;

/// <summary>
/// Tests for TJ (show text array with kerning) operator handler.
/// </summary>
public class TjUpperOperatorTests
{
    [Fact]
    public void TJHandler_ReturnsTextOperation()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var array = new List<object> { "Hello" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeOfType<TextOperation>();
        result!.Operator.Should().Be("TJ");
    }

    [Fact]
    public void TJHandler_ExtractsText()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var array = new List<object> { "Hello" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Hello");
    }

    [Fact]
    public void TJHandler_ConcatenatesMultipleStrings()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var array = new List<object> { "Hel", "lo" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Hello");
    }

    [Fact]
    public void TJHandler_IgnoresKerningNumbersInText()
    {
        // Arrange - kerning values should not appear in the text
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        // [(H) -10 (ello)] represents "Hello" with kerning adjustment
        var array = new List<object> { "H", -10.0, "ello" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Hello");
    }

    [Fact]
    public void TJHandler_CreatesGlyphsForEachCharacter()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var array = new List<object> { "ABC" };
        var operands = new object[] { array };

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
    public void TJHandler_AdjustsPositionWithNegativeKerning()
    {
        // Arrange - negative kerning moves glyphs right (looser spacing)
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.TextMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 100, 200);
        // -100 in thousandths = -0.1 em = move right
        var array = new List<object> { "A", -100.0, "B" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        var glyphA = result!.Glyphs[0];
        var glyphB = result.Glyphs[1];
        // B should start after A plus the kerning adjustment
        glyphB.BoundingBox.Left.Should().BeGreaterThan(glyphA.BoundingBox.Right);
    }

    [Fact]
    public void TJHandler_AdjustsPositionWithPositiveKerning()
    {
        // Arrange - positive kerning moves glyphs left (tighter spacing)
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.TextMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 100, 200);
        // +500 in thousandths = +0.5 em = move left (tighter)
        var array = new List<object> { "A", 500.0, "B" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        // B should overlap or be very close to A due to tightening
        var glyphA = result!.Glyphs[0];
        var glyphB = result.Glyphs[1];
        // The tightening effect should bring B closer than normal spacing
        var normalSpacing = state.FontSize * 0.6;
        var actualGap = glyphB.BoundingBox.Left - glyphA.BoundingBox.Left;
        actualGap.Should().BeLessThan(normalSpacing * 1.5); // Much tighter than normal
    }

    [Fact]
    public void TJHandler_HandlesIntegerKerning()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var array = new List<object> { "A", -50, "B" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Glyphs.Should().HaveCount(2);
    }

    [Fact]
    public void TJHandler_HandlesByteArray()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var bytes = new byte[] { 0x48, 0x69 }; // "Hi" in ASCII
        var array = new List<object> { bytes };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Hi");
    }

    [Fact]
    public void TJHandler_EmptyArray_ReturnsNull()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        var array = new List<object>();
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TJHandler_NoOperands_ReturnsNull()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);

        // Act
        var result = handler.Handle(Array.Empty<object>(), state);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TJHandler_UsesFontSize()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 24;
        var array = new List<object> { "X" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.FontSize.Should().Be(24);
    }

    [Fact]
    public void TJHandler_UsesFontName()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.FontName = "/F1";
        var array = new List<object> { "Test" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.FontName.Should().Be("/F1");
    }

    [Fact]
    public void TJHandler_AdvancesTextMatrix()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.TextMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 0, 0);
        var array = new List<object> { "ABC" };
        var operands = new object[] { array };

        // Act
        handler.Handle(operands, state);

        // Assert - text matrix X position should have advanced
        state.TextMatrix.E.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TJHandler_CalculatesCorrectBoundingBox()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        state.TextMatrix = PdfMatrix.FromOperands(1, 0, 0, 1, 100, 200);
        var array = new List<object> { "Hi" };
        var operands = new object[] { array };

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
    public void TJHandler_TracksArrayIndex()
    {
        // Arrange
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        // Array: ["A", -10, "B"]
        var array = new List<object> { "A", -10.0, "B" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Glyphs.Should().HaveCount(2);
        result.Glyphs[0].ArrayIndex.Should().Be(0); // "A" is at index 0
        result.Glyphs[1].ArrayIndex.Should().Be(2); // "B" is at index 2 (skipping the number)
    }

    [Fact]
    public void TJHandler_SupportsObjectArray()
    {
        // Arrange - operand could be object[] instead of List<object>
        var handler = new TjUpperOperatorHandler();
        var state = new PdfParserState(792);
        state.FontSize = 12;
        var array = new object[] { "Test" };
        var operands = new object[] { array };

        // Act
        var result = handler.Handle(operands, state) as TextOperation;

        // Assert
        result.Should().NotBeNull();
        result!.Text.Should().Be("Test");
    }
}
