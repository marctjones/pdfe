using System.Text;
using PdfEditor.Redaction.ContentStream;
using PdfEditor.Redaction.Fonts;
using PdfEditor.Redaction.StringEncoding;

namespace PdfEditor.Redaction.Operators.TextShowing;

/// <summary>
/// Handler for Tj (show text) operator.
/// Produces a TextOperation with glyph positions for redaction.
/// Supports CID/CJK fonts with proper encoding detection.
/// </summary>
public class TjOperatorHandler : IOperatorHandler
{
    private readonly HexStringParser _hexParser = new();

    // Lazy Windows-1252 encoding for proper character encoding
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
        bool wasHexString = false;

        // Get font info for CID-aware parsing
        var fontInfo = state.GetCurrentFontInfo();
        bool isCidFont = fontInfo?.IsCidFont ?? false;

        if (stringOperand is byte[] bytes)
        {
            rawBytes = bytes;
            wasHexString = true; // byte[] operands come from hex strings
            // Use font-aware decoding for CID/CJK support
            text = TextDecoder.Decode(bytes, fontInfo);
        }
        else if (stringOperand is string s)
        {
            // For CJK fonts with ToUnicode CMap, we need to re-decode from raw bytes
            // because the parser decoded with Windows-1252 but the font uses custom encoding
            if (fontInfo?.HasToUnicode == true)
            {
                // Convert string back to bytes for ToUnicode decoding
                rawBytes = System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes(s);
                text = TextDecoder.Decode(rawBytes, fontInfo);
            }
            else
            {
                // For standard fonts, use the string as-is
                text = s;
                // Still set rawBytes for CID font reconstruction (if needed)
                if (isCidFont)
                {
                    rawBytes = System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes(s);
                }
            }
        }
        else
        {
            return null;
        }

        if (string.IsNullOrEmpty(text))
            return null;

        // Calculate glyph positions with CJK-aware widths and raw bytes
        var glyphs = CalculateGlyphPositions(text, rawBytes, wasHexString, fontInfo, state);

        // Calculate bounding box from all glyphs
        var boundingBox = CalculateBoundingBox(glyphs);

        // Advance text matrix by total width
        AdvanceTextMatrix(text, glyphs, state);

        // Calculate effective font size including text matrix scaling
        // PDF uses: effectiveSize = Tf_size * Tm_scale
        // For example: "/F1 1 Tf" + "9 0 0 9 x y Tm" → effective size = 1 * 9 = 9pt
        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * Math.Abs(textMatrix.D);  // Y-scale from matrix
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
            // Copy text state parameters for reconstruction (issue #122)
            CharacterSpacing = state.CharacterSpacing,
            WordSpacing = state.WordSpacing,
            HorizontalScaling = state.HorizontalScaling,
            TextRenderingMode = state.TextRenderingMode,
            TextRise = state.TextRise,
            TextLeading = state.TextLeading,
            // CJK support (issue #174)
            WasHexString = wasHexString,
            IsCidFont = isCidFont,
            RawBytes = rawBytes
        };
    }

    private List<GlyphPosition> CalculateGlyphPositions(
        string text,
        byte[]? rawBytes,
        bool wasHexString,
        FontInfo? fontInfo,
        PdfParserState state)
    {
        var glyphs = new List<GlyphPosition>();

        // Get starting position from text matrix
        var (startX, startY) = state.GetCurrentTextPosition();

        // Extract effective font size from text matrix
        // Text matrix [a b c d e f] has scaling in 'a' (X) and 'd' (Y)
        var textMatrix = state.TextMatrix;
        var effectiveFontSize = state.FontSize * textMatrix.D;  // Y-scale from matrix
        var charHeight = effectiveFontSize;

        double currentX = startX;

        bool isCidFont = fontInfo?.IsCidFont ?? false;
        int bytesPerChar = fontInfo?.BytesPerCharacter ?? 1;

        // For CID fonts, we need to correlate Unicode chars with raw byte pairs
        // Each CID is 2 bytes, each Unicode char corresponds to one CID
        int byteIndex = 0;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

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

            // Extract raw bytes for this glyph
            byte[]? glyphRawBytes = null;
            int cidValue = 0;

            if (rawBytes != null && byteIndex + bytesPerChar <= rawBytes.Length)
            {
                glyphRawBytes = new byte[bytesPerChar];
                Array.Copy(rawBytes, byteIndex, glyphRawBytes, 0, bytesPerChar);

                // Calculate CID value
                cidValue = bytesPerChar switch
                {
                    1 => glyphRawBytes[0],
                    2 => (glyphRawBytes[0] << 8) | glyphRawBytes[1],
                    _ => 0
                };

                byteIndex += bytesPerChar;
            }
            else if (!isCidFont)
            {
                // For Western fonts without raw bytes, encode character back to Windows-1252
                // CRITICAL FIX (Issue #187): Simple (byte)ch fails for Unicode characters like U+2019 (right quote)
                // which came from Windows-1252 byte 0x92. We must re-encode to Windows-1252 to preserve the byte.
                try
                {
                    var encoded = Windows1252Encoding.Value.GetBytes(new[] { ch });
                    if (encoded.Length == 1)
                    {
                        cidValue = encoded[0];
                        glyphRawBytes = encoded;
                    }
                    else
                    {
                        // Multi-byte result - use fallback
                        cidValue = (byte)(ch & 0xFF);
                        glyphRawBytes = new[] { (byte)(ch & 0xFF) };
                    }
                }
                catch
                {
                    // Encoding failed - use Unicode code point clamped to byte
                    cidValue = (byte)(ch & 0xFF);
                    glyphRawBytes = new[] { (byte)(ch & 0xFF) };
                }
            }

            glyphs.Add(new GlyphPosition
            {
                Character = ch.ToString(),
                BoundingBox = glyphBox,
                ArrayIndex = 0,
                StringIndex = i,
                RawBytes = glyphRawBytes,
                CidValue = cidValue,
                IsCidGlyph = isCidFont,
                WasHexString = wasHexString
            });

            // Advance position
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
        // Calculate total width from actual glyph positions
        double totalWidth = 0;
        if (glyphs.Count > 0)
        {
            totalWidth = glyphs[^1].BoundingBox.Right - glyphs[0].BoundingBox.Left;
        }
        else
        {
            // Fallback: estimate based on character count
            var textMatrix = state.TextMatrix;
            var effectiveFontSize = state.FontSize * textMatrix.D;
            var charWidth = effectiveFontSize * 0.6 * (state.HorizontalScaling / 100.0);
            totalWidth = text.Length * (charWidth + state.CharacterSpacing);
        }

        var advance = PdfMatrix.Translate(totalWidth, 0);
        state.TextMatrix = advance.Multiply(state.TextMatrix);
    }
}
