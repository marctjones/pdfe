using System.Text;

namespace Pdfe.Core.Primitives;

/// <summary>
/// PDF string object. Strings can be literal (parentheses) or hexadecimal (angle brackets).
/// ISO 32000-2:2020 Section 7.3.4.
/// </summary>
public sealed class PdfString : PdfObject, IEquatable<PdfString>
{
    /// <summary>
    /// The raw bytes of the string.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// Whether this string was originally a hex string.
    /// </summary>
    public bool IsHex { get; }

    /// <summary>
    /// Creates a new PDF string from bytes.
    /// </summary>
    public PdfString(byte[] bytes, bool isHex = false)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        IsHex = isHex;
    }

    /// <summary>
    /// Creates a new PDF string from a .NET string using PDFDocEncoding.
    /// </summary>
    public PdfString(string text, bool isHex = false)
    {
        // Use PDFDocEncoding for simple strings, or UTF-16BE with BOM for Unicode
        bool hasNonLatin = text.Any(c => c > 255);
        if (hasNonLatin)
        {
            // UTF-16BE with BOM
            var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
            Bytes = new byte[2 + utf16.Length];
            Bytes[0] = 0xFE; // BOM
            Bytes[1] = 0xFF;
            Array.Copy(utf16, 0, Bytes, 2, utf16.Length);
        }
        else
        {
            // PDFDocEncoding (ISO-8859-1 compatible for 0-255)
            Bytes = text.Select(c => (byte)c).ToArray();
        }
        IsHex = isHex;
    }

    /// <inheritdoc />
    public override PdfObjectType ObjectType => PdfObjectType.String;

    /// <summary>
    /// Gets the string value as decoded text.
    /// </summary>
    public string Value => DecodeToString();

    /// <summary>
    /// Decode the bytes to a .NET string.
    /// </summary>
    private string DecodeToString()
    {
        if (Bytes.Length == 0)
            return string.Empty;

        // Check for UTF-16BE BOM
        if (Bytes.Length >= 2 && Bytes[0] == 0xFE && Bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2);
        }

        // Check for UTF-8 BOM
        if (Bytes.Length >= 3 && Bytes[0] == 0xEF && Bytes[1] == 0xBB && Bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(Bytes, 3, Bytes.Length - 3);
        }

        // PDFDocEncoding (compatible with ISO-8859-1 for printable range)
        return DecodePdfDocEncoding(Bytes);
    }

    /// <summary>
    /// Decode bytes using PDFDocEncoding.
    /// </summary>
    private static string DecodePdfDocEncoding(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes)
        {
            // PDFDocEncoding maps 0-127 to ASCII, 128-255 to specific characters
            // For simplicity, we use ISO-8859-1 which is close enough for most cases
            // A full implementation would have a mapping table for 128-159
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Encode for writing as a literal string (parentheses).
    /// </summary>
    public string ToLiteralString()
    {
        var sb = new StringBuilder("(");
        foreach (byte b in Bytes)
        {
            switch ((char)b)
            {
                case '(':
                    sb.Append("\\(");
                    break;
                case ')':
                    sb.Append("\\)");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (b < 32 || b > 126)
                    {
                        // Octal escape for non-printable
                        sb.Append('\\');
                        sb.Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        sb.Append((char)b);
                    }
                    break;
            }
        }
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// Encode for writing as a hex string (angle brackets).
    /// </summary>
    public string ToHexString()
    {
        var sb = new StringBuilder("<");
        foreach (byte b in Bytes)
        {
            sb.Append(b.ToString("X2"));
        }
        sb.Append('>');
        return sb.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => IsHex ? ToHexString() : ToLiteralString();

    /// <summary>
    /// Create a PdfString from a hex-encoded string (without angle brackets).
    /// </summary>
    public static PdfString FromHex(string hex)
    {
        // Remove whitespace
        hex = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Odd-length hex strings are padded with 0
        if (hex.Length % 2 != 0)
            hex += "0";

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return new PdfString(bytes, isHex: true);
    }

    /// <summary>
    /// Implicit conversion from string.
    /// </summary>
    public static implicit operator PdfString(string s) => new(s);

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(PdfString s) => s.Value;

    /// <inheritdoc />
    public bool Equals(PdfString? other) =>
        other is not null && Bytes.SequenceEqual(other.Bytes);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PdfString other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (byte b in Bytes)
                hash = hash * 31 + b;
            return hash;
        }
    }
}
