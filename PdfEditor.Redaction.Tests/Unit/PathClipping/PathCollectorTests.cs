using FluentAssertions;
using PdfEditor.Redaction;
using PdfEditor.Redaction.PathClipping;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.PathClipping;

/// <summary>
/// Tests for the PathCollector class that groups path operations into complete paths.
/// Issue #197: Partial shape coverage redaction.
/// </summary>
public class PathCollectorTests
{
    private readonly PathCollector _collector = new();

    [Fact]
    public void CollectPaths_Rectangle_CollectsAsCompletePath()
    {
        // Arrange: A rectangle (re) followed by fill (f)
        var operations = new List<PdfOperation>
        {
            new PathOperation
            {
                Operator = "re",
                Operands = new List<object> { 50.0, 50.0, 100.0, 100.0 },
                StreamPosition = 0,
                Type = PathType.Rectangle,
                BoundingBox = new PdfRectangle(50, 50, 150, 150)
            },
            new PathOperation
            {
                Operator = "f",
                Operands = new List<object>(),
                StreamPosition = 1,
                Type = PathType.Fill,
                BoundingBox = new PdfRectangle(50, 50, 150, 150)
            }
        };

        // Act
        var paths = _collector.CollectPaths(operations);

        // Assert
        paths.Should().HaveCount(1);
        paths[0].IsRectangle.Should().BeTrue();
        paths[0].PaintType.Should().Be(PathType.Fill);
        paths[0].Subpaths.Should().HaveCount(1);
        paths[0].Subpaths[0].Should().HaveCount(5, "Rectangle has 4 corners + closing point");
    }

    [Fact]
    public void CollectPaths_Triangle_CollectsAsCompletePath()
    {
        // Arrange: m, l, l, h, f (triangle)
        var operations = new List<PdfOperation>
        {
            new PathOperation
            {
                Operator = "m",
                Operands = new List<object> { 50.0, 0.0 },
                StreamPosition = 0,
                Type = PathType.MoveTo
            },
            new PathOperation
            {
                Operator = "l",
                Operands = new List<object> { 0.0, 100.0 },
                StreamPosition = 1,
                Type = PathType.LineTo
            },
            new PathOperation
            {
                Operator = "l",
                Operands = new List<object> { 100.0, 100.0 },
                StreamPosition = 2,
                Type = PathType.LineTo
            },
            new PathOperation
            {
                Operator = "h",
                Operands = new List<object>(),
                StreamPosition = 3,
                Type = PathType.ClosePath
            },
            new PathOperation
            {
                Operator = "S",
                Operands = new List<object>(),
                StreamPosition = 4,
                Type = PathType.Stroke
            }
        };

        // Act
        var paths = _collector.CollectPaths(operations);

        // Assert
        paths.Should().HaveCount(1);
        paths[0].PaintType.Should().Be(PathType.Stroke);
        paths[0].Subpaths.Should().HaveCount(1);
        paths[0].Subpaths[0].Should().HaveCount(4, "Triangle has 3 vertices + closing point");
    }

    [Fact]
    public void CollectPaths_MultiplePaths_CollectsEach()
    {
        // Arrange: Two separate rectangles
        var operations = new List<PdfOperation>
        {
            new PathOperation
            {
                Operator = "re",
                Operands = new List<object> { 0.0, 0.0, 50.0, 50.0 },
                StreamPosition = 0,
                Type = PathType.Rectangle
            },
            new PathOperation
            {
                Operator = "f",
                Operands = new List<object>(),
                StreamPosition = 1,
                Type = PathType.Fill
            },
            new PathOperation
            {
                Operator = "re",
                Operands = new List<object> { 100.0, 100.0, 50.0, 50.0 },
                StreamPosition = 2,
                Type = PathType.Rectangle
            },
            new PathOperation
            {
                Operator = "f",
                Operands = new List<object>(),
                StreamPosition = 3,
                Type = PathType.Fill
            }
        };

        // Act
        var paths = _collector.CollectPaths(operations);

        // Assert
        paths.Should().HaveCount(2);
    }

    [Fact]
    public void CollectedPath_GetBoundingBox_CalculatesCorrectly()
    {
        // Arrange
        var path = new CollectedPath();
        path.Subpaths.Add(new List<PathPoint>
        {
            new(10, 20),
            new(100, 20),
            new(100, 80),
            new(10, 80),
            new(10, 20)
        });

        // Act
        var bbox = path.GetBoundingBox();

        // Assert
        bbox.Left.Should().Be(10);
        bbox.Bottom.Should().Be(20);
        bbox.Right.Should().Be(100);
        bbox.Top.Should().Be(80);
    }
}
