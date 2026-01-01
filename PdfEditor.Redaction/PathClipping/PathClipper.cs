using Clipper2Lib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Redaction.PathClipping;

/// <summary>
/// Clips PDF paths against redaction areas using Clipper2 polygon boolean operations.
/// This enables partial shape redaction - removing only the portion of a shape that
/// overlaps with the redaction area.
/// </summary>
public class PathClipper
{
    private readonly ILogger _logger;

    // Clipper2 uses integer coordinates. Scale PDF points to preserve precision.
    private const double Scale = 1000.0;

    public PathClipper(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Clip a path against a redaction area, returning the remaining path(s).
    /// </summary>
    /// <param name="pathPoints">Points defining the path polygon (closed).</param>
    /// <param name="redactionArea">The area to subtract from the path.</param>
    /// <returns>List of resulting polygons after subtraction. Empty if path is fully removed.</returns>
    public List<List<PathPoint>> ClipPath(List<PathPoint> pathPoints, PdfRectangle redactionArea)
    {
        if (pathPoints.Count < 3)
        {
            _logger.LogDebug("Path has fewer than 3 points, cannot clip");
            return new List<List<PathPoint>> { pathPoints };
        }

        // Convert path to Clipper2 format (scaled integers)
        var subjectPath = new Path64();
        foreach (var point in pathPoints)
        {
            subjectPath.Add(new Point64((long)(point.X * Scale), (long)(point.Y * Scale)));
        }

        // Convert redaction area to Clipper2 format
        var clipPath = new Path64
        {
            new Point64((long)(redactionArea.Left * Scale), (long)(redactionArea.Bottom * Scale)),
            new Point64((long)(redactionArea.Right * Scale), (long)(redactionArea.Bottom * Scale)),
            new Point64((long)(redactionArea.Right * Scale), (long)(redactionArea.Top * Scale)),
            new Point64((long)(redactionArea.Left * Scale), (long)(redactionArea.Top * Scale))
        };

        // Perform boolean difference: subject - clip
        var clipper = new Clipper64();
        clipper.AddSubject(subjectPath);
        clipper.AddClip(clipPath);

        var solution = new Paths64();
        clipper.Execute(ClipType.Difference, FillRule.NonZero, solution);

        if (solution.Count == 0)
        {
            _logger.LogDebug("Path fully removed by redaction area");
            return new List<List<PathPoint>>();
        }

        // Convert back to PathPoint format
        var result = new List<List<PathPoint>>();
        foreach (var path in solution)
        {
            var polygon = new List<PathPoint>();
            foreach (var point in path)
            {
                polygon.Add(new PathPoint(point.X / Scale, point.Y / Scale));
            }
            result.Add(polygon);
        }

        _logger.LogDebug("Path clipped: {OriginalPoints} points → {ResultCount} polygons",
            pathPoints.Count, result.Count);

        return result;
    }

    /// <summary>
    /// Check if a path is fully contained within a redaction area.
    /// If so, the entire path should be removed (no clipping needed).
    /// </summary>
    public bool IsFullyContained(List<PathPoint> pathPoints, PdfRectangle redactionArea)
    {
        return pathPoints.All(p =>
            p.X >= redactionArea.Left && p.X <= redactionArea.Right &&
            p.Y >= redactionArea.Bottom && p.Y <= redactionArea.Top);
    }

    /// <summary>
    /// Check if a path has any overlap with a redaction area.
    /// </summary>
    public bool HasOverlap(List<PathPoint> pathPoints, PdfRectangle redactionArea)
    {
        // Quick bounding box check first
        var minX = pathPoints.Min(p => p.X);
        var maxX = pathPoints.Max(p => p.X);
        var minY = pathPoints.Min(p => p.Y);
        var maxY = pathPoints.Max(p => p.Y);

        var pathBBox = new PdfRectangle(minX, minY, maxX, maxY);
        return pathBBox.IntersectsWith(redactionArea);
    }
}

/// <summary>
/// A point in a PDF path.
/// </summary>
public readonly struct PathPoint
{
    public double X { get; }
    public double Y { get; }

    public PathPoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
