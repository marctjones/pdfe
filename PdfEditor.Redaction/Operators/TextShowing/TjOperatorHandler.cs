using System.Text;
using PdfEditor.Redaction.ContentStream;

namespace PdfEditor.Redaction.Operators.TextShowing;

/// <summary>
/// Handler for Tj (show text) operator.
/// Produces a TextOperation with glyph positions for redaction.
/// </summary>
public class TjOperatorHandler : IOperatorHandler
{
    private static readonly Lazy<Encoding> Windows1252Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("Windows-1252");
    });

    public string OperatorName => "Tj";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tj: (string) Tj  or <hexstring> Tj
        if (operands.Count == 0)
            return null;

        // Get the string content
        var stringOperand = operands[0];
        string text;
        byte[]? rawBytes = null;

        if (stringOperand is byte[] bytes)
        {
            rawBytes = bytes;
            text = Windows1252Encoding.Value.GetString(bytes);
        }
        else if (stringOperand is string s)
        {
            text = s;
        }
        else
        {
            return null;
        }

        if (string.IsNullOrEmpty(text))
            return null;

        // Calculate glyph positions
        var glyphs = CalculateGlyphPositions(text, state);

        // Calculate bounding box from all glyphs
        var boundingBox = CalculateBoundingBox(glyphs, state);

        // Advance text matrix by total width
        AdvanceTextMatrix(text.Length, state);

        return new TextOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            Text = text,
            Glyphs = glyphs,
            FontName = state.FontName,
            FontSize = state.FontSize,
            BoundingBox = boundingBox
        };
    }

    private List<GlyphPosition> CalculateGlyphPositions(string text, PdfParserState state)
    {
        var glyphs = new List<GlyphPosition>();

        // Get starting position from text matrix
        var (startX, startY) = state.GetCurrentTextPosition();

        // Extract effective font size from text matrix
        // Text matrix [a b c d e f] has scaling in 'a' (X) and 'd' (Y)
        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * textMatrix.D;  // Y-scale from matrix

        // Approximate character width (proper font metrics would be better)
        var charWidth = effectiveFontSize * 0.6 * (state.HorizontalScaling / 100.0);
        var charHeight = effectiveFontSize;

        double currentX = startX;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // Skip whitespace for positioning but include in text
            var glyphWidth = charWidth;
            if (ch == ' ')
            {
                glyphWidth += state.WordSpacing;
            }

            var glyphBox = new PdfRectangle
            {
                Left = currentX,
                Bottom = startY,
                Right = currentX + glyphWidth,
                Top = startY + charHeight
            };

            glyphs.Add(new GlyphPosition
            {
                Character = ch.ToString(),
                BoundingBox = glyphBox,
                ArrayIndex = 0,
                StringIndex = i
            });

            // Advance position
            currentX += glyphWidth + state.CharacterSpacing;
        }

        return glyphs;
    }

    private static PdfRectangle CalculateBoundingBox(IReadOnlyList<GlyphPosition> glyphs, PdfParserState state)
    {
        if (glyphs.Count == 0)
            return new PdfRectangle();

        var left = double.MaxValue;
        var bottom = double.MaxValue;
        var right = double.MinValue;
        var top = double.MinValue;

        foreach (var glyph in glyphs)
        {
            if (glyph.BoundingBox.Left < left) left = glyph.BoundingBox.Left;
            if (glyph.BoundingBox.Bottom < bottom) bottom = glyph.BoundingBox.Bottom;
            if (glyph.BoundingBox.Right > right) right = glyph.BoundingBox.Right;
            if (glyph.BoundingBox.Top > top) top = glyph.BoundingBox.Top;
        }

        return new PdfRectangle { Left = left, Bottom = bottom, Right = right, Top = top };
    }

    private void AdvanceTextMatrix(int charCount, PdfParserState state)
    {
        // Advance text matrix by total width of characters
        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * textMatrix.D;
        var charWidth = effectiveFontSize * 0.6 * (state.HorizontalScaling / 100.0);
        var totalWidth = charCount * (charWidth + state.CharacterSpacing);

        var advance = PdfMatrix.Translate(totalWidth, 0);
        state.TextMatrix = advance.Multiply(state.TextMatrix);
    }
}
