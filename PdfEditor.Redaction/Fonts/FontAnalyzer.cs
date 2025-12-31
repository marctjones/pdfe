using PdfSharp.Pdf;

namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// Analyzes fonts in PDF pages to determine encoding strategies.
/// This is a convenience wrapper around FontDictionaryParser that provides
/// caching and lookup functionality.
/// </summary>
public class FontAnalyzer
{
    private readonly Dictionary<string, FontInfo> _fontCache = new();
    private bool _fontsLoaded;

    /// <summary>
    /// Analyze fonts in a page and cache the results.
    /// </summary>
    /// <param name="page">The PDF page to analyze.</param>
    public void AnalyzePage(PdfPage page)
    {
        var fonts = FontDictionaryParser.ExtractFonts(page);
        foreach (var (name, info) in fonts)
        {
            _fontCache[name] = info;
        }
        _fontsLoaded = true;
    }

    /// <summary>
    /// Get font info by name.
    /// </summary>
    /// <param name="fontName">The font name (e.g., "/F1" or "F1").</param>
    /// <returns>FontInfo if found, null otherwise.</returns>
    public FontInfo? GetFont(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName))
            return null;

        var normalizedName = fontName.StartsWith("/") ? fontName : "/" + fontName;

        if (_fontCache.TryGetValue(normalizedName, out var fontInfo))
            return fontInfo;

        // Try without leading slash
        if (_fontCache.TryGetValue(fontName, out fontInfo))
            return fontInfo;

        return null;
    }

    /// <summary>
    /// Check if a font is a CID font.
    /// </summary>
    /// <param name="fontName">The font name.</param>
    /// <returns>True if CID font, false otherwise.</returns>
    public bool IsCidFont(string? fontName)
    {
        var fontInfo = GetFont(fontName);
        return fontInfo?.IsCidFont ?? false;
    }

    /// <summary>
    /// Get all analyzed fonts.
    /// </summary>
    public IReadOnlyDictionary<string, FontInfo> GetAllFonts() => _fontCache;

    /// <summary>
    /// Check if fonts have been analyzed.
    /// </summary>
    public bool HasFonts => _fontsLoaded && _fontCache.Count > 0;

    /// <summary>
    /// Clear the font cache.
    /// </summary>
    public void Clear()
    {
        _fontCache.Clear();
        _fontsLoaded = false;
    }
}
