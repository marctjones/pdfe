using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Reconstructs PDF text operations from kept text segments.
/// Creates new Tj/TJ operations with proper positioning (Tm operators).
/// </summary>
public class OperationReconstructor
{
    private readonly ILogger<OperationReconstructor> _logger;

    public OperationReconstructor() : this(NullLogger<OperationReconstructor>.Instance)
    {
    }

    public OperationReconstructor(ILogger<OperationReconstructor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reconstruct text operations from kept segments.
    /// </summary>
    /// <param name="segments">Segments to keep (already filtered by TextSegmenter).</param>
    /// <param name="originalOperation">The original text operation being split.</param>
    /// <returns>List of new text operations representing the kept segments.</returns>
    public List<TextOperation> ReconstructOperations(
        List<TextSegment> segments,
        TextOperation originalOperation)
    {
        var newOperations = new List<TextOperation>();

        foreach (var segment in segments)
        {
            // Create new text operation for this segment
            var newOp = new TextOperation
            {
                // Use Tj operator (show string) for simplicity
                Operator = "Tj",

                // Operands contain just the text string
                Operands = new List<object> { segment.Text },

                // Bounding box from segment position/size
                BoundingBox = new PdfRectangle(
                    segment.StartX,
                    segment.StartY,
                    segment.StartX + segment.Width,
                    segment.StartY + segment.Height),

                // Copy text state info from original
                Text = segment.Text,
                FontSize = originalOperation.FontSize,

                // Note: StreamPosition will be set during serialization
                StreamPosition = 0,

                // Empty glyphs list - not needed for serialization
                Glyphs = new List<GlyphPosition>()
            };

            newOperations.Add(newOp);

            _logger.LogDebug("Reconstructed operation for segment [{Start},{End}): '{Text}' at ({X},{Y})",
                segment.StartIndex, segment.EndIndex, segment.Text, segment.StartX, segment.StartY);
        }

        return newOperations;
    }

    /// <summary>
    /// Generate positioning operator (Tm - set text matrix) for a text segment.
    /// </summary>
    /// <param name="segment">The text segment to position.</param>
    /// <returns>State operation representing Tm operator.</returns>
    public StateOperation CreatePositioningOperation(TextSegment segment)
    {
        // Tm operator: a b c d e f Tm
        // For simple positioning (no rotation/skew):
        // [1 0 0 1 x y] Tm
        // Where (x, y) is the text position

        return new StateOperation
        {
            Operator = "Tm",
            Operands = new List<object>
            {
                1.0,  // a - horizontal scaling
                0.0,  // b - vertical skew
                0.0,  // c - horizontal skew
                1.0,  // d - vertical scaling
                segment.StartX,  // e - horizontal position
                segment.StartY   // f - vertical position (baseline)
            },
            StreamPosition = 0  // Will be set during serialization
        };
    }

    /// <summary>
    /// Generate complete operation sequence for segments (with positioning).
    /// Includes BT/ET text block with font selection and Tm operators before each Tj operator.
    /// </summary>
    /// <param name="segments">Segments to reconstruct.</param>
    /// <param name="originalOperation">Original operation for context.</param>
    /// <returns>List of operations (BT, Tf, [Tm, Tj]*, ET).</returns>
    public List<PdfOperation> ReconstructWithPositioning(
        List<TextSegment> segments,
        TextOperation originalOperation)
    {
        var operations = new List<PdfOperation>();

        if (segments.Count == 0)
        {
            return operations;
        }

        // Begin text block
        operations.Add(new TextStateOperation
        {
            Operator = "BT",
            Operands = new List<object>(),
            StreamPosition = 0
        });

        // Set font and size (Tf operator)
        // Use font from original operation, or default to /F1 if not available
        var fontName = originalOperation.FontName ?? "/F1";
        var fontSize = originalOperation.FontSize;

        _logger.LogDebug("ReconstructWithPositioning: FontName='{Font}', FontSize={Size}", fontName, fontSize);
        _logger.LogDebug("ReconstructWithPositioning: Creating Tf operator with {ArgCount} operands", 2);

        operations.Add(new TextStateOperation
        {
            Operator = "Tf",
            Operands = new List<object> { fontName, fontSize },
            StreamPosition = 0
        });

        foreach (var segment in segments)
        {
            // Add positioning operator
            operations.Add(CreatePositioningOperation(segment));

            // Add text operation
            var textOp = new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { segment.Text },
                BoundingBox = new PdfRectangle(
                    segment.StartX,
                    segment.StartY,
                    segment.StartX + segment.Width,
                    segment.StartY + segment.Height),
                Text = segment.Text,
                FontName = fontName,
                FontSize = fontSize,
                StreamPosition = 0,
                Glyphs = new List<GlyphPosition>()
            };

            operations.Add(textOp);
        }

        // End text block
        operations.Add(new TextStateOperation
        {
            Operator = "ET",
            Operands = new List<object>(),
            StreamPosition = 0
        });

        _logger.LogDebug("Reconstructed {Count} segments into {OpCount} operations (with BT/Tf/ET and positioning)",
            segments.Count, operations.Count);

        return operations;
    }
}
