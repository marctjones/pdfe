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
    ///
    /// CRITICAL FIX: PDF font size is calculated as: effectiveSize = Tf_size * Tm_scale
    /// Many PDFs use "/F1 1 Tf" with size 1, and encode actual size in Tm matrix.
    /// TextOperation.FontSize contains the EFFECTIVE size (already includes Tm scaling).
    /// When injecting Tf, we must use the ORIGINAL Tf size (typically 1), not the effective size!
    /// </summary>
    public byte[] Build(IEnumerable<PdfOperation> operations)
    {
        var sb = new StringBuilder();
        var operationList = operations.OrderBy(op => op.StreamPosition).ToList();

        // Track font state for injection into BT blocks
        // CRITICAL: Track the RAW Tf size, not the effective size
        string? lastFontName = null;
        double lastTfSize = 1.0;  // Default to 1 (common pattern: size in Tm, not Tf)

        // First pass: collect font info from ACTUAL Tf operators only
        // Do NOT use TextOperation.FontSize as it contains the effective size
        foreach (var op in operationList)
        {
            if (op is TextStateOperation tso && tso.Operator == "Tf" && tso.Operands.Count >= 2)
            {
                lastFontName = tso.Operands[0]?.ToString();
                if (tso.Operands[1] is double d) lastTfSize = d;
                else if (tso.Operands[1] is int i) lastTfSize = i;
                else if (tso.Operands[1] is float f) lastTfSize = f;
            }
            else if (op is TextOperation textOp && string.IsNullOrEmpty(lastFontName))
            {
                // Only use TextOperation.FontName as a LAST RESORT for the font name
                // NEVER use TextOperation.FontSize for Tf injection
                if (!string.IsNullOrEmpty(textOp.FontName))
                {
                    lastFontName = textOp.FontName;
                    // Keep lastTfSize at 1.0 - do NOT use textOp.FontSize
                }
            }
        }

        // Now we have the font info. Second pass: serialize with Tf injection
        string? currentBlockFontName = null;
        double currentBlockTfSize = 1.0;  // Track raw Tf size, not effective size
        bool inTextBlock = false;
        bool needTfInjection = false;

        foreach (var operation in operationList)
        {
            // Track font state from Tf operators
            if (operation is TextStateOperation tso && tso.Operator == "Tf" && tso.Operands.Count >= 2)
            {
                currentBlockFontName = tso.Operands[0]?.ToString();
                if (tso.Operands[1] is double d) currentBlockTfSize = d;
                else if (tso.Operands[1] is int i) currentBlockTfSize = i;
                else if (tso.Operands[1] is float f) currentBlockTfSize = f;
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
                // Get font name from the TextOperation if available
                // CRITICAL: Use the RAW Tf size, not TextOperation.FontSize (which is effective size)
                string? fontToUse = null;
                double tfSizeToUse = 1.0;  // Default to 1 (most common pattern)

                if (operation is TextOperation textOp && !string.IsNullOrEmpty(textOp.FontName))
                {
                    fontToUse = textOp.FontName;
                    // Use the tracked raw Tf size, NOT textOp.FontSize
                    tfSizeToUse = currentBlockTfSize > 0 ? currentBlockTfSize : (lastTfSize > 0 ? lastTfSize : 1.0);
                }
                else if (!string.IsNullOrEmpty(currentBlockFontName))
                {
                    fontToUse = currentBlockFontName;
                    tfSizeToUse = currentBlockTfSize;
                }
                else if (!string.IsNullOrEmpty(lastFontName))
                {
                    fontToUse = lastFontName;
                    tfSizeToUse = lastTfSize;
                }

                if (!string.IsNullOrEmpty(fontToUse))
                {
                    // Inject Tf operator before the text showing operator
                    // Use the RAW Tf size, not the effective size
                    sb.Append(fontToUse);
                    sb.Append(' ');
                    SerializeNumber(tfSizeToUse, sb);
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
                        // CRITICAL FIX (Issue #187): Encode Unicode characters back to Windows-1252.
                        // The parser decoded Windows-1252 bytes to Unicode (e.g., byte 0x92 → U+2019 right quote).
                        // We must re-encode to Windows-1252 to get the correct byte value for octal escape.
                        // Without this, U+2019 (8217) → octal "20031" which is invalid!
                        byte byteValue;
                        try
                        {
                            var encoded = Windows1252Encoding.Value.GetBytes(new[] { c });
                            byteValue = encoded.Length == 1 ? encoded[0] : (byte)(c & 0xFF);
                        }
                        catch
                        {
                            byteValue = (byte)(c & 0xFF);
                        }
                        sb.Append($"\\{Convert.ToString(byteValue, 8).PadLeft(3, '0')}");
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
