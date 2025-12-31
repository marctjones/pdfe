using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Unit tests for RotationTransform coordinate transformations.
///
/// These tests use empirically measured data from RotationDiagnosticTests to verify
/// that the transformation formulas correctly map between visual and content stream spaces.
///
/// EMPIRICAL DATA (from diagnostic tests):
/// MediaBox: 612 x 792 for all rotations
///
/// Rotation | PdfPig Visual BBox           | Parser Content Stream BBox
/// ---------|------------------------------|----------------------------
/// 0°       | (302.4,391.7)-(398.2,409.8)  | (300.0,392.0)-(386.4,416.0)
/// 90°      | (492.0,121.8)-(509.5,217.6)  | (392.0,492.0)-(478.4,516.0)
/// 180°     | (201.8,374.2)-(297.6,392.3)  | (312.0,400.0)-(398.4,424.0)
/// 270°     | (474.2,416.0)-(492.3,483.4)  | (400.0,300.0)-(486.4,324.0)
/// </summary>
public class RotationTransformTests
{
    private readonly ITestOutputHelper _output;
    private const double MediaBoxWidth = 612;
    private const double MediaBoxHeight = 792;
    private const double Tolerance = 30.0; // Allow for font rendering differences

    public RotationTransformTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GetVisualDimensions_0Degrees_SameDimensions()
    {
        var (w, h) = RotationTransform.GetVisualDimensions(MediaBoxWidth, MediaBoxHeight, 0);
        w.Should().Be(MediaBoxWidth);
        h.Should().Be(MediaBoxHeight);
    }

    [Fact]
    public void GetVisualDimensions_90Degrees_SwappedDimensions()
    {
        var (w, h) = RotationTransform.GetVisualDimensions(MediaBoxWidth, MediaBoxHeight, 90);
        w.Should().Be(MediaBoxHeight); // 792
        h.Should().Be(MediaBoxWidth);  // 612
    }

    [Fact]
    public void GetVisualDimensions_180Degrees_SameDimensions()
    {
        var (w, h) = RotationTransform.GetVisualDimensions(MediaBoxWidth, MediaBoxHeight, 180);
        w.Should().Be(MediaBoxWidth);
        h.Should().Be(MediaBoxHeight);
    }

    [Fact]
    public void GetVisualDimensions_270Degrees_SwappedDimensions()
    {
        var (w, h) = RotationTransform.GetVisualDimensions(MediaBoxWidth, MediaBoxHeight, 270);
        w.Should().Be(MediaBoxHeight);
        h.Should().Be(MediaBoxWidth);
    }

    [Fact]
    public void VisualToContentStream_0Degrees_NoChange()
    {
        // 0°: Visual and content stream are the same
        var (cx, cy) = RotationTransform.VisualToContentStream(302.4, 391.7, 0, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"0° transform: Visual (302.4, 391.7) → Content ({cx:F1}, {cy:F1})");
        _output.WriteLine($"Expected: ~(300.0, 392.0)");

        cx.Should().BeApproximately(302.4, 5, "X should be unchanged for 0°");
        cy.Should().BeApproximately(391.7, 5, "Y should be unchanged for 0°");
    }

    [Fact]
    public void VisualToContentStream_90Degrees_TransformsCorrectly()
    {
        // 90° rotation: Visual page is 792x612 (swapped from MediaBox 612x792)
        // Formula: contentX = visualHeight - visualY, contentY = visualX
        // where visualHeight = 612 for 90° rotation
        var visualX = 492.0;
        var visualY = 121.8;
        var visualHeight = MediaBoxWidth; // 612 for 90° rotation

        // Expected per formula:
        var expectedContentX = visualHeight - visualY; // 612 - 121.8 = 490.2
        var expectedContentY = visualX;                // 492.0

        var (cx, cy) = RotationTransform.VisualToContentStream(visualX, visualY, 90, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"90° transform: Visual ({visualX}, {visualY}) → Content ({cx:F1}, {cy:F1})");
        _output.WriteLine($"Expected: ~({expectedContentX:F1}, {expectedContentY:F1})");
        _output.WriteLine($"Formula used: contentX = visualHeight - visualY = {visualHeight} - {visualY} = {visualHeight - visualY:F1}");
        _output.WriteLine($"Formula used: contentY = visualX = {visualX}");

        // Check transformation matches formula
        cx.Should().BeApproximately(expectedContentX, 5,
            $"90° X transform: Visual ({visualX}, {visualY}) → Content X should be ~{expectedContentX:F1}");
        cy.Should().BeApproximately(expectedContentY, 5,
            $"90° Y transform: Visual ({visualX}, {visualY}) → Content Y should be ~{expectedContentY:F1}");
    }

    [Fact]
    public void VisualToContentStream_180Degrees_TransformsCorrectly()
    {
        // 180°: Visual (201.8-297.6, 374.2-392.3) → Content (312.0-398.4, 400.0-424.0)
        // Using visual left-bottom corner: (201.8, 374.2)
        var visualX = 201.8;
        var visualY = 374.2;
        var expectedContentX = 312.0; // Actually should map to right edge of content
        var expectedContentY = 400.0;

        var (cx, cy) = RotationTransform.VisualToContentStream(visualX, visualY, 180, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"180° transform: Visual ({visualX}, {visualY}) → Content ({cx:F1}, {cy:F1})");
        _output.WriteLine($"Expected: ~({expectedContentX}, {expectedContentY})");
        _output.WriteLine($"Formula used: contentX = mediaBoxWidth - visualX = {MediaBoxWidth} - {visualX} = {MediaBoxWidth - visualX}");
        _output.WriteLine($"Formula used: contentY = mediaBoxHeight - visualY = {MediaBoxHeight} - {visualY} = {MediaBoxHeight - visualY}");

        // For 180°, the LEFT of visual maps to RIGHT of content (text direction is reversed)
        // So we expect contentX to be near the RIGHT edge of content box (398.4)
        var actualExpectedX = MediaBoxWidth - visualX; // 612 - 201.8 = 410.2
        var actualExpectedY = MediaBoxHeight - visualY; // 792 - 374.2 = 417.8

        cx.Should().BeApproximately(actualExpectedX, Tolerance,
            $"180° X transform: Visual ({visualX}, {visualY}) → Content X should be ~{actualExpectedX}");
        cy.Should().BeApproximately(actualExpectedY, Tolerance,
            $"180° Y transform: Visual ({visualX}, {visualY}) → Content Y should be ~{actualExpectedY}");
    }

    [Fact]
    public void VisualToContentStream_270Degrees_TransformsCorrectly()
    {
        // 270° rotation: Visual page is 792x612 (swapped from MediaBox 612x792)
        // Formula: contentX = visualY, contentY = mediaBoxHeight - visualX
        var visualX = 474.2;
        var visualY = 416.0;

        // Expected per formula:
        var expectedContentX = visualY;                        // 416.0
        var expectedContentY = MediaBoxHeight - visualX;       // 792 - 474.2 = 317.8

        var (cx, cy) = RotationTransform.VisualToContentStream(visualX, visualY, 270, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"270° transform: Visual ({visualX}, {visualY}) → Content ({cx:F1}, {cy:F1})");
        _output.WriteLine($"Expected: ~({expectedContentX:F1}, {expectedContentY:F1})");
        _output.WriteLine($"Formula used: contentX = visualY = {visualY}");
        _output.WriteLine($"Formula used: contentY = mediaBoxHeight - visualX = {MediaBoxHeight} - {visualX} = {MediaBoxHeight - visualX:F1}");

        cx.Should().BeApproximately(expectedContentX, 5,
            $"270° X transform: Visual ({visualX}, {visualY}) → Content X should be ~{expectedContentX:F1}");
        cy.Should().BeApproximately(expectedContentY, 5,
            $"270° Y transform: Visual ({visualX}, {visualY}) → Content Y should be ~{expectedContentY:F1}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void VisualToContentStream_Rectangle_ProducesValidBounds(int rotation)
    {
        // Test that rectangle transformation produces valid bounds (min < max)
        var visualRect = new PdfRectangle(100, 200, 300, 400);

        var contentRect = RotationTransform.VisualToContentStream(
            visualRect, rotation, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"{rotation}° rect transform:");
        _output.WriteLine($"  Visual: ({visualRect.Left},{visualRect.Bottom}) to ({visualRect.Right},{visualRect.Top})");
        _output.WriteLine($"  Content: ({contentRect.Left:F1},{contentRect.Bottom:F1}) to ({contentRect.Right:F1},{contentRect.Top:F1})");

        contentRect.Left.Should().BeLessThan(contentRect.Right, "Left should be less than Right");
        contentRect.Bottom.Should().BeLessThan(contentRect.Top, "Bottom should be less than Top");
        contentRect.Width.Should().BeGreaterThan(0, "Width should be positive");
        contentRect.Height.Should().BeGreaterThan(0, "Height should be positive");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RoundTrip_VisualToContentAndBack_ReturnsOriginal(int rotation)
    {
        // Test that transforming to content and back returns the original point
        var originalX = 350.0;
        var originalY = 450.0;

        var (contentX, contentY) = RotationTransform.VisualToContentStream(
            originalX, originalY, rotation, MediaBoxWidth, MediaBoxHeight);

        var (roundTripX, roundTripY) = RotationTransform.ContentStreamToVisual(
            contentX, contentY, rotation, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"{rotation}° round trip:");
        _output.WriteLine($"  Original: ({originalX}, {originalY})");
        _output.WriteLine($"  → Content: ({contentX:F1}, {contentY:F1})");
        _output.WriteLine($"  → Back: ({roundTripX:F1}, {roundTripY:F1})");

        roundTripX.Should().BeApproximately(originalX, 0.01, "X should round-trip correctly");
        roundTripY.Should().BeApproximately(originalY, 0.01, "Y should round-trip correctly");
    }

    /// <summary>
    /// Test using the EXACT diagnostic data to verify formulas produce intersecting boxes.
    /// </summary>
    [Theory]
    [InlineData(0, 302.4, 391.7, 398.2, 409.8, 300.0, 392.0, 386.4, 416.0)]
    [InlineData(90, 492.0, 121.8, 509.5, 217.6, 392.0, 492.0, 478.4, 516.0)]
    [InlineData(180, 201.8, 374.2, 297.6, 392.3, 312.0, 400.0, 398.4, 424.0)]
    [InlineData(270, 474.2, 416.0, 492.3, 483.4, 400.0, 300.0, 486.4, 324.0)]
    public void TransformedVisualBox_IntersectsWithContentBox(
        int rotation,
        double visualLeft, double visualBottom, double visualRight, double visualTop,
        double contentLeft, double contentBottom, double contentRight, double contentTop)
    {
        var visualRect = new PdfRectangle(visualLeft, visualBottom, visualRight, visualTop);
        var contentRect = new PdfRectangle(contentLeft, contentBottom, contentRight, contentTop);

        var transformedRect = RotationTransform.VisualToContentStream(
            visualRect, rotation, MediaBoxWidth, MediaBoxHeight);

        _output.WriteLine($"");
        _output.WriteLine($"=== Rotation {rotation}° Intersection Test ===");
        _output.WriteLine($"Visual rect (PdfPig):     ({visualRect.Left:F1},{visualRect.Bottom:F1}) to ({visualRect.Right:F1},{visualRect.Top:F1})");
        _output.WriteLine($"Transformed to content:   ({transformedRect.Left:F1},{transformedRect.Bottom:F1}) to ({transformedRect.Right:F1},{transformedRect.Top:F1})");
        _output.WriteLine($"Expected content (Parser):({contentRect.Left:F1},{contentRect.Bottom:F1}) to ({contentRect.Right:F1},{contentRect.Top:F1})");

        // Check for intersection
        bool intersects = !(transformedRect.Right < contentRect.Left ||
                           contentRect.Right < transformedRect.Left ||
                           transformedRect.Top < contentRect.Bottom ||
                           contentRect.Top < transformedRect.Bottom);

        _output.WriteLine($"Intersects: {intersects}");

        intersects.Should().BeTrue(
            $"Transformed visual rect should intersect with content rect for {rotation}° rotation. " +
            $"Transformed: ({transformedRect.Left:F1},{transformedRect.Bottom:F1})-({transformedRect.Right:F1},{transformedRect.Top:F1}), " +
            $"Content: ({contentRect.Left:F1},{contentRect.Bottom:F1})-({contentRect.Right:F1},{contentRect.Top:F1})");
    }
}
