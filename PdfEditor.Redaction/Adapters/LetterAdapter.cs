using Pdfe.Core.Document;
using Pdfe.Core.Text;
using CorePdfRectangle = Pdfe.Core.Document.PdfRectangle;

namespace PdfEditor.Redaction.Adapters;

/// <summary>
/// Adapter for Letter to provide compatibility with existing code that expects PdfPig Letter.
/// </summary>
public class LetterAdapter
{
    private readonly Letter _letter;

    /// <summary>
    /// The underlying Pdfe.Core Letter.
    /// </summary>
    public Letter Letter => _letter;

    internal LetterAdapter(Letter letter)
    {
        _letter = letter;
    }

    /// <summary>
    /// The Unicode character value.
    /// </summary>
    public string Value => _letter.Value;

    /// <summary>
    /// The glyph bounding box (as Pdfe.Core type).
    /// </summary>
    public CorePdfRectangle GlyphRectangle => _letter.GlyphRectangle;

    /// <summary>
    /// The glyph bounding box (as PdfEditor.Redaction type).
    /// </summary>
    public PdfRectangle GlyphRectangleAsRedaction => ToRedactionRect(_letter.GlyphRectangle);

    private static PdfRectangle ToRedactionRect(CorePdfRectangle r) =>
        new(r.Left, r.Bottom, r.Right, r.Top);

    /// <summary>
    /// Font size in points.
    /// </summary>
    public double FontSize => _letter.FontSize;

    /// <summary>
    /// Font name.
    /// </summary>
    public string FontName => _letter.FontName;

    /// <summary>
    /// X coordinate of glyph start position.
    /// </summary>
    public double StartBaseLine => _letter.StartX;

    /// <summary>
    /// Baseline Y coordinate (PdfPig compatibility).
    /// </summary>
    public double EndBaseLine => _letter.StartY;

    /// <summary>
    /// Glyph width.
    /// </summary>
    public double Width => _letter.Width;

    /// <summary>
    /// Point representing the start location (PdfPig compatibility).
    /// </summary>
    public PointAdapter Location => new(_letter.StartX, _letter.StartY);

    /// <summary>
    /// Character code in font encoding.
    /// </summary>
    public int CharacterCode => _letter.CharacterCode;

    public override string ToString() => _letter.ToString();
}

/// <summary>
/// Simple point adapter for PdfPig compatibility.
/// </summary>
public readonly struct PointAdapter
{
    public double X { get; }
    public double Y { get; }

    public PointAdapter(double x, double y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"({X:F2}, {Y:F2})";
}
