using FluentAssertions;
using Xunit;

namespace PdfEditor.Redaction.Tests;

/// <summary>
/// Tests for PdfRectangle struct - core geometry for redaction.
/// </summary>
public class PdfRectangleTests
{
    [Fact]
    public void Width_CalculatedCorrectly()
    {
        var rect = new PdfRectangle(10, 20, 110, 120);
        rect.Width.Should().Be(100);
    }

    [Fact]
    public void Height_CalculatedCorrectly()
    {
        var rect = new PdfRectangle(10, 20, 110, 120);
        rect.Height.Should().Be(100);
    }

    [Theory]
    [InlineData(50, 50, 150, 150, true)]   // Overlapping interior
    [InlineData(25, 25, 75, 75, true)]     // Overlapping corner region
    [InlineData(200, 200, 300, 300, false)] // No overlap - completely separate
    [InlineData(100, 0, 200, 100, false)]  // Adjacent at right edge (no overlap)
    [InlineData(0, 100, 100, 200, false)]  // Adjacent at top edge (no overlap)
    [InlineData(50, 25, 150, 75, true)]    // Overlapping horizontally
    [InlineData(25, 50, 75, 150, true)]    // Overlapping vertically
    public void IntersectsWith_DetectsIntersectionsCorrectly(
        double left, double bottom, double right, double top, bool expected)
    {
        // Arrange
        var rect1 = new PdfRectangle(0, 0, 100, 100);
        var rect2 = new PdfRectangle(left, bottom, right, top);

        // Act
        var result = rect1.IntersectsWith(rect2);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IntersectsWith_IsSymmetric()
    {
        var rect1 = new PdfRectangle(0, 0, 100, 100);
        var rect2 = new PdfRectangle(50, 50, 150, 150);

        rect1.IntersectsWith(rect2).Should().Be(rect2.IntersectsWith(rect1));
    }

    [Theory]
    [InlineData(50, 50, true)]     // Inside
    [InlineData(0, 0, true)]       // Bottom-left corner
    [InlineData(100, 100, true)]   // Top-right corner
    [InlineData(-1, 50, false)]    // Outside left
    [InlineData(101, 50, false)]   // Outside right
    [InlineData(50, -1, false)]    // Outside bottom
    [InlineData(50, 101, false)]   // Outside top
    public void Contains_DetectsPointsCorrectly(double x, double y, bool expected)
    {
        var rect = new PdfRectangle(0, 0, 100, 100);
        rect.Contains(x, y).Should().Be(expected);
    }

    [Fact]
    public void RecordEquality_Works()
    {
        var rect1 = new PdfRectangle(10, 20, 30, 40);
        var rect2 = new PdfRectangle(10, 20, 30, 40);
        var rect3 = new PdfRectangle(10, 20, 30, 41);

        rect1.Should().Be(rect2);
        rect1.Should().NotBe(rect3);
    }
}
