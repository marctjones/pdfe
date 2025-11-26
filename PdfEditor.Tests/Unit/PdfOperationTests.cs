using Xunit;
using FluentAssertions;
using Avalonia;
using PdfEditor.Services.Redaction;
using PdfSharp.Pdf.Content.Objects;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for PdfOperation intersection and containment logic
/// These tests verify that the redaction correctly identifies which operations
/// should be removed based on their bounding boxes.
/// </summary>
public class PdfOperationTests
{
    #region Intersection Tests

    [Theory]
    [InlineData(0, 0, 100, 50, 50, 25, 100, 50, true)]     // Overlapping right side
    [InlineData(0, 0, 100, 50, 200, 200, 50, 50, false)]   // Non-overlapping (distant)
    [InlineData(0, 0, 100, 50, 25, 10, 50, 30, true)]      // Overlapping contained
    [InlineData(0, 0, 100, 50, -50, -50, 75, 75, true)]    // Partial overlap top-left
    [InlineData(100, 100, 50, 50, 0, 0, 100, 100, false)]  // Touching but not overlapping
    [InlineData(0, 0, 100, 100, 0, 0, 100, 100, true)]     // Exact same area
    [InlineData(10, 10, 80, 80, 0, 0, 100, 100, true)]     // Inner contained in outer
    [InlineData(0, 0, 100, 100, 10, 10, 80, 80, true)]     // Outer contains inner
    [InlineData(0, 0, 100, 50, 100, 0, 100, 50, false)]    // Adjacent horizontally
    [InlineData(0, 0, 100, 50, 0, 50, 100, 50, false)]     // Adjacent vertically
    public void IntersectsWith_VariousBounds_ShouldCalculateCorrectly(
        double op_x, double op_y, double op_w, double op_h,
        double area_x, double area_y, double area_w, double area_h,
        bool expectedResult)
    {
        // Arrange
        var operation = CreateTextOperationWithBounds(op_x, op_y, op_w, op_h);
        var redactionArea = new Rect(area_x, area_y, area_w, area_h);

        // Act
        var result = operation.IntersectsWith(redactionArea);

        // Assert
        result.Should().Be(expectedResult,
            $"Operation at ({op_x},{op_y},{op_w}x{op_h}) intersection with " +
            $"area ({area_x},{area_y},{area_w}x{area_h}) should be {expectedResult}");
    }

    [Fact]
    public void IntersectsWith_ZeroWidthBoundingBox_ShouldReturnFalse()
    {
        // Arrange - Operation with zero width
        var operation = CreateTextOperationWithBounds(50, 50, 0, 20);
        var redactionArea = new Rect(0, 0, 100, 100);

        // Act
        var result = operation.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeFalse("Operations with zero width should not intersect");
    }

    [Fact]
    public void IntersectsWith_ZeroHeightBoundingBox_ShouldReturnFalse()
    {
        // Arrange - Operation with zero height
        var operation = CreateTextOperationWithBounds(50, 50, 100, 0);
        var redactionArea = new Rect(0, 0, 100, 100);

        // Act
        var result = operation.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeFalse("Operations with zero height should not intersect");
    }

    [Fact]
    public void IntersectsWith_StateOperation_ShouldAlwaysReturnFalse()
    {
        // Arrange - State operations should never be removed
        var cSequence = new CSequence();
        var stateOp = new StateOperation(cSequence)
        {
            Type = StateOperationType.SaveState,
            BoundingBox = new Rect(0, 0, 100, 100)
        };
        var redactionArea = new Rect(0, 0, 100, 100);

        // Act
        var result = stateOp.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeFalse("State operations should never be removed");
    }

    [Fact]
    public void IntersectsWith_TextStateOperation_ShouldAlwaysReturnFalse()
    {
        // Arrange - Text state operations should never be removed
        var cSequence = new CSequence();
        var textStateOp = new TextStateOperation(cSequence)
        {
            Type = TextStateOperationType.BeginText,
            BoundingBox = new Rect(0, 0, 100, 100)
        };
        var redactionArea = new Rect(0, 0, 100, 100);

        // Act
        var result = textStateOp.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeFalse("Text state operations should never be removed");
    }

    [Fact]
    public void IntersectsWith_GenericOperation_ShouldAlwaysReturnFalse()
    {
        // Arrange - Generic operations should be preserved
        var cSequence = new CSequence();
        var genericOp = new GenericOperation(cSequence, "custom")
        {
            BoundingBox = new Rect(0, 0, 100, 100)
        };
        var redactionArea = new Rect(0, 0, 100, 100);

        // Act
        var result = genericOp.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeFalse("Generic operations should be preserved");
    }

    #endregion

    #region Containment Tests

    [Theory]
    [InlineData(25, 25, 50, 50, 0, 0, 100, 100, true)]     // Completely contained
    [InlineData(0, 0, 100, 100, 0, 0, 100, 100, true)]     // Exact match
    [InlineData(0, 0, 100, 100, 25, 25, 50, 50, false)]    // Larger than container
    [InlineData(50, 50, 100, 100, 0, 0, 100, 100, false)]  // Partially outside
    [InlineData(0, 0, 50, 50, 50, 50, 100, 100, false)]    // Completely outside
    public void IsContainedIn_VariousBounds_ShouldCalculateCorrectly(
        double op_x, double op_y, double op_w, double op_h,
        double area_x, double area_y, double area_w, double area_h,
        bool expectedResult)
    {
        // Arrange
        var operation = CreateTextOperationWithBounds(op_x, op_y, op_w, op_h);
        var container = new Rect(area_x, area_y, area_w, area_h);

        // Act
        var result = operation.IsContainedIn(container);

        // Assert
        result.Should().Be(expectedResult);
    }

    #endregion

    #region Path Operation Tests

    [Fact]
    public void PathOperation_Rectangle_ShouldHaveCorrectBoundingBox()
    {
        // Arrange
        var cSequence = new CSequence();
        var pathOp = new PathOperation(cSequence)
        {
            Type = PathType.Rectangle,
            BoundingBox = new Rect(100, 200, 150, 80)
        };
        var overlappingArea = new Rect(120, 220, 50, 50);
        var nonOverlappingArea = new Rect(300, 300, 50, 50);

        // Act & Assert
        pathOp.IntersectsWith(overlappingArea).Should().BeTrue();
        pathOp.IntersectsWith(nonOverlappingArea).Should().BeFalse();
    }

    [Fact]
    public void PathOperation_WithStroke_ShouldBeRemovable()
    {
        // Arrange
        var cSequence = new CSequence();
        var pathOp = new PathOperation(cSequence)
        {
            Type = PathType.Stroke,
            IsStroke = true,
            IsFill = false,
            BoundingBox = new Rect(50, 50, 100, 100)
        };
        var redactionArea = new Rect(60, 60, 50, 50);

        // Act
        var result = pathOp.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeTrue("Stroke operations should be removable");
    }

    [Fact]
    public void PathOperation_WithFill_ShouldBeRemovable()
    {
        // Arrange
        var cSequence = new CSequence();
        var pathOp = new PathOperation(cSequence)
        {
            Type = PathType.Fill,
            IsStroke = false,
            IsFill = true,
            BoundingBox = new Rect(50, 50, 100, 100)
        };
        var redactionArea = new Rect(60, 60, 50, 50);

        // Act
        var result = pathOp.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeTrue("Fill operations should be removable");
    }

    #endregion

    #region Image Operation Tests

    [Fact]
    public void ImageOperation_ShouldBeRemovableWhenIntersecting()
    {
        // Arrange
        var cSequence = new CSequence();
        var imageOp = new ImageOperation(cSequence)
        {
            ResourceName = "Im1",
            Position = new Point(100, 100),
            Width = 200,
            Height = 150,
            BoundingBox = new Rect(100, 100, 200, 150)
        };
        var overlappingArea = new Rect(150, 150, 100, 100);
        var nonOverlappingArea = new Rect(350, 350, 50, 50);

        // Act & Assert
        imageOp.IntersectsWith(overlappingArea).Should().BeTrue(
            "Image should be removed when redaction area overlaps");
        imageOp.IntersectsWith(nonOverlappingArea).Should().BeFalse(
            "Image should be preserved when redaction area does not overlap");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void IntersectsWith_VerySmallOverlap_ShouldBeFalse()
    {
        // Arrange - Just 1 point overlap at corner (99,49) to (100,50)
        // Text spans (0,0) to (100,50), selection spans (99,49) to (149,99)
        // This tiny overlap should NOT result in intersection to prevent
        // adjacent content from being accidentally removed during redaction.
        var operation = CreateTextOperationWithBounds(0, 0, 100, 50);
        var redactionArea = new Rect(99, 49, 50, 50); // 1 point overlap

        // Act
        var result = operation.IntersectsWith(redactionArea);

        // Assert - tiny overlaps should NOT intersect
        // This prevents adjacent lines from being accidentally removed
        result.Should().BeFalse("Tiny overlaps (< 20% of height) should not result in intersection");
    }

    [Fact]
    public void IntersectsWith_NegativeCoordinates_ShouldWorkCorrectly()
    {
        // Arrange - Negative coordinates (possible in transformed content)
        var operation = CreateTextOperationWithBounds(-50, -50, 100, 100);
        var redactionArea = new Rect(-25, -25, 50, 50);

        // Act
        var result = operation.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeTrue("Negative coordinates should work correctly");
    }

    [Fact]
    public void IntersectsWith_LargeCoordinates_ShouldWorkCorrectly()
    {
        // Arrange - Large page coordinates
        var operation = CreateTextOperationWithBounds(5000, 7000, 200, 50);
        var redactionArea = new Rect(5050, 7010, 100, 30);

        // Act
        var result = operation.IntersectsWith(redactionArea);

        // Assert
        result.Should().BeTrue("Large coordinates should work correctly");
    }

    #endregion

    #region Helper Methods

    private TextOperation CreateTextOperationWithBounds(double x, double y, double width, double height)
    {
        var cSequence = new CSequence();
        return new TextOperation(cSequence)
        {
            Text = "Test Text",
            BoundingBox = new Rect(x, y, width, height),
            Position = new Point(x, y),
            FontSize = 12,
            FontName = "Helvetica"
        };
    }

    #endregion
}
