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
    /// This method ensures font state is properly maintained across BT/ET blocks.
    /// Issue #167: Each BT block that contains Tj must have a Tf operator.
    /// </summary>
    public byte[] Build(IEnumerable<PdfOperation> operations)
    {
        var sb = new StringBuilder();
        var operationList = operations.OrderBy(op => op.StreamPosition).ToList();

        // Track font state for injection into BT blocks
        string? lastFontName = null;
        double lastFontSize = 12.0;

        // First pass: collect font info from all Tf operators and TextOperations
        foreach (var op in operationList)
        {
            if (op is TextStateOperation tso && tso.Operator == "Tf" && tso.Operands.Count >= 2)
            {
                lastFontName = tso.Operands[0]?.ToString();
                if (tso.Operands[1] is double d) lastFontSize = d;
                else if (tso.Operands[1] is int i) lastFontSize = i;
                else if (tso.Operands[1] is float f) lastFontSize = f;
            }
            else if (op is TextOperation textOp)
            {
                // TextOperations from parsing have font info - use as fallback
                if (!string.IsNullOrEmpty(textOp.FontName))
                {
                    lastFontName = textOp.FontName;
                    if (textOp.FontSize > 0) lastFontSize = textOp.FontSize;
                }
            }
        }

        // Now we have the font info. Second pass: serialize with Tf injection
        string? currentBlockFontName = null;
        double currentBlockFontSize = 12.0;
        bool inTextBlock = false;
        bool needTfInjection = false;

        foreach (var operation in operationList)
        {
            // Track font state from Tf operators
            if (operation is TextStateOperation tso && tso.Operator == "Tf" && tso.Operands.Count >= 2)
            {
                currentBlockFontName = tso.Operands[0]?.ToString();
                if (tso.Operands[1] is double d) currentBlockFontSize = d;
                else if (tso.Operands[1] is int i) currentBlockFontSize = i;
                else if (tso.Operands[1] is float f) currentBlockFontSize = f;
                needTfInjection = false; // We have a Tf now
            }

            // Detect BT (begin text block)
            if (operation.Operator == "BT")
            {
                inTextBlock = true;
                needTfInjection = true; // Assume we need Tf until we see one
                SerializeOperation(operation, sb);
                sb.Append('\n');
                continue;
            }

            // Detect ET (end text block)
            if (operation.Operator == "ET")
            {
                inTextBlock = false;
                needTfInjection = false;
                SerializeOperation(operation, sb);
                sb.Append('\n');
                continue;
            }

            // If we're about to emit a Tj and haven't seen Tf in this BT block, inject one
            if (inTextBlock && needTfInjection && IsTextShowingOperator(operation.Operator))
            {
                // Get font from the TextOperation if available
                string? fontToUse = null;
                double sizeToUse = 12.0;

                if (operation is TextOperation textOp && !string.IsNullOrEmpty(textOp.FontName))
                {
                    fontToUse = textOp.FontName;
                    sizeToUse = textOp.FontSize > 0 ? textOp.FontSize : 12.0;
                }
                else if (!string.IsNullOrEmpty(currentBlockFontName))
                {
                    fontToUse = currentBlockFontName;
                    sizeToUse = currentBlockFontSize;
                }
                else if (!string.IsNullOrEmpty(lastFontName))
                {
                    fontToUse = lastFontName;
                    sizeToUse = lastFontSize;
                }

                if (!string.IsNullOrEmpty(fontToUse))
                {
                    // Inject Tf operator before the text showing operator
                    sb.Append(fontToUse);
                    sb.Append(' ');
                    SerializeNumber(sizeToUse, sb);
                    sb.Append(" Tf\n");
                    needTfInjection = false;
                }
            }

            SerializeOperation(operation, sb);
            sb.Append('\n');
        }

        var result = sb.ToString();
        return Encoding.ASCII.GetBytes(result);
    }

    /// <summary>
    /// Check if an operator is a text showing operator (Tj, TJ, ', ")
    /// </summary>
    private static bool IsTextShowingOperator(string op)
    {
        return op == "Tj" || op == "TJ" || op == "'" || op == "\"";
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
        // CJK support (Issue #174): Check if this operation needs hex string encoding
        if (operation is TextOperation textOp)
        {
            _currentOpIsCidFont = textOp.IsCidFont || textOp.WasHexString;
        }
        else
        {
            _currentOpIsCidFont = false;
        }

        // Serialize operands
        foreach (var operand in operation.Operands)
        {
            SerializeOperand(operand, sb);
            sb.Append(' ');
        }

        // Add operator
        sb.Append(operation.Operator);
    }

    // Track whether current operation is CID font (for hex string serialization)
    private bool _currentOpIsCidFont;

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
                // CJK support (Issue #174): Use hex strings for CID fonts
                // Hex strings are required for proper 2-byte CID encoding
                if (_currentOpIsCidFont)
                {
                    SerializeHexString(bytes, sb);
                }
                else
                {
                    SerializeLiteralStringFromBytes(bytes, sb);
                }
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

    /// <summary>
    /// Serialize bytes as a hex string for CID/CJK fonts.
    /// CID fonts require hex strings because they use 2-byte character codes.
    /// </summary>
    private void SerializeHexString(byte[] bytes, StringBuilder sb)
    {
        sb.Append('<');
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("X2"));
        }
        sb.Append('>');
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
