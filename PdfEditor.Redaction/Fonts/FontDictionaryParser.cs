using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// Parses font dictionaries from PDF page resources to extract font information.
/// Used to determine encoding strategies for CID/CJK fonts.
/// </summary>
public static class FontDictionaryParser
{
    /// <summary>
    /// Extract font information from all fonts in a page's resources.
    /// </summary>
    /// <param name="page">The PDF page to extract fonts from.</param>
    /// <returns>Dictionary mapping font names (e.g., "/F1") to FontInfo.</returns>
    public static Dictionary<string, FontInfo> ExtractFonts(PdfPage page)
    {
        var fonts = new Dictionary<string, FontInfo>();

        try
        {
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null)
                return fonts;

            var fontDict = resources.Elements.GetDictionary("/Font");
            if (fontDict == null)
                return fonts;

            foreach (var key in fontDict.Elements.Keys)
            {
                try
                {
                    var fontName = key.StartsWith("/") ? key : "/" + key;
                    var fontRef = fontDict.Elements[key];
                    var fontInfo = ParseFontDictionary(fontName, fontRef);
                    if (fontInfo != null)
                    {
                        fonts[fontName] = fontInfo;
                    }
                }
                catch
                {
                    // Skip fonts that can't be parsed
                }
            }
        }
        catch
        {
            // Return empty dictionary if resources can't be accessed
        }

        return fonts;
    }

    /// <summary>
    /// Parse a single font dictionary to extract font information.
    /// </summary>
    private static FontInfo? ParseFontDictionary(string fontName, PdfItem? fontItem)
    {
        if (fontItem == null)
            return null;

        // Dereference if it's a reference
        PdfDictionary? fontDict = null;
        if (fontItem is PdfReference fontRef)
        {
            fontDict = fontRef.Value as PdfDictionary;
        }
        else if (fontItem is PdfDictionary dict)
        {
            fontDict = dict;
        }

        if (fontDict == null)
            return null;

        // Extract basic font properties
        var subtype = GetName(fontDict, "/Subtype");
        var baseFont = GetName(fontDict, "/BaseFont");
        var encoding = GetEncodingName(fontDict);

        // Check if this is a CID font (Type0 with DescendantFonts)
        bool isCidFont = subtype == "Type0" && HasDescendantFonts(fontDict);

        return new FontInfo
        {
            Name = fontName,
            Subtype = subtype,
            BaseFont = baseFont,
            Encoding = encoding,
            IsCidFont = isCidFont
        };
    }

    /// <summary>
    /// Get a name value from a dictionary, stripping the leading slash.
    /// </summary>
    private static string? GetName(PdfDictionary dict, string key)
    {
        try
        {
            var item = dict.Elements[key];
            if (item is PdfName name)
            {
                var value = name.Value;
                return value.StartsWith("/") ? value.Substring(1) : value;
            }
            if (item is PdfString str)
            {
                return str.Value;
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    /// <summary>
    /// Get the encoding name from the font dictionary.
    /// Encoding can be a name or a dictionary with /BaseEncoding.
    /// </summary>
    private static string? GetEncodingName(PdfDictionary fontDict)
    {
        try
        {
            var encodingItem = fontDict.Elements["/Encoding"];

            if (encodingItem is PdfName name)
            {
                var value = name.Value;
                return value.StartsWith("/") ? value.Substring(1) : value;
            }

            if (encodingItem is PdfReference encodingRef)
            {
                var deref = encodingRef.Value;
                // Dereferenced encoding is typically a dictionary with /BaseEncoding
                if (deref is PdfDictionary encodingDict)
                {
                    return GetName(encodingDict, "/BaseEncoding");
                }
                // If it's somehow a name, try to extract from string representation
                if (deref != null)
                {
                    var str = deref.ToString();
                    if (str != null && str.StartsWith("/"))
                    {
                        return str.Substring(1);
                    }
                }
            }

            if (encodingItem is PdfDictionary encDict)
            {
                return GetName(encDict, "/BaseEncoding");
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }

    /// <summary>
    /// Check if the font dictionary has DescendantFonts (indicating CID font).
    /// </summary>
    private static bool HasDescendantFonts(PdfDictionary fontDict)
    {
        try
        {
            var descendants = fontDict.Elements["/DescendantFonts"];
            if (descendants is PdfArray arr && arr.Elements.Count > 0)
            {
                return true;
            }
            if (descendants is PdfReference)
            {
                return true; // Assume it has descendants if there's a reference
            }
        }
        catch
        {
            // Ignore errors
        }
        return false;
    }
}
