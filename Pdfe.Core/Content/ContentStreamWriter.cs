using System.Globalization;
using System.Text;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Content;

/// <summary>
/// Serializes a ContentStream to PDF content stream bytes.
/// ISO 32000-2:2020 Section 7.8.2.
/// </summary>
public class ContentStreamWriter
{
    private readonly StringBuilder _sb = new();

    /// <summary>
    /// Write a ContentStream to bytes.
    /// </summary>
    public byte[] Write(ContentStream content)
    {
        _sb.Clear();

        foreach (var op in content.Operators)
        {
            WriteOperator(op);
        }

        return Encoding.Latin1.GetBytes(_sb.ToString());
    }

    /// <summary>
    /// Write a single operator.
    /// </summary>
    private void WriteOperator(ContentOperator op)
    {
        // Write operands
        foreach (var operand in op.Operands)
        {
            WriteOperand(operand);
            _sb.Append(' ');
        }

        // Write operator name
        _sb.Append(op.Name);
        _sb.Append('\n');
    }

    /// <summary>
    /// Write an operand value.
    /// </summary>
    private void WriteOperand(PdfObject obj)
    {
        switch (obj)
        {
            case PdfNull:
                _sb.Append("null");
                break;

            case PdfBoolean b:
                _sb.Append(b.Value ? "true" : "false");
                break;

            case PdfInteger i:
                _sb.Append(i.Value);
                break;

            case PdfReal r:
                // Format with enough precision but without unnecessary decimals
                var value = r.Value;
                if (Math.Abs(value - Math.Round(value)) < 0.00001)
                {
                    _sb.Append(((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    _sb.Append(value.ToString("G", CultureInfo.InvariantCulture));
                }
                break;

            case PdfString s:
                WriteString(s.Value);
                break;

            case PdfName n:
                _sb.Append('/');
                WriteName(n.Value);
                break;

            case PdfArray a:
                _sb.Append('[');
                for (int i = 0; i < a.Count; i++)
                {
                    if (i > 0) _sb.Append(' ');
                    WriteOperand(a[i]);
                }
                _sb.Append(']');
                break;

            case PdfDictionary d:
                _sb.Append("<<");
                foreach (var kvp in d)
                {
                    _sb.Append('/');
                    WriteName(kvp.Key.Value);
                    _sb.Append(' ');
                    WriteOperand(kvp.Value);
                    _sb.Append(' ');
                }
                _sb.Append(">>");
                break;

            default:
                // Unknown type - try ToString
                _sb.Append(obj.ToString());
                break;
        }
    }

    /// <summary>
    /// Write a string literal with proper escaping.
    /// </summary>
    private void WriteString(string value)
    {
        _sb.Append('(');

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    _sb.Append("\\\\");
                    break;
                case '(':
                    _sb.Append("\\(");
                    break;
                case ')':
                    _sb.Append("\\)");
                    break;
                case '\n':
                    _sb.Append("\\n");
                    break;
                case '\r':
                    _sb.Append("\\r");
                    break;
                case '\t':
                    _sb.Append("\\t");
                    break;
                case '\b':
                    _sb.Append("\\b");
                    break;
                case '\f':
                    _sb.Append("\\f");
                    break;
                default:
                    if (c < 32 || c > 126)
                    {
                        // Write as octal escape
                        _sb.Append('\\');
                        _sb.Append(Convert.ToString(c, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        _sb.Append(c);
                    }
                    break;
            }
        }

        _sb.Append(')');
    }

    /// <summary>
    /// Write a name with proper encoding.
    /// </summary>
    private void WriteName(string name)
    {
        foreach (var c in name)
        {
            if (c < 33 || c > 126 || c == '#' || c == '/' || c == '[' || c == ']' ||
                c == '<' || c == '>' || c == '(' || c == ')' || c == '{' || c == '}' ||
                c == '%')
            {
                // Write as hex escape
                _sb.Append('#');
                _sb.Append(((int)c).ToString("X2"));
            }
            else
            {
                _sb.Append(c);
            }
        }
    }
}
