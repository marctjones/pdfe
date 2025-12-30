using System.Text;
using PdfEditor.Redaction.Fonts;

namespace PdfEditor.Redaction.Operators.TextShowing;

/// <summary>
/// Handler for TJ (show text with kerning adjustments) operator.
/// TJ takes an array containing strings and numbers.
/// Numbers adjust horizontal position (in thousandths of text space units).
/// Example: [(H) -10 (ello)] TJ shows "Hello" with -10/1000 em adjustment.
/// Supports CID/CJK fonts with proper encoding detection.
/// </summary>
public class TjUpperOperatorHandler : IOperatorHandler
{
    public string OperatorName => "TJ";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // TJ: [ array ] TJ
        if (operands.Count == 0)
            return null;

        // The operand should be an array (List<object> or object[])
        var array = GetArray(operands[0]);
        if (array == null || array.Count == 0)
            return null;

        // Process the array and build glyphs
        var (text, glyphs) = ProcessArray(array, state);

        if (string.IsNullOrEmpty(text))
            return null;

        // Calculate bounding box from all glyphs
        var boundingBox = CalculateBoundingBox(glyphs);

        return new TextOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = state.InTextObject,
            Text = text,
            Glyphs = glyphs,
            FontName = state.FontName,
            FontSize = state.FontSize,
            BoundingBox = boundingBox,
            // Copy text state parameters for reconstruction (issue #122)
            CharacterSpacing = state.CharacterSpacing,
            WordSpacing = state.WordSpacing,
            HorizontalScaling = state.HorizontalScaling,
            TextRenderingMode = state.TextRenderingMode,
            TextRise = state.TextRise,
            TextLeading = state.TextLeading
        };
    }

    private static IList<object>? GetArray(object operand)
    {
        return operand switch
        {
            List<object> list => list,
            object[] arr => arr,
            _ => null
        };
    }

    private (string text, List<GlyphPosition> glyphs) ProcessArray(IList<object> array, PdfParserState state)
    {
        var textBuilder = new StringBuilder();
        var glyphs = new List<GlyphPosition>();

        // Get starting position from text matrix
        var startPosition = state.GetCurrentTextPosition();
        var startX = startPosition.X;
        var startY = startPosition.Y;
        var currentX = startX;

        // Extract effective font size from text matrix
        // Text matrix [a b c d e f] has scaling in 'a' (X) and 'd' (Y)
        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * textMatrix.D;  // Y-scale from matrix
        var charHeight = effectiveFontSize;

        // Get font info for encoding
        var fontInfo = state.GetCurrentFontInfo();

        int arrayIndex = 0;
        int globalStringIndex = 0;

        foreach (var element in array)
        {
            if (element is double d)
            {
                // Number: adjust horizontal position
                // Negative = move right, Positive = move left (tightening)
                // Value is in thousandths of text space units
                var adjustment = d / 1000.0 * state.FontSize * (state.HorizontalScaling / 100.0);
                currentX -= adjustment;
            }
            else if (element is int i)
            {
                var adjustment = i / 1000.0 * state.FontSize * (state.HorizontalScaling / 100.0);
                currentX -= adjustment;
            }
            else if (element is float f)
            {
                var adjustment = f / 1000.0 * state.FontSize * (state.HorizontalScaling / 100.0);
                currentX -= adjustment;
            }
            else if (element is long l)
            {
                var adjustment = l / 1000.0 * state.FontSize * (state.HorizontalScaling / 100.0);
                currentX -= adjustment;
            }
            else if (element is decimal m)
            {
                var adjustment = (double)m / 1000.0 * state.FontSize * (state.HorizontalScaling / 100.0);
                currentX -= adjustment;
            }
            else
            {
                // String element - decode using font-aware encoding
                string text;
                if (element is byte[] bytes)
                {
                    text = TextDecoder.Decode(bytes, fontInfo);
                }
                else if (element is string s)
                {
                    text = s;
                }
                else
                {
                    arrayIndex++;
                    continue;
                }

                // Process each character in the string
                for (int charIndex = 0; charIndex < text.Length; charIndex++)
                {
                    var ch = text[charIndex];
                    textBuilder.Append(ch);

                    // Calculate character width based on character type
                    // CJK characters are full-width (width ≈ height)
                    // Latin characters are approximately 0.6 × height
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
                        ArrayIndex = arrayIndex,
                        StringIndex = globalStringIndex
                    });

                    // Advance position
                    currentX += glyphWidth + state.CharacterSpacing;
                    globalStringIndex++;
                }
            }

            arrayIndex++;
        }

        // Update text matrix to final position
        var totalAdvance = currentX - startX;
        var advance = PdfMatrix.Translate(totalAdvance, 0);
        state.TextMatrix = advance.Multiply(state.TextMatrix);

        return (textBuilder.ToString(), glyphs);
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
}
