using PdfEditor.Redaction.ContentStream;
using PdfEditor.Redaction.Fonts;

namespace PdfEditor.Redaction.Operators.TextShowing;

/// <summary>
/// Handler for ' (single quote / move to next line and show text) operator.
/// Equivalent to: T* (string) Tj
/// 1. Move to start of next line (using current text leading)
/// 2. Show the text string
/// </summary>
public class QuoteOperatorHandler : IOperatorHandler
{
    public string OperatorName => "'";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // ': (string) '
        if (operands.Count == 0)
            return null;

        // Step 1: Move to next line (T* behavior)
        // T* is equivalent to: 0 -TL Td
        var tx = 0.0;
        var ty = -state.TextLeading;

        var translateMatrix = PdfMatrix.Translate(tx, ty);
        state.TextLineMatrix = translateMatrix.Multiply(state.TextLineMatrix);
        state.TextMatrix = state.TextLineMatrix;

        // Step 2: Show text (Tj behavior)
        var stringOperand = operands[0];
        string text;
        byte[]? rawBytes = null;

        if (stringOperand is byte[] bytes)
        {
            rawBytes = bytes;
            var fontInfo = state.GetCurrentFontInfo();
            text = TextDecoder.Decode(bytes, fontInfo);
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
        var boundingBox = CalculateBoundingBox(glyphs);

        // Advance text matrix by total width
        AdvanceTextMatrix(text, glyphs, state);

        // Calculate effective font size including text matrix scaling
        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * Math.Abs(textMatrix.D);
        if (effectiveFontSize <= 0 || effectiveFontSize > 1000)
        {
            effectiveFontSize = state.FontSize > 0 ? state.FontSize : 12.0;
        }

        return new TextOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = state.InTextObject,
            Text = text,
            Glyphs = glyphs,
            FontName = state.FontName,
            FontSize = effectiveFontSize,  // Use effective size including Tm scaling
            BoundingBox = boundingBox,
            CharacterSpacing = state.CharacterSpacing,
            WordSpacing = state.WordSpacing,
            HorizontalScaling = state.HorizontalScaling,
            TextRenderingMode = state.TextRenderingMode,
            TextRise = state.TextRise,
            TextLeading = state.TextLeading
        };
    }

    private List<GlyphPosition> CalculateGlyphPositions(string text, PdfParserState state)
    {
        var glyphs = new List<GlyphPosition>();

        var (startX, startY) = state.GetCurrentTextPosition();

        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * textMatrix.D;
        var charHeight = effectiveFontSize;

        double currentX = startX;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            double widthFactor = TextDecoder.IsFullWidthCharacter(ch) ? 1.0 : 0.6;
            var charWidth = effectiveFontSize * widthFactor * (state.HorizontalScaling / 100.0);

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

            currentX += glyphWidth + state.CharacterSpacing;
        }

        return glyphs;
    }

    private static PdfRectangle CalculateBoundingBox(IReadOnlyList<GlyphPosition> glyphs)
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

    private void AdvanceTextMatrix(string text, List<GlyphPosition> glyphs, PdfParserState state)
    {
        double totalWidth = 0;
        if (glyphs.Count > 0)
        {
            totalWidth = glyphs[^1].BoundingBox.Right - glyphs[0].BoundingBox.Left;
        }
        else
        {
            var textMatrix = state.TextMatrix;
            var effectiveFontSize = state.FontSize * textMatrix.D;
            var charWidth = effectiveFontSize * 0.6 * (state.HorizontalScaling / 100.0);
            totalWidth = text.Length * (charWidth + state.CharacterSpacing);
        }

        var advance = PdfMatrix.Translate(totalWidth, 0);
        state.TextMatrix = advance.Multiply(state.TextMatrix);
    }
}
