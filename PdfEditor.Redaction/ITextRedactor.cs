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

    #region Partial Glyph Redaction (Issue #199)

    /// <summary>
    /// When true, glyphs that partially overlap the redaction area are:
    /// 1. Removed from the text stream (for security - text extraction won't find them)
    /// 2. Preserved as rasterized images (for visual appearance of non-redacted portions)
    ///
    /// When false (default), the current center-point behavior applies:
    /// - Glyph is removed entirely if its center is inside the redaction area
    /// - Glyph is kept entirely if its center is outside the redaction area
    ///
    /// Default: false (backward compatible, current behavior)
    /// See issue #199: Partial glyph redaction - preserve visible portion as rasterized image
    /// </summary>
    public bool PreservePartialGlyphsAsImages { get; set; } = false;

    #endregion

    #region Partial Image Redaction (Issue #276)

    /// <summary>
    /// When true (default), images that partially overlap the redaction area are modified
    /// to black out only the covered portion, preserving the rest of the image.
    ///
    /// When false, entire images are removed if any part intersects
    /// with the redaction area.
    ///
    /// Default: true (black out only the covered portion)
    /// See issue #276: Partial image redaction (black out only the covered portion)
    /// </summary>
    public bool RedactImagesPartially { get; set; } = true;

    /// <summary>
    /// DPI (dots per inch) for rasterizing partial glyphs when PreservePartialGlyphsAsImages is true.
    /// Higher values produce better quality but larger file sizes.
    ///
    /// Recommended values:
    /// - 150 DPI: Fast, suitable for screen viewing
    /// - 300 DPI: Standard, good for most printing (default)
    /// - 600 DPI: High quality, archival/professional printing
    ///
    /// Default: 300
    /// </summary>
    public int PartialGlyphRasterizationDpi { get; set; } = 300;

    /// <summary>
    /// Strategy for determining when a glyph should be removed during redaction.
    /// Default: AnyOverlap (most secure - removes glyphs that touch the redaction area)
    /// See issue #206: Detect glyphs with partial overlap of redaction area
    /// </summary>
    public GlyphRemovalStrategy GlyphRemovalStrategy { get; set; } = GlyphRemovalStrategy.AnyOverlap;

    #endregion
}

/// <summary>
/// Strategy for determining when a glyph should be removed during redaction.
/// </summary>
public enum GlyphRemovalStrategy
{
    /// <summary>
    /// Remove glyph if its center point is inside the redaction area.
    /// This is the current/default behavior for backward compatibility.
    /// Glyphs at edges may be partially visible or partially hidden.
    /// </summary>
    CenterPoint,

    /// <summary>
    /// Remove glyph if ANY part of it intersects the redaction area.
    /// Most aggressive - ensures no part of any glyph is visible in the redacted region.
    /// May remove glyphs that are mostly outside the area.
    /// </summary>
    AnyOverlap,

    /// <summary>
    /// Remove glyph only if it's FULLY contained within the redaction area.
    /// Most conservative - glyphs at edges remain completely visible.
    /// May leave partial glyphs visible inside the redacted region.
    /// </summary>
    FullyContained
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
    /// Number of image operations removed (XObject Do operators and inline images).
    /// </summary>
    public int ImageRedactionCount { get; init; }

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
    public static RedactionResult Succeeded(int count, IEnumerable<int> pages, IEnumerable<RedactionDetail>? details = null, int imageCount = 0)
        => new()
        {
            Success = true,
            RedactionCount = count,
            ImageRedactionCount = imageCount,
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

    /// <summary>
    /// Check if this rectangle fully contains another rectangle.
    /// </summary>
    public bool Contains(PdfRectangle other)
    {
        return Left <= other.Left &&
               Right >= other.Right &&
               Bottom <= other.Bottom &&
               Top >= other.Top;
    }

    /// <summary>
    /// Get the type of overlap between this rectangle (as redaction area) and a glyph.
    /// </summary>
    /// <param name="glyph">The glyph bounding box to check.</param>
    /// <returns>The type of overlap.</returns>
    public GlyphOverlapType GetOverlapType(PdfRectangle glyph)
    {
        if (!IntersectsWith(glyph))
            return GlyphOverlapType.None;

        if (Contains(glyph))
            return GlyphOverlapType.Full;

        return GlyphOverlapType.Partial;
    }

    /// <summary>
    /// Convert from PdfPig's PdfRectangle format (normalizing swapped coordinates for rotated text).
    /// </summary>
    public static PdfRectangle FromPdfPig(UglyToad.PdfPig.Core.PdfRectangle rect)
    {
        // Normalize for rotated text where Left > Right or Bottom > Top
        double left = Math.Min(rect.Left, rect.Right);
        double right = Math.Max(rect.Left, rect.Right);
        double bottom = Math.Min(rect.Bottom, rect.Top);
        double top = Math.Max(rect.Bottom, rect.Top);
        return new PdfRectangle(left, bottom, right, top);
    }
}

/// <summary>
/// Describes how a glyph overlaps with a redaction area.
/// </summary>
public enum GlyphOverlapType
{
    /// <summary>
    /// Glyph does not intersect with redaction area at all.
    /// Action: Keep the glyph as-is.
    /// </summary>
    None,

    /// <summary>
    /// Glyph partially overlaps redaction area (intersects but not fully contained).
    /// Action: Candidate for rasterization - remove text but preserve visible portion as image.
    /// </summary>
    Partial,

    /// <summary>
    /// Glyph is fully contained within redaction area.
    /// Action: Remove entirely from content stream.
    /// </summary>
    Full
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
    /// Number of text redactions applied to the page.
    /// </summary>
    public int RedactionCount { get; init; }

    /// <summary>
    /// Number of image operations removed (XObject Do operators and inline images).
    /// </summary>
    public int ImageRedactionCount { get; init; }

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
    public static PageRedactionResult Succeeded(IEnumerable<RedactionDetail> details, int imageCount = 0)
        => new()
        {
            Success = true,
            RedactionCount = details.Count(),
            ImageRedactionCount = imageCount,
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
