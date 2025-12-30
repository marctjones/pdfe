using PdfEditor.Redaction.Fonts;

namespace PdfEditor.Redaction.Operators;

/// <summary>
/// Shared state for PDF content stream parsing.
/// Contains graphics state, text state, and transformation matrices.
/// </summary>
public class PdfParserState
{
    /// <summary>
    /// Page height in points (for coordinate conversion).
    /// </summary>
    public double PageHeight { get; }

    /// <summary>
    /// Font registry containing information about fonts defined in page resources.
    /// Used to determine encoding for CID/CJK fonts.
    /// </summary>
    public Dictionary<string, FontInfo> FontRegistry { get; set; } = new();

    /// <summary>
    /// Current stream position (for ordering operations).
    /// </summary>
    public int StreamPosition { get; set; }

    /// <summary>
    /// Whether we're inside a BT...ET text object.
    /// </summary>
    public bool InTextObject { get; set; }

    #region Graphics State

    /// <summary>
    /// Current transformation matrix (CTM).
    /// Default: identity matrix [1 0 0 1 0 0].
    /// </summary>
    public PdfMatrix TransformationMatrix { get; set; } = PdfMatrix.Identity;

    /// <summary>
    /// Stack for q/Q save/restore operations.
    /// </summary>
    private readonly Stack<GraphicsStateSnapshot> _graphicsStateStack = new();

    #endregion

    #region Text State

    /// <summary>
    /// Text matrix - set by Tm, modified by Td/TD/T*.
    /// </summary>
    public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;

    /// <summary>
    /// Text line matrix - set by Tm, used for T* calculations.
    /// </summary>
    public PdfMatrix TextLineMatrix { get; set; } = PdfMatrix.Identity;

    /// <summary>
    /// Current font name (e.g., "/F1").
    /// </summary>
    public string? FontName { get; set; }

    /// <summary>
    /// Current font size in points.
    /// </summary>
    public double FontSize { get; set; } = 12.0;

    /// <summary>
    /// Character spacing (Tc operator).
    /// </summary>
    public double CharacterSpacing { get; set; }

    /// <summary>
    /// Word spacing (Tw operator).
    /// </summary>
    public double WordSpacing { get; set; }

    /// <summary>
    /// Horizontal scaling as percentage (Tz operator).
    /// Default: 100%.
    /// </summary>
    public double HorizontalScaling { get; set; } = 100.0;

    /// <summary>
    /// Text leading (TL operator, also set by TD).
    /// </summary>
    public double TextLeading { get; set; }

    /// <summary>
    /// Text rendering mode (Tr operator).
    /// 0=fill, 1=stroke, 2=fill+stroke, 3=invisible.
    /// </summary>
    public int TextRenderingMode { get; set; }

    /// <summary>
    /// Text rise (Ts operator).
    /// </summary>
    public double TextRise { get; set; }

    #endregion

    /// <summary>
    /// Create parser state for a page.
    /// </summary>
    public PdfParserState(double pageHeight)
    {
        PageHeight = pageHeight;
    }

    /// <summary>
    /// Save current graphics state (q operator).
    /// </summary>
    public void SaveGraphicsState()
    {
        _graphicsStateStack.Push(new GraphicsStateSnapshot
        {
            TransformationMatrix = TransformationMatrix
        });
    }

    /// <summary>
    /// Restore graphics state (Q operator).
    /// </summary>
    public void RestoreGraphicsState()
    {
        if (_graphicsStateStack.Count > 0)
        {
            var snapshot = _graphicsStateStack.Pop();
            TransformationMatrix = snapshot.TransformationMatrix;
        }
    }

    /// <summary>
    /// Reset text matrices to identity (called by BT).
    /// </summary>
    public void BeginTextObject()
    {
        InTextObject = true;
        TextMatrix = PdfMatrix.Identity;
        TextLineMatrix = PdfMatrix.Identity;
    }

    /// <summary>
    /// End text object (called by ET).
    /// </summary>
    public void EndTextObject()
    {
        InTextObject = false;
    }

    /// <summary>
    /// Get current text position in page coordinates (PDF origin).
    /// </summary>
    public (double X, double Y) GetCurrentTextPosition()
    {
        // Transform (0,0) through text matrix and CTM
        var combined = TextMatrix.Multiply(TransformationMatrix);
        return (combined.E, combined.F);
    }

    /// <summary>
    /// Get information about the current font, if available.
    /// </summary>
    public FontInfo? GetCurrentFontInfo()
    {
        if (string.IsNullOrEmpty(FontName))
            return null;

        // Try with and without leading slash
        var nameWithSlash = FontName.StartsWith("/") ? FontName : "/" + FontName;
        var nameWithoutSlash = FontName.StartsWith("/") ? FontName.Substring(1) : FontName;

        if (FontRegistry.TryGetValue(nameWithSlash, out var fontInfo))
            return fontInfo;

        if (FontRegistry.TryGetValue(nameWithoutSlash, out fontInfo))
            return fontInfo;

        return null;
    }

    /// <summary>
    /// Whether the current font is a CID/CJK font.
    /// </summary>
    public bool IsCurrentFontCid => GetCurrentFontInfo()?.IsCidFont ?? false;

    /// <summary>
    /// Whether the current font is likely a CJK font (CID or CJK-named).
    /// </summary>
    public bool IsCurrentFontCjk => GetCurrentFontInfo()?.IsCjkFont ?? false;

    /// <summary>
    /// Snapshot of graphics state for save/restore.
    /// </summary>
    private record GraphicsStateSnapshot
    {
        public required PdfMatrix TransformationMatrix { get; init; }
    }
}

/// <summary>
/// 2D transformation matrix for PDF operations.
/// Represents: [a b 0; c d 0; e f 1]
/// </summary>
public readonly struct PdfMatrix
{
    public double A { get; init; }
    public double B { get; init; }
    public double C { get; init; }
    public double D { get; init; }
    public double E { get; init; }
    public double F { get; init; }

    /// <summary>
    /// Identity matrix [1 0 0 1 0 0].
    /// </summary>
    public static PdfMatrix Identity => new() { A = 1, B = 0, C = 0, D = 1, E = 0, F = 0 };

    /// <summary>
    /// Create matrix from PDF operands [a b c d e f].
    /// </summary>
    public static PdfMatrix FromOperands(double a, double b, double c, double d, double e, double f)
        => new() { A = a, B = b, C = c, D = d, E = e, F = f };

    /// <summary>
    /// Create translation matrix.
    /// </summary>
    public static PdfMatrix Translate(double tx, double ty)
        => new() { A = 1, B = 0, C = 0, D = 1, E = tx, F = ty };

    /// <summary>
    /// Multiply this matrix by another (this × other).
    /// </summary>
    public PdfMatrix Multiply(PdfMatrix other)
    {
        return new PdfMatrix
        {
            A = A * other.A + B * other.C,
            B = A * other.B + B * other.D,
            C = C * other.A + D * other.C,
            D = C * other.B + D * other.D,
            E = E * other.A + F * other.C + other.E,
            F = E * other.B + F * other.D + other.F
        };
    }

    /// <summary>
    /// Transform a point through this matrix.
    /// </summary>
    public (double X, double Y) Transform(double x, double y)
    {
        return (
            A * x + C * y + E,
            B * x + D * y + F
        );
    }

    public override string ToString() => $"[{A:F2} {B:F2} {C:F2} {D:F2} {E:F2} {F:F2}]";
}
