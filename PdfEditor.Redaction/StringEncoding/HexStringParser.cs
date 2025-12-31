using PdfEditor.Redaction.Fonts;

namespace PdfEditor.Redaction.StringEncoding;

/// <summary>
/// Parses PDF hex string operands into GlyphInfo sequences.
///
/// Hex strings in PDF content streams look like: &lt;0048006500...&gt;
/// For CID fonts, each 4 hex digits = 1 CID (2 bytes, big-endian).
/// For Western fonts, each 2 hex digits = 1 character code.
/// </summary>
public class HexStringParser
{
    private readonly ToUnicodeCMapParser _cmapParser = new();

    /// <summary>
    /// Parse a hex string operand into a sequence of glyphs.
    /// </summary>
    /// <param name="hexString">The hex string content (without angle brackets).</param>
    /// <param name="font">The font info for encoding detection.</param>
    /// <returns>A sequence of GlyphInfo objects with Unicode values.</returns>
    public GlyphSequence Parse(string hexString, FontInfo? font)
    {
        var sequence = new GlyphSequence { Font = font };

        if (string.IsNullOrEmpty(hexString))
            return sequence;

        // Clean up hex string (remove whitespace)
        hexString = CleanHexString(hexString);

        // Pad to even length
        if (hexString.Length % 2 != 0)
            hexString += "0";

        // Get CID-to-Unicode mapper if available
        CidToUnicodeMapper? mapper = null;
        if (font?.HasToUnicode == true)
        {
            var mapping = _cmapParser.Parse(font.ToUnicodeData!);
            mapper = new CidToUnicodeMapper(mapping);
        }
        else if (font?.IsIdentityEncoding == true)
        {
            mapper = CidToUnicodeMapper.CreateIdentity();
        }

        // Determine bytes per character
        int bytesPerChar = font?.BytesPerCharacter ?? 1;
        int hexCharsPerGlyph = bytesPerChar * 2;

        int sourceIndex = 0;
        for (int i = 0; i + hexCharsPerGlyph <= hexString.Length; i += hexCharsPerGlyph)
        {
            try
            {
                var hexChunk = hexString.Substring(i, hexCharsPerGlyph);
                var bytes = HexToBytes(hexChunk);
                int cid = BytesToCid(bytes);
                string unicode = GetUnicode(cid, bytes, mapper, font);

                var glyph = new GlyphInfo
                {
                    UnicodeValue = unicode,
                    RawBytes = bytes,
                    CidValue = cid,
                    Font = font,
                    SourceIndex = sourceIndex++
                };

                sequence.Add(glyph);
            }
            catch
            {
                // Skip malformed hex sequences
            }
        }

        return sequence;
    }

    /// <summary>
    /// Parse a literal string operand (parentheses format) into glyphs.
    /// </summary>
    /// <param name="literalString">The string content (without parentheses, already unescaped).</param>
    /// <param name="font">The font info for encoding detection.</param>
    /// <returns>A sequence of GlyphInfo objects with Unicode values.</returns>
    public GlyphSequence ParseLiteral(string literalString, FontInfo? font)
    {
        var sequence = new GlyphSequence { Font = font };

        if (string.IsNullOrEmpty(literalString))
            return sequence;

        // Get CID-to-Unicode mapper if available
        CidToUnicodeMapper? mapper = null;
        if (font?.HasToUnicode == true)
        {
            var mapping = _cmapParser.Parse(font.ToUnicodeData!);
            mapper = new CidToUnicodeMapper(mapping);
        }
        else if (font?.IsIdentityEncoding == true)
        {
            mapper = CidToUnicodeMapper.CreateIdentity();
        }

        // For CID fonts, process as 2-byte sequences
        if (font?.IsCidFont == true)
        {
            var bytes = System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes(literalString);
            int sourceIndex = 0;

            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                int cid = (bytes[i] << 8) | bytes[i + 1];
                string unicode = GetUnicode(cid, new[] { bytes[i], bytes[i + 1] }, mapper, font);

                var glyph = new GlyphInfo
                {
                    UnicodeValue = unicode,
                    RawBytes = new[] { bytes[i], bytes[i + 1] },
                    CidValue = cid,
                    Font = font,
                    SourceIndex = sourceIndex++
                };

                sequence.Add(glyph);
            }
        }
        else
        {
            // Western font: each byte is a character code
            int sourceIndex = 0;
            foreach (char c in literalString)
            {
                byte b = (byte)c;
                string unicode = GetUnicode(b, new[] { b }, mapper, font);

                var glyph = GlyphInfo.FromSingleByte(b, string.IsNullOrEmpty(unicode) ? c : unicode[0], font, sourceIndex++);
                sequence.Add(glyph);
            }
        }

        return sequence;
    }

    /// <summary>
    /// Parse raw bytes into glyphs (for TJ array elements).
    /// </summary>
    /// <param name="bytes">The raw bytes.</param>
    /// <param name="font">The font info.</param>
    /// <returns>A sequence of GlyphInfo objects.</returns>
    public GlyphSequence ParseBytes(byte[] bytes, FontInfo? font)
    {
        var sequence = new GlyphSequence { Font = font };

        if (bytes == null || bytes.Length == 0)
            return sequence;

        // Get CID-to-Unicode mapper if available
        CidToUnicodeMapper? mapper = null;
        if (font?.HasToUnicode == true)
        {
            var mapping = _cmapParser.Parse(font.ToUnicodeData!);
            mapper = new CidToUnicodeMapper(mapping);
        }
        else if (font?.IsIdentityEncoding == true)
        {
            mapper = CidToUnicodeMapper.CreateIdentity();
        }

        int bytesPerChar = font?.BytesPerCharacter ?? 1;
        int sourceIndex = 0;

        for (int i = 0; i + bytesPerChar <= bytes.Length; i += bytesPerChar)
        {
            byte[] glyphBytes = new byte[bytesPerChar];
            Array.Copy(bytes, i, glyphBytes, 0, bytesPerChar);

            int cid = BytesToCid(glyphBytes);
            string unicode = GetUnicode(cid, glyphBytes, mapper, font);

            var glyph = new GlyphInfo
            {
                UnicodeValue = unicode,
                RawBytes = glyphBytes,
                CidValue = cid,
                Font = font,
                SourceIndex = sourceIndex++
            };

            sequence.Add(glyph);
        }

        return sequence;
    }

    /// <summary>
    /// Get Unicode value for a CID.
    /// </summary>
    private string GetUnicode(int cid, byte[] bytes, CidToUnicodeMapper? mapper, FontInfo? font)
    {
        // Try ToUnicode CMap first
        if (mapper != null)
        {
            var unicode = mapper.MapCidToUnicode(cid);
            if (!string.IsNullOrEmpty(unicode))
                return unicode;
        }

        // Fallback for single-byte fonts: use Windows-1252 or ISO-8859-1
        if (bytes.Length == 1)
        {
            try
            {
                // Try Windows-1252 encoding (common for Western PDFs)
                var encoding = System.Text.Encoding.GetEncoding(1252);
                return encoding.GetString(bytes);
            }
            catch
            {
                return ((char)bytes[0]).ToString();
            }
        }

        // For 2-byte without ToUnicode: try as Unicode code point
        if (bytes.Length == 2)
        {
            int codePoint = (bytes[0] << 8) | bytes[1];
            if (codePoint > 0 && codePoint <= 0x10FFFF)
            {
                try
                {
                    return char.ConvertFromUtf32(codePoint);
                }
                catch
                {
                    return "\uFFFD"; // Replacement character
                }
            }
        }

        return "\uFFFD"; // Replacement character
    }

    /// <summary>
    /// Remove whitespace from hex string.
    /// </summary>
    private static string CleanHexString(string hex)
    {
        var cleaned = new System.Text.StringBuilder(hex.Length);
        foreach (char c in hex)
        {
            if (Uri.IsHexDigit(c))
                cleaned.Append(c);
        }
        return cleaned.ToString();
    }

    /// <summary>
    /// Convert hex string to bytes.
    /// </summary>
    private static byte[] HexToBytes(string hex)
    {
        int length = hex.Length / 2;
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    /// <summary>
    /// Convert bytes to CID value (big-endian).
    /// </summary>
    private static int BytesToCid(byte[] bytes)
    {
        return bytes.Length switch
        {
            1 => bytes[0],
            2 => (bytes[0] << 8) | bytes[1],
            3 => (bytes[0] << 16) | (bytes[1] << 8) | bytes[2],
            4 => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3],
            _ => 0
        };
    }
}
