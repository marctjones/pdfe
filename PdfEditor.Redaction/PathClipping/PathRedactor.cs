using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Redaction.PathClipping;

/// <summary>
/// Orchestrates partial shape redaction.
/// Collects paths, clips them against redaction areas, and reconstructs the result.
/// </summary>
public class PathRedactor
{
    private readonly PathCollector _collector;
    private readonly PathClipper _clipper;
    private readonly PathReconstructor _reconstructor;
    private readonly ILogger _logger;

    public PathRedactor(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _collector = new PathCollector(_logger);
        _clipper = new PathClipper(_logger);
        _reconstructor = new PathReconstructor();
    }

    /// <summary>
    /// Process operations, applying partial redaction to paths that intersect with redaction areas.
    /// </summary>
    /// <param name="operations">List of PDF operations to process.</param>
    /// <param name="redactionAreas">Areas to redact.</param>
    /// <returns>Modified list of operations with clipped paths.</returns>
    public List<PdfOperation> ProcessOperations(
        IReadOnlyList<PdfOperation> operations,
        IReadOnlyList<PdfRectangle> redactionAreas)
    {
        if (redactionAreas.Count == 0)
            return operations.ToList();

        var result = new List<PdfOperation>();

        // Collect complete paths from operations
        var paths = _collector.CollectPaths(operations);

        // Build a set of stream positions that belong to collected paths
        var pathPositions = new HashSet<int>();
        foreach (var path in paths)
        {
            foreach (var op in path.Operations)
            {
                pathPositions.Add(op.StreamPosition);
            }
            if (path.PaintOperator != null)
            {
                pathPositions.Add(path.PaintOperator.StreamPosition);
            }
        }

        // Track which paths we've processed
        var processedPaths = new HashSet<int>();

        foreach (var operation in operations)
        {
            // If this operation is part of a path, handle it specially
            if (pathPositions.Contains(operation.StreamPosition))
            {
                // Find which path this belongs to
                var owningPath = paths.FirstOrDefault(p =>
                    p.Operations.Any(o => o.StreamPosition == operation.StreamPosition) ||
                    p.PaintOperator?.StreamPosition == operation.StreamPosition);

                if (owningPath != null && !processedPaths.Contains(owningPath.Operations.FirstOrDefault()?.StreamPosition ?? -1))
                {
                    // Mark this path as processed
                    if (owningPath.Operations.Count > 0)
                    {
                        processedPaths.Add(owningPath.Operations[0].StreamPosition);
                    }

                    // Process the complete path
                    var processedOps = ProcessPath(owningPath, redactionAreas);
                    result.AddRange(processedOps);
                }
                // Skip individual operations - they're handled as part of the complete path
                continue;
            }

            // Non-path operations pass through unchanged
            result.Add(operation);
        }

        return result;
    }

    /// <summary>
    /// Process a single complete path against redaction areas.
    /// </summary>
    private List<PdfOperation> ProcessPath(CollectedPath path, IReadOnlyList<PdfRectangle> redactionAreas)
    {
        var pathBBox = path.GetBoundingBox();

        // Quick check: does path intersect with any redaction area?
        var intersectingAreas = redactionAreas
            .Where(area => pathBBox.IntersectsWith(area))
            .ToList();

        if (intersectingAreas.Count == 0)
        {
            // No intersection - keep original path
            var originalOps = new List<PdfOperation>();
            originalOps.AddRange(path.Operations);
            if (path.PaintOperator != null)
                originalOps.Add(path.PaintOperator);
            return originalOps;
        }

        // Process each subpath
        var allResultPolygons = new List<List<PathPoint>>();

        foreach (var subpath in path.Subpaths)
        {
            if (subpath.Count < 3)
                continue;

            var currentPolygons = new List<List<PathPoint>> { subpath };

            // Apply each redaction area
            foreach (var area in intersectingAreas)
            {
                var newPolygons = new List<List<PathPoint>>();

                foreach (var polygon in currentPolygons)
                {
                    if (!_clipper.HasOverlap(polygon, area))
                    {
                        // No overlap with this area - keep polygon
                        newPolygons.Add(polygon);
                        continue;
                    }

                    if (_clipper.IsFullyContained(polygon, area))
                    {
                        // Fully contained - remove entirely
                        _logger.LogDebug("Subpath fully contained in redaction area, removing");
                        continue;
                    }

                    // Partial overlap - clip
                    var clipped = _clipper.ClipPath(polygon, area);
                    newPolygons.AddRange(clipped.Where(p => _reconstructor.IsValidPolygon(p)));
                }

                currentPolygons = newPolygons;
            }

            allResultPolygons.AddRange(currentPolygons);
        }

        if (allResultPolygons.Count == 0)
        {
            // Path fully removed
            _logger.LogDebug("Path fully removed by redaction");
            return new List<PdfOperation>();
        }

        // Reconstruct path operations from clipped polygons.
        // Use the original path's start position - PathReconstructor assigns the same
        // stream position to all operations to keep them grouped together.
        var startPosition = path.Operations.FirstOrDefault()?.StreamPosition ?? 0;
        var reconstructed = _reconstructor.Reconstruct(allResultPolygons, path.PaintType, startPosition);

        return reconstructed.Cast<PdfOperation>().ToList();
    }
}
