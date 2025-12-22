using UglyToad.PdfPig.Content;

namespace PdfEditor.Redaction;

/// <summary>
/// Core interface for TRUE glyph-level text redaction.
/// Implementations remove text from PDF content streams, not just visual overlay.
/// </summary>
public interface ITextRedactor
{
    /// <summary>
    /// Redact all instances of the specified text from a PDF.
    /// </summary>
    /// <param name="inputPath">Path to the input PDF file.</param>
    /// <param name="outputPath">Path where the redacted PDF will be saved.</param>
    /// <param name="textToRedact">The text to remove from the PDF structure.</param>
    /// <param name="options">Optional redaction options.</param>
    /// <returns>Result containing information about what was redacted.</returns>
    RedactionResult RedactText(string inputPath, string outputPath, string textToRedact, RedactionOptions? options = null);

    /// <summary>
    /// Redact text at specific page locations.
    /// </summary>
    /// <param name="inputPath">Path to the input PDF file.</param>
    /// <param name="outputPath">Path where the redacted PDF will be saved.</param>
    /// <param name="locations">Collection of page locations to redact.</param>
    /// <param name="options">Optional redaction options.</param>
    /// <returns>Result containing information about what was redacted.</returns>
    RedactionResult RedactLocations(string inputPath, string outputPath, IEnumerable<RedactionLocation> locations, RedactionOptions? options = null);
}

/// <summary>
/// Specifies a location in a PDF to redact.
/// </summary>
public record RedactionLocation
{
    /// <summary>
    /// 1-based page number.
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// Bounding box in PDF coordinates (bottom-left origin, points).
    /// </summary>
    public required PdfRectangle BoundingBox { get; init; }
}

/// <summary>
/// Options for controlling redaction behavior.
/// </summary>
public class RedactionOptions
{
    /// <summary>
    /// Whether to draw a visual marker (black rectangle) at redaction locations.
    /// Default: true
    /// </summary>
    public bool DrawVisualMarker { get; set; } = true;

    /// <summary>
    /// Color for visual markers (R, G, B values from 0-1).
    /// Default: black (0, 0, 0)
    /// </summary>
    public (double R, double G, double B) MarkerColor { get; set; } = (0, 0, 0);

    /// <summary>
    /// Whether to sanitize document metadata (Info dictionary, XMP).
    /// Default: false
    /// </summary>
    public bool SanitizeMetadata { get; set; } = false;

    /// <summary>
    /// Case-sensitive text matching.
    /// Default: true
    /// </summary>
    public bool CaseSensitive { get; set; } = true;
}

/// <summary>
/// Result of a redaction operation.
/// </summary>
public class RedactionResult
{
    /// <summary>
    /// Whether the redaction completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of text instances removed.
    /// </summary>
    public int RedactionCount { get; init; }

    /// <summary>
    /// Pages that were modified.
    /// </summary>
    public IReadOnlyList<int> AffectedPages { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Detailed information about each redaction.
    /// </summary>
    public IReadOnlyList<RedactionDetail> Details { get; init; } = Array.Empty<RedactionDetail>();

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static RedactionResult Succeeded(int count, IEnumerable<int> pages, IEnumerable<RedactionDetail>? details = null)
        => new()
        {
            Success = true,
            RedactionCount = count,
            AffectedPages = pages.Distinct().OrderBy(p => p).ToList(),
            Details = details?.ToList() ?? new List<RedactionDetail>()
        };

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static RedactionResult Failed(string error)
        => new()
        {
            Success = false,
            ErrorMessage = error
        };
}

/// <summary>
/// Details about a single redaction.
/// </summary>
public record RedactionDetail
{
    /// <summary>
    /// 1-based page number.
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// The text that was removed.
    /// </summary>
    public required string RedactedText { get; init; }

    /// <summary>
    /// Location where text was removed (PDF coordinates).
    /// </summary>
    public required PdfRectangle Location { get; init; }
}

/// <summary>
/// Simple rectangle in PDF coordinates (points, bottom-left origin).
/// </summary>
public readonly record struct PdfRectangle(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;
    public double Height => Top - Bottom;

    /// <summary>
    /// Check if this rectangle intersects with another.
    /// </summary>
    public bool IntersectsWith(PdfRectangle other)
    {
        return Left < other.Right &&
               Right > other.Left &&
               Bottom < other.Top &&
               Top > other.Bottom;
    }

    /// <summary>
    /// Check if this rectangle contains a point.
    /// </summary>
    public bool Contains(double x, double y)
    {
        return x >= Left && x <= Right && y >= Bottom && y <= Top;
    }
}
