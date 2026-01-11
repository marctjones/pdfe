using System.Text;
using System.Text.RegularExpressions;

namespace Pdfe.Core.Text;

/// <summary>
/// Parses ToUnicode CMap streams to build a character code to Unicode mapping.
/// </summary>
public class ToUnicodeCMapParser
{
    private readonly Dictionary<int, string> _bfCharMappings = new();
    private readonly List<(int start, int end, int unicodeStart)> _bfRangeMappings = new();
    private readonly List<(int start, int end, string[] unicodeArray)> _bfRangeArrayMappings = new();

    /// <summary>
    /// Parse a ToUnicode CMap stream and build the mapping dictionary.
    /// </summary>
    public static Dictionary<int, string> Parse(byte[] cmapData)
    {
        var parser = new ToUnicodeCMapParser();
        var content = Encoding.UTF8.GetString(cmapData);
        parser.ParseContent(content);
        return parser.BuildMapping();
    }

    /// <summary>
    /// Parse a ToUnicode CMap stream and build the mapping dictionary.
    /// </summary>
    public static Dictionary<int, string> Parse(string cmapContent)
    {
        var parser = new ToUnicodeCMapParser();
        parser.ParseContent(cmapContent);
        return parser.BuildMapping();
    }

    private void ParseContent(string content)
    {
        // Parse bfchar mappings: <srcCode> <dstString>
        ParseBfChar(content);

        // Parse bfrange mappings: <srcCodeLo> <srcCodeHi> <dstStringLo>
        // or <srcCodeLo> <srcCodeHi> [<dstString1> <dstString2> ...]
        ParseBfRange(content);
    }

    private void ParseBfChar(string content)
    {
        // Match: N beginbfchar ... endbfchar
        var bfCharBlockPattern = new Regex(@"(\d+)\s+beginbfchar\s*(.*?)\s*endbfchar",
            RegexOptions.Singleline);

        foreach (Match block in bfCharBlockPattern.Matches(content))
        {
            var mappings = block.Groups[2].Value;
            ParseBfCharMappings(mappings);
        }
    }

    private void ParseBfCharMappings(string mappings)
    {
        // Match pairs: <srcCode> <dstCode>
        var pairPattern = new Regex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>");

        foreach (Match match in pairPattern.Matches(mappings))
        {
            var srcHex = match.Groups[1].Value;
            var dstHex = match.Groups[2].Value;

            var srcCode = Convert.ToInt32(srcHex, 16);
            var dstUnicode = HexToUnicodeString(dstHex);

            _bfCharMappings[srcCode] = dstUnicode;
        }
    }

    private void ParseBfRange(string content)
    {
        // Match: N beginbfrange ... endbfrange
        var bfRangeBlockPattern = new Regex(@"(\d+)\s+beginbfrange\s*(.*?)\s*endbfrange",
            RegexOptions.Singleline);

        foreach (Match block in bfRangeBlockPattern.Matches(content))
        {
            var mappings = block.Groups[2].Value;
            ParseBfRangeMappings(mappings);
        }
    }

    private void ParseBfRangeMappings(string mappings)
    {
        // Match: <srcLo> <srcHi> <dstLo> (simple range)
        var simpleRangePattern = new Regex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>");

        foreach (Match match in simpleRangePattern.Matches(mappings))
        {
            var srcLoHex = match.Groups[1].Value;
            var srcHiHex = match.Groups[2].Value;
            var dstLoHex = match.Groups[3].Value;

            var srcLo = Convert.ToInt32(srcLoHex, 16);
            var srcHi = Convert.ToInt32(srcHiHex, 16);
            var dstLo = Convert.ToInt32(dstLoHex, 16);

            _bfRangeMappings.Add((srcLo, srcHi, dstLo));
        }

        // Match: <srcLo> <srcHi> [<dst1> <dst2> ...] (array range)
        var arrayRangePattern = new Regex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[(.*?)\]",
            RegexOptions.Singleline);

        foreach (Match match in arrayRangePattern.Matches(mappings))
        {
            var srcLoHex = match.Groups[1].Value;
            var srcHiHex = match.Groups[2].Value;
            var arrayContent = match.Groups[3].Value;

            var srcLo = Convert.ToInt32(srcLoHex, 16);
            var srcHi = Convert.ToInt32(srcHiHex, 16);

            // Parse the array of hex strings
            var arrayItemPattern = new Regex(@"<([0-9A-Fa-f]+)>");
            var items = new List<string>();
            foreach (Match item in arrayItemPattern.Matches(arrayContent))
            {
                items.Add(HexToUnicodeString(item.Groups[1].Value));
            }

            if (items.Count > 0)
            {
                _bfRangeArrayMappings.Add((srcLo, srcHi, items.ToArray()));
            }
        }
    }

    private static string HexToUnicodeString(string hex)
    {
        // Hex string represents UTF-16BE code units
        // Each pair of hex digits is one byte
        if (hex.Length <= 4)
        {
            // Single code point
            var codePoint = Convert.ToInt32(hex, 16);
            return char.ConvertFromUtf32(codePoint);
        }

        // Multiple code units (UTF-16BE)
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private Dictionary<int, string> BuildMapping()
    {
        var result = new Dictionary<int, string>();

        // Add bfchar mappings
        foreach (var kvp in _bfCharMappings)
        {
            result[kvp.Key] = kvp.Value;
        }

        // Add simple bfrange mappings
        foreach (var (start, end, unicodeStart) in _bfRangeMappings)
        {
            for (int srcCode = start; srcCode <= end; srcCode++)
            {
                var dstCode = unicodeStart + (srcCode - start);
                result[srcCode] = char.ConvertFromUtf32(dstCode);
            }
        }

        // Add array bfrange mappings
        foreach (var (start, end, unicodeArray) in _bfRangeArrayMappings)
        {
            for (int srcCode = start; srcCode <= end && (srcCode - start) < unicodeArray.Length; srcCode++)
            {
                result[srcCode] = unicodeArray[srcCode - start];
            }
        }

        return result;
    }
}
