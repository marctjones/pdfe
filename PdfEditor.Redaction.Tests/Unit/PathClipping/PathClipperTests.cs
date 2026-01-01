using FluentAssertions;
using PdfEditor.Redaction;
using PdfEditor.Redaction.PathClipping;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.PathClipping;

/// <summary>
/// Tests for the PathClipper class that performs polygon clipping using Clipper2.
/// Issue #197: Partial shape coverage redaction.
/// </summary>
public class PathClipperTests
{
    private readonly PathClipper _clipper = new();

    [Fact]
    public void ClipPath_NoOverlap_ReturnsOriginalPath()
    {
        // Arrange: Rectangle at (0,0)-(100,100), redaction area at (200,200)-(300,300)
        var path = new List<PathPoint>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)
        };
        var redactionArea = new PdfRectangle(200, 200, 300, 300);

        // Act
        var result = _clipper.ClipPath(path, redactionArea);

        // Assert: Original path should be returned (no change)
        // Clipper2 normalizes to 4 points (without duplicate closing point)
        result.Should().HaveCount(1);
        result[0].Should().HaveCountGreaterOrEqualTo(4);
    }

    [Fact]
    public void ClipPath_FullyContained_ReturnsEmpty()
    {
        // Arrange: Rectangle at (50,50)-(150,150), redaction covers entire shape
        var path = new List<PathPoint>
        {
            new(50, 50), new(150, 50), new(150, 150), new(50, 150), new(50, 50)
        };
        var redactionArea = new PdfRectangle(0, 0, 200, 200);

        // Act
        var result = _clipper.ClipPath(path, redactionArea);

        // Assert: Path should be fully removed
        result.Should().BeEmpty();
    }

    [Fact]
    public void ClipPath_PartialOverlap_ReturnsClippedPath()
    {
        // Arrange: Rectangle at (0,0)-(100,100), redaction area at (50,50)-(150,150)
        // This should remove the upper-right corner of the rectangle
        var path = new List<PathPoint>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)
        };
        var redactionArea = new PdfRectangle(50, 50, 150, 150);

        // Act
        var result = _clipper.ClipPath(path, redactionArea);

        // Assert: Should have one polygon with the corner removed (L-shape)
        result.Should().HaveCount(1);
        result[0].Count.Should().BeGreaterThan(4, "Clipped polygon should have more vertices than original rectangle");
    }

    [Fact]
    public void ClipPath_MiddleSlice_ReturnsTwoParts()
    {
        // Arrange: Rectangle at (0,0)-(100,100), redaction cuts through the middle horizontally
        var path = new List<PathPoint>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100), new(0, 0)
        };
        var redactionArea = new PdfRectangle(40, -10, 60, 110); // Vertical slice through middle

        // Act
        var result = _clipper.ClipPath(path, redactionArea);

        // Assert: Should have two separate polygons (left and right parts)
        result.Should().HaveCount(2);
    }

    [Fact]
    public void HasOverlap_NoOverlap_ReturnsFalse()
    {
        var path = new List<PathPoint>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        var redactionArea = new PdfRectangle(200, 200, 300, 300);

        _clipper.HasOverlap(path, redactionArea).Should().BeFalse();
    }

    [Fact]
    public void HasOverlap_WithOverlap_ReturnsTrue()
    {
        var path = new List<PathPoint>
        {
            new(0, 0), new(100, 0), new(100, 100), new(0, 100)
        };
        var redactionArea = new PdfRectangle(50, 50, 150, 150);

        _clipper.HasOverlap(path, redactionArea).Should().BeTrue();
    }

    [Fact]
    public void IsFullyContained_FullyContained_ReturnsTrue()
    {
        var path = new List<PathPoint>
        {
            new(25, 25), new(75, 25), new(75, 75), new(25, 75)
        };
        var redactionArea = new PdfRectangle(0, 0, 100, 100);

        _clipper.IsFullyContained(path, redactionArea).Should().BeTrue();
    }

    [Fact]
    public void IsFullyContained_PartiallyOutside_ReturnsFalse()
    {
        var path = new List<PathPoint>
        {
            new(50, 50), new(150, 50), new(150, 150), new(50, 150)
        };
        var redactionArea = new PdfRectangle(0, 0, 100, 100);

        _clipper.IsFullyContained(path, redactionArea).Should().BeFalse();
    }
}
