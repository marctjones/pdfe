namespace PdfEditor.Redaction.PathClipping;

/// <summary>
/// Reconstructs PDF path operations from clipped polygon points.
/// Converts the output from Clipper2 back to valid PDF path operators.
/// </summary>
public class PathReconstructor
{
    /// <summary>
    /// Reconstruct PDF path operations from a list of polygons.
    /// </summary>
    /// <param name="polygons">Polygons to convert (output from PathClipper).</param>
    /// <param name="paintType">The paint type for the path (stroke, fill, etc.).</param>
    /// <param name="streamPosition">Starting stream position for new operations.</param>
    /// <returns>List of PDF path operations representing the clipped shape.</returns>
    public List<PathOperation> Reconstruct(
        List<List<PathPoint>> polygons,
        PathType paintType,
        int streamPosition)
    {
        var operations = new List<PathOperation>();

        // CRITICAL: Use the SAME stream position for all reconstructed operations.
        // This ensures they stay grouped together and don't interleave with other
        // operations (like Q state restore) that have adjacent positions.
        // ContentStreamBuilder serializes by StreamPosition, then by list order.

        foreach (var polygon in polygons)
        {
            if (polygon.Count < 3)
                continue;

            // Move to first point
            operations.Add(new PathOperation
            {
                Operator = "m",
                Operands = new List<object> { polygon[0].X, polygon[0].Y },
                StreamPosition = streamPosition,
                Type = PathType.MoveTo,
                BoundingBox = CalculateBoundingBox(polygon)
            });

            // Line to remaining points
            for (int i = 1; i < polygon.Count; i++)
            {
                // Skip duplicate points (e.g., closing point same as first)
                if (Math.Abs(polygon[i].X - polygon[i - 1].X) < 0.001 &&
                    Math.Abs(polygon[i].Y - polygon[i - 1].Y) < 0.001)
                    continue;

                operations.Add(new PathOperation
                {
                    Operator = "l",
                    Operands = new List<object> { polygon[i].X, polygon[i].Y },
                    StreamPosition = streamPosition,
                    Type = PathType.LineTo,
                    BoundingBox = CalculateBoundingBox(polygon)
                });
            }

            // Close path if not already closed
            var first = polygon[0];
            var last = polygon[^1];
            if (Math.Abs(first.X - last.X) > 0.001 || Math.Abs(first.Y - last.Y) > 0.001)
            {
                operations.Add(new PathOperation
                {
                    Operator = "h",
                    Operands = new List<object>(),
                    StreamPosition = streamPosition,
                    Type = PathType.ClosePath,
                    BoundingBox = CalculateBoundingBox(polygon)
                });
            }
        }

        // Add paint operator
        if (operations.Count > 0)
        {
            var paintOperator = GetPaintOperator(paintType);
            var allPoints = polygons.SelectMany(p => p).ToList();

            operations.Add(new PathOperation
            {
                Operator = paintOperator,
                Operands = new List<object>(),
                StreamPosition = streamPosition,
                Type = paintType,
                BoundingBox = CalculateBoundingBox(allPoints)
            });
        }

        return operations;
    }

    /// <summary>
    /// Check if a polygon is simple (no degenerate cases).
    /// </summary>
    public bool IsValidPolygon(List<PathPoint> polygon)
    {
        if (polygon.Count < 3)
            return false;

        // Check for zero area
        var area = CalculateArea(polygon);
        return Math.Abs(area) > 0.1; // Minimum area threshold
    }

    private static double CalculateArea(List<PathPoint> polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var j = (i + 1) % polygon.Count;
            area += polygon[i].X * polygon[j].Y;
            area -= polygon[j].X * polygon[i].Y;
        }
        return Math.Abs(area) / 2;
    }

    private static PdfRectangle CalculateBoundingBox(List<PathPoint> points)
    {
        if (points.Count == 0)
            return new PdfRectangle(0, 0, 0, 0);

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        return new PdfRectangle(minX, minY, maxX, maxY);
    }

    private static string GetPaintOperator(PathType paintType) => paintType switch
    {
        PathType.Stroke => "S",
        PathType.Fill => "f",
        PathType.FillStroke => "B",
        PathType.EndPath => "n",
        _ => "f" // Default to fill
    };
}
