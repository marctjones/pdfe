using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Redaction.PathClipping;

/// <summary>
/// Collects PDF path operations (m, l, c, re, h) into complete path objects.
/// A complete path starts with a move (m) or rectangle (re) and ends with a
/// paint operator (S, s, f, F, f*, B, B*, b, b*, n).
/// </summary>
public class PathCollector
{
    private readonly ILogger _logger;

    public PathCollector(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Collect path operations from a list of PDF operations.
    /// Groups path construction operators with their paint operators.
    /// </summary>
    public List<CollectedPath> CollectPaths(IReadOnlyList<PdfOperation> operations)
    {
        var result = new List<CollectedPath>();
        var currentPath = new CollectedPath();
        var currentSubpath = new List<PathPoint>();
        var currentPoint = new PathPoint(0, 0);

        foreach (var op in operations.OfType<PathOperation>())
        {
            switch (op.Type)
            {
                case PathType.MoveTo:
                    // Start a new subpath
                    if (currentSubpath.Count > 0)
                    {
                        currentPath.Subpaths.Add(currentSubpath.ToList());
                    }
                    currentSubpath.Clear();

                    if (op.Operands.Count >= 2)
                    {
                        var x = GetDouble(op.Operands[0]);
                        var y = GetDouble(op.Operands[1]);
                        currentPoint = new PathPoint(x, y);
                        currentSubpath.Add(currentPoint);
                    }
                    currentPath.Operations.Add(op);
                    break;

                case PathType.LineTo:
                    if (op.Operands.Count >= 2)
                    {
                        var x = GetDouble(op.Operands[0]);
                        var y = GetDouble(op.Operands[1]);
                        currentPoint = new PathPoint(x, y);
                        currentSubpath.Add(currentPoint);
                    }
                    currentPath.Operations.Add(op);
                    break;

                case PathType.CurveTo:
                    // Bézier curve: approximate with line to endpoint for now
                    // Full curve support would require subdivision
                    if (op.Operands.Count >= 6)
                    {
                        // c operator: x1 y1 x2 y2 x3 y3
                        var x3 = GetDouble(op.Operands[4]);
                        var y3 = GetDouble(op.Operands[5]);

                        // Add intermediate control points for better approximation
                        var x1 = GetDouble(op.Operands[0]);
                        var y1 = GetDouble(op.Operands[1]);
                        var x2 = GetDouble(op.Operands[2]);
                        var y2 = GetDouble(op.Operands[3]);

                        // Approximate Bézier with line segments
                        ApproximateBezier(currentSubpath, currentPoint.X, currentPoint.Y,
                            x1, y1, x2, y2, x3, y3);

                        currentPoint = new PathPoint(x3, y3);
                    }
                    else if (op.Operands.Count >= 4)
                    {
                        // v or y operator: 4 operands
                        var x2 = GetDouble(op.Operands[2]);
                        var y2 = GetDouble(op.Operands[3]);
                        currentPoint = new PathPoint(x2, y2);
                        currentSubpath.Add(currentPoint);
                    }
                    currentPath.Operations.Add(op);
                    break;

                case PathType.Rectangle:
                    // Rectangle is a complete subpath: x y width height
                    if (currentSubpath.Count > 0)
                    {
                        currentPath.Subpaths.Add(currentSubpath.ToList());
                    }
                    currentSubpath.Clear();

                    if (op.Operands.Count >= 4)
                    {
                        var x = GetDouble(op.Operands[0]);
                        var y = GetDouble(op.Operands[1]);
                        var w = GetDouble(op.Operands[2]);
                        var h = GetDouble(op.Operands[3]);

                        // Rectangle as 4 corners (clockwise)
                        currentSubpath.Add(new PathPoint(x, y));
                        currentSubpath.Add(new PathPoint(x + w, y));
                        currentSubpath.Add(new PathPoint(x + w, y + h));
                        currentSubpath.Add(new PathPoint(x, y + h));
                        currentSubpath.Add(new PathPoint(x, y)); // Close

                        currentPath.Subpaths.Add(currentSubpath.ToList());
                        currentSubpath.Clear();

                        // Current point after re is (x, y)
                        currentPoint = new PathPoint(x, y);
                    }
                    currentPath.Operations.Add(op);
                    currentPath.IsRectangle = true;
                    break;

                case PathType.ClosePath:
                    // Close current subpath by connecting to start
                    if (currentSubpath.Count > 0)
                    {
                        currentSubpath.Add(currentSubpath[0]);
                        currentPath.Subpaths.Add(currentSubpath.ToList());
                        currentSubpath.Clear();
                    }
                    currentPath.Operations.Add(op);
                    break;

                case PathType.Stroke:
                case PathType.Fill:
                case PathType.FillStroke:
                case PathType.EndPath:
                    // Paint operator - completes the path
                    if (currentSubpath.Count > 0)
                    {
                        currentPath.Subpaths.Add(currentSubpath.ToList());
                        currentSubpath.Clear();
                    }

                    currentPath.PaintOperator = op;
                    currentPath.PaintType = op.Type;

                    if (currentPath.Operations.Count > 0 || currentPath.PaintOperator != null)
                    {
                        result.Add(currentPath);
                    }

                    // Start a new path
                    currentPath = new CollectedPath();
                    break;
            }
        }

        // Handle any remaining path (shouldn't happen in valid PDFs)
        if (currentSubpath.Count > 0 || currentPath.Operations.Count > 0)
        {
            if (currentSubpath.Count > 0)
            {
                currentPath.Subpaths.Add(currentSubpath.ToList());
            }
            if (currentPath.Operations.Count > 0)
            {
                result.Add(currentPath);
            }
        }

        _logger.LogDebug("Collected {Count} complete paths", result.Count);
        return result;
    }

    /// <summary>
    /// Approximate a cubic Bézier curve with line segments.
    /// Uses recursive subdivision for smooth approximation.
    /// </summary>
    private void ApproximateBezier(List<PathPoint> points,
        double x0, double y0,  // Start point
        double x1, double y1,  // Control point 1
        double x2, double y2,  // Control point 2
        double x3, double y3,  // End point
        int depth = 0)
    {
        const int MaxDepth = 4;  // Maximum subdivision depth
        const double Flatness = 1.0;  // Flatness tolerance in points

        if (depth >= MaxDepth)
        {
            points.Add(new PathPoint(x3, y3));
            return;
        }

        // Check if curve is flat enough
        // Distance from control points to line from start to end
        var dx = x3 - x0;
        var dy = y3 - y0;
        var d1 = Math.Abs((x1 - x3) * dy - (y1 - y3) * dx);
        var d2 = Math.Abs((x2 - x3) * dy - (y2 - y3) * dx);
        var len = Math.Sqrt(dx * dx + dy * dy);

        if (len > 0 && (d1 + d2) / len < Flatness)
        {
            points.Add(new PathPoint(x3, y3));
            return;
        }

        // Subdivide curve at midpoint using de Casteljau's algorithm
        var x01 = (x0 + x1) / 2;
        var y01 = (y0 + y1) / 2;
        var x12 = (x1 + x2) / 2;
        var y12 = (y1 + y2) / 2;
        var x23 = (x2 + x3) / 2;
        var y23 = (y2 + y3) / 2;

        var x012 = (x01 + x12) / 2;
        var y012 = (y01 + y12) / 2;
        var x123 = (x12 + x23) / 2;
        var y123 = (y12 + y23) / 2;

        var x0123 = (x012 + x123) / 2;
        var y0123 = (y012 + y123) / 2;

        // Recurse on both halves
        ApproximateBezier(points, x0, y0, x01, y01, x012, y012, x0123, y0123, depth + 1);
        ApproximateBezier(points, x0123, y0123, x123, y123, x23, y23, x3, y3, depth + 1);
    }

    private static double GetDouble(object obj) => obj switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, out var parsed) => parsed,
        _ => 0
    };
}

/// <summary>
/// A complete PDF path (one or more subpaths with a paint operator).
/// </summary>
public class CollectedPath
{
    /// <summary>
    /// The original path construction operations (m, l, c, re, h).
    /// </summary>
    public List<PathOperation> Operations { get; } = new();

    /// <summary>
    /// Collected subpaths as point lists. Each subpath is a sequence of connected points.
    /// </summary>
    public List<List<PathPoint>> Subpaths { get; } = new();

    /// <summary>
    /// The paint operator that ends this path (S, s, f, F, f*, B, B*, b, b*, n).
    /// </summary>
    public PathOperation? PaintOperator { get; set; }

    /// <summary>
    /// The paint type for this path.
    /// </summary>
    public PathType PaintType { get; set; }

    /// <summary>
    /// Whether this path was created from a rectangle (re) operator.
    /// </summary>
    public bool IsRectangle { get; set; }

    /// <summary>
    /// Get all points from all subpaths as a flat list.
    /// </summary>
    public List<PathPoint> GetAllPoints()
    {
        return Subpaths.SelectMany(sp => sp).ToList();
    }

    /// <summary>
    /// Calculate bounding box of this path.
    /// </summary>
    public PdfRectangle GetBoundingBox()
    {
        var allPoints = GetAllPoints();
        if (allPoints.Count == 0)
            return new PdfRectangle(0, 0, 0, 0);

        var minX = allPoints.Min(p => p.X);
        var maxX = allPoints.Max(p => p.X);
        var minY = allPoints.Min(p => p.Y);
        var maxY = allPoints.Max(p => p.Y);

        return new PdfRectangle(minX, minY, maxX, maxY);
    }
}
