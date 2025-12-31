namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// Information about a PDF font extracted from the font dictionary.
/// Used to determine encoding and character handling strategies.
/// </summary>
public class FontInfo
{
    /// <summary>
    /// Font name as referenced in content stream (e.g., "/F1", "/TT0").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Font type from the /Subtype entry.
    /// Common values: Type0, Type1, TrueType, Type3, CIDFontType0, CIDFontType2
    /// </summary>
    public string? Subtype { get; init; }

    /// <summary>
    /// Base font name (e.g., "Helvetica", "MS-Mincho").
    /// </summary>
    public string? BaseFont { get; init; }

    /// <summary>
    /// Encoding name if specified (e.g., "WinAnsiEncoding", "Identity-H").
    /// </summary>
    public string? Encoding { get; init; }

    /// <summary>
    /// Whether this is a CID-keyed font (Type0 with CIDFont descendant).
    /// CID fonts are used for CJK (Chinese, Japanese, Korean) text.
    /// </summary>
    public bool IsCidFont { get; init; }

    /// <summary>
    /// The raw ToUnicode CMap stream bytes (if present).
    /// Used to map CID values to Unicode characters.
    /// </summary>
    public byte[]? ToUnicodeData { get; init; }

    /// <summary>
    /// Whether a ToUnicode CMap is available for this font.
    /// </summary>
    public bool HasToUnicode => ToUnicodeData != null && ToUnicodeData.Length > 0;

    /// <summary>
    /// Whether the encoding is Identity-H (horizontal) or Identity-V (vertical).
    /// For Identity encoding, CID values directly map to Unicode code points.
    /// </summary>
    public bool IsIdentityEncoding =>
        Encoding != null &&
        (Encoding.Equals("Identity-H", StringComparison.OrdinalIgnoreCase) ||
         Encoding.Equals("Identity-V", StringComparison.OrdinalIgnoreCase) ||
         Encoding.Contains("Identity"));

    /// <summary>
    /// Number of bytes per character code.
    /// Western fonts use 1 byte, CID fonts typically use 2 bytes.
    /// </summary>
    public int BytesPerCharacter => IsCidFont ? 2 : 1;

    /// <summary>
    /// Whether this font likely contains CJK characters based on encoding or base font name.
    /// </summary>
    public bool IsCjkFont => IsCidFont || IsCjkEncoding || IsCjkBaseFont;

    /// <summary>
    /// Whether the encoding indicates CJK content.
    /// </summary>
    private bool IsCjkEncoding =>
        Encoding != null && (
            Encoding.Contains("Identity") ||
            Encoding.Contains("UniGB") ||    // Simplified Chinese
            Encoding.Contains("UniCNS") ||   // Traditional Chinese
            Encoding.Contains("UniJIS") ||   // Japanese
            Encoding.Contains("UniKS") ||    // Korean
            Encoding.Contains("GBK") ||
            Encoding.Contains("GB2312") ||
            Encoding.Contains("Big5") ||
            Encoding.Contains("ShiftJIS") ||
            Encoding.Contains("EUC")
        );

    /// <summary>
    /// Whether the base font name suggests CJK content.
    /// </summary>
    private bool IsCjkBaseFont =>
        BaseFont != null && (
            BaseFont.Contains("Gothic") ||
            BaseFont.Contains("Mincho") ||
            BaseFont.Contains("Ming") ||
            BaseFont.Contains("Song") ||
            BaseFont.Contains("Hei") ||
            BaseFont.Contains("Kai") ||
            BaseFont.Contains("SimSun") ||
            BaseFont.Contains("SimHei") ||
            BaseFont.Contains("MingLiU") ||
            BaseFont.Contains("Batang") ||
            BaseFont.Contains("Dotum") ||
            BaseFont.Contains("Gulim") ||
            BaseFont.Contains("Malgun")
        );

    /// <summary>
    /// Recommended encoding to use for decoding text strings.
    /// </summary>
    public TextEncoding RecommendedEncoding
    {
        get
        {
            if (IsCidFont)
            {
                // CID fonts typically use Identity-H/V with UTF-16BE encoded strings
                return TextEncoding.Utf16BigEndian;
            }

            if (Encoding == "WinAnsiEncoding" || Encoding == null)
            {
                return TextEncoding.Windows1252;
            }

            if (Encoding == "MacRomanEncoding")
            {
                return TextEncoding.MacRoman;
            }

            // For unknown encodings, try UTF-16BE then fall back
            return TextEncoding.Utf16BigEndian;
        }
    }

    public override string ToString() =>
        $"FontInfo({Name}, Subtype={Subtype}, IsCID={IsCidFont}, Encoding={Encoding})";
}

/// <summary>
/// Text encoding types for PDF string decoding.
/// </summary>
public enum TextEncoding
{
    /// <summary>Windows-1252 (WinAnsiEncoding) - common for Western PDFs.</summary>
    Windows1252,

    /// <summary>Mac Roman encoding.</summary>
    MacRoman,

    /// <summary>UTF-16 Big Endian - common for CID/CJK fonts.</summary>
    Utf16BigEndian,

    /// <summary>Raw bytes as hex - fallback when decoding fails.</summary>
    RawHex
}
