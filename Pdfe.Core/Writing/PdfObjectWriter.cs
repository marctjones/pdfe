using System.Globalization;
using System.Text;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Writing;

/// <summary>
/// Serializes PDF objects to their text representation.
/// </summary>
public static class PdfObjectWriter
{
    /// <summary>
    /// Serialize a PDF object to its string representation.
    /// </summary>
    public static string Serialize(PdfObject obj)
    {
        var sb = new StringBuilder();
        SerializeObject(obj, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Write a PDF object to a StringBuilder.
    /// </summary>
    public static void SerializeObject(PdfObject obj, StringBuilder sb)
    {
        switch (obj)
        {
            case PdfNull:
                sb.Append("null");
                break;

            case PdfBoolean b:
                sb.Append(b.Value ? "true" : "false");
                break;

            case PdfInteger i:
                sb.Append(i.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case PdfReal r:
                var value = r.Value;
                // Write integers without decimal point
                if (Math.Abs(value - Math.Round(value)) < 1e-10)
                    sb.Append(((long)value).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
                break;

            case PdfName n:
                SerializeName(n, sb);
                break;

            case PdfString s:
                SerializeString(s, sb);
                break;

            case PdfArray a:
                SerializeArray(a, sb);
                break;

            case PdfDictionary d:
                SerializeDictionary(d, sb);
                break;

            case PdfReference r:
                sb.Append($"{r.ObjectNum} {r.Generation} R");
                break;

            default:
                throw new ArgumentException($"Unknown PDF object type: {obj.GetType().Name}");
        }
    }

    /// <summary>
    /// Serialize a stream object (dictionary part only, data handled separately).
    /// </summary>
    public static void SerializeStreamDictionary(PdfStream stream, StringBuilder sb)
    {
        // Write dictionary entries (but not the stream data)
        sb.Append("<<");
        foreach (var kvp in stream)
        {
            sb.Append(' ');
            SerializeName(kvp.Key, sb);
            sb.Append(' ');
            SerializeObject(kvp.Value, sb);
        }
        sb.Append(" >>");
    }

    private static void SerializeName(PdfName name, StringBuilder sb)
    {
        sb.Append('/');
        foreach (char c in name.Value)
        {
            // Characters that need to be escaped with #XX
            if (c < 33 || c > 126 || c == '#' || c == '/' || c == '%' ||
                c == '(' || c == ')' || c == '<' || c == '>' ||
                c == '[' || c == ']' || c == '{' || c == '}')
            {
                sb.Append('#');
                sb.Append(((int)c).ToString("X2"));
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    private static void SerializeString(PdfString str, StringBuilder sb)
    {
        if (str.IsHex)
        {
            sb.Append('<');
            foreach (byte b in str.Bytes)
                sb.Append(b.ToString("X2"));
            sb.Append('>');
        }
        else
        {
            sb.Append('(');
            foreach (byte b in str.Bytes)
            {
                char c = (char)b;
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '(':
                        sb.Append("\\(");
                        break;
                    case ')':
                        sb.Append("\\)");
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
                    default:
                        if (b < 32 || b > 126)
                            sb.Append($"\\{Convert.ToString(b, 8).PadLeft(3, '0')}");
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append(')');
        }
    }

    private static void SerializeArray(PdfArray array, StringBuilder sb)
    {
        sb.Append('[');
        for (int i = 0; i < array.Count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            SerializeObject(array[i], sb);
        }
        sb.Append(']');
    }

    private static void SerializeDictionary(PdfDictionary dict, StringBuilder sb)
    {
        sb.Append("<<");
        foreach (var kvp in dict)
        {
            sb.Append(' ');
            SerializeName(kvp.Key, sb);
            sb.Append(' ');
            SerializeObject(kvp.Value, sb);
        }
        sb.Append(" >>");
    }
}
