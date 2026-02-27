using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Text;
using Pdfe.Core.Text.Segmentation;
using Xunit;

namespace Pdfe.Core.Tests.Text.Segmentation;

/// <summary>
/// Tests for TextSegmenter - the foundation for partial text redaction.
/// </summary>
public class TextSegmenterTests
{
    private readonly TextSegmenter _segmenter = new();

    #region Basic Segmentation Tests

    [Fact]
    public void BuildSegments_NoLetterMatches_NoOverlap_KeepsEntireOperation()
    {
        // Arrange
        var text = "Hello World";
        var operationBounds = new PdfRectangle(100, 700, 200, 720);
        var letterMatches = new List<LetterMatch>();
        var redactionArea = new PdfRectangle(300, 700, 400, 720); // No overlap

        // Act
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].Keep.Should().BeTrue();
        segments[0].Text.Should().Be("Hello World");
        segments[0].StartIndex.Should().Be(0);
        segments[0].EndIndex.Should().Be(11);
    }

    [Fact]
    public void BuildSegments_NoLetterMatches_FullOverlap_RemovesEntireOperation()
    {
        // Arrange
        var text = "Hello World";
        var operationBounds = new PdfRectangle(100, 700, 200, 720);
        var letterMatches = new List<LetterMatch>();
        var redactionArea = new PdfRectangle(50, 650, 250, 750); // Covers entire operation

        // Act
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert
        segments.Should().BeEmpty("entire operation should be removed");
    }

    [Fact]
    public void BuildSegments_PartialTextRedaction_SegmentsCorrectly()
    {
        // Arrange - "Hello World" where we redact "World"
        var text = "Hello World";
        var operationBounds = new PdfRectangle(100, 700, 200, 720);

        // Create letter matches for each character
        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('H', 0, 100, 700, 10),
            CreateMatch('e', 1, 110, 700, 8),
            CreateMatch('l', 2, 118, 700, 5),
            CreateMatch('l', 3, 123, 700, 5),
            CreateMatch('o', 4, 128, 700, 9),
            CreateMatch(' ', 5, 137, 700, 5),
            CreateMatch('W', 6, 142, 700, 12), // Start of redaction area
            CreateMatch('o', 7, 154, 700, 9),
            CreateMatch('r', 8, 163, 700, 6),
            CreateMatch('l', 9, 169, 700, 5),
            CreateMatch('d', 10, 174, 700, 9)
        };

        // Redaction area covers "World" (x: 142-183)
        var redactionArea = new PdfRectangle(142, 695, 185, 725);

        // Act
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert - should keep only "Hello "
        segments.Should().HaveCount(1);
        segments[0].Keep.Should().BeTrue();
        segments[0].Text.Should().Be("Hello ");
        segments[0].StartIndex.Should().Be(0);
        segments[0].EndIndex.Should().Be(6);
    }

    #endregion

    #region Strategy Tests

    [Fact]
    public void BuildSegments_AnyOverlap_RemovesPartiallyOverlappingGlyphs()
    {
        // Arrange - letter partially in redaction area
        var text = "Test";
        var operationBounds = new PdfRectangle(100, 700, 150, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('T', 0, 100, 700, 10),  // Outside
            CreateMatch('e', 1, 110, 700, 8),   // Partially in (110-118, redaction starts at 115)
            CreateMatch('s', 2, 118, 700, 6),   // Inside
            CreateMatch('t', 3, 124, 700, 5)    // Inside
        };

        var redactionArea = new PdfRectangle(115, 695, 135, 725);

        // Act - AnyOverlap strategy (default)
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea,
            GlyphRemovalStrategy.AnyOverlap);

        // Assert - should keep only "T" (e, s, t are removed due to overlap)
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("T");
    }

    [Fact]
    public void BuildSegments_FullyContained_KeepsPartiallyOverlappingGlyphs()
    {
        // Arrange - same setup as above
        var text = "Test";
        var operationBounds = new PdfRectangle(100, 700, 150, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('T', 0, 100, 700, 10),  // Outside
            CreateMatch('e', 1, 110, 700, 8),   // Partially in (110-118)
            CreateMatch('s', 2, 118, 700, 6),   // Fully inside
            CreateMatch('t', 3, 124, 700, 5)    // Fully inside
        };

        var redactionArea = new PdfRectangle(115, 695, 135, 725);

        // Act - FullyContained strategy
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea,
            GlyphRemovalStrategy.FullyContained);

        // Assert - should keep "Te" (only s, t are fully contained)
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("Te");
    }

    [Fact]
    public void BuildSegments_CenterPoint_UsesGlyphCenter()
    {
        // Arrange
        var text = "AB";
        var operationBounds = new PdfRectangle(100, 700, 130, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('A', 0, 100, 700, 10),  // Center at 105 (outside redaction)
            CreateMatch('B', 1, 110, 700, 10)   // Center at 115 (inside redaction: 112-125)
        };

        var redactionArea = new PdfRectangle(112, 695, 125, 725);

        // Act
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea,
            GlyphRemovalStrategy.CenterPoint);

        // Assert - should keep only "A"
        segments.Should().HaveCount(1);
        segments[0].Text.Should().Be("A");
    }

    #endregion

    #region Overlap Type Tests

    [Fact]
    public void BuildSegments_PartialOverlap_SetsOverlapTypeCorrectly()
    {
        // Arrange - letter partially overlapping redaction area
        var text = "X";
        var operationBounds = new PdfRectangle(100, 700, 110, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('X', 0, 100, 700, 10)  // Partially in redaction (100-110, redaction: 105-120)
        };

        var redactionArea = new PdfRectangle(105, 695, 120, 725);

        // Act - Build all segments (including removed ones)
        var allSegments = GetAllSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert - should have a removed segment with Partial overlap
        var removedSegments = allSegments.Where(s => !s.Keep).ToList();
        removedSegments.Should().HaveCount(1);
        removedSegments[0].OverlapType.Should().Be(GlyphOverlapType.Partial);
        removedSegments[0].IsPartialOverlap.Should().BeTrue();
    }

    [Fact]
    public void BuildSegments_FullOverlap_SetsOverlapTypeCorrectly()
    {
        // Arrange - letter fully inside redaction area
        var text = "X";
        var operationBounds = new PdfRectangle(100, 700, 110, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('X', 0, 100, 700, 10)  // Fully in redaction (100-110, redaction: 95-115)
        };

        var redactionArea = new PdfRectangle(95, 695, 115, 725);

        // Act
        var allSegments = GetAllSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert
        var removedSegments = allSegments.Where(s => !s.Keep).ToList();
        removedSegments.Should().HaveCount(1);
        removedSegments[0].OverlapType.Should().Be(GlyphOverlapType.Full);
        removedSegments[0].IsPartialOverlap.Should().BeFalse();
    }

    #endregion

    #region Multiple Segment Tests

    [Fact]
    public void BuildSegments_MultipleKeepSegments_CreatesCorrectSegments()
    {
        // Arrange - "A B C" where we redact "B"
        var text = "A B C";
        var operationBounds = new PdfRectangle(100, 700, 150, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('A', 0, 100, 700, 10),
            CreateMatch(' ', 1, 110, 700, 5),
            CreateMatch('B', 2, 115, 700, 10),  // Redacted
            CreateMatch(' ', 3, 125, 700, 5),
            CreateMatch('C', 4, 130, 700, 10)
        };

        var redactionArea = new PdfRectangle(115, 695, 125, 725);

        // Act
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert - should have two segments: "A " and " C"
        segments.Should().HaveCount(2);
        segments[0].Text.Should().Be("A ");
        segments[0].StartIndex.Should().Be(0);
        segments[0].EndIndex.Should().Be(2);

        segments[1].Text.Should().Be(" C");
        segments[1].StartIndex.Should().Be(3);
        segments[1].EndIndex.Should().Be(5);
    }

    #endregion

    #region CJK Support Tests

    [Fact]
    public void BuildSegments_PreservesLetterMatches_ForReconstruction()
    {
        // Arrange
        var text = "Test";
        var operationBounds = new PdfRectangle(100, 700, 150, 720);

        var letterMatches = new List<LetterMatch>
        {
            CreateMatch('T', 0, 100, 700, 10),
            CreateMatch('e', 1, 110, 700, 8),
            CreateMatch('s', 2, 118, 700, 6),
            CreateMatch('t', 3, 124, 700, 5)
        };

        // Add raw bytes for CJK reconstruction
        letterMatches[0].RawBytes = new byte[] { 0x54 }; // 'T'
        letterMatches[1].RawBytes = new byte[] { 0x65 }; // 'e'

        var redactionArea = new PdfRectangle(300, 700, 400, 720); // No overlap

        // Act
        var segments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea);

        // Assert
        segments.Should().HaveCount(1);
        segments[0].LetterMatches.Should().HaveCount(4);
        segments[0].GetRawBytes().Should().Equal(new byte[] { 0x54, 0x65 });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Create a letter match for testing.
    /// </summary>
    private LetterMatch CreateMatch(char character, int index, double x, double y, double width)
    {
        var letter = new Letter(
            character.ToString(),
            new PdfRectangle(x, y, x + width, y + 20),
            fontSize: 12,
            fontName: "TestFont",
            startX: x,
            startY: y,
            width: width,
            characterCode: character
        );

        return new LetterMatch
        {
            Letter = letter,
            CharacterIndex = index
        };
    }

    /// <summary>
    /// Get all segments (including removed ones) for testing overlap types.
    /// This is a test helper that modifies the segmenter to return all segments.
    /// </summary>
    private List<TextSegment> GetAllSegments(
        string text,
        PdfRectangle operationBounds,
        List<LetterMatch> letterMatches,
        PdfRectangle redactionArea)
    {
        // Build segments normally
        var keptSegments = _segmenter.BuildSegments(text, operationBounds, letterMatches, redactionArea);

        // Rebuild with inverted logic to get removed segments
        var allSegments = new List<TextSegment>();
        TextSegment? currentSegment = null;

        for (int i = 0; i < text.Length; i++)
        {
            var match = letterMatches.FirstOrDefault(m => m.CharacterIndex == i);
            if (match == null) continue;

            var (shouldRemove, overlapType) = GetLetterOverlapInfo(
                match.Letter.GlyphRectangle, redactionArea);

            bool keep = !shouldRemove;
            var rect = match.Letter.GlyphRectangle;

            if (currentSegment == null || currentSegment.Keep != keep ||
                (!keep && currentSegment.OverlapType != overlapType))
            {
                if (currentSegment != null)
                {
                    allSegments.Add(currentSegment);
                }

                currentSegment = new TextSegment
                {
                    StartIndex = i,
                    EndIndex = i + 1,
                    Keep = keep,
                    OverlapType = keep ? GlyphOverlapType.None : overlapType,
                    StartX = rect.Left,
                    StartY = rect.Bottom,
                    Width = rect.Width,
                    Height = rect.Height,
                    OriginalText = text
                };
            }
            else
            {
                currentSegment.EndIndex = i + 1;
                currentSegment.Width += rect.Width;
            }
        }

        if (currentSegment != null)
        {
            allSegments.Add(currentSegment);
        }

        return allSegments;
    }

    private (bool ShouldRemove, GlyphOverlapType OverlapType) GetLetterOverlapInfo(
        PdfRectangle glyphRect,
        PdfRectangle redactionArea)
    {
        var normalizedGlyph = glyphRect.Normalize();
        var normalizedRedaction = redactionArea.Normalize();

        if (!normalizedGlyph.IntersectsWith(normalizedRedaction))
            return (false, GlyphOverlapType.None);

        bool fullyContained =
            normalizedRedaction.Contains(normalizedGlyph.Left, normalizedGlyph.Bottom) &&
            normalizedRedaction.Contains(normalizedGlyph.Right, normalizedGlyph.Top) &&
            normalizedRedaction.Contains(normalizedGlyph.Left, normalizedGlyph.Top) &&
            normalizedRedaction.Contains(normalizedGlyph.Right, normalizedGlyph.Bottom);

        var overlapType = fullyContained ? GlyphOverlapType.Full : GlyphOverlapType.Partial;
        return (true, overlapType);
    }

    #endregion
}
