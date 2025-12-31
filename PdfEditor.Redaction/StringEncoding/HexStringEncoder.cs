using System.Text;
using PdfEditor.Redaction.Fonts;

namespace PdfEditor.Redaction.StringEncoding;

/// <summary>
/// Encodes glyph sequences back to PDF string operands (hex or literal format).
///
/// This is the inverse of HexStringParser. After redaction removes some glyphs,
/// we need to reconstruct the string operand with only the remaining glyphs.
/// </summary>
public class HexStringEncoder
{
    /// <summary>
    /// Encode a glyph sequence to a hex string operand.
    /// Returns the content between angle brackets (without the brackets).
    /// </summary>
    /// <param name="sequence">The glyph sequence to encode.</param>
    /// <returns>Hex string content, e.g., "0048006500"</returns>
    public string EncodeToHex(GlyphSequence sequence)
    {
        if (sequence.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var glyph in sequence.Glyphs)
        {
            sb.Append(glyph.ToHexString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encode a glyph sequence to a hex string operand with brackets.
    /// </summary>
    /// <param name="sequence">The glyph sequence to encode.</param>
    /// <returns>Hex string with brackets, e.g., "&lt;0048006500&gt;"</returns>
    public string EncodeToHexWithBrackets(GlyphSequence sequence)
    {
        return $"<{EncodeToHex(sequence)}>";
    }

    /// <summary>
    /// Encode glyphs to a hex string.
    /// </summary>
    /// <param name="glyphs">The glyphs to encode.</param>
    /// <returns>Hex string content without brackets.</returns>
    public string EncodeToHex(IEnumerable<GlyphInfo> glyphs)
    {
        var sb = new StringBuilder();
        foreach (var glyph in glyphs)
        {
            sb.Append(glyph.ToHexString());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encode a glyph sequence to a literal string operand.
    /// Returns the content between parentheses (without the parentheses),
    /// with special characters escaped.
    /// </summary>
    /// <param name="sequence">The glyph sequence to encode.</param>
    /// <returns>Escaped literal string content.</returns>
    public string EncodeToLiteral(GlyphSequence sequence)
    {
        if (sequence.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var glyph in sequence.Glyphs)
        {
            foreach (byte b in glyph.RawBytes)
            {
                char c = (char)b;
                // Escape special PDF string characters
                sb.Append(c switch
                {
                    '\\' => "\\\\",
                    '(' => "\\(",
                    ')' => "\\)",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ when b < 32 || b > 126 => $"\\{b:D3}", // Octal escape for non-printable
                    _ => c.ToString()
                });
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encode a glyph sequence to a literal string with parentheses.
    /// </summary>
    /// <param name="sequence">The glyph sequence to encode.</param>
    /// <returns>Literal string with parentheses, e.g., "(Hello)"</returns>
    public string EncodeToLiteralWithParens(GlyphSequence sequence)
    {
        return $"({EncodeToLiteral(sequence)})";
    }

    /// <summary>
    /// Encode a segment to the appropriate string format based on font type.
    /// CID fonts always use hex strings; Western fonts may use either.
    /// </summary>
    /// <param name="segment">The glyph segment to encode.</param>
    /// <param name="preferHex">Whether to prefer hex format for Western fonts.</param>
    /// <returns>The encoded string operand with appropriate delimiters.</returns>
    public string EncodeSegment(GlyphSegment segment, bool preferHex = false)
    {
        if (segment.Count == 0)
            return preferHex || segment.Font?.IsCidFont == true ? "<>" : "()";

        // CID fonts must use hex strings
        if (segment.Font?.IsCidFont == true)
        {
            return $"<{EncodeToHex(segment.Glyphs)}>";
        }

        // For Western fonts, use literal unless hex is preferred or bytes are non-printable
        if (preferHex || HasNonPrintableBytes(segment.Glyphs))
        {
            return $"<{EncodeToHex(segment.Glyphs)}>";
        }

        return $"({EncodeToLiteralFromGlyphs(segment.Glyphs)})";
    }

    /// <summary>
    /// Encode multiple segments into a TJ array format.
    /// Used when segments have positioning adjustments between them.
    /// </summary>
    /// <param name="segments">The segments to encode.</param>
    /// <param name="adjustments">Position adjustments between segments (in text space units, negative = advance).</param>
    /// <returns>TJ array content, e.g., "[&lt;0048&gt; -50 &lt;0065&gt;]"</returns>
    public string EncodeTjArray(IEnumerable<GlyphSegment> segments, IEnumerable<double>? adjustments = null)
    {
        var segmentList = segments.ToList();
        var adjustmentList = adjustments?.ToList() ?? new List<double>();

        if (segmentList.Count == 0)
            return "[]";

        var sb = new StringBuilder();
        sb.Append('[');

        for (int i = 0; i < segmentList.Count; i++)
        {
            var segment = segmentList[i];

            // Add string operand
            sb.Append(EncodeSegment(segment));

            // Add adjustment if present (before next segment)
            if (i < adjustmentList.Count && Math.Abs(adjustmentList[i]) > 0.001)
            {
                sb.Append(' ');
                sb.Append(FormatNumber(adjustmentList[i]));
            }

            if (i < segmentList.Count - 1)
            {
                sb.Append(' ');
            }
        }

        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Encode a single segment with position adjustment.
    /// Used for simple Tj operators that need to become TJ with adjustment.
    /// </summary>
    /// <param name="segment">The segment to encode.</param>
    /// <param name="leadingAdjustment">Adjustment before the text (optional).</param>
    /// <returns>Tj operand or TJ array depending on adjustment.</returns>
    public string EncodeWithAdjustment(GlyphSegment segment, double leadingAdjustment = 0)
    {
        // If no adjustment, use simple format
        if (Math.Abs(leadingAdjustment) < 0.001)
        {
            return EncodeSegment(segment);
        }

        // Need TJ array format for adjustment
        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(FormatNumber(leadingAdjustment));
        sb.Append(' ');
        sb.Append(EncodeSegment(segment));
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Check if any glyphs have non-printable bytes.
    /// </summary>
    private static bool HasNonPrintableBytes(IEnumerable<GlyphInfo> glyphs)
    {
        foreach (var glyph in glyphs)
        {
            foreach (byte b in glyph.RawBytes)
            {
                if (b < 32 || b > 126)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Encode glyphs to literal string content.
    /// </summary>
    private string EncodeToLiteralFromGlyphs(IEnumerable<GlyphInfo> glyphs)
    {
        var sb = new StringBuilder();
        foreach (var glyph in glyphs)
        {
            foreach (byte b in glyph.RawBytes)
            {
                char c = (char)b;
                sb.Append(c switch
                {
                    '\\' => "\\\\",
                    '(' => "\\(",
                    ')' => "\\)",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ when b < 32 || b > 126 => $"\\{b:D3}",
                    _ => c.ToString()
                });
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Format a number for PDF output.
    /// </summary>
    private static string FormatNumber(double value)
    {
        // Use integer format if close to whole number
        if (Math.Abs(value - Math.Round(value)) < 0.0001)
        {
            return ((int)Math.Round(value)).ToString();
        }
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
