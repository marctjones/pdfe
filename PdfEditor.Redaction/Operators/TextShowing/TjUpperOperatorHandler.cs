using System.Text;

namespace PdfEditor.Redaction.Operators.TextShowing;

/// <summary>
/// Handler for TJ (show text with kerning adjustments) operator.
/// TJ takes an array containing strings and numbers.
/// Numbers adjust horizontal position (in thousandths of text space units).
/// Example: [(H) -10 (ello)] TJ shows "Hello" with -10/1000 em adjustment.
/// </summary>
public class TjUpperOperatorHandler : IOperatorHandler
{
    private static readonly Lazy<Encoding> Windows1252Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("Windows-1252");
    });

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
            Text = text,
            Glyphs = glyphs,
            FontName = state.FontName,
            FontSize = state.FontSize,
            BoundingBox = boundingBox
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
        var xScale = textMatrix.A;  // X-scale from matrix

        // Character dimensions (using effective font size from matrix)
        var charWidth = effectiveFontSize * 0.6 * (state.HorizontalScaling / 100.0);
        var charHeight = effectiveFontSize;

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
                // String element
                string text;
                if (element is byte[] bytes)
                {
                    text = Windows1252Encoding.Value.GetString(bytes);
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
