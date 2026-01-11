using Pdfe.Core.Document;

namespace Pdfe.Core.Text;

/// <summary>
/// Represents a single letter (glyph) extracted from a PDF page.
/// Contains the character value, position, font information, and bounding box.
/// </summary>
public class Letter
{
    /// <summary>
    /// The Unicode character(s) represented by this glyph.
    /// May be more than one character for ligatures.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// The bounding box of the glyph in PDF coordinates (origin at bottom-left).
    /// </summary>
    public PdfRectangle GlyphRectangle { get; }

    /// <summary>
    /// The font size in points.
    /// </summary>
    public double FontSize { get; }

    /// <summary>
    /// The name of the font used to render this glyph.
    /// </summary>
    public string FontName { get; }

    /// <summary>
    /// The X coordinate of the glyph start position.
    /// </summary>
    public double StartX { get; }

    /// <summary>
    /// The Y coordinate of the glyph baseline.
    /// </summary>
    public double StartY { get; }

    /// <summary>
    /// The width of the glyph in text space units.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// The character code in the font's encoding (before Unicode mapping).
    /// </summary>
    public int CharacterCode { get; }

    /// <summary>
    /// The baseline start point of the glyph.
    /// </summary>
    public PdfPoint StartBaseLine => new(StartX, StartY);

    /// <summary>
    /// The baseline end point of the glyph.
    /// </summary>
    public PdfPoint EndBaseLine => new(StartX + Width, StartY);

    public Letter(
        string value,
        PdfRectangle glyphRectangle,
        double fontSize,
        string fontName,
        double startX,
        double startY,
        double width,
        int characterCode)
    {
        Value = value;
        GlyphRectangle = glyphRectangle;
        FontSize = fontSize;
        FontName = fontName;
        StartX = startX;
        StartY = startY;
        Width = width;
        CharacterCode = characterCode;
    }

    public override string ToString() => $"'{Value}' at ({GlyphRectangle.Left:F1}, {GlyphRectangle.Bottom:F1})";
}
