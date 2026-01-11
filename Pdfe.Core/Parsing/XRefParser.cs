using System.Globalization;
using System.Text;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Parsing;

/// <summary>
/// Parser for PDF cross-reference tables and streams.
/// ISO 32000-2:2020 Section 7.5.
/// </summary>
public class XRefParser
{
    private readonly Stream _stream;
    private readonly PdfLexer _lexer;

    /// <summary>
    /// Creates a new XRef parser for the specified stream.
    /// </summary>
    public XRefParser(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _lexer = new PdfLexer(stream, ownsStream: false);
    }

    /// <summary>
    /// Find the startxref position by scanning from the end of file.
    /// </summary>
    public long FindStartXRef()
    {
        // Read the last 1024 bytes to find startxref
        const int searchSize = 1024;
        long fileSize = _stream.Length;
        long searchStart = Math.Max(0, fileSize - searchSize);

        _stream.Position = searchStart;
        var buffer = new byte[searchSize];
        int bytesRead = _stream.Read(buffer, 0, (int)(fileSize - searchStart));

        // Search for "startxref" backwards
        string content = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        int idx = content.LastIndexOf("startxref", StringComparison.Ordinal);

        if (idx < 0)
            throw new PdfParseException("Could not find startxref");

        // Parse the position after "startxref"
        int pos = idx + 9; // Length of "startxref"

        // Skip whitespace
        while (pos < content.Length && char.IsWhiteSpace(content[pos]))
            pos++;

        // Read the number
        var sb = new StringBuilder();
        while (pos < content.Length && char.IsDigit(content[pos]))
        {
            sb.Append(content[pos]);
            pos++;
        }

        if (sb.Length == 0)
            throw new PdfParseException("Invalid startxref value");

        return long.Parse(sb.ToString(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parse the cross-reference section at the specified position.
    /// Returns the trailer dictionary and populates the xref table.
    /// </summary>
    public (PdfDictionary Trailer, Dictionary<int, XRefEntry> XRef) ParseXRef(long position)
    {
        _lexer.Seek(position);

        var token = _lexer.NextToken();

        if (token.IsKeyword("xref"))
        {
            // Traditional xref table
            return ParseTraditionalXRef();
        }
        else if (token.Type == PdfTokenType.Integer)
        {
            // XRef stream (PDF 1.5+)
            _lexer.Seek(position);
            return ParseXRefStream();
        }
        else
        {
            throw new PdfParseException($"Expected 'xref' or xref stream at position {position}, got {token.Value}");
        }
    }

    /// <summary>
    /// Parse a traditional cross-reference table.
    /// </summary>
    private (PdfDictionary Trailer, Dictionary<int, XRefEntry> XRef) ParseTraditionalXRef()
    {
        var xref = new Dictionary<int, XRefEntry>();

        // Parse subsections
        while (true)
        {
            var token = _lexer.NextToken();

            if (token.IsKeyword("trailer"))
                break;

            if (token.Type != PdfTokenType.Integer)
                throw new PdfParseException($"Expected subsection start number, got {token.Type}");

            int startObj = int.Parse(token.Value, CultureInfo.InvariantCulture);

            var countToken = _lexer.NextToken();
            if (countToken.Type != PdfTokenType.Integer)
                throw new PdfParseException($"Expected subsection count, got {countToken.Type}");

            int count = int.Parse(countToken.Value, CultureInfo.InvariantCulture);

            // Read entries
            for (int i = 0; i < count; i++)
            {
                var entry = ParseXRefEntry();
                xref[startObj + i] = entry;
            }
        }

        // Parse trailer dictionary
        var parser = new PdfParser(_lexer);
        var trailer = parser.ParseObject() as PdfDictionary
            ?? throw new PdfParseException("Expected trailer dictionary");

        return (trailer, xref);
    }

    /// <summary>
    /// Parse a single xref entry (20 bytes: offset gen status).
    /// </summary>
    private XRefEntry ParseXRefEntry()
    {
        // Each entry is exactly 20 bytes: 10 digits offset, space, 5 digits gen, space, f/n, EOL
        // But we'll use the lexer for robustness

        var offsetToken = _lexer.NextToken();
        if (offsetToken.Type != PdfTokenType.Integer)
            throw new PdfParseException($"Expected offset in xref entry, got {offsetToken.Type}");

        var genToken = _lexer.NextToken();
        if (genToken.Type != PdfTokenType.Integer)
            throw new PdfParseException($"Expected generation in xref entry, got {genToken.Type}");

        var statusToken = _lexer.NextToken();
        if (statusToken.Type != PdfTokenType.Keyword || (statusToken.Value != "n" && statusToken.Value != "f"))
            throw new PdfParseException($"Expected 'n' or 'f' in xref entry, got '{statusToken.Value}'");

        return new XRefEntry
        {
            Offset = long.Parse(offsetToken.Value, CultureInfo.InvariantCulture),
            Generation = int.Parse(genToken.Value, CultureInfo.InvariantCulture),
            InUse = statusToken.Value == "n"
        };
    }

    /// <summary>
    /// Parse a cross-reference stream (PDF 1.5+).
    /// </summary>
    private (PdfDictionary Trailer, Dictionary<int, XRefEntry> XRef) ParseXRefStream()
    {
        var parser = new PdfParser(_lexer);
        var indirectObj = parser.ParseIndirectObject();

        if (indirectObj.Value is not PdfStream stream)
            throw new PdfParseException("Expected xref stream");

        if (stream.GetNameOrNull("Type") != "XRef")
            throw new PdfParseException("Stream is not an XRef stream");

        // Decode the stream
        var decompressor = new StreamDecompressor();
        decompressor.Decompress(stream);

        var data = stream.DecodedData;

        // Get W array (field widths)
        var wArray = stream.GetArray("W");
        if (wArray.Count != 3)
            throw new PdfParseException("XRef stream /W array must have 3 elements");

        int w1 = wArray.GetInt(0); // Type field width
        int w2 = wArray.GetInt(1); // Field 2 width
        int w3 = wArray.GetInt(2); // Field 3 width
        int entrySize = w1 + w2 + w3;

        // Get Index array (subsections)
        var indexArray = stream.GetArrayOrNull("Index");
        var subsections = new List<(int Start, int Count)>();

        if (indexArray != null)
        {
            for (int i = 0; i < indexArray.Count; i += 2)
            {
                subsections.Add((indexArray.GetInt(i), indexArray.GetInt(i + 1)));
            }
        }
        else
        {
            // Default: single section from 0 to Size
            int size = stream.GetInt("Size");
            subsections.Add((0, size));
        }

        // Parse entries
        var xref = new Dictionary<int, XRefEntry>();
        int dataPos = 0;

        foreach (var (start, count) in subsections)
        {
            for (int i = 0; i < count; i++)
            {
                if (dataPos + entrySize > data.Length)
                    throw new PdfParseException("XRef stream data too short");

                // Read fields
                int type = w1 > 0 ? ReadBigEndianInt(data, dataPos, w1) : 1; // Default type is 1
                dataPos += w1;

                long field2 = w2 > 0 ? ReadBigEndianLong(data, dataPos, w2) : 0;
                dataPos += w2;

                int field3 = w3 > 0 ? ReadBigEndianInt(data, dataPos, w3) : 0;
                dataPos += w3;

                var entry = type switch
                {
                    0 => new XRefEntry { InUse = false, Offset = 0, Generation = field3 }, // Free entry
                    1 => new XRefEntry { InUse = true, Offset = field2, Generation = field3 }, // In use
                    2 => new XRefEntry { InUse = true, ObjectStreamNumber = (int)field2, IndexInStream = field3 }, // Compressed
                    _ => throw new PdfParseException($"Unknown xref entry type: {type}")
                };

                xref[start + i] = entry;
            }
        }

        // The stream dictionary is also the trailer
        return (stream, xref);
    }

    private static int ReadBigEndianInt(byte[] data, int offset, int width)
    {
        int result = 0;
        for (int i = 0; i < width; i++)
        {
            result = (result << 8) | data[offset + i];
        }
        return result;
    }

    private static long ReadBigEndianLong(byte[] data, int offset, int width)
    {
        long result = 0;
        for (int i = 0; i < width; i++)
        {
            result = (result << 8) | data[offset + i];
        }
        return result;
    }
}

/// <summary>
/// Entry in the cross-reference table.
/// </summary>
public class XRefEntry
{
    /// <summary>
    /// Byte offset of the object in the file (for uncompressed objects).
    /// </summary>
    public long Offset { get; init; }

    /// <summary>
    /// Generation number of the object.
    /// </summary>
    public int Generation { get; init; }

    /// <summary>
    /// Whether this object is in use (true) or free (false).
    /// </summary>
    public bool InUse { get; init; }

    /// <summary>
    /// For compressed objects: the object number of the object stream containing this object.
    /// </summary>
    public int? ObjectStreamNumber { get; init; }

    /// <summary>
    /// For compressed objects: the index of this object within the object stream.
    /// </summary>
    public int? IndexInStream { get; init; }

    /// <summary>
    /// Whether this object is stored in an object stream.
    /// </summary>
    public bool IsCompressed => ObjectStreamNumber.HasValue;

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsCompressed)
            return $"Compressed in obj {ObjectStreamNumber} index {IndexInStream}";
        return $"Offset {Offset} gen {Generation} {(InUse ? "n" : "f")}";
    }
}
