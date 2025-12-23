using System.Text;

namespace PdfEditor.Redaction.ContentStream.Building;

/// <summary>
/// Rebuilds PDF content streams from parsed operations.
/// Used to create the modified content stream after filtering out redacted operations.
/// </summary>
public class ContentStreamBuilder : IContentStreamBuilder
{
    private static readonly Lazy<Encoding> Windows1252Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("Windows-1252");
    });

    /// <summary>
    /// Build a content stream from a list of operations.
    /// </summary>
    public byte[] Build(IEnumerable<PdfOperation> operations)
    {
        var sb = new StringBuilder();

        foreach (var operation in operations.OrderBy(op => op.StreamPosition))
        {
            SerializeOperation(operation, sb);
            sb.Append('\n');
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Build a content stream, excluding operations that intersect with redaction areas.
    /// </summary>
    public byte[] BuildWithRedactions(IEnumerable<PdfOperation> operations, IEnumerable<PdfRectangle> redactionAreas)
    {
        var areas = redactionAreas.ToList();

        // Filter out operations that intersect with any redaction area
        var filteredOps = operations
            .Where(op => !ShouldRedact(op, areas))
            .OrderBy(op => op.StreamPosition);

        return Build(filteredOps);
    }

    private bool ShouldRedact(PdfOperation operation, IReadOnlyList<PdfRectangle> redactionAreas)
    {
        // State operations should never be redacted
        if (operation is StateOperation || operation is TextStateOperation)
            return false;

        // Check if operation intersects with any redaction area
        return redactionAreas.Any(area => operation.IntersectsWith(area));
    }

    private void SerializeOperation(PdfOperation operation, StringBuilder sb)
    {
        // Serialize operands
        foreach (var operand in operation.Operands)
        {
            SerializeOperand(operand, sb);
            sb.Append(' ');
        }

        // Add operator
        sb.Append(operation.Operator);
    }

    private void SerializeOperand(object operand, StringBuilder sb)
    {
        switch (operand)
        {
            case double d:
                SerializeNumber(d, sb);
                break;

            case float f:
                SerializeNumber(f, sb);
                break;

            case int i:
                sb.Append(i);
                break;

            case long l:
                sb.Append(l);
                break;

            case string s when s.StartsWith("/"):
                // Name
                sb.Append(s);
                break;

            case string s:
                // Literal string
                SerializeLiteralString(s, sb);
                break;

            case byte[] bytes:
                // Hex string or literal string from bytes
                SerializeLiteralStringFromBytes(bytes, sb);
                break;

            case List<object> array:
                SerializeArray(array, sb);
                break;

            case object[] array:
                SerializeArray(array.ToList(), sb);
                break;

            default:
                sb.Append(operand?.ToString() ?? "null");
                break;
        }
    }

    private void SerializeNumber(double value, StringBuilder sb)
    {
        // Use integer format if value is a whole number
        if (Math.Abs(value % 1) < 0.0001)
        {
            sb.Append((long)value);
        }
        else
        {
            // Use reasonable precision without trailing zeros
            sb.Append(value.ToString("G10"));
        }
    }

    private void SerializeLiteralString(string text, StringBuilder sb)
    {
        sb.Append('(');

        foreach (char c in text)
        {
            switch (c)
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
                    if (c < 32 || c > 126)
                    {
                        // Octal escape for non-printable characters
                        sb.Append($"\\{Convert.ToString(c, 8).PadLeft(3, '0')}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        sb.Append(')');
    }

    private void SerializeLiteralStringFromBytes(byte[] bytes, StringBuilder sb)
    {
        sb.Append('(');

        foreach (byte b in bytes)
        {
            switch (b)
            {
                case (byte)'(':
                    sb.Append("\\(");
                    break;
                case (byte)')':
                    sb.Append("\\)");
                    break;
                case (byte)'\\':
                    sb.Append("\\\\");
                    break;
                case (byte)'\n':
                    sb.Append("\\n");
                    break;
                case (byte)'\r':
                    sb.Append("\\r");
                    break;
                case (byte)'\t':
                    sb.Append("\\t");
                    break;
                case (byte)'\b':
                    sb.Append("\\b");
                    break;
                case (byte)'\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (b < 32 || b > 126)
                    {
                        // Octal escape for non-printable characters
                        sb.Append($"\\{Convert.ToString(b, 8).PadLeft(3, '0')}");
                    }
                    else
                    {
                        sb.Append((char)b);
                    }
                    break;
            }
        }

        sb.Append(')');
    }

    private void SerializeArray(List<object> array, StringBuilder sb)
    {
        sb.Append('[');

        bool first = true;
        foreach (var element in array)
        {
            if (!first)
                sb.Append(' ');
            first = false;

            SerializeOperand(element, sb);
        }

        sb.Append(']');
    }
}
