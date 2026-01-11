using System.IO.Compression;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Parsing;

/// <summary>
/// Decompresses PDF streams by applying filters.
/// ISO 32000-2:2020 Section 7.4.
/// </summary>
public class StreamDecompressor
{
    /// <summary>
    /// Decompress a stream in place, setting its DecodedData property.
    /// </summary>
    public void Decompress(PdfStream stream)
    {
        var data = stream.EncodedData;
        var filters = stream.Filters;
        var parms = stream.DecodeParams;

        // Apply filters in order
        for (int i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var filterParms = i < parms.Count ? parms[i] : null;

            data = ApplyFilter(filter, data, filterParms);
        }

        stream.SetDecodedData(data);
    }

    /// <summary>
    /// Apply a single filter to data.
    /// </summary>
    public byte[] ApplyFilter(string filterName, byte[] data, PdfDictionary? parms)
    {
        return filterName switch
        {
            "FlateDecode" or "Fl" => DecodeFlateDecode(data, parms),
            "ASCIIHexDecode" or "AHx" => DecodeASCIIHex(data),
            "ASCII85Decode" or "A85" => DecodeASCII85(data),
            "LZWDecode" or "LZW" => DecodeLZW(data, parms),
            "RunLengthDecode" or "RL" => DecodeRunLength(data),
            "DCTDecode" or "DCT" => data, // JPEG - pass through
            "JPXDecode" => data, // JPEG2000 - pass through
            "CCITTFaxDecode" or "CCF" => data, // Fax - pass through for now
            "JBIG2Decode" => data, // JBIG2 - pass through for now
            "Crypt" => data, // Encryption handled separately
            _ => throw new NotSupportedException($"Unknown filter: {filterName}")
        };
    }

    /// <summary>
    /// Decode FlateDecode (zlib/deflate compression).
    /// </summary>
    private byte[] DecodeFlateDecode(byte[] data, PdfDictionary? parms)
    {
        // Skip zlib header if present (2 bytes: CMF, FLG)
        int offset = 0;
        if (data.Length >= 2)
        {
            int cmf = data[0];
            int flg = data[1];
            // Check for valid zlib header
            if ((cmf * 256 + flg) % 31 == 0)
            {
                int cm = cmf & 0x0F;
                if (cm == 8) // Deflate
                {
                    offset = 2;
                    // Check for dictionary
                    if ((flg & 0x20) != 0)
                        offset += 4;
                }
            }
        }

        using var input = new MemoryStream(data, offset, data.Length - offset);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        deflate.CopyTo(output);
        var decompressed = output.ToArray();

        // Apply predictor if specified
        if (parms != null && parms.ContainsKey("Predictor"))
        {
            int predictor = parms.GetInt("Predictor", 1);
            if (predictor > 1)
            {
                decompressed = ApplyPredictor(decompressed, parms);
            }
        }

        return decompressed;
    }

    /// <summary>
    /// Apply PNG predictor to decompressed data.
    /// </summary>
    private byte[] ApplyPredictor(byte[] data, PdfDictionary parms)
    {
        int predictor = parms.GetInt("Predictor", 1);
        int colors = parms.GetInt("Colors", 1);
        int bitsPerComponent = parms.GetInt("BitsPerComponent", 8);
        int columns = parms.GetInt("Columns", 1);

        // Calculate bytes per row
        int bytesPerPixel = (colors * bitsPerComponent + 7) / 8;
        int rowBytes = (colors * columns * bitsPerComponent + 7) / 8;

        if (predictor == 2)
        {
            // TIFF Predictor 2
            return ApplyTiffPredictor(data, colors, columns, bitsPerComponent);
        }
        else if (predictor >= 10 && predictor <= 15)
        {
            // PNG predictors
            return ApplyPngPredictor(data, rowBytes, bytesPerPixel);
        }

        return data;
    }

    /// <summary>
    /// Apply PNG predictor filters.
    /// </summary>
    private byte[] ApplyPngPredictor(byte[] data, int rowBytes, int bytesPerPixel)
    {
        // PNG predictor: each row starts with a filter byte
        int rowStride = rowBytes + 1; // +1 for filter byte
        int rows = data.Length / rowStride;

        var output = new byte[rows * rowBytes];
        var prevRow = new byte[rowBytes];

        for (int row = 0; row < rows; row++)
        {
            int srcOffset = row * rowStride;
            int dstOffset = row * rowBytes;

            int filter = data[srcOffset];
            var currentRow = new byte[rowBytes];

            for (int i = 0; i < rowBytes; i++)
            {
                byte raw = data[srcOffset + 1 + i];
                byte left = i >= bytesPerPixel ? currentRow[i - bytesPerPixel] : (byte)0;
                byte up = prevRow[i];
                byte upLeft = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : (byte)0;

                currentRow[i] = filter switch
                {
                    0 => raw, // None
                    1 => (byte)(raw + left), // Sub
                    2 => (byte)(raw + up), // Up
                    3 => (byte)(raw + (left + up) / 2), // Average
                    4 => (byte)(raw + PaethPredictor(left, up, upLeft)), // Paeth
                    _ => raw
                };

                output[dstOffset + i] = currentRow[i];
            }

            Array.Copy(currentRow, prevRow, rowBytes);
        }

        return output;
    }

    /// <summary>
    /// Paeth predictor function used in PNG filtering.
    /// </summary>
    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;
        if (pb <= pc)
            return b;
        return c;
    }

    /// <summary>
    /// Apply TIFF Predictor 2 (horizontal differencing).
    /// </summary>
    private byte[] ApplyTiffPredictor(byte[] data, int colors, int columns, int bitsPerComponent)
    {
        if (bitsPerComponent != 8)
            return data; // Only support 8-bit for now

        int bytesPerRow = colors * columns;
        int rows = data.Length / bytesPerRow;
        var output = new byte[data.Length];

        for (int row = 0; row < rows; row++)
        {
            int rowOffset = row * bytesPerRow;

            for (int col = 0; col < columns; col++)
            {
                for (int comp = 0; comp < colors; comp++)
                {
                    int idx = rowOffset + col * colors + comp;
                    if (col == 0)
                    {
                        output[idx] = data[idx];
                    }
                    else
                    {
                        int prevIdx = rowOffset + (col - 1) * colors + comp;
                        output[idx] = (byte)(data[idx] + output[prevIdx]);
                    }
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Decode ASCIIHexDecode (hex encoding).
    /// </summary>
    private byte[] DecodeASCIIHex(byte[] data)
    {
        var output = new List<byte>();
        int highNibble = -1;

        foreach (byte b in data)
        {
            if (b == '>') // End of data
                break;

            if (char.IsWhiteSpace((char)b))
                continue;

            int nibble = HexValue((char)b);
            if (nibble < 0)
                throw new PdfParseException($"Invalid hex digit in ASCIIHexDecode: {(char)b}");

            if (highNibble < 0)
            {
                highNibble = nibble;
            }
            else
            {
                output.Add((byte)((highNibble << 4) | nibble));
                highNibble = -1;
            }
        }

        // Handle odd number of digits
        if (highNibble >= 0)
        {
            output.Add((byte)(highNibble << 4));
        }

        return output.ToArray();
    }

    /// <summary>
    /// Decode ASCII85Decode (base-85 encoding).
    /// </summary>
    private byte[] DecodeASCII85(byte[] data)
    {
        var output = new List<byte>();
        var group = new List<int>();

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];

            // Skip whitespace
            if (char.IsWhiteSpace((char)b))
                continue;

            // End of data
            if (b == '~' && i + 1 < data.Length && data[i + 1] == '>')
                break;

            // Special case: 'z' represents four zero bytes
            if (b == 'z')
            {
                if (group.Count > 0)
                    throw new PdfParseException("Invalid 'z' in ASCII85 group");

                output.AddRange(new byte[4]);
                continue;
            }

            if (b < 33 || b > 117)
                throw new PdfParseException($"Invalid character in ASCII85: {(char)b}");

            group.Add(b - 33);

            if (group.Count == 5)
            {
                DecodeASCII85Group(group, output, 4);
                group.Clear();
            }
        }

        // Handle partial group at end
        if (group.Count > 0)
        {
            while (group.Count < 5)
                group.Add(84); // Pad with 'u' (highest value)

            int outBytes = group.Count - 1; // Partial group produces fewer bytes
            DecodeASCII85Group(group, output, outBytes);
        }

        return output.ToArray();
    }

    private void DecodeASCII85Group(List<int> group, List<byte> output, int numBytes)
    {
        long value = 0;
        for (int i = 0; i < 5; i++)
        {
            value = value * 85 + group[i];
        }

        // Extract bytes (big-endian)
        var bytes = new byte[4];
        for (int i = 3; i >= 0; i--)
        {
            bytes[i] = (byte)(value & 0xFF);
            value >>= 8;
        }

        output.AddRange(bytes.Take(numBytes));
    }

    /// <summary>
    /// Decode LZWDecode compression.
    /// </summary>
    private byte[] DecodeLZW(byte[] data, PdfDictionary? parms)
    {
        // LZW implementation
        var output = new List<byte>();
        var table = new Dictionary<int, byte[]>();

        // Initialize table with single-byte codes
        for (int i = 0; i < 256; i++)
        {
            table[i] = new[] { (byte)i };
        }

        int clearCode = 256;
        int eoiCode = 257;
        int nextCode = 258;
        int codeSize = 9;

        int bitPos = 0;
        int prevCode = -1;

        while (bitPos + codeSize <= data.Length * 8)
        {
            int code = ReadBits(data, bitPos, codeSize);
            bitPos += codeSize;

            if (code == eoiCode)
                break;

            if (code == clearCode)
            {
                // Reset table
                table.Clear();
                for (int i = 0; i < 256; i++)
                {
                    table[i] = new[] { (byte)i };
                }
                nextCode = 258;
                codeSize = 9;
                prevCode = -1;
                continue;
            }

            byte[] entry;
            if (table.TryGetValue(code, out var existing))
            {
                entry = existing;
            }
            else if (code == nextCode && prevCode >= 0)
            {
                // Special case: code not in table yet
                var prev = table[prevCode];
                entry = new byte[prev.Length + 1];
                Array.Copy(prev, entry, prev.Length);
                entry[prev.Length] = prev[0];
            }
            else
            {
                throw new PdfParseException($"Invalid LZW code: {code}");
            }

            output.AddRange(entry);

            // Add new entry to table
            if (prevCode >= 0 && nextCode < 4096)
            {
                var prev = table[prevCode];
                var newEntry = new byte[prev.Length + 1];
                Array.Copy(prev, newEntry, prev.Length);
                newEntry[prev.Length] = entry[0];
                table[nextCode] = newEntry;
                nextCode++;

                // Increase code size if needed
                if (nextCode >= (1 << codeSize) && codeSize < 12)
                    codeSize++;
            }

            prevCode = code;
        }

        var result = output.ToArray();

        // Apply predictor if specified
        if (parms != null && parms.ContainsKey("Predictor"))
        {
            int predictor = parms.GetInt("Predictor", 1);
            if (predictor > 1)
            {
                result = ApplyPredictor(result, parms);
            }
        }

        return result;
    }

    private static int ReadBits(byte[] data, int bitPos, int numBits)
    {
        int result = 0;
        for (int i = 0; i < numBits; i++)
        {
            int byteIdx = (bitPos + i) / 8;
            int bitIdx = 7 - ((bitPos + i) % 8);
            if (byteIdx < data.Length)
            {
                if ((data[byteIdx] & (1 << bitIdx)) != 0)
                {
                    result |= (1 << (numBits - 1 - i));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Decode RunLengthDecode compression.
    /// </summary>
    private byte[] DecodeRunLength(byte[] data)
    {
        var output = new List<byte>();
        int i = 0;

        while (i < data.Length)
        {
            int length = data[i++];

            if (length == 128) // EOD
                break;

            if (length < 128)
            {
                // Copy next (length + 1) bytes literally
                int count = length + 1;
                for (int j = 0; j < count && i < data.Length; j++)
                {
                    output.Add(data[i++]);
                }
            }
            else
            {
                // Repeat next byte (257 - length) times
                int count = 257 - length;
                if (i < data.Length)
                {
                    byte b = data[i++];
                    for (int j = 0; j < count; j++)
                    {
                        output.Add(b);
                    }
                }
            }
        }

        return output.ToArray();
    }

    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return -1;
    }
}
