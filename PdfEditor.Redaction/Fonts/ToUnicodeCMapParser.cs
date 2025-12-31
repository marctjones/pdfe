using System.Text;
using System.Text.RegularExpressions;

namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// Parses ToUnicode CMap streams to create CID → Unicode mappings.
///
/// CMap format example:
/// <code>
/// beginbfchar
/// &lt;0048&gt; &lt;0048&gt;    ; CID 0x48 = Unicode U+0048 ('H')
/// endbfchar
/// beginbfrange
/// &lt;0041&gt; &lt;005A&gt; &lt;0041&gt;  ; CID 0x41-0x5A = Unicode U+0041-U+005A (A-Z)
/// endbfrange
/// </code>
/// </summary>
public class ToUnicodeCMapParser
{
    // Pattern for bfchar entries: <XXXX> <YYYY>
    private static readonly Regex BfCharPattern = new(
        @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>",
        RegexOptions.Compiled);

    // Pattern for bfrange entries: <XXXX> <YYYY> <ZZZZ>
    private static readonly Regex BfRangePattern = new(
        @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>",
        RegexOptions.Compiled);

    // Pattern for bfrange with array: <XXXX> <YYYY> [ <ZZZZ> <AAAA> ... ]
    private static readonly Regex BfRangeArrayPattern = new(
        @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[(.*?)\]",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Parse a ToUnicode CMap stream and return a CID → Unicode mapping.
    /// </summary>
    /// <param name="cmapData">Raw CMap stream bytes.</param>
    /// <returns>Dictionary mapping CID values to Unicode strings.</returns>
    public Dictionary<int, string> Parse(byte[] cmapData)
    {
        var mapping = new Dictionary<int, string>();

        if (cmapData == null || cmapData.Length == 0)
            return mapping;

        try
        {
            // Decode as ASCII/Latin-1 (CMap is text-based)
            var cmapText = Encoding.ASCII.GetString(cmapData);

            // Parse bfchar sections
            ParseBfCharSections(cmapText, mapping);

            // Parse bfrange sections
            ParseBfRangeSections(cmapText, mapping);
        }
        catch
        {
            // Return partial results on error
        }

        return mapping;
    }

    /// <summary>
    /// Parse all bfchar sections in the CMap.
    /// </summary>
    private void ParseBfCharSections(string cmapText, Dictionary<int, string> mapping)
    {
        // Find all beginbfchar...endbfchar sections
        int start = 0;
        while ((start = cmapText.IndexOf("beginbfchar", start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = cmapText.IndexOf("endbfchar", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;

            var section = cmapText.Substring(start, end - start);
            ParseBfCharEntries(section, mapping);

            start = end + 9; // Move past "endbfchar"
        }
    }

    /// <summary>
    /// Parse bfchar entries within a section.
    /// </summary>
    private void ParseBfCharEntries(string section, Dictionary<int, string> mapping)
    {
        var matches = BfCharPattern.Matches(section);
        foreach (Match match in matches)
        {
            try
            {
                var cidHex = match.Groups[1].Value;
                var unicodeHex = match.Groups[2].Value;

                int cid = Convert.ToInt32(cidHex, 16);
                string unicode = HexToUnicodeString(unicodeHex);

                mapping[cid] = unicode;
            }
            catch
            {
                // Skip invalid entries
            }
        }
    }

    /// <summary>
    /// Parse all bfrange sections in the CMap.
    /// </summary>
    private void ParseBfRangeSections(string cmapText, Dictionary<int, string> mapping)
    {
        // Find all beginbfrange...endbfrange sections
        int start = 0;
        while ((start = cmapText.IndexOf("beginbfrange", start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = cmapText.IndexOf("endbfrange", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;

            var section = cmapText.Substring(start, end - start);
            ParseBfRangeEntries(section, mapping);

            start = end + 10; // Move past "endbfrange"
        }
    }

    /// <summary>
    /// Parse bfrange entries within a section.
    /// </summary>
    private void ParseBfRangeEntries(string section, Dictionary<int, string> mapping)
    {
        // First try array format: <start> <end> [ <u1> <u2> ... ]
        var arrayMatches = BfRangeArrayPattern.Matches(section);
        foreach (Match match in arrayMatches)
        {
            try
            {
                int cidStart = Convert.ToInt32(match.Groups[1].Value, 16);
                int cidEnd = Convert.ToInt32(match.Groups[2].Value, 16);
                var arrayContent = match.Groups[3].Value;

                // Extract all hex strings from the array
                var hexValues = Regex.Matches(arrayContent, @"<([0-9A-Fa-f]+)>");
                int index = 0;
                for (int cid = cidStart; cid <= cidEnd && index < hexValues.Count; cid++, index++)
                {
                    string unicode = HexToUnicodeString(hexValues[index].Groups[1].Value);
                    mapping[cid] = unicode;
                }
            }
            catch
            {
                // Skip invalid entries
            }
        }

        // Then try simple format: <start> <end> <unicodeStart>
        var simpleMatches = BfRangePattern.Matches(section);
        foreach (Match match in simpleMatches)
        {
            try
            {
                int cidStart = Convert.ToInt32(match.Groups[1].Value, 16);
                int cidEnd = Convert.ToInt32(match.Groups[2].Value, 16);
                int unicodeStart = Convert.ToInt32(match.Groups[3].Value, 16);

                // Map range: each CID maps to consecutive Unicode values
                for (int cid = cidStart; cid <= cidEnd; cid++)
                {
                    int unicodeValue = unicodeStart + (cid - cidStart);

                    // Handle surrogate pairs for values > 0xFFFF
                    if (unicodeValue > 0xFFFF)
                    {
                        mapping[cid] = char.ConvertFromUtf32(unicodeValue);
                    }
                    else
                    {
                        mapping[cid] = ((char)unicodeValue).ToString();
                    }
                }
            }
            catch
            {
                // Skip invalid entries
            }
        }
    }

    /// <summary>
    /// Convert a hex string to a Unicode string.
    /// Handles both 2-byte (BMP) and 4-byte (surrogate pair) values.
    /// </summary>
    private static string HexToUnicodeString(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return string.Empty;

        // Pad to even length
        if (hex.Length % 2 != 0)
            hex = "0" + hex;

        var sb = new StringBuilder();

        // Process in 2-byte (4 hex char) chunks for UTF-16
        for (int i = 0; i < hex.Length; i += 4)
        {
            if (i + 4 <= hex.Length)
            {
                int codeUnit = Convert.ToInt32(hex.Substring(i, 4), 16);
                sb.Append((char)codeUnit);
            }
            else if (i + 2 <= hex.Length)
            {
                // Handle leftover 2 chars as single byte
                int codeUnit = Convert.ToInt32(hex.Substring(i, 2), 16);
                sb.Append((char)codeUnit);
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// Provides CID to Unicode mapping using a parsed ToUnicode CMap.
/// </summary>
public class CidToUnicodeMapper
{
    private readonly Dictionary<int, string> _mapping;
    private readonly bool _useIdentity;

    /// <summary>
    /// Create a mapper from a parsed CMap.
    /// </summary>
    /// <param name="mapping">The CID → Unicode mapping.</param>
    public CidToUnicodeMapper(Dictionary<int, string> mapping)
    {
        _mapping = mapping ?? new Dictionary<int, string>();
        _useIdentity = false;
    }

    /// <summary>
    /// Create an identity mapper (CID = Unicode code point).
    /// Used when no ToUnicode CMap is present but font uses Identity-H/V encoding.
    /// </summary>
    public static CidToUnicodeMapper CreateIdentity()
    {
        return new CidToUnicodeMapper(null!) { };
    }

    /// <summary>
    /// Private constructor for identity mapper.
    /// </summary>
    private CidToUnicodeMapper()
    {
        _mapping = new Dictionary<int, string>();
        _useIdentity = true;
    }

    /// <summary>
    /// Map a CID value to its Unicode string.
    /// </summary>
    /// <param name="cid">The Character ID.</param>
    /// <returns>Unicode string, or null if no mapping exists.</returns>
    public string? MapCidToUnicode(int cid)
    {
        if (_useIdentity)
        {
            // Identity mapping: CID = Unicode code point
            if (cid > 0xFFFF)
            {
                return char.ConvertFromUtf32(cid);
            }
            return ((char)cid).ToString();
        }

        return _mapping.TryGetValue(cid, out var unicode) ? unicode : null;
    }

    /// <summary>
    /// Map multiple CID values to a Unicode string.
    /// </summary>
    /// <param name="cids">The CID values.</param>
    /// <returns>Concatenated Unicode string.</returns>
    public string MapCidsToUnicode(IEnumerable<int> cids)
    {
        var sb = new StringBuilder();
        foreach (var cid in cids)
        {
            var unicode = MapCidToUnicode(cid);
            if (unicode != null)
            {
                sb.Append(unicode);
            }
            else
            {
                // Fallback: use replacement character
                sb.Append('\uFFFD');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Check if a mapping exists for a CID.
    /// </summary>
    public bool HasMapping(int cid)
    {
        if (_useIdentity)
            return true;
        return _mapping.ContainsKey(cid);
    }

    /// <summary>
    /// Get the number of mappings.
    /// </summary>
    public int Count => _mapping.Count;

    /// <summary>
    /// Whether this is an identity mapper.
    /// </summary>
    public bool IsIdentity => _useIdentity;
}
