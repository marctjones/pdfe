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

    /// <summary>
    /// Redact specific areas on a PdfPage in-place.
    ///
    /// IMPORTANT: Coordinates must be in PDF coordinate system (bottom-left origin, points).
    /// The page is modified directly; the document is NOT saved.
    ///
    /// For glyph-level redaction (UseGlyphLevelRedaction = true), you should provide pageLetters
    /// for best performance when making multiple redactions on the same page.
    /// Call ExtractLettersFromPage() once and cache the result.
    /// </summary>
    /// <param name="page">The PdfPage to redact (from PdfSharp).</param>
    /// <param name="areas">Areas to redact (PDF coordinates, bottom-left origin).</param>
    /// <param name="options">Optional redaction options.</param>
    /// <param name="pageLetters">Optional pre-extracted letters for glyph-level redaction (improves performance).</param>
    /// <returns>Result containing information about what was redacted.</returns>
    PageRedactionResult RedactPage(
        PdfSharp.Pdf.PdfPage page,
        IEnumerable<PdfRectangle> areas,
        RedactionOptions? options = null,
        IReadOnlyList<Letter>? pageLetters = null);

    /// <summary>
    /// Extract PdfPig letters from a PdfPage for glyph-level redaction.
    ///
    /// IMPORTANT: This method saves the document to a MemoryStream to extract letters.
    /// Cache and reuse the results for multiple redactions on the same page to avoid
    /// repeated document serialization.
    ///
    /// RECOMMENDATION: For best performance with multiple redactions:
    /// 1. Call ExtractLettersFromPage() once
    /// 2. Cache the returned letters
    /// 3. Pass the cached letters to multiple RedactPage() calls
    /// </summary>
    /// <param name="page">The PdfPage to extract letters from.</param>
    /// <returns>List of letters on the page for use in glyph-level redaction.</returns>
    IReadOnlyList<Letter> ExtractLettersFromPage(PdfSharp.Pdf.PdfPage page);

    /// <summary>
    /// Sanitize document metadata after redactions.
    ///
    /// This removes:
    /// - Title, Subject, Author, Keywords from Info dictionary
    /// - XMP metadata stream
    /// - Other identifying information
    ///
    /// Call this AFTER all redactions, before saving the document.
    /// </summary>
    /// <param name="document">The PdfDocument to sanitize.</param>
    void SanitizeDocumentMetadata(PdfSharp.Pdf.PdfDocument document);
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
    /// Default: true (security best practice - prevents redacted text leaking via metadata)
    /// See issue #150: Metadata may contain redacted text - security concern
    /// </summary>
    public bool SanitizeMetadata { get; set; } = true;

    /// <summary>
    /// Case-sensitive text matching.
    /// Default: true
    /// </summary>
    public bool CaseSensitive { get; set; } = true;

    /// <summary>
    /// Whether to use glyph-level redaction (removes individual characters).
    /// When true, only glyphs intersecting the redaction area are removed.
    /// When false, entire text operations are removed (whole-operation removal).
    /// Default: true (TRUE glyph-level redaction is the purpose of this library)
    /// </summary>
    public bool UseGlyphLevelRedaction { get; set; } = true;

    /// <summary>
    /// Whether to preserve PDF/A identification metadata after redaction.
    /// When true, PDF/A documents will retain their PDF/A identification in XMP metadata.
    /// Default: true (maintains document compliance claims)
    /// See issue #157: Preserve PDF/A XMP metadata during redaction
    /// </summary>
    public bool PreservePdfAMetadata { get; set; } = true;

    /// <summary>
    /// Whether to redact annotations (comments, highlights, stamps, form fields) in the redaction area.
    /// When true, annotations that intersect with the redaction area will be removed.
    /// Default: true (security best practice - annotations may contain sensitive data)
    /// See issue #164: Implement annotation redaction
    /// </summary>
    public bool RedactAnnotations { get; set; } = true;

    /// <summary>
    /// Whether to remove transparency features for PDF/A-1 compliance.
    /// PDF/A-1 (ISO 19005-1) forbids transparency entirely.
    /// When true, removes /Group transparency entries, normalizes ExtGState (CA, ca, SMask, BM).
    /// Default: true (ensures visual markers don't break PDF/A-1 compliance)
    /// See issue #158: Draw redaction boxes without transparency for PDF/A
    /// </summary>
    public bool RemovePdfATransparency { get; set; } = true;
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

/// <summary>
/// Result of a single-page redaction operation.
/// </summary>
public class PageRedactionResult
{
    /// <summary>
    /// Whether the redaction completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of redactions applied to the page.
    /// </summary>
    public int RedactionCount { get; init; }

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
    public static PageRedactionResult Succeeded(IEnumerable<RedactionDetail> details)
        => new()
        {
            Success = true,
            RedactionCount = details.Count(),
            Details = details.ToList()
        };

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static PageRedactionResult Failed(string error)
        => new()
        {
            Success = false,
            ErrorMessage = error
        };
}
