using System.Text;
using PdfEditor.Redaction.Operators;

namespace PdfEditor.Redaction.ContentStream.Parsing;

/// <summary>
/// Parses PDF content streams into structured operations with bounding boxes.
/// Uses the modular operator handler system.
/// </summary>
public class ContentStreamParser : IContentStreamParser
{
    private readonly OperatorRegistry _registry;

    private static readonly Lazy<Encoding> Windows1252Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("Windows-1252");
    });

    /// <summary>
    /// Create a parser with the default operator registry.
    /// </summary>
    public ContentStreamParser() : this(OperatorRegistry.CreateDefault())
    {
    }

    /// <summary>
    /// Create a parser with a custom operator registry.
    /// </summary>
    public ContentStreamParser(OperatorRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Parse a content stream into a list of operations.
    /// </summary>
    public IReadOnlyList<PdfOperation> Parse(byte[] contentBytes, double pageHeight)
    {
        var operations = new List<PdfOperation>();
        var state = new PdfParserState(pageHeight);
        var operandStack = new List<object>();

        int position = 0;
        int operationIndex = 0;

        while (position < contentBytes.Length)
        {
            // Skip whitespace
            while (position < contentBytes.Length && IsWhitespace(contentBytes[position]))
                position++;

            if (position >= contentBytes.Length)
                break;

            // Try to parse next token
            var token = ParseToken(contentBytes, ref position);
            if (token == null)
            {
                position++;
                continue;
            }

            if (token.IsOperator)
            {
                // Dispatch to handler
                state.StreamPosition = operationIndex++;
                var handler = _registry.GetHandler(token.StringValue);

                // CRITICAL: Pass a COPY of operandStack, not the list itself!
                // Otherwise when we Clear() the stack, it clears the list that was assigned
                // to the operation's Operands property.
                var operandsCopy = operandStack.ToList();

                if (handler != null)
                {
                    var operation = handler.Handle(operandsCopy, state);
                    if (operation != null)
                    {
                        operations.Add(operation);
                    }
                }
                else
                {
                    // Unhandled operator - create generic operation
                    var genericOp = CreateGenericOperation(token.StringValue, operandsCopy, state);
                    if (genericOp != null)
                    {
                        operations.Add(genericOp);
                    }
                }

                operandStack.Clear();
            }
            else
            {
                // Add operand to stack
                operandStack.Add(token.Value);
            }
        }

        return operations;
    }

    private Token? ParseToken(byte[] bytes, ref int pos)
    {
        if (pos >= bytes.Length)
            return null;

        byte b = bytes[pos];

        // Literal string: (...)
        if (b == (byte)'(')
        {
            return ParseLiteralString(bytes, ref pos);
        }

        // Hex string: <...> (but not dictionary <<)
        if (b == (byte)'<' && pos + 1 < bytes.Length && bytes[pos + 1] != (byte)'<')
        {
            return ParseHexString(bytes, ref pos);
        }

        // Array: [...]
        if (b == (byte)'[')
        {
            return ParseArray(bytes, ref pos);
        }

        // Name: /name
        if (b == (byte)'/')
        {
            return ParseName(bytes, ref pos);
        }

        // Number or operator
        if (IsNumberStart(b))
        {
            return ParseNumber(bytes, ref pos);
        }

        // Operator (keyword)
        return ParseOperator(bytes, ref pos);
    }

    private Token? ParseLiteralString(byte[] bytes, ref int pos)
    {
        if (bytes[pos] != (byte)'(')
            return null;

        pos++; // Skip opening (
        var content = new List<byte>();
        int parenDepth = 1;

        while (pos < bytes.Length && parenDepth > 0)
        {
            byte b = bytes[pos];

            if (b == (byte)'\\' && pos + 1 < bytes.Length)
            {
                pos++;
                byte escaped = bytes[pos];
                switch (escaped)
                {
                    case (byte)'n': content.Add((byte)'\n'); break;
                    case (byte)'r': content.Add((byte)'\r'); break;
                    case (byte)'t': content.Add((byte)'\t'); break;
                    case (byte)'b': content.Add((byte)'\b'); break;
                    case (byte)'f': content.Add((byte)'\f'); break;
                    case (byte)'(': content.Add((byte)'('); break;
                    case (byte)')': content.Add((byte)')'); break;
                    case (byte)'\\': content.Add((byte)'\\'); break;
                    default:
                        if (escaped >= '0' && escaped <= '7')
                        {
                            int octalValue = escaped - '0';
                            int digitsRead = 1;
                            while (digitsRead < 3 && pos + 1 < bytes.Length &&
                                   bytes[pos + 1] >= '0' && bytes[pos + 1] <= '7')
                            {
                                pos++;
                                octalValue = (octalValue * 8) + (bytes[pos] - '0');
                                digitsRead++;
                            }
                            content.Add((byte)octalValue);
                        }
                        else
                        {
                            content.Add(escaped);
                        }
                        break;
                }
            }
            else if (b == (byte)'(')
            {
                parenDepth++;
                content.Add(b);
            }
            else if (b == (byte)')')
            {
                parenDepth--;
                if (parenDepth > 0)
                    content.Add(b);
            }
            else
            {
                content.Add(b);
            }
            pos++;
        }

        var text = Windows1252Encoding.Value.GetString(content.ToArray());
        return new Token(text, false);
    }

    private Token? ParseHexString(byte[] bytes, ref int pos)
    {
        if (bytes[pos] != (byte)'<')
            return null;

        pos++; // Skip opening <
        var hexChars = new List<char>();

        while (pos < bytes.Length && bytes[pos] != (byte)'>')
        {
            byte b = bytes[pos];
            if (!IsWhitespace(b))
            {
                if ((b >= '0' && b <= '9') || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f'))
                {
                    hexChars.Add((char)b);
                }
            }
            pos++;
        }

        if (pos < bytes.Length && bytes[pos] == (byte)'>')
            pos++;

        // Odd number: final digit assumed 0
        if (hexChars.Count % 2 == 1)
            hexChars.Add('0');

        var decodedBytes = new byte[hexChars.Count / 2];
        for (int i = 0; i < decodedBytes.Length; i++)
        {
            string hex = new string(new[] { hexChars[i * 2], hexChars[i * 2 + 1] });
            decodedBytes[i] = Convert.ToByte(hex, 16);
        }

        // Return as byte array for text operators to handle encoding
        return new Token(decodedBytes, false);
    }

    private Token? ParseArray(byte[] bytes, ref int pos)
    {
        if (bytes[pos] != (byte)'[')
            return null;

        pos++; // Skip opening [
        var elements = new List<object>();

        while (pos < bytes.Length && bytes[pos] != (byte)']')
        {
            // Skip whitespace
            while (pos < bytes.Length && IsWhitespace(bytes[pos]))
                pos++;

            if (pos >= bytes.Length || bytes[pos] == (byte)']')
                break;

            var element = ParseToken(bytes, ref pos);
            if (element != null && !element.IsOperator)
            {
                elements.Add(element.Value);
            }
        }

        if (pos < bytes.Length && bytes[pos] == (byte)']')
            pos++;

        return new Token(elements, false);
    }

    private Token? ParseName(byte[] bytes, ref int pos)
    {
        if (bytes[pos] != (byte)'/')
            return null;

        var sb = new StringBuilder();
        sb.Append('/');
        pos++;

        while (pos < bytes.Length && !IsDelimiter(bytes[pos]) && !IsWhitespace(bytes[pos]))
        {
            sb.Append((char)bytes[pos]);
            pos++;
        }

        return new Token(sb.ToString(), false);
    }

    private Token? ParseNumber(byte[] bytes, ref int pos)
    {
        var sb = new StringBuilder();

        // Handle sign
        if (bytes[pos] == (byte)'+' || bytes[pos] == (byte)'-')
        {
            sb.Append((char)bytes[pos]);
            pos++;
        }

        bool hasDecimal = false;

        while (pos < bytes.Length)
        {
            byte b = bytes[pos];
            if (b >= '0' && b <= '9')
            {
                sb.Append((char)b);
            }
            else if (b == '.' && !hasDecimal)
            {
                hasDecimal = true;
                sb.Append('.');
            }
            else
            {
                break;
            }
            pos++;
        }

        var numStr = sb.ToString();
        if (string.IsNullOrEmpty(numStr) || numStr == "+" || numStr == "-" || numStr == ".")
            return null;

        if (hasDecimal)
        {
            return double.TryParse(numStr, out var d) ? new Token(d, false) : null;
        }
        else
        {
            return int.TryParse(numStr, out var i) ? new Token((double)i, false) : null;
        }
    }

    private Token? ParseOperator(byte[] bytes, ref int pos)
    {
        var sb = new StringBuilder();

        while (pos < bytes.Length && !IsWhitespace(bytes[pos]) && !IsDelimiter(bytes[pos]))
        {
            sb.Append((char)bytes[pos]);
            pos++;
        }

        var op = sb.ToString();
        return string.IsNullOrEmpty(op) ? null : new Token(op, true);
    }

    private PdfOperation? CreateGenericOperation(string opName, IReadOnlyList<object> operands, PdfParserState state)
    {
        // Handle graphics state operators
        switch (opName)
        {
            case "q":
                state.SaveGraphicsState();
                return new StateOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject,
                    IsSave = true
                };

            case "Q":
                state.RestoreGraphicsState();
                return new StateOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject,
                    IsRestore = true
                };

            case "cm":
                if (operands.Count >= 6)
                {
                    var matrix = PdfMatrix.FromOperands(
                        GetDouble(operands[0]),
                        GetDouble(operands[1]),
                        GetDouble(operands[2]),
                        GetDouble(operands[3]),
                        GetDouble(operands[4]),
                        GetDouble(operands[5]));
                    state.TransformationMatrix = matrix.Multiply(state.TransformationMatrix);
                }
                return new StateOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject
                };

            // Path operators
            case "m":
            case "l":
            case "c":
            case "v":
            case "y":
            case "h":
            case "re":
            case "S":
            case "s":
            case "f":
            case "F":
            case "f*":
            case "B":
            case "B*":
            case "b":
            case "b*":
            case "n":
                return new PathOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject,
                    Type = GetPathType(opName)
                };

            // Image XObject
            case "Do":
                var xobjName = operands.Count > 0 ? operands[0]?.ToString() ?? "" : "";
                return new ImageOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject,
                    XObjectName = xobjName
                };

            default:
                // Unknown operator - return generic state operation
                // This handles text state operators like Tc, Tw, Tz, Ts, Tr that don't have dedicated handlers
                return new TextStateOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject
                };
        }
    }

    private static PathType GetPathType(string op) => op switch
    {
        "m" => PathType.MoveTo,
        "l" => PathType.LineTo,
        "c" or "v" or "y" => PathType.CurveTo,
        "h" => PathType.ClosePath,
        "re" => PathType.Rectangle,
        "S" or "s" => PathType.Stroke,
        "f" or "F" or "f*" => PathType.Fill,
        "B" or "B*" or "b" or "b*" => PathType.FillStroke,
        "n" => PathType.EndPath,
        _ => PathType.MoveTo
    };

    private static double GetDouble(object obj) => obj switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        decimal m => (double)m,
        string s when double.TryParse(s, out var parsed) => parsed,
        _ => 0
    };

    private static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\n' || b == '\r' || b == '\f' || b == 0;

    private static bool IsDelimiter(byte b) =>
        b == '(' || b == ')' || b == '<' || b == '>' ||
        b == '[' || b == ']' || b == '{' || b == '}' ||
        b == '/' || b == '%';

    private static bool IsNumberStart(byte b) =>
        (b >= '0' && b <= '9') || b == '+' || b == '-' || b == '.';

    /// <summary>
    /// Internal token representation.
    /// </summary>
    private record Token(object Value, bool IsOperator)
    {
        public string StringValue => Value?.ToString() ?? "";
    }
}
