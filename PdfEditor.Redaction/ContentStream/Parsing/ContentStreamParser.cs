using System.Text;
using PdfEditor.Redaction.Fonts;
using PdfEditor.Redaction.Operators;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

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
    /// Parse a PDF page's content stream with full font awareness.
    /// Extracts fonts from page resources for proper CID/CJK encoding support.
    /// </summary>
    public IReadOnlyList<PdfOperation> ParsePage(PdfPage page)
    {
        var pageHeight = page.Height.Point;

        // Extract fonts from page resources for CID/CJK support
        var fonts = FontDictionaryParser.ExtractFonts(page);

        // Get content stream bytes
        var contentBytes = GetContentStreamBytes(page);
        if (contentBytes == null || contentBytes.Length == 0)
            return new List<PdfOperation>();

        // Get resources for Form XObject support
        var resources = page.Elements.GetDictionary("/Resources");

        // Parse with font awareness
        return ParseWithFonts(contentBytes, pageHeight, fonts, resources);
    }

    /// <summary>
    /// Parse a content stream with font information for proper CID/CJK encoding.
    /// </summary>
    private IReadOnlyList<PdfOperation> ParseWithFonts(
        byte[] contentBytes,
        double pageHeight,
        Dictionary<string, FontInfo> fonts,
        PdfDictionary? resources)
    {
        var operations = new List<PdfOperation>();
        var state = new PdfParserState(pageHeight);
        state.FontRegistry = fonts;
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

                // CRITICAL: Pass a COPY of operandStack
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
                operandStack.Add(token.Value);
            }
        }

        // Handle Form XObjects if resources available
        if (resources != null)
        {
            operations = ResolveFormXObjects(operations, resources, pageHeight);
        }

        return operations;
    }

    /// <summary>
    /// Get content stream bytes from a page.
    /// </summary>
    private byte[]? GetContentStreamBytes(PdfPage page)
    {
        try
        {
            using var ms = new MemoryStream();
            foreach (var item in page.Contents.Elements)
            {
                if (item is PdfReference pdfRef && pdfRef.Value is PdfDictionary dict && dict.Stream != null)
                {
                    ms.Write(dict.Stream.Value, 0, dict.Stream.Value.Length);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve Form XObject references in operations.
    /// </summary>
    private List<PdfOperation> ResolveFormXObjects(List<PdfOperation> operations, PdfDictionary resources, double pageHeight)
    {
        var result = new List<PdfOperation>();

        foreach (var op in operations)
        {
            if (op is ImageOperation imageOp && imageOp.Operator == "Do")
            {
                var formOp = TryParseFormXObject(imageOp, resources, pageHeight);
                result.Add(formOp ?? op);
            }
            else
            {
                result.Add(op);
            }
        }

        return result;
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

    /// <summary>
    /// Parse a content stream with access to page resources for Form XObject resolution.
    /// This method detects Form XObjects and recursively parses their content streams.
    /// </summary>
    public IReadOnlyList<PdfOperation> ParseWithResources(byte[] contentBytes, double pageHeight, PdfDictionary? resources)
    {
        // First, parse the main content stream
        var operations = Parse(contentBytes, pageHeight).ToList();

        if (resources == null)
            return operations;

        // Find all Do operations and check if they reference Form XObjects
        var result = new List<PdfOperation>();

        foreach (var op in operations)
        {
            if (op is ImageOperation imageOp && imageOp.Operator == "Do")
            {
                // Try to resolve the XObject and check if it's a Form
                var formOp = TryParseFormXObject(imageOp, resources, pageHeight);
                if (formOp != null)
                {
                    result.Add(formOp);
                }
                else
                {
                    // Keep as ImageOperation (it's an image or couldn't be resolved)
                    result.Add(op);
                }
            }
            else
            {
                result.Add(op);
            }
        }

        return result;
    }

    /// <summary>
    /// Try to parse a Form XObject referenced by a Do operation.
    /// Returns null if it's not a Form XObject or can't be parsed.
    /// </summary>
    private FormXObjectOperation? TryParseFormXObject(ImageOperation doOp, PdfDictionary resources, double pageHeight)
    {
        try
        {
            // Get the XObject dictionary from resources
            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects == null)
                return null;

            // Get the name without leading slash
            var name = doOp.XObjectName;
            if (name.StartsWith("/"))
                name = name.Substring(1);

            // Look up the XObject by name
            PdfDictionary? xObject = null;

            // Try with and without leading slash
            var key = "/" + name;
            if (!xObjects.Elements.ContainsKey(key))
            {
                key = name;
                if (!xObjects.Elements.ContainsKey(key))
                    return null;
            }

            var element = xObjects.Elements[key];
            if (element is PdfSharp.Pdf.Advanced.PdfReference pdfRef)
            {
                xObject = pdfRef.Value as PdfDictionary;
            }
            else if (element is PdfDictionary dict)
            {
                xObject = dict;
            }

            if (xObject == null)
                return null;

            // Check if it's a Form XObject
            var subtype = xObject.Elements.GetName("/Subtype");
            if (subtype != "/Form")
                return null;

            // It's a Form XObject - parse it
            return ParseFormXObject(doOp, xObject, pageHeight);
        }
        catch
        {
            // If anything fails, return null (keep as ImageOperation)
            return null;
        }
    }

    /// <summary>
    /// Parse a Form XObject's content stream.
    /// </summary>
    private FormXObjectOperation? ParseFormXObject(ImageOperation doOp, PdfDictionary formXObject, double pageHeight)
    {
        try
        {
            // Get the form's stream content
            byte[]? streamBytes = null;

            if (formXObject.Stream?.Value is byte[] bytes)
            {
                streamBytes = bytes;
            }
            else if (formXObject is PdfDictionary dictWithStream)
            {
                // Try to get unfiltered stream
                try
                {
                    streamBytes = dictWithStream.Stream?.UnfilteredValue;
                }
                catch
                {
                    streamBytes = dictWithStream.Stream?.Value;
                }
            }

            if (streamBytes == null || streamBytes.Length == 0)
            {
                // Form has no content - return an empty FormXObjectOperation
                return new FormXObjectOperation
                {
                    Operator = doOp.Operator,
                    Operands = doOp.Operands,
                    StreamPosition = doOp.StreamPosition,
                    InsideTextBlock = doOp.InsideTextBlock,
                    XObjectName = doOp.XObjectName,
                    ContentStreamBytes = streamBytes
                };
            }

            // Get BBox
            var bboxArray = formXObject.Elements.GetArray("/BBox");
            var formBBox = new PdfRectangle(0, 0, 0, 0);
            if (bboxArray != null && bboxArray.Elements.Count >= 4)
            {
                formBBox = new PdfRectangle(
                    GetArrayDouble(bboxArray, 0),
                    GetArrayDouble(bboxArray, 1),
                    GetArrayDouble(bboxArray, 2),
                    GetArrayDouble(bboxArray, 3));
            }

            // Get transformation matrix
            var matrixArray = formXObject.Elements.GetArray("/Matrix");
            var formMatrix = new double[] { 1, 0, 0, 1, 0, 0 };
            if (matrixArray != null && matrixArray.Elements.Count >= 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    formMatrix[i] = GetArrayDouble(matrixArray, i);
                }
            }

            // Parse nested content stream
            // Note: We use pageHeight for coordinate conversion, but the form's content
            // is in form coordinate space which may differ from page coordinates.
            // The FormMatrix should be applied to convert to page coordinates.
            var nestedOps = Parse(streamBytes, pageHeight).ToList();

            // Get form's resources for recursive parsing
            var formResources = formXObject.Elements.GetDictionary("/Resources");
            if (formResources != null)
            {
                // Recursively parse any nested Form XObjects
                nestedOps = ParseWithResources(streamBytes, pageHeight, formResources).ToList();
            }

            return new FormXObjectOperation
            {
                Operator = doOp.Operator,
                Operands = doOp.Operands,
                StreamPosition = doOp.StreamPosition,
                InsideTextBlock = doOp.InsideTextBlock,
                XObjectName = doOp.XObjectName,
                NestedOperations = nestedOps,
                FormBBox = formBBox,
                FormMatrix = formMatrix,
                ContentStreamBytes = streamBytes
            };
        }
        catch
        {
            return null;
        }
    }

    private static double GetArrayDouble(PdfArray array, int index)
    {
        if (index >= array.Elements.Count)
            return 0;

        var element = array.Elements[index];
        if (element is PdfReal real)
            return real.Value;
        if (element is PdfInteger integer)
            return integer.Value;
        if (element is PdfLongInteger longInt)
            return longInt.Value;

        return 0;
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

            int beforePos = pos;
            var element = ParseToken(bytes, ref pos);
            if (element != null && !element.IsOperator)
            {
                elements.Add(element.Value);
            }

            // If ParseToken didn't advance, force advance to prevent infinite loop
            // This can happen with unrecognized characters like } inside arrays
            if (pos == beforePos)
            {
                pos++;
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

            // Rectangle operator - calculate bounding box
            case "re":
                var rectBBox = CalculateRectangleBoundingBox(operands, state.TransformationMatrix);
                return new PathOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject,
                    Type = PathType.Rectangle,
                    BoundingBox = rectBBox
                };

            // Other path operators (bounding box not calculated - would need path tracking)
            case "m":
            case "l":
            case "c":
            case "v":
            case "y":
            case "h":
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
                // Calculate bounding box from CTM - images are defined in unit square [0,0,1,1]
                var imageBBox = CalculateXObjectBoundingBox(state.TransformationMatrix);
                return new ImageOperation
                {
                    Operator = opName,
                    Operands = operands.ToList(),
                    StreamPosition = state.StreamPosition,
                    InsideTextBlock = state.InTextObject,
                    XObjectName = xobjName,
                    BoundingBox = imageBBox
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

    /// <summary>
    /// Calculate bounding box for a rectangle (re operator) with CTM transformation.
    /// Rectangle operands: x y width height
    /// </summary>
    private static PdfRectangle CalculateRectangleBoundingBox(IReadOnlyList<object> operands, PdfMatrix ctm)
    {
        if (operands.Count < 4)
            return new PdfRectangle(0, 0, 0, 0);

        var x = GetDouble(operands[0]);
        var y = GetDouble(operands[1]);
        var w = GetDouble(operands[2]);
        var h = GetDouble(operands[3]);

        // Transform all four corners through the CTM
        var (x0, y0) = ctm.Transform(x, y);
        var (x1, y1) = ctm.Transform(x + w, y);
        var (x2, y2) = ctm.Transform(x + w, y + h);
        var (x3, y3) = ctm.Transform(x, y + h);

        // Find bounding box that contains all corners
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));

        return new PdfRectangle(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Calculate bounding box for an XObject (image) from the current transformation matrix.
    /// Images are defined in a unit square [0,0] to [1,1], transformed by the CTM.
    /// </summary>
    private static PdfRectangle CalculateXObjectBoundingBox(PdfMatrix ctm)
    {
        // Transform all four corners of the unit square
        var (x0, y0) = ctm.Transform(0, 0);  // Bottom-left
        var (x1, y1) = ctm.Transform(1, 0);  // Bottom-right
        var (x2, y2) = ctm.Transform(1, 1);  // Top-right
        var (x3, y3) = ctm.Transform(0, 1);  // Top-left

        // Find bounding box that contains all corners
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));

        return new PdfRectangle(minX, minY, maxX, maxY);
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
