using FluentAssertions;
using PdfEditor.Redaction;
using PdfEditor.Redaction.GlyphLevel;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Unit tests for PartialGlyphRasterizer.
/// Tests the GetVisibleRegions static method and coordinate conversion logic.
/// Part of issue #207: Render partial glyph region to high-resolution image.
/// </summary>
public class PartialGlyphRasterizerTests
{
    #region GetVisibleRegions Tests

    [Fact]
    public void GetVisibleRegions_NoIntersection_ReturnsEntireGlyph()
    {
        // Arrange - glyph and redaction don't overlap
        var glyph = new PdfRectangle(0, 0, 20, 20);
        var redaction = new PdfRectangle(100, 100, 200, 200);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert
        regions.Should().HaveCount(1);
        regions[0].Should().Be(glyph);
    }

    [Fact]
    public void GetVisibleRegions_GlyphFullyCovered_ReturnsEmpty()
    {
        // Arrange - redaction fully covers glyph
        var glyph = new PdfRectangle(110, 110, 130, 130);
        var redaction = new PdfRectangle(100, 100, 200, 200);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert
        // When fully covered, there should be no visible regions (or possibly empty regions)
        regions.Should().BeEmpty();
    }

    [Fact]
    public void GetVisibleRegions_RedactionFromLeft_ReturnsRightPortion()
    {
        // Arrange - redaction covers left half of glyph
        var glyph = new PdfRectangle(80, 100, 120, 120);
        var redaction = new PdfRectangle(0, 0, 100, 200);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert
        regions.Should().ContainSingle(r =>
            r.Left == 100 && r.Right == 120 &&
            r.Bottom == 100 && r.Top == 120,
            "Right portion of glyph should be visible");
    }

    [Fact]
    public void GetVisibleRegions_RedactionFromRight_ReturnsLeftPortion()
    {
        // Arrange - redaction covers right half of glyph
        var glyph = new PdfRectangle(80, 100, 120, 120);
        var redaction = new PdfRectangle(100, 0, 200, 200);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert
        regions.Should().ContainSingle(r =>
            r.Left == 80 && r.Right == 100 &&
            r.Bottom == 100 && r.Top == 120,
            "Left portion of glyph should be visible");
    }

    [Fact]
    public void GetVisibleRegions_RedactionFromTop_ReturnsBottomPortion()
    {
        // Arrange - redaction covers top half of glyph
        var glyph = new PdfRectangle(100, 80, 120, 120);
        var redaction = new PdfRectangle(0, 100, 200, 200);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert - should have bottom portion visible
        regions.Should().Contain(r =>
            r.Bottom == 80 && r.Top == 100,
            "Bottom portion of glyph should be visible");
    }

    [Fact]
    public void GetVisibleRegions_RedactionFromBottom_ReturnsTopPortion()
    {
        // Arrange - redaction covers bottom half of glyph
        var glyph = new PdfRectangle(100, 80, 120, 120);
        var redaction = new PdfRectangle(0, 0, 200, 100);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert - should have top portion visible
        regions.Should().Contain(r =>
            r.Bottom == 100 && r.Top == 120,
            "Top portion of glyph should be visible");
    }

    [Fact]
    public void GetVisibleRegions_RedactionInMiddle_ReturnsLeftAndRightPortions()
    {
        // Arrange - thin vertical redaction in middle of glyph
        var glyph = new PdfRectangle(80, 100, 120, 120);
        var redaction = new PdfRectangle(95, 100, 105, 120);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert - should have left and right portions
        regions.Should().HaveCount(2);
        regions.Should().Contain(r => r.Left == 80 && r.Right == 95, "Left portion");
        regions.Should().Contain(r => r.Left == 105 && r.Right == 120, "Right portion");
    }

    [Fact]
    public void GetVisibleRegions_CornerOverlap_ReturnsLShapedRegions()
    {
        // Arrange - redaction overlaps bottom-left corner
        var glyph = new PdfRectangle(90, 90, 110, 110);
        var redaction = new PdfRectangle(0, 0, 100, 100);

        // Act
        var regions = PartialGlyphRasterizer.GetVisibleRegions(glyph, redaction);

        // Assert - should have right and top portions (L-shaped)
        regions.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_NullPdfBytes_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new PartialGlyphRasterizer(null!, 0, 792.0);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("pdfBytes");
    }

    [Fact]
    public void Constructor_ValidParameters_CreatesInstance()
    {
        // Arrange
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"

        // Act
        using var rasterizer = new PartialGlyphRasterizer(pdfBytes, 0, 792.0);

        // Assert
        rasterizer.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_CustomDpi_AcceptsValue()
    {
        // Arrange
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        // Act - should not throw
        using var rasterizer150 = new PartialGlyphRasterizer(pdfBytes, 0, 792.0, dpi: 150);
        using var rasterizer600 = new PartialGlyphRasterizer(pdfBytes, 0, 792.0, dpi: 600);

        // Assert
        rasterizer150.Should().NotBeNull();
        rasterizer600.Should().NotBeNull();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var rasterizer = new PartialGlyphRasterizer(pdfBytes, 0, 792.0);

        // Act & Assert - should not throw
        rasterizer.Dispose();
        rasterizer.Dispose();
    }

    #endregion
}
