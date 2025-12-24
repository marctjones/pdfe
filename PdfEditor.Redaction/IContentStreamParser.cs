namespace PdfEditor.Redaction;

/// <summary>
/// Parses PDF content streams into structured operations.
/// This is the foundation for TRUE glyph-level redaction.
/// </summary>
public interface IContentStreamParser
{
    /// <summary>
    /// Parse a content stream into a list of operations with bounding boxes.
    /// </summary>
    /// <param name="contentBytes">Raw content stream bytes (decompressed).</param>
    /// <param name="pageHeight">Page height in points (for coordinate conversion).</param>
    /// <returns>List of parsed operations with position information.</returns>
    IReadOnlyList<PdfOperation> Parse(byte[] contentBytes, double pageHeight);
}

/// <summary>
/// Base class for all PDF operations parsed from content streams.
/// </summary>
public abstract class PdfOperation
{
    /// <summary>
    /// The raw operator string (e.g., "Tj", "TJ", "m", "l").
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Raw operands as they appear in the content stream.
    /// </summary>
    public required IReadOnlyList<object> Operands { get; init; }

    /// <summary>
    /// Bounding box of this operation in PDF coordinates (bottom-left origin).
    /// May be empty for state-only operations.
    /// </summary>
    public PdfRectangle BoundingBox { get; init; }

    /// <summary>
    /// Original position in the content stream (for ordering).
    /// </summary>
    public int StreamPosition { get; init; }

    /// <summary>
    /// Whether this operation was inside a BT...ET text block.
    /// Used to filter TextStateOperations during glyph-level redaction.
    /// </summary>
    public bool InsideTextBlock { get; init; }

    /// <summary>
    /// Check if this operation intersects with a rectangle.
    /// </summary>
    public virtual bool IntersectsWith(PdfRectangle area) => BoundingBox.IntersectsWith(area);
}

/// <summary>
/// Text-showing operation (Tj, TJ, ', ").
/// Contains the actual text content that can be redacted.
/// </summary>
public class TextOperation : PdfOperation
{
    /// <summary>
    /// The text content of this operation.
    /// For TJ arrays, this is the concatenated string content.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Individual character positions for glyph-level redaction.
    /// </summary>
    public required IReadOnlyList<GlyphPosition> Glyphs { get; init; }

    /// <summary>
    /// Font name at the time of this operation.
    /// </summary>
    public string? FontName { get; init; }

    /// <summary>
    /// Font size in points.
    /// </summary>
    public double FontSize { get; init; }
}

/// <summary>
/// Position information for a single glyph.
/// </summary>
public record GlyphPosition
{
    /// <summary>
    /// The character value.
    /// </summary>
    public required string Character { get; init; }

    /// <summary>
    /// Position in PDF coordinates.
    /// </summary>
    public required PdfRectangle BoundingBox { get; init; }

    /// <summary>
    /// Index in the original TJ array element (for partial redaction).
    /// </summary>
    public int ArrayIndex { get; init; }

    /// <summary>
    /// Index within the string element.
    /// </summary>
    public int StringIndex { get; init; }
}

/// <summary>
/// Graphics state operation (q, Q, cm, etc.).
/// </summary>
public class StateOperation : PdfOperation
{
    /// <summary>
    /// Whether this is a save (q) or restore (Q) operation.
    /// </summary>
    public bool IsSave { get; init; }
    public bool IsRestore { get; init; }

    public override bool IntersectsWith(PdfRectangle area) => false;
}

/// <summary>
/// Text state operation (Tf, Td, Tm, etc.).
/// </summary>
public class TextStateOperation : PdfOperation
{
    public override bool IntersectsWith(PdfRectangle area) => false;
}

/// <summary>
/// Path/graphics operation (m, l, c, re, S, f, etc.).
/// </summary>
public class PathOperation : PdfOperation
{
    /// <summary>
    /// The path type (stroke, fill, clip, etc.).
    /// </summary>
    public PathType Type { get; init; }
}

/// <summary>
/// Types of path operations.
/// </summary>
public enum PathType
{
    MoveTo,
    LineTo,
    CurveTo,
    Rectangle,
    ClosePath,
    Stroke,
    Fill,
    FillStroke,
    Clip,
    EndPath
}

/// <summary>
/// Image XObject invocation (Do operator).
/// </summary>
public class ImageOperation : PdfOperation
{
    /// <summary>
    /// Name of the XObject resource.
    /// </summary>
    public required string XObjectName { get; init; }
}
