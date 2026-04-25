using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Text;
using Pdfe.Core.Text.Segmentation;
using Xunit;

namespace Pdfe.Core.Tests.Text.Segmentation;

/// <summary>
/// Tests for LetterFinder — the bridge from content-stream text ops to
/// page-extracted letters with per-glyph bounding boxes.
/// </summary>
public class LetterFinderTests
{
    private readonly LetterFinder _finder = new();

    // Build a page's worth of letters from a string, laying them out left-to-right.
    private static IReadOnlyList<Letter> MakeLetters(string text, double startX = 100, double y = 700, double width = 7)
    {
        var letters = new List<Letter>();
        for (int i = 0; i < text.Length; i++)
        {
            double lx = startX + i * width;
            letters.Add(new Letter(
                value: text[i].ToString(),
                glyphRectangle: new PdfRectangle(lx, y, lx + width, y + 12),
                fontSize: 12,
                fontName: "TestFont",
                startX: lx,
                startY: y,
                width: width,
                characterCode: text[i]));
        }
        return letters;
    }

    [Fact]
    public void Find_EmptyOperation_ReturnsNoMatches()
    {
        var letters = MakeLetters("HELLO WORLD");
        var matches = _finder.FindOperationLetters("", letters);
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Find_EmptyPage_ReturnsNoMatches()
    {
        var matches = _finder.FindOperationLetters("Hello", Array.Empty<Letter>());
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Find_TextPresentOnce_ReturnsMatchingLettersInOrder()
    {
        var letters = MakeLetters("HELLO WORLD");
        var matches = _finder.FindOperationLetters("WORLD", letters);

        matches.Should().HaveCount(5);
        string.Concat(matches.Select(m => m.Letter.Value)).Should().Be("WORLD");
        matches[0].CharacterIndex.Should().Be(0);
        matches[4].CharacterIndex.Should().Be(4);
        // Spatially, "WORLD" starts at index 6 in "HELLO WORLD" → x ≈ 100 + 6*7 = 142.
        matches[0].Letter.GlyphRectangle.Left.Should().BeApproximately(142, 0.1);
    }

    [Fact]
    public void Find_TextAbsent_ReturnsNoMatches()
    {
        var letters = MakeLetters("HELLO WORLD");
        var matches = _finder.FindOperationLetters("ZORRO", letters);
        matches.Should().BeEmpty();
    }

    [Fact]
    public void Find_TextPresentMultipleTimes_PrefersTightestCluster()
    {
        // Two occurrences of "IT": one with letters spread out (noise), one tight.
        // Letters: I T _ _ I T   (spaces simulated as letters for simplicity)
        // Only the second I and T sit next to each other; the first pair is
        // separated by three filler letters on a different baseline.
        var letters = new List<Letter>
        {
            // Far-apart "IT": huge horizontal spread.
            new("I", new PdfRectangle(100, 700, 107, 712), 12, "F", 100, 700, 7, 'I'),
            new("X", new PdfRectangle(200, 700, 207, 712), 12, "F", 200, 700, 7, 'X'),
            new("Y", new PdfRectangle(300, 700, 307, 712), 12, "F", 300, 700, 7, 'Y'),
            new("Z", new PdfRectangle(400, 700, 407, 712), 12, "F", 400, 700, 7, 'Z'),
            // Tight "IT": next to each other.
            new("I", new PdfRectangle(500, 700, 507, 712), 12, "F", 500, 700, 7, 'I'),
            new("T", new PdfRectangle(507, 700, 514, 712), 12, "F", 507, 700, 7, 'T'),
            // A stray T way off on its own — so the first "I" + this T isn't a match.
            new("T", new PdfRectangle(800, 700, 807, 712), 12, "F", 800, 700, 7, 'T'),
        };

        var matches = _finder.FindOperationLetters("IT", letters);

        matches.Should().HaveCount(2);
        // Should pick the tight pair at index 4-5, not anything involving the
        // loose letters at index 0 + some-later-T.
        matches[0].Letter.GlyphRectangle.Left.Should().BeApproximately(500, 0.1);
        matches[1].Letter.GlyphRectangle.Left.Should().BeApproximately(507, 0.1);
    }

    [Fact]
    public void Find_FormFieldWithFillUnderscores_MatchesMeaningfulPrefix()
    {
        // Content stream has "FULL NAME: ___" (3 underscores) but the extractor
        // sees a different number due to TJ kerning. The meaningful-prefix
        // fallback should still locate the label.
        var letters = MakeLetters("FULL NAME: _________");

        var matches = _finder.FindOperationLetters("FULL NAME: ______", letters);

        matches.Should().NotBeEmpty();
        // Matched only the meaningful prefix (up to and including the colon
        // plus any trailing whitespace got trimmed off, so "FULL NAME:").
        string.Concat(matches.Select(m => m.Letter.Value)).Should().StartWith("FULL NAME:");
    }

    [Fact]
    public void Find_CaseInsensitiveFallback_FindsMatch()
    {
        var letters = MakeLetters("HELLO WORLD");
        var matches = _finder.FindOperationLetters("hello", letters);

        matches.Should().HaveCount(5);
        string.Concat(matches.Select(m => m.Letter.Value)).Should().Be("HELLO");
    }

    [Fact]
    public void Find_ReturnsLetterMatchesWithIncrementingCharacterIndex()
    {
        var letters = MakeLetters("ABCDEF");
        var matches = _finder.FindOperationLetters("BCD", letters);

        matches.Should().HaveCount(3);
        matches.Select(m => m.CharacterIndex).Should().Equal(0, 1, 2);
    }
}
