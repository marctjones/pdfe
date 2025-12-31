using System.Text;

namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// Decodes PDF text strings based on font encoding.
/// Handles Windows-1252, UTF-16BE, and fallback encodings.
/// </summary>
public static class TextDecoder
{
    private static readonly Lazy<Encoding> Windows1252Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("Windows-1252");
    });

    // Cache for parsed ToUnicode CMaps
    private static readonly Dictionary<byte[], CidToUnicodeMapper> _toUnicodeCache = new();
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Decode a PDF text string based on font information.
    /// Uses ToUnicode CMap when available for accurate CJK text decoding.
    /// </summary>
    /// <param name="bytes">Raw bytes from the PDF content stream.</param>
    /// <param name="fontInfo">Font information, or null for default encoding.</param>
    /// <returns>Decoded text string.</returns>
    public static string Decode(byte[] bytes, FontInfo? fontInfo)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        // If font has ToUnicode CMap, use it for decoding
        if (fontInfo?.HasToUnicode == true && fontInfo.ToUnicodeData != null)
        {
            var decoded = DecodeWithToUnicode(bytes, fontInfo);
            if (!string.IsNullOrEmpty(decoded) && IsLikelyValidText(decoded))
            {
                return decoded;
            }
        }

        var encoding = fontInfo?.RecommendedEncoding ?? TextEncoding.Windows1252;
        return Decode(bytes, encoding);
    }

    /// <summary>
    /// Decode bytes using the font's ToUnicode CMap.
    /// </summary>
    private static string DecodeWithToUnicode(byte[] bytes, FontInfo fontInfo)
    {
        try
        {
            // Get or create the mapper for this font's ToUnicode data
            CidToUnicodeMapper mapper;
            lock (_cacheLock)
            {
                if (!_toUnicodeCache.TryGetValue(fontInfo.ToUnicodeData!, out mapper!))
                {
                    var parser = new ToUnicodeCMapParser();
                    var mapping = parser.Parse(fontInfo.ToUnicodeData!);
                    mapper = new CidToUnicodeMapper(mapping);
                    _toUnicodeCache[fontInfo.ToUnicodeData!] = mapper;
                }
            }

            // Determine bytes per character code
            // For CID fonts, typically 2 bytes; for simple fonts, 1 byte
            int bytesPerChar = fontInfo.BytesPerCharacter;

            // Decode each character code using the ToUnicode mapping
            var sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i += bytesPerChar)
            {
                int charCode;
                if (bytesPerChar == 2 && i + 1 < bytes.Length)
                {
                    // Big-endian 2-byte character code
                    charCode = (bytes[i] << 8) | bytes[i + 1];
                }
                else
                {
                    // Single byte character code
                    charCode = bytes[i];
                }

                var unicode = mapper.MapCidToUnicode(charCode);
                if (unicode != null)
                {
                    sb.Append(unicode);
                }
                else
                {
                    // Fallback: use the character code directly if it's in printable range
                    if (charCode >= 0x20 && charCode <= 0x7E)
                    {
                        sb.Append((char)charCode);
                    }
                    else
                    {
                        // Use replacement character
                        sb.Append('\uFFFD');
                    }
                }
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Decode a PDF text string using a specific encoding.
    /// </summary>
    /// <param name="bytes">Raw bytes from the PDF content stream.</param>
    /// <param name="encoding">Encoding to use.</param>
    /// <returns>Decoded text string.</returns>
    public static string Decode(byte[] bytes, TextEncoding encoding)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        try
        {
            return encoding switch
            {
                TextEncoding.Windows1252 => DecodeWindows1252(bytes),
                TextEncoding.MacRoman => DecodeMacRoman(bytes),
                TextEncoding.Utf16BigEndian => DecodeUtf16Be(bytes),
                TextEncoding.RawHex => DecodeAsHex(bytes),
                _ => DecodeWindows1252(bytes)
            };
        }
        catch
        {
            // If decoding fails, try alternatives
            return TryAlternativeDecodings(bytes);
        }
    }

    /// <summary>
    /// Decode using Windows-1252 (WinAnsiEncoding).
    /// </summary>
    private static string DecodeWindows1252(byte[] bytes)
    {
        return Windows1252Encoding.Value.GetString(bytes);
    }

    /// <summary>
    /// Decode using Mac Roman encoding.
    /// </summary>
    private static string DecodeMacRoman(byte[] bytes)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var macRoman = Encoding.GetEncoding("macintosh");
            return macRoman.GetString(bytes);
        }
        catch
        {
            // Fall back to Windows-1252
            return DecodeWindows1252(bytes);
        }
    }

    /// <summary>
    /// Decode using UTF-16 Big Endian (common for CID fonts).
    /// </summary>
    private static string DecodeUtf16Be(byte[] bytes)
    {
        // UTF-16BE requires even number of bytes
        if (bytes.Length % 2 != 0)
        {
            // Might be single-byte encoding, try Windows-1252 first
            var win1252Result = DecodeWindows1252(bytes);
            if (IsLikelyValidText(win1252Result))
                return win1252Result;
        }

        // Check for BOM (optional in PDF)
        int startIndex = 0;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            startIndex = 2; // Skip BOM
        }

        var text = Encoding.BigEndianUnicode.GetString(bytes, startIndex, bytes.Length - startIndex);

        // Validate the result - if it's mostly control characters, it's probably wrong
        if (!IsLikelyValidText(text))
        {
            // Try Windows-1252 as fallback
            var fallback = DecodeWindows1252(bytes);
            if (IsLikelyValidText(fallback))
                return fallback;
        }

        return text;
    }

    /// <summary>
    /// Decode as hex representation (fallback).
    /// </summary>
    private static string DecodeAsHex(byte[] bytes)
    {
        return "[" + BitConverter.ToString(bytes).Replace("-", "") + "]";
    }

    /// <summary>
    /// Try alternative decodings when primary fails.
    /// </summary>
    private static string TryAlternativeDecodings(byte[] bytes)
    {
        // Try UTF-16BE first (for CJK)
        try
        {
            if (bytes.Length >= 2 && bytes.Length % 2 == 0)
            {
                var utf16 = DecodeUtf16Be(bytes);
                if (IsLikelyValidText(utf16))
                    return utf16;
            }
        }
        catch { }

        // Try Windows-1252
        try
        {
            var win1252 = DecodeWindows1252(bytes);
            if (IsLikelyValidText(win1252))
                return win1252;
        }
        catch { }

        // Final fallback: hex representation
        return DecodeAsHex(bytes);
    }

    /// <summary>
    /// Check if a string is likely valid text (not garbage from wrong encoding).
    /// </summary>
    private static bool IsLikelyValidText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        int printableCount = 0;
        int totalCount = 0;

        foreach (var ch in text)
        {
            totalCount++;
            // Count printable characters (includes CJK ranges)
            if (char.IsLetterOrDigit(ch) ||
                char.IsPunctuation(ch) ||
                char.IsWhiteSpace(ch) ||
                char.IsSymbol(ch) ||
                IsCjkCharacter(ch))
            {
                printableCount++;
            }
        }

        // If at least 70% of characters are printable, consider it valid
        return totalCount > 0 && (double)printableCount / totalCount >= 0.7;
    }

    /// <summary>
    /// Check if a character is in CJK Unicode ranges.
    /// </summary>
    public static bool IsCjkCharacter(char ch)
    {
        // CJK Unified Ideographs
        if (ch >= 0x4E00 && ch <= 0x9FFF) return true;
        // CJK Unified Ideographs Extension A
        if (ch >= 0x3400 && ch <= 0x4DBF) return true;
        // CJK Compatibility Ideographs
        if (ch >= 0xF900 && ch <= 0xFAFF) return true;
        // Hiragana
        if (ch >= 0x3040 && ch <= 0x309F) return true;
        // Katakana
        if (ch >= 0x30A0 && ch <= 0x30FF) return true;
        // Hangul Syllables
        if (ch >= 0xAC00 && ch <= 0xD7AF) return true;
        // Hangul Jamo
        if (ch >= 0x1100 && ch <= 0x11FF) return true;
        // CJK Symbols and Punctuation
        if (ch >= 0x3000 && ch <= 0x303F) return true;
        // Halfwidth and Fullwidth Forms
        if (ch >= 0xFF00 && ch <= 0xFFEF) return true;
        // Bopomofo
        if (ch >= 0x3100 && ch <= 0x312F) return true;

        return false;
    }

    /// <summary>
    /// Check if a character is full-width (CJK or fullwidth forms).
    /// Full-width characters typically occupy the same width as height.
    /// </summary>
    public static bool IsFullWidthCharacter(char ch)
    {
        // CJK characters are full-width
        if (IsCjkCharacter(ch)) return true;

        // Fullwidth ASCII variants
        if (ch >= 0xFF01 && ch <= 0xFF5E) return true;

        // Fullwidth punctuation and symbols
        if (ch >= 0xFF5F && ch <= 0xFFEF) return true;

        return false;
    }
}
