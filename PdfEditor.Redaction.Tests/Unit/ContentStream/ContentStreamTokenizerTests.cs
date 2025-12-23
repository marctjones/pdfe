using FluentAssertions;
using PdfEditor.Redaction.ContentStream;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.ContentStream;

/// <summary>
/// Tests for ContentStreamTokenizer - low-level PDF content stream parsing.
/// </summary>
public class ContentStreamTokenizerTests
{
    #region Literal String Parsing

    [Fact]
    public void ExtractTextStrings_SimpleLiteralString_ExtractsCorrectly()
    {
        // Arrange
        var content = "(Hello) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].StringType.Should().Be(StringType.Literal);
        tokens[0].DecodedText.Should().Be("Hello");
    }

    [Fact]
    public void ExtractTextStrings_EscapedParentheses_HandlesCorrectly()
    {
        // Arrange
        var content = @"(Hello \(World\)) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedText.Should().Be("Hello (World)");
    }

    [Fact]
    public void ExtractTextStrings_NestedParentheses_HandlesCorrectly()
    {
        // Arrange: Nested parens without escapes are valid in PDF
        var content = "(Hello (World)) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedText.Should().Be("Hello (World)");
    }

    [Fact]
    public void ExtractTextStrings_OctalEscape_DecodesCorrectly()
    {
        // Arrange: \101\102\103 = ABC
        var content = @"(\101\102\103) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedText.Should().Be("ABC");
    }

    [Fact]
    public void ExtractTextStrings_EscapeSequences_DecodesCorrectly()
    {
        // Arrange
        var content = @"(Line1\nLine2\tTabbed) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedText.Should().Be("Line1\nLine2\tTabbed");
    }

    [Fact]
    public void ExtractTextStrings_EmptyString_ReturnsEmptyText()
    {
        // Arrange
        var content = "() Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedText.Should().BeEmpty();
    }

    #endregion

    #region Hex String Parsing

    [Fact]
    public void ExtractTextStrings_HexString_DecodesCorrectly()
    {
        // Arrange: 48656C6C6F = "Hello"
        var content = "<48656C6C6F> Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].StringType.Should().Be(StringType.Hex);
        tokens[0].DecodedText.Should().Be("Hello");
    }

    [Fact]
    public void ExtractTextStrings_HexStringWithSpaces_DecodesCorrectly()
    {
        // Arrange: Whitespace is ignored in hex strings
        var content = "<48 65 6C 6C 6F> Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedText.Should().Be("Hello");
    }

    [Fact]
    public void ExtractTextStrings_OddHexDigits_PadsWithZero()
    {
        // Arrange: Odd number of hex digits, last assumed to be 0
        // <414> becomes <4140> = "A@"  (0x41 = 'A', 0x40 = '@')
        var content = "<414> Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(1);
        tokens[0].DecodedBytes.Should().BeEquivalentTo(new byte[] { 0x41, 0x40 });
    }

    #endregion

    #region Multiple Strings

    [Fact]
    public void ExtractTextStrings_MultipleStrings_ExtractsAll()
    {
        // Arrange
        var content = "(Hello) Tj (World) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(2);
        tokens[0].DecodedText.Should().Be("Hello");
        tokens[1].DecodedText.Should().Be("World");
    }

    [Fact]
    public void ExtractTextStrings_MixedStringTypes_ExtractsAll()
    {
        // Arrange
        var content = "(Literal) Tj <486578> Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens.Should().HaveCount(2);
        tokens[0].StringType.Should().Be(StringType.Literal);
        tokens[0].DecodedText.Should().Be("Literal");
        tokens[1].StringType.Should().Be(StringType.Hex);
        tokens[1].DecodedText.Should().Be("Hex");
    }

    #endregion

    #region Text Operator Detection

    [Fact]
    public void FindTextOperators_TjOperator_DetectsCorrectly()
    {
        // Arrange
        var content = "(Hello) Tj"u8.ToArray();

        // Act
        var operators = ContentStreamTokenizer.FindTextOperators(content);

        // Assert
        operators.Should().HaveCount(1);
        operators[0].OperatorType.Should().Be("Tj");
    }

    [Fact]
    public void FindTextOperators_TJOperator_DetectsCorrectly()
    {
        // Arrange
        var content = "[(Hello)] TJ"u8.ToArray();

        // Act
        var operators = ContentStreamTokenizer.FindTextOperators(content);

        // Assert
        operators.Should().HaveCount(1);
        operators[0].OperatorType.Should().Be("TJ");
    }

    [Fact]
    public void FindTextOperators_QuoteOperator_DetectsCorrectly()
    {
        // Arrange
        var content = "(Hello) '"u8.ToArray();

        // Act
        var operators = ContentStreamTokenizer.FindTextOperators(content);

        // Assert
        operators.Should().HaveCount(1);
        operators[0].OperatorType.Should().Be("'");
    }

    [Fact]
    public void FindTextOperators_MultipleOperators_DetectsAll()
    {
        // Arrange
        var content = "(Hello) Tj (World) Tj [(Array)] TJ"u8.ToArray();

        // Act
        var operators = ContentStreamTokenizer.FindTextOperators(content);

        // Assert
        operators.Should().HaveCount(3);
        operators[0].OperatorType.Should().Be("Tj");
        operators[1].OperatorType.Should().Be("Tj");
        operators[2].OperatorType.Should().Be("TJ");
    }

    [Fact]
    public void FindTextOperators_TjInString_BothDetected_RequiresFullParser()
    {
        // NOTE: FindTextOperators is a simple pattern matcher that doesn't track string context.
        // It will find "Tj" inside strings too. The full ContentStreamParser handles this properly
        // by parsing strings first and only recognizing operators outside string boundaries.

        // Arrange: "Tj" inside a string will be detected by simple pattern matching
        var content = "(This has Tj inside) Tj"u8.ToArray();

        // Act
        var operators = ContentStreamTokenizer.FindTextOperators(content);

        // Assert: Simple matcher finds both - this is expected behavior
        // The full parser (not this utility) handles string context properly
        operators.Should().HaveCount(2);
        operators[0].Position.Should().Be(10); // Inside string
        operators[1].Position.Should().Be(21); // Real operator
    }

    #endregion

    #region Position Tracking

    [Fact]
    public void ExtractTextStrings_TracksPositions_Correctly()
    {
        // Arrange
        var content = "(Hello) Tj"u8.ToArray();

        // Act
        var tokens = ContentStreamTokenizer.ExtractTextStrings(content);

        // Assert
        tokens[0].StartPosition.Should().Be(0);
        tokens[0].EndPosition.Should().Be(7); // "(Hello)"
        tokens[0].RawBytes.Should().BeEquivalentTo("(Hello)"u8.ToArray());
    }

    #endregion
}
