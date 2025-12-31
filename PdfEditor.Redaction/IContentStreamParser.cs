using PdfSharp.Pdf;

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

    /// <summary>
    /// Parse a content stream with access to page resources for Form XObject resolution.
    /// </summary>
    /// <param name="contentBytes">Raw content stream bytes (decompressed).</param>
    /// <param name="pageHeight">Page height in points (for coordinate conversion).</param>
    /// <param name="resources">Page resources dictionary (for XObject lookup).</param>
    /// <returns>List of parsed operations, including nested Form XObject operations.</returns>
    IReadOnlyList<PdfOperation> ParseWithResources(byte[] contentBytes, double pageHeight, PdfDictionary? resources);

    /// <summary>
    /// Parse a PDF page's content stream with full font awareness.
    /// Extracts fonts from page resources for proper CID/CJK encoding support.
    /// </summary>
    /// <param name="page">The PDF page to parse.</param>
    /// <returns>List of parsed operations with proper text encoding.</returns>
    IReadOnlyList<PdfOperation> ParsePage(PdfPage page);
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

    #region Text State Parameters (Issue #122)
    // These parameters must be preserved during glyph-level redaction reconstruction

    /// <summary>
    /// Character spacing (Tc operator).
    /// Default: 0.0
    /// </summary>
    public double CharacterSpacing { get; init; }

    /// <summary>
    /// Word spacing (Tw operator).
    /// Default: 0.0
    /// </summary>
    public double WordSpacing { get; init; }

    /// <summary>
    /// Horizontal scaling as percentage (Tz operator).
    /// Default: 100.0
    /// </summary>
    public double HorizontalScaling { get; init; } = 100.0;

    /// <summary>
    /// Text rendering mode (Tr operator).
    /// 0=fill, 1=stroke, 2=fill+stroke, 3=invisible, etc.
    /// Default: 0 (fill)
    /// </summary>
    public int TextRenderingMode { get; init; }

    /// <summary>
    /// Text rise (Ts operator) for superscript/subscript.
    /// Default: 0.0
    /// </summary>
    public double TextRise { get; init; }

    /// <summary>
    /// Text leading (TL operator).
    /// Default: 0.0
    /// </summary>
    public double TextLeading { get; init; }

    #endregion

    #region CJK Support (Issue #174)

    /// <summary>
    /// Whether this text operation used hex string format in the original PDF.
    /// CID fonts typically use hex strings like &lt;0048006500&gt;.
    /// </summary>
    public bool WasHexString { get; init; }

    /// <summary>
    /// Whether this text is from a CID-keyed font.
    /// CID fonts require 2-byte character codes and special reconstruction.
    /// </summary>
    public bool IsCidFont { get; init; }

    /// <summary>
    /// The raw bytes of the operand (for faithful reconstruction).
    /// </summary>
    public byte[]? RawBytes { get; init; }

    #endregion
}

/// <summary>
/// Position information for a single glyph.
/// Enhanced for CJK font support with raw byte preservation.
/// </summary>
public record GlyphPosition
{
    /// <summary>
    /// The Unicode character value (decoded from raw bytes).
    /// For CJK fonts, this is the ToUnicode-mapped or Identity-decoded value.
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

    /// <summary>
    /// Raw bytes as they appear in the PDF content stream.
    /// For Western fonts: 1 byte. For CID fonts: 2 bytes (big-endian).
    /// Used for faithful reconstruction during redaction.
    /// </summary>
    public byte[]? RawBytes { get; init; }

    /// <summary>
    /// Character ID (CID) for CID-keyed fonts.
    /// For Western fonts, this is the single byte value.
    /// For CID fonts, this is the 2-byte big-endian value.
    /// </summary>
    public int CidValue { get; init; }

    /// <summary>
    /// Whether this glyph came from a CID-keyed font.
    /// </summary>
    public bool IsCidGlyph { get; init; }

    /// <summary>
    /// Whether the original operand was a hex string (true) or literal string (false).
    /// Needed to reconstruct the operand in the same format.
    /// </summary>
    public bool WasHexString { get; init; }
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

/// <summary>
/// Form XObject invocation (Do operator with /Subtype /Form).
/// Contains nested operations parsed from the Form XObject's content stream.
/// </summary>
public class FormXObjectOperation : PdfOperation
{
    /// <summary>
    /// Name of the XObject resource (e.g., "/Fm1").
    /// </summary>
    public required string XObjectName { get; init; }

    /// <summary>
    /// Operations parsed from the Form XObject's nested content stream.
    /// </summary>
    public List<PdfOperation> NestedOperations { get; init; } = new();

    /// <summary>
    /// The Form XObject's BBox (bounding box in form coordinate space).
    /// </summary>
    public PdfRectangle FormBBox { get; init; } = new PdfRectangle(0, 0, 0, 0);

    /// <summary>
    /// The Form XObject's transformation matrix (6 values: a, b, c, d, e, f).
    /// Applied when rendering the form: [a b c d e f] means x' = ax + cy + e, y' = bx + dy + f.
    /// </summary>
    public double[] FormMatrix { get; init; } = new double[] { 1, 0, 0, 1, 0, 0 };

    /// <summary>
    /// Reference to the Form XObject's content stream bytes (for modification).
    /// </summary>
    public byte[]? ContentStreamBytes { get; set; }
}

/// <summary>
/// Inline image operation (BI...ID...EI sequence).
/// Inline images are embedded directly in the content stream rather than as XObjects.
/// </summary>
public class InlineImageOperation : PdfOperation
{
    /// <summary>
    /// Raw bytes of the complete BI...ID...EI sequence.
    /// These bytes are written verbatim to the output content stream.
    /// </summary>
    public required byte[] RawBytes { get; init; }

    /// <summary>
    /// Image width from the inline image dictionary (/W or /Width).
    /// </summary>
    public int ImageWidth { get; init; }

    /// <summary>
    /// Image height from the inline image dictionary (/H or /Height).
    /// </summary>
    public int ImageHeight { get; init; }

    /// <summary>
    /// Bits per component from the inline image dictionary (/BPC or /BitsPerComponent).
    /// </summary>
    public int BitsPerComponent { get; init; }

    /// <summary>
    /// Color space abbreviation from the inline image dictionary (/CS or /ColorSpace).
    /// Common values: G (Grayscale), RGB, CMYK, I (Indexed).
    /// </summary>
    public string? ColorSpace { get; init; }

    /// <summary>
    /// Filter abbreviation from the inline image dictionary (/F or /Filter).
    /// Common values: AHx (ASCIIHexDecode), A85 (ASCII85Decode), LZW, Fl (FlateDecode), etc.
    /// </summary>
    public string? Filter { get; init; }
}
