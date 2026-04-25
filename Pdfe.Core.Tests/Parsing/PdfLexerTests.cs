using FluentAssertions;
using Pdfe.Core.Parsing;
using System.Text;
using Xunit;

namespace Pdfe.Core.Tests.Parsing;

public class PdfLexerTests
{
    [Theory]
    [InlineData("123", PdfTokenType.Integer, "123")]
    [InlineData("-45", PdfTokenType.Integer, "-45")]
    [InlineData("+67", PdfTokenType.Integer, "+67")]
    [InlineData("0", PdfTokenType.Integer, "0")]
    public void NextToken_Integer_ReturnsCorrectToken(string input, PdfTokenType expectedType, string expectedValue)
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));

        var token = lexer.NextToken();

        token.Type.Should().Be(expectedType);
        token.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("3.14", PdfTokenType.Real, "3.14")]
    [InlineData("-0.5", PdfTokenType.Real, "-0.5")]
    [InlineData(".25", PdfTokenType.Real, ".25")]
    [InlineData("123.456", PdfTokenType.Real, "123.456")]
    public void NextToken_Real_ReturnsCorrectToken(string input, PdfTokenType expectedType, string expectedValue)
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));

        var token = lexer.NextToken();

        token.Type.Should().Be(expectedType);
        token.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "null")]
    [InlineData("obj", "obj")]
    [InlineData("endobj", "endobj")]
    [InlineData("stream", "stream")]
    [InlineData("endstream", "endstream")]
    [InlineData("R", "R")]
    public void NextToken_Keyword_ReturnsCorrectToken(string input, string expectedValue)
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));

        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.Keyword);
        token.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("/Name", "Name")]
    [InlineData("/Type", "Type")]
    [InlineData("/A;Name_With-Various.Chars", "A;Name_With-Various.Chars")]
    [InlineData("/Name#20With#20Spaces", "Name With Spaces")]
    public void NextToken_Name_ReturnsCorrectToken(string input, string expectedValue)
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));

        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.Name);
        token.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("(Hello)", "Hello")]
    [InlineData("(Hello World)", "Hello World")]
    [InlineData("(Nested (parens) work)", "Nested (parens) work")]
    [InlineData(@"(Line\nBreak)", "Line\nBreak")]
    [InlineData(@"(Tab\tChar)", "Tab\tChar")]
    [InlineData(@"(Backslash\\Char)", "Backslash\\Char")]
    [InlineData(@"(Paren\(Escape)", "Paren(Escape")]
    public void NextToken_LiteralString_ReturnsCorrectToken(string input, string expectedValue)
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));

        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.LiteralString);
        token.Value.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("<48656C6C6F>", "48656C6C6F")] // "Hello"
    [InlineData("<48 65 6C 6C 6F>", "48656C6C6F")] // With whitespace
    [InlineData("<4865>", "4865")]
    public void NextToken_HexString_ReturnsCorrectToken(string input, string expectedValue)
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes(input));

        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.HexString);
        token.Value.Should().Be(expectedValue);
    }

    [Fact]
    public void NextToken_ArrayDelimiters_ReturnsCorrectTokens()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("[ 1 2 3 ]"));

        lexer.NextToken().Type.Should().Be(PdfTokenType.ArrayStart);
        lexer.NextToken().Type.Should().Be(PdfTokenType.Integer);
        lexer.NextToken().Type.Should().Be(PdfTokenType.Integer);
        lexer.NextToken().Type.Should().Be(PdfTokenType.Integer);
        lexer.NextToken().Type.Should().Be(PdfTokenType.ArrayEnd);
    }

    [Fact]
    public void NextToken_DictionaryDelimiters_ReturnsCorrectTokens()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("<< /Key /Value >>"));

        lexer.NextToken().Type.Should().Be(PdfTokenType.DictionaryStart);
        lexer.NextToken().Type.Should().Be(PdfTokenType.Name);
        lexer.NextToken().Type.Should().Be(PdfTokenType.Name);
        lexer.NextToken().Type.Should().Be(PdfTokenType.DictionaryEnd);
    }

    [Fact]
    public void NextToken_SkipsWhitespace()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("  \t\n\r  123  "));

        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.Integer);
        token.Value.Should().Be("123");
    }

    [Fact]
    public void NextToken_SkipsComments()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("% This is a comment\n123"));

        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.Integer);
        token.Value.Should().Be("123");
    }

    [Fact]
    public void NextToken_IndirectReference_TokenSequence()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("5 0 R"));

        var t1 = lexer.NextToken();
        var t2 = lexer.NextToken();
        var t3 = lexer.NextToken();

        t1.Type.Should().Be(PdfTokenType.Integer);
        t1.Value.Should().Be("5");
        t2.Type.Should().Be(PdfTokenType.Integer);
        t2.Value.Should().Be("0");
        t3.Type.Should().Be(PdfTokenType.Keyword);
        t3.Value.Should().Be("R");
    }

    [Fact]
    public void NextToken_AtEndOfFile_ReturnsEof()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("123"));

        lexer.NextToken(); // consume the integer
        var token = lexer.NextToken();

        token.Type.Should().Be(PdfTokenType.Eof);
    }

    [Fact]
    public void Position_TracksCurrentPosition()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("123 456"));

        lexer.Position.Should().Be(0);
        lexer.NextToken();
        // After "123" and whitespace
        lexer.NextToken();
        lexer.Position.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Seek_MovesToPosition()
    {
        using var lexer = new PdfLexer(Encoding.ASCII.GetBytes("123 456 789"));

        lexer.NextToken(); // 123
        lexer.NextToken(); // 456

        lexer.Seek(0);
        var token = lexer.NextToken();

        token.Value.Should().Be("123");
    }
}
