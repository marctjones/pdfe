using System.Text;
using FluentAssertions;
using Pdfe.Core.Text;
using Xunit;

namespace Pdfe.Core.Tests.Text;

/// <summary>
/// Tests for ToUnicode CMap parsing (PDF §9.10.3).
/// Converts character codes to Unicode strings via CMap.
/// </summary>
public class ToUnicodeCMapParserTests
{
    [Fact]
    public void Parse_EmptyCMap_ReturnsEmptyDictionary()
    {
        var cmap = "";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SimpleBfChar_SingleMapping()
    {
        var cmap = @"
            1 beginbfchar
            <0041> <0041>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(1);
        result[0x0041].Should().Be("A");
    }

    [Fact]
    public void Parse_BfChar_MultipleEntries()
    {
        var cmap = @"
            3 beginbfchar
            <0041> <0041>
            <0042> <0042>
            <0043> <0043>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(3);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B");
        result[0x0043].Should().Be("C");
    }

    [Fact]
    public void Parse_SimpleBfRange_SequentialMapping()
    {
        var cmap = @"
            1 beginbfrange
            <0041> <0043> <0041>
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(3);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B");
        result[0x0043].Should().Be("C");
    }

    [Fact]
    public void Parse_BfRange_ArrayDestination()
    {
        var cmap = @"
            1 beginbfrange
            <0041> <0042> [<0041> <0042>]
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(2);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B");
    }

    [Fact]
    public void Parse_MultipleBfCharSections()
    {
        var cmap = @"
            2 beginbfchar
            <0041> <0041>
            <0042> <0042>
            endbfchar
            2 beginbfchar
            <0043> <0043>
            <0044> <0044>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(4);
        result[0x0041].Should().Be("A");
        result[0x0044].Should().Be("D");
    }

    [Fact]
    public void Parse_MixedBfCharAndRange()
    {
        var cmap = @"
            1 beginbfchar
            <0041> <0041>
            endbfchar
            1 beginbfrange
            <0042> <0043> <0042>
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(3);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B");
        result[0x0043].Should().Be("C");
    }

    [Fact]
    public void Parse_TwoByteCharCodes_CJK()
    {
        // 0x4E2D is the CJK character U+4E2D (Chinese "中")
        var cmap = @"
            1 beginbfchar
            <4E2D> <4E2D>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(1);
        var ch = result[0x4E2D];
        ch.Should().Be("中");
    }

    [Fact]
    public void Parse_ByteArray_SameAsString()
    {
        var cmapStr = @"
            1 beginbfchar
            <0041> <0041>
            endbfchar
        ";
        var cmapBytes = Encoding.UTF8.GetBytes(cmapStr);

        var resultStr = ToUnicodeCMapParser.Parse(cmapStr);
        var resultBytes = ToUnicodeCMapParser.Parse(cmapBytes);

        resultStr.Should().Equal(resultBytes);
    }

    [Fact]
    public void Parse_InvalidCMap_ReturnsPartialResults()
    {
        var cmap = @"
            2 beginbfchar
            <0041> <0041>
            <0042> <0042>
        ";
        // Missing endbfchar
        var result = ToUnicodeCMapParser.Parse(cmap);
        // Should return empty (regex doesn't match without closing)
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BfRangeArrayWithMultipleEntries()
    {
        var cmap = @"
            1 beginbfrange
            <0041> <0043> [<0041> <0042> <0043>]
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(3);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B");
        result[0x0043].Should().Be("C");
    }

    [Fact]
    public void Parse_BfRangeArrayPartial_OnlyPresentEntries()
    {
        var cmap = @"
            1 beginbfrange
            <0041> <0043> [<0041> <0042>]
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(2);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B");
    }

    [Fact]
    public void Parse_HexVariations_CaseInsensitive()
    {
        var cmap = @"
            2 beginbfchar
            <00AA> <00AA>
            <00Bb> <00Bb>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(2);
        result[0x00AA].Should().NotBeNull();
        result[0x00BB].Should().NotBeNull();
    }

    [Fact]
    public void Parse_SingleByteRange()
    {
        var cmap = @"
            1 beginbfrange
            <20> <7E> <20>
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(95); // 0x20 to 0x7E inclusive
        result[0x20].Should().Be(" ");
        result[0x7E].Should().Be("~");
    }

    [Fact]
    public void Parse_UnicodeMapping_HighCodePoint()
    {
        // Map to a CJK code point (U+4E2D = 中)
        var cmap = @"
            1 beginbfchar
            <0001> <4E2D>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(1);
        result[0x0001].Should().Be("中"); // CJK character U+4E2D
    }

    [Fact]
    public void Parse_Surrogate_Pair_Handling()
    {
        // UTF-16BE surrogate pair D83D+DE00 = U+1F600 😀
        var cmap = @"
            1 beginbfchar
            <0001> <D83DDE00>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(1);
        result[0x0001].Should().Be("😀"); // Grinning face emoji U+1F600
    }

    [Fact]
    public void Parse_BfChar_OverwriteWithLater()
    {
        // If same source code appears twice, later should win
        var cmap = @"
            2 beginbfchar
            <0041> <0041>
            <0041> <0042>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result[0x0041].Should().Be("B");
    }

    [Fact]
    public void Parse_ComplexCMap_RealWorldLike()
    {
        var cmap = @"
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CIDSystemInfo
            << /Registry (Adobe)
            /Ordering (UCS)
            /Supplement 0
            >> def
            /CMapName /Adobe-Identity-UCS def
            /CMapType 2 def
            1 begincodespacerange
            <0000> <FFFF>
            endcodespacerange
            2 beginbfchar
            <0001> <0041>
            <0002> <0042>
            endbfchar
            1 beginbfrange
            <0003> <0005> <0043>
            endbfrange
            endcmap
            CMapName currentdict /CMap defineresource pop
            end
            end
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(5); // 2 from bfchar + 3 from bfrange
        result[0x0001].Should().Be("A");
        result[0x0003].Should().Be("C");
        result[0x0005].Should().Be("E");
    }

    [Fact]
    public void Parse_EmptyBfCharBlock_NoResults()
    {
        var cmap = @"
            0 beginbfchar
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LargeRangeMapping()
    {
        var cmap = @"
            1 beginbfrange
            <0000> <00FF> <0000>
            endbfrange
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(256);
        result[0x0000].Should().Be(" ");
        result[0x00FF].Should().Be("ÿ");
    }

    [Fact]
    public void Parse_SpecialCharacters_Symbols()
    {
        var cmap = @"
            3 beginbfchar
            <0001> <00A9>
            <0002> <00AE>
            <0003> <2122>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(3);
        result[0x0001].Should().Be("©");
        result[0x0002].Should().Be("®");
        result[0x0003].Should().Be("™");
    }

    [Fact]
    public void Parse_BfCharWithWhitespaceVariation()
    {
        var cmap = @"
            1 beginbfchar
            <0041>   <0041>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(1);
        result[0x0041].Should().Be("A");
    }

    [Fact]
    public void Parse_BfRangeOverwrittenByBfChar()
    {
        var cmap = @"
            1 beginbfrange
            <0041> <0043> <0041>
            endbfrange
            1 beginbfchar
            <0042> <0099>
            endbfchar
        ";
        var result = ToUnicodeCMapParser.Parse(cmap);
        result.Should().HaveCount(3);
        result[0x0041].Should().Be("A");
        result[0x0042].Should().Be("B"); // bfrange overrides bfchar in BuildMapping
        result[0x0043].Should().Be("C");
    }
}
