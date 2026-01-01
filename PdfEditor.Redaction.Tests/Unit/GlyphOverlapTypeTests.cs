using FluentAssertions;
using PdfEditor.Redaction;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Unit tests for GlyphOverlapType detection.
/// Tests the PdfRectangle.GetOverlapType() method for detecting partial glyph overlaps.
/// Part of issue #206 and #211.
/// </summary>
public class GlyphOverlapTypeTests
{
    #region GetOverlapType Tests

    [Fact]
    public void GetOverlapType_NoIntersection_ReturnsNone()
    {
        // Arrange - redaction area and glyph don't overlap
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(300, 300, 320, 320);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.None);
    }

    [Fact]
    public void GetOverlapType_GlyphFullyInside_ReturnsFull()
    {
        // Arrange - glyph is entirely within redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(120, 120, 140, 140);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Full);
    }

    [Fact]
    public void GetOverlapType_GlyphPartiallyOverlapping_ReturnsPartial()
    {
        // Arrange - glyph overlaps but extends beyond redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(180, 180, 220, 220); // Extends beyond right and top

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Partial);
    }

    [Fact]
    public void GetOverlapType_GlyphOnLeftEdge_ReturnsPartial()
    {
        // Arrange - glyph extends to the left of redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(80, 150, 120, 170);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Partial);
    }

    [Fact]
    public void GetOverlapType_GlyphOnRightEdge_ReturnsPartial()
    {
        // Arrange - glyph extends to the right of redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(180, 150, 220, 170);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Partial);
    }

    [Fact]
    public void GetOverlapType_GlyphOnBottomEdge_ReturnsPartial()
    {
        // Arrange - glyph extends below redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(150, 80, 170, 120);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Partial);
    }

    [Fact]
    public void GetOverlapType_GlyphOnTopEdge_ReturnsPartial()
    {
        // Arrange - glyph extends above redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(150, 180, 170, 220);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Partial);
    }

    [Fact]
    public void GetOverlapType_GlyphCompletelyContainsRedaction_ReturnsPartial()
    {
        // Arrange - glyph is larger than and contains the redaction area
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(50, 50, 250, 250);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        // This is partial because the glyph is not fully inside the redaction area
        result.Should().Be(GlyphOverlapType.Partial);
    }

    [Fact]
    public void GetOverlapType_GlyphExactlySameAsRedaction_ReturnsFull()
    {
        // Arrange - glyph is exactly the same size as redaction
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(100, 100, 200, 200);

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        result.Should().Be(GlyphOverlapType.Full);
    }

    [Fact]
    public void GetOverlapType_GlyphTouchingEdge_ReturnsNone()
    {
        // Arrange - glyph touches but doesn't overlap (adjacent)
        var redactionArea = new PdfRectangle(100, 100, 200, 200);
        var glyph = new PdfRectangle(200, 100, 220, 200); // Right edge touches left edge of glyph

        // Act
        var result = redactionArea.GetOverlapType(glyph);

        // Assert
        // Touching but not overlapping should be None
        result.Should().Be(GlyphOverlapType.None);
    }

    #endregion

    #region Contains(PdfRectangle) Tests

    [Fact]
    public void Contains_FullyContained_ReturnsTrue()
    {
        var outer = new PdfRectangle(0, 0, 100, 100);
        var inner = new PdfRectangle(10, 10, 90, 90);

        outer.Contains(inner).Should().BeTrue();
    }

    [Fact]
    public void Contains_SameRectangle_ReturnsTrue()
    {
        var rect = new PdfRectangle(0, 0, 100, 100);

        rect.Contains(rect).Should().BeTrue();
    }

    [Fact]
    public void Contains_PartialOverlap_ReturnsFalse()
    {
        var outer = new PdfRectangle(0, 0, 100, 100);
        var inner = new PdfRectangle(50, 50, 150, 150);

        outer.Contains(inner).Should().BeFalse();
    }

    [Fact]
    public void Contains_NoOverlap_ReturnsFalse()
    {
        var outer = new PdfRectangle(0, 0, 100, 100);
        var inner = new PdfRectangle(200, 200, 300, 300);

        outer.Contains(inner).Should().BeFalse();
    }

    #endregion

    #region FromPdfPig Normalization Tests

    [Fact]
    public void FromPdfPig_NormalCoordinates_PreservesThem()
    {
        var pdfPigRect = new UglyToad.PdfPig.Core.PdfRectangle(10, 20, 30, 40);

        var result = PdfRectangle.FromPdfPig(pdfPigRect);

        result.Left.Should().Be(10);
        result.Bottom.Should().Be(20);
        result.Right.Should().Be(30);
        result.Top.Should().Be(40);
    }

    [Fact]
    public void FromPdfPig_SwappedLeftRight_Normalizes()
    {
        // Rotated text can have Left > Right
        var pdfPigRect = new UglyToad.PdfPig.Core.PdfRectangle(30, 20, 10, 40);

        var result = PdfRectangle.FromPdfPig(pdfPigRect);

        result.Left.Should().Be(10);
        result.Right.Should().Be(30);
    }

    [Fact]
    public void FromPdfPig_SwappedBottomTop_Normalizes()
    {
        // Rotated text can have Bottom > Top
        var pdfPigRect = new UglyToad.PdfPig.Core.PdfRectangle(10, 40, 30, 20);

        var result = PdfRectangle.FromPdfPig(pdfPigRect);

        result.Bottom.Should().Be(20);
        result.Top.Should().Be(40);
    }

    [Fact]
    public void FromPdfPig_BothSwapped_NormalizesAll()
    {
        // 180° rotated text might have both swapped
        var pdfPigRect = new UglyToad.PdfPig.Core.PdfRectangle(30, 40, 10, 20);

        var result = PdfRectangle.FromPdfPig(pdfPigRect);

        result.Left.Should().Be(10);
        result.Bottom.Should().Be(20);
        result.Right.Should().Be(30);
        result.Top.Should().Be(40);
    }

    #endregion
}
