using System.Text;
using FluentAssertions;
using Pdfe.Core.Content;
using Xunit;

namespace Pdfe.Core.Tests.Content.Operators;

/// <summary>
/// Tests for inline image operators: BI, ID, EI (§8.9.7).
/// Inline images embed raw pixel data directly in the content stream.
/// Without correct BI/ID/EI handling the binary data corrupts the token
/// stream and every subsequent operator is mis-parsed.
/// </summary>
public class InlineImageTests
{
    [Fact]
    public void Parse_InlineImage_ProducesBI_Operator()
    {
        // Minimal inline image: 1x1 pixel, 1-bit grayscale
        var content = "BI\n/W 1\n/H 1\n/BPC 1\n/CS /G\nID\xFF\nEI\n";
        var parser = new ContentStreamParser(Encoding.UTF8.GetBytes(content));

        var result = parser.Parse();

        result.Operators.Should().ContainSingle(op => op.Name == "BI",
            "inline image should produce exactly one BI operator");
    }

    [Fact]
    public void Parse_InlineImage_DoesNotLeakRawBytesAsOperators()
    {
        // The 4 raw image data bytes 0x42 0x54 0x20 0x45 spell "BT E" in ASCII —
        // which, if the tokeniser reads them, would look like the BT text operator.
        // Correct BI/ID/EI handling must consume them without emitting BT.
        byte[] raw = { 0x42, 0x54, 0x20, 0x45 }; // "BT E" — would confuse plain tokeniser
        var header = Encoding.ASCII.GetBytes("BI\n/W 4\n/H 1\n/BPC 8\n/CS /G\nID\n");
        var footer = Encoding.ASCII.GetBytes("\nEI\n");
        var content = header.Concat(raw).Concat(footer).ToArray();

        var result = new ContentStreamParser(content).Parse();

        result.Operators.Should().NotContain(op => op.Name == "BT",
            "binary image bytes must not be tokenised as PDF operators");
        result.Operators.Should().ContainSingle(op => op.Name == "BI");
    }

    [Fact]
    public void Parse_InlineImage_OperatorsBeforeAndAfterParsedCorrectly()
    {
        // Surrounding text operators must be unaffected by the inline image.
        var content = "BT (before) Tj ET\n" +
                      "BI\n/W 2\n/H 2\n/BPC 8\n/CS /G\nID\n\x00\x00\x00\x00\nEI\n" +
                      "BT (after) Tj ET\n";

        var result = new ContentStreamParser(Encoding.UTF8.GetBytes(content)).Parse();

        var tjOps = result.Operators.Where(op => op.Name == "Tj").ToList();
        tjOps.Should().HaveCount(2, "both Tj operators must survive inline image parsing");
        tjOps[0].TextContent.Should().Be("before");
        tjOps[1].TextContent.Should().Be("after");
    }

    [Fact]
    public void Parse_InlineImage_AbbreviatedParameterNamesAccepted()
    {
        // PDF allows abbreviated names for inline image parameters (e.g. /W for /Width)
        var content = "BI /W 8 /H 4 /BPC 1 /CS /G /F /A85 ID XXXXXX EI ";

        var result = new ContentStreamParser(Encoding.ASCII.GetBytes(content)).Parse();

        result.Operators.Should().ContainSingle(op => op.Name == "BI");
    }

    [Fact]
    public void Parse_MultipleInlineImages_AllProduceSeparateBIOperators()
    {
        var img = "BI /W 1 /H 1 /BPC 1 /CS /G ID \xFF EI ";
        var content = img + img + img;

        var result = new ContentStreamParser(Encoding.ASCII.GetBytes(content)).Parse();

        result.Operators.Count(op => op.Name == "BI").Should()
            .Be(3, "three sequential inline images must each produce a BI operator");
    }
}
