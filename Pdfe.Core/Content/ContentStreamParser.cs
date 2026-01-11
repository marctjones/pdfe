using System.Text;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Content;

/// <summary>
/// Parses PDF content stream bytes into a sequence of operators.
/// ISO 32000-2:2020 Section 7.8.2.
/// </summary>
public class ContentStreamParser
{
    private readonly byte[] _content;
    private readonly PdfPage? _page;
    private int _pos;

    // Graphics state tracking
    private readonly Stack<GraphicsState> _stateStack = new();
    private GraphicsState _state = new();

    // Text state tracking
    private double _fontSize = 12;
    private string _fontName = "";
    private PdfDictionary? _currentFont;
    private Dictionary<int, string>? _toUnicodeMap;
    private double _textLeading;
    private double _charSpacing;
    private double _wordSpacing;
    private double _horizontalScaling = 100;

    // Text matrix
    private double _tm_a = 1, _tm_b, _tm_c, _tm_d = 1, _tm_e, _tm_f;
    private double _tlm_e, _tlm_f;

    // Current path for bounds calculation
    private double _pathMinX, _pathMinY, _pathMaxX, _pathMaxY;
    private bool _pathStarted;
    private double _currentX, _currentY;

    /// <summary>
    /// Create a parser for the given content bytes.
    /// </summary>
    /// <param name="content">Raw content stream bytes.</param>
    /// <param name="page">Optional page reference for font resolution.</param>
    public ContentStreamParser(byte[] content, PdfPage? page = null)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _page = page;
    }

    /// <summary>
    /// Parse the content stream into a ContentStream object.
    /// </summary>
    public ContentStream Parse()
    {
        _pos = 0;
        var operators = new List<ContentOperator>();
        var operands = new List<PdfObject>();

        while (_pos < _content.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _content.Length) break;

            var token = ParseToken();
            if (token == null) continue;

            if (token is string op && IsOperator(op))
            {
                var contentOp = CreateOperator(op, operands);
                if (contentOp != null)
                {
                    operators.Add(contentOp);
                }
                operands.Clear();
            }
            else if (token is PdfObject pdfObj)
            {
                operands.Add(pdfObj);
            }
        }

        return new ContentStream(operators);
    }

    /// <summary>
    /// Create a ContentOperator and calculate its bounds/properties.
    /// </summary>
    private ContentOperator? CreateOperator(string name, List<PdfObject> operands)
    {
        var op = new ContentOperator(name, operands.ToList());

        // Execute operator to update state and calculate bounds
        ExecuteOperator(name, operands, op);

        return op;
    }

    /// <summary>
    /// Execute an operator to track state and calculate bounds.
    /// </summary>
    private void ExecuteOperator(string name, List<PdfObject> operands, ContentOperator op)
    {
        switch (name)
        {
            // Graphics state
            case "q":
                _stateStack.Push(_state.Clone());
                break;

            case "Q":
                if (_stateStack.Count > 0)
                    _state = _stateStack.Pop();
                break;

            case "cm":
                if (operands.Count >= 6)
                {
                    var a = GetNumber(operands[0]);
                    var b = GetNumber(operands[1]);
                    var c = GetNumber(operands[2]);
                    var d = GetNumber(operands[3]);
                    var e = GetNumber(operands[4]);
                    var f = GetNumber(operands[5]);
                    _state.MultiplyCtm(a, b, c, d, e, f);
                }
                break;

            // Path construction
            case "m":
                if (operands.Count >= 2)
                {
                    var x = GetNumber(operands[0]);
                    var y = GetNumber(operands[1]);
                    StartPath(x, y);
                }
                break;

            case "l":
                if (operands.Count >= 2)
                {
                    var x = GetNumber(operands[0]);
                    var y = GetNumber(operands[1]);
                    ExtendPath(x, y);
                }
                break;

            case "c":
                if (operands.Count >= 6)
                {
                    ExtendPath(GetNumber(operands[0]), GetNumber(operands[1]));
                    ExtendPath(GetNumber(operands[2]), GetNumber(operands[3]));
                    ExtendPath(GetNumber(operands[4]), GetNumber(operands[5]));
                }
                break;

            case "v":
                if (operands.Count >= 4)
                {
                    ExtendPath(GetNumber(operands[0]), GetNumber(operands[1]));
                    ExtendPath(GetNumber(operands[2]), GetNumber(operands[3]));
                }
                break;

            case "y":
                if (operands.Count >= 4)
                {
                    ExtendPath(GetNumber(operands[0]), GetNumber(operands[1]));
                    ExtendPath(GetNumber(operands[2]), GetNumber(operands[3]));
                }
                break;

            case "re":
                if (operands.Count >= 4)
                {
                    var x = GetNumber(operands[0]);
                    var y = GetNumber(operands[1]);
                    var w = GetNumber(operands[2]);
                    var h = GetNumber(operands[3]);
                    StartPath(x, y);
                    ExtendPath(x + w, y);
                    ExtendPath(x + w, y + h);
                    ExtendPath(x, y + h);
                }
                break;

            case "h":
                // Close path - no bounds change
                break;

            // Path painting - assign bounds to operator
            case "S":
            case "s":
            case "f":
            case "F":
            case "f*":
            case "B":
            case "B*":
            case "b":
            case "b*":
                if (_pathStarted)
                {
                    op.BoundingBox = TransformBounds(_pathMinX, _pathMinY, _pathMaxX, _pathMaxY);
                }
                EndPath();
                break;

            case "n":
                EndPath();
                break;

            // Text object
            case "BT":
                _tm_a = 1; _tm_b = 0; _tm_c = 0; _tm_d = 1; _tm_e = 0; _tm_f = 0;
                _tlm_e = 0; _tlm_f = 0;
                break;

            case "ET":
                // Text block ended - no state to reset
                break;

            // Text state
            case "Tf":
                if (operands.Count >= 2)
                {
                    _fontName = operands[0] is PdfName n ? n.Value : "";
                    _fontSize = GetNumber(operands[1]);
                    LoadFont();
                }
                break;

            case "TL":
                if (operands.Count >= 1)
                    _textLeading = GetNumber(operands[0]);
                break;

            case "Tc":
                if (operands.Count >= 1)
                    _charSpacing = GetNumber(operands[0]);
                break;

            case "Tw":
                if (operands.Count >= 1)
                    _wordSpacing = GetNumber(operands[0]);
                break;

            case "Tz":
                if (operands.Count >= 1)
                    _horizontalScaling = GetNumber(operands[0]);
                break;

            // Text positioning
            case "Td":
                if (operands.Count >= 2)
                {
                    var tx = GetNumber(operands[0]);
                    var ty = GetNumber(operands[1]);
                    _tlm_e += tx;
                    _tlm_f += ty;
                    _tm_e = _tlm_e;
                    _tm_f = _tlm_f;
                }
                break;

            case "TD":
                if (operands.Count >= 2)
                {
                    var tx = GetNumber(operands[0]);
                    var ty = GetNumber(operands[1]);
                    _textLeading = -ty;
                    _tlm_e += tx;
                    _tlm_f += ty;
                    _tm_e = _tlm_e;
                    _tm_f = _tlm_f;
                }
                break;

            case "Tm":
                if (operands.Count >= 6)
                {
                    _tm_a = GetNumber(operands[0]);
                    _tm_b = GetNumber(operands[1]);
                    _tm_c = GetNumber(operands[2]);
                    _tm_d = GetNumber(operands[3]);
                    _tm_e = GetNumber(operands[4]);
                    _tm_f = GetNumber(operands[5]);
                    _tlm_e = _tm_e;
                    _tlm_f = _tm_f;
                }
                break;

            case "T*":
                _tlm_f -= _textLeading;
                _tm_e = _tlm_e;
                _tm_f = _tlm_f;
                break;

            // Text showing
            case "Tj":
                if (operands.Count >= 1)
                {
                    var (text, bounds) = ProcessTextString(operands[0]);
                    op.TextContent = text;
                    op.BoundingBox = bounds;
                }
                break;

            case "TJ":
                if (operands.Count >= 1 && operands[0] is PdfArray arr)
                {
                    var (text, bounds) = ProcessTextArray(arr);
                    op.TextContent = text;
                    op.BoundingBox = bounds;
                }
                break;

            case "'":
                _tlm_f -= _textLeading;
                _tm_e = _tlm_e;
                _tm_f = _tlm_f;
                if (operands.Count >= 1)
                {
                    var (text, bounds) = ProcessTextString(operands[0]);
                    op.TextContent = text;
                    op.BoundingBox = bounds;
                }
                break;

            case "\"":
                if (operands.Count >= 3)
                {
                    _wordSpacing = GetNumber(operands[0]);
                    _charSpacing = GetNumber(operands[1]);
                    _tlm_f -= _textLeading;
                    _tm_e = _tlm_e;
                    _tm_f = _tlm_f;
                    var (text, bounds) = ProcessTextString(operands[2]);
                    op.TextContent = text;
                    op.BoundingBox = bounds;
                }
                break;

            // XObject
            case "Do":
                // XObject bounds depend on the object type and CTM
                // For now, we don't calculate bounds for XObjects
                break;
        }
    }

    #region Text Processing

    private (string text, PdfRectangle? bounds) ProcessTextString(PdfObject obj)
    {
        byte[] bytes;
        if (obj is PdfString ps)
            bytes = Encoding.Latin1.GetBytes(ps.Value);
        else
            return ("", null);

        return ProcessTextBytes(bytes);
    }

    private (string text, PdfRectangle? bounds) ProcessTextArray(PdfArray arr)
    {
        var sb = new StringBuilder();
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool hasBounds = false;

        foreach (var item in arr)
        {
            if (item is PdfString ps)
            {
                var bytes = Encoding.Latin1.GetBytes(ps.Value);
                var (text, bounds) = ProcessTextBytes(bytes);
                sb.Append(text);

                if (bounds.HasValue)
                {
                    var b = bounds.Value;
                    minX = Math.Min(minX, b.Left);
                    minY = Math.Min(minY, b.Bottom);
                    maxX = Math.Max(maxX, b.Right);
                    maxY = Math.Max(maxY, b.Top);
                    hasBounds = true;
                }
            }
            else if (item is PdfInteger pi)
            {
                // Adjust position (negative = move right)
                var adj = pi.Value;
                _tm_e -= (adj / 1000.0) * _fontSize * (_horizontalScaling / 100.0);
            }
            else if (item is PdfReal pr)
            {
                var adj = pr.Value;
                _tm_e -= (adj / 1000.0) * _fontSize * (_horizontalScaling / 100.0);
            }
        }

        var result = hasBounds ? new PdfRectangle(minX, minY, maxX, maxY) : (PdfRectangle?)null;
        return (sb.ToString(), result);
    }

    private (string text, PdfRectangle? bounds) ProcessTextBytes(byte[] bytes)
    {
        var sb = new StringBuilder();
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var b in bytes)
        {
            var charCode = (int)b;
            var unicode = DecodeCharacter(charCode);
            var charWidth = GetCharWidth(charCode);

            // Transform position
            var (x, y) = TransformTextPoint(_tm_e, _tm_f);

            // Calculate glyph dimensions
            var glyphWidth = charWidth * _fontSize * (_horizontalScaling / 100.0) / 1000.0;
            var glyphHeight = _fontSize;

            // Update bounds
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + glyphWidth);
            maxY = Math.Max(maxY, y + glyphHeight);

            sb.Append(unicode);

            // Advance text position
            var tx = (charWidth / 1000.0) * _fontSize * (_horizontalScaling / 100.0);
            tx += _charSpacing;
            if (charCode == 32) tx += _wordSpacing;

            _tm_e += tx * _tm_a;
            _tm_f += tx * _tm_b;
        }

        if (bytes.Length == 0)
            return (sb.ToString(), null);

        return (sb.ToString(), new PdfRectangle(minX, minY, maxX, maxY));
    }

    private (double x, double y) TransformTextPoint(double tx, double ty)
    {
        // Apply CTM to text position
        var x = tx * _state.Ctm_a + ty * _state.Ctm_c + _state.Ctm_e;
        var y = tx * _state.Ctm_b + ty * _state.Ctm_d + _state.Ctm_f;
        return (x, y);
    }

    private void LoadFont()
    {
        if (_page == null) return;

        _currentFont = _page.GetFont(_fontName);
        _toUnicodeMap = null;

        if (_currentFont != null)
        {
            var toUnicodeObj = _currentFont.GetOptional("ToUnicode");
            if (toUnicodeObj != null)
            {
                var resolved = _page.Document.Resolve(toUnicodeObj);
                if (resolved is PdfStream stream)
                {
                    try
                    {
                        _toUnicodeMap = Text.ToUnicodeCMapParser.Parse(stream.DecodedData);
                    }
                    catch
                    {
                        // Ignore CMap parsing errors
                    }
                }
            }
        }
    }

    private string DecodeCharacter(int charCode)
    {
        if (_toUnicodeMap != null && _toUnicodeMap.TryGetValue(charCode, out var unicode))
            return unicode;

        // Fall back to WinAnsi encoding
        if (charCode < 128 || charCode >= 160)
            return ((char)charCode).ToString();

        return charCode switch
        {
            128 => "\u20AC", 130 => "\u201A", 131 => "\u0192", 132 => "\u201E",
            133 => "\u2026", 134 => "\u2020", 135 => "\u2021", 136 => "\u02C6",
            137 => "\u2030", 138 => "\u0160", 139 => "\u2039", 140 => "\u0152",
            142 => "\u017D", 145 => "\u2018", 146 => "\u2019", 147 => "\u201C",
            148 => "\u201D", 149 => "\u2022", 150 => "\u2013", 151 => "\u2014",
            152 => "\u02DC", 153 => "\u2122", 154 => "\u0161", 155 => "\u203A",
            156 => "\u0153", 158 => "\u017E", 159 => "\u0178",
            _ => ((char)charCode).ToString()
        };
    }

    private double GetCharWidth(int charCode)
    {
        if (_currentFont != null)
        {
            var widthsObj = _currentFont.GetOptional("Widths");
            if (widthsObj is PdfArray widths)
            {
                var firstChar = _currentFont.GetInt("FirstChar", 0);
                var lastChar = _currentFont.GetInt("LastChar", 255);

                if (charCode >= firstChar && charCode <= lastChar)
                {
                    var index = charCode - firstChar;
                    if (index < widths.Count)
                        return widths.GetNumber(index);
                }
            }
        }

        return 600; // Default width
    }

    #endregion

    #region Path Tracking

    private void StartPath(double x, double y)
    {
        _pathStarted = true;
        _pathMinX = _pathMaxX = x;
        _pathMinY = _pathMaxY = y;
        _currentX = x;
        _currentY = y;
    }

    private void ExtendPath(double x, double y)
    {
        if (!_pathStarted)
        {
            StartPath(x, y);
            return;
        }

        _pathMinX = Math.Min(_pathMinX, x);
        _pathMinY = Math.Min(_pathMinY, y);
        _pathMaxX = Math.Max(_pathMaxX, x);
        _pathMaxY = Math.Max(_pathMaxY, y);
        _currentX = x;
        _currentY = y;
    }

    private void EndPath()
    {
        _pathStarted = false;
    }

    private PdfRectangle TransformBounds(double minX, double minY, double maxX, double maxY)
    {
        // Transform all four corners through CTM
        var corners = new[]
        {
            TransformPoint(minX, minY),
            TransformPoint(maxX, minY),
            TransformPoint(minX, maxY),
            TransformPoint(maxX, maxY)
        };

        return new PdfRectangle(
            corners.Min(p => p.x),
            corners.Min(p => p.y),
            corners.Max(p => p.x),
            corners.Max(p => p.y)
        );
    }

    private (double x, double y) TransformPoint(double x, double y)
    {
        var tx = x * _state.Ctm_a + y * _state.Ctm_c + _state.Ctm_e;
        var ty = x * _state.Ctm_b + y * _state.Ctm_d + _state.Ctm_f;
        return (tx, ty);
    }

    #endregion

    #region Tokenization

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _content.Length)
        {
            var c = _content[_pos];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == 0)
            {
                _pos++;
            }
            else if (c == '%')
            {
                // Skip comment to end of line
                while (_pos < _content.Length && _content[_pos] != '\n' && _content[_pos] != '\r')
                    _pos++;
            }
            else
            {
                break;
            }
        }
    }

    private object? ParseToken()
    {
        if (_pos >= _content.Length) return null;

        var c = _content[_pos];

        // String literal
        if (c == '(')
            return ParseStringLiteral();

        // Hex string
        if (c == '<')
        {
            if (_pos + 1 < _content.Length && _content[_pos + 1] == '<')
            {
                // Dictionary - skip
                _pos += 2;
                SkipDictionary();
                return null;
            }
            return ParseHexString();
        }

        // Array
        if (c == '[')
            return ParseArray();

        // Name
        if (c == '/')
            return ParseName();

        // Number or operator
        if (char.IsDigit((char)c) || c == '-' || c == '+' || c == '.')
            return ParseNumber();

        // Keyword/operator
        if (char.IsLetter((char)c) || c == '\'' || c == '"' || c == '*')
            return ParseKeyword();

        _pos++;
        return null;
    }

    private PdfString ParseStringLiteral()
    {
        var sb = new StringBuilder();
        _pos++; // Skip '('
        int depth = 1;

        while (_pos < _content.Length && depth > 0)
        {
            var c = _content[_pos];

            if (c == '\\' && _pos + 1 < _content.Length)
            {
                _pos++;
                var escaped = _content[_pos];
                switch (escaped)
                {
                    case (byte)'n': sb.Append('\n'); break;
                    case (byte)'r': sb.Append('\r'); break;
                    case (byte)'t': sb.Append('\t'); break;
                    case (byte)'b': sb.Append('\b'); break;
                    case (byte)'f': sb.Append('\f'); break;
                    case (byte)'(': sb.Append('('); break;
                    case (byte)')': sb.Append(')'); break;
                    case (byte)'\\': sb.Append('\\'); break;
                    default:
                        if (escaped >= '0' && escaped <= '7')
                        {
                            var octal = new StringBuilder();
                            octal.Append((char)escaped);
                            while (octal.Length < 3 && _pos + 1 < _content.Length &&
                                   _content[_pos + 1] >= '0' && _content[_pos + 1] <= '7')
                            {
                                _pos++;
                                octal.Append((char)_content[_pos]);
                            }
                            var code = Convert.ToInt32(octal.ToString(), 8);
                            sb.Append((char)code);
                        }
                        else
                        {
                            sb.Append((char)escaped);
                        }
                        break;
                }
            }
            else if (c == '(')
            {
                depth++;
                sb.Append((char)c);
            }
            else if (c == ')')
            {
                depth--;
                if (depth > 0) sb.Append((char)c);
            }
            else
            {
                sb.Append((char)c);
            }
            _pos++;
        }

        return new PdfString(sb.ToString());
    }

    private PdfString ParseHexString()
    {
        _pos++; // Skip '<'
        var hex = new StringBuilder();

        while (_pos < _content.Length && _content[_pos] != '>')
        {
            var c = _content[_pos];
            if (char.IsLetterOrDigit((char)c))
                hex.Append((char)c);
            _pos++;
        }
        _pos++; // Skip '>'

        var hexStr = hex.ToString();
        if (hexStr.Length % 2 == 1)
            hexStr += "0";

        var sb = new StringBuilder();
        for (int i = 0; i < hexStr.Length; i += 2)
        {
            var code = Convert.ToInt32(hexStr.Substring(i, 2), 16);
            sb.Append((char)code);
        }

        return new PdfString(sb.ToString());
    }

    private PdfArray ParseArray()
    {
        var result = new PdfArray();
        _pos++; // Skip '['

        while (_pos < _content.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _content.Length || _content[_pos] == ']')
            {
                _pos++;
                break;
            }

            var item = ParseToken();
            if (item is PdfObject pdfObj)
                result.Add(pdfObj);
        }

        return result;
    }

    private PdfName ParseName()
    {
        _pos++; // Skip '/'
        var sb = new StringBuilder();

        while (_pos < _content.Length)
        {
            var c = _content[_pos];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '/' ||
                c == '[' || c == ']' || c == '<' || c == '>' || c == '(' || c == ')')
                break;

            if (c == '#' && _pos + 2 < _content.Length)
            {
                var hex = Encoding.ASCII.GetString(_content, _pos + 1, 2);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
                {
                    sb.Append((char)code);
                    _pos += 3;
                    continue;
                }
            }

            sb.Append((char)c);
            _pos++;
        }

        return new PdfName(sb.ToString());
    }

    private PdfObject ParseNumber()
    {
        var sb = new StringBuilder();

        while (_pos < _content.Length)
        {
            var c = _content[_pos];
            if (char.IsDigit((char)c) || c == '-' || c == '+' || c == '.')
            {
                sb.Append((char)c);
                _pos++;
            }
            else
            {
                break;
            }
        }

        var str = sb.ToString();
        if (str.Contains('.'))
        {
            if (double.TryParse(str, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                return new PdfReal(d);
        }
        else
        {
            if (int.TryParse(str, out var i))
                return new PdfInteger(i);
        }

        return new PdfInteger(0);
    }

    private string ParseKeyword()
    {
        var sb = new StringBuilder();

        while (_pos < _content.Length)
        {
            var c = _content[_pos];
            if (char.IsLetterOrDigit((char)c) || c == '\'' || c == '"' || c == '*')
            {
                sb.Append((char)c);
                _pos++;
            }
            else
            {
                break;
            }
        }

        return sb.ToString();
    }

    private void SkipDictionary()
    {
        int depth = 1;
        while (_pos < _content.Length && depth > 0)
        {
            if (_pos + 1 < _content.Length)
            {
                if (_content[_pos] == '<' && _content[_pos + 1] == '<')
                {
                    depth++;
                    _pos += 2;
                    continue;
                }
                if (_content[_pos] == '>' && _content[_pos + 1] == '>')
                {
                    depth--;
                    _pos += 2;
                    continue;
                }
            }
            _pos++;
        }
    }

    private static double GetNumber(PdfObject obj)
    {
        return obj switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            _ => 0
        };
    }

    private static readonly HashSet<string> Operators = new()
    {
        // Graphics state
        "q", "Q", "cm", "w", "J", "j", "M", "d", "ri", "i", "gs",
        // Path construction
        "m", "l", "c", "v", "y", "h", "re",
        // Path painting
        "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n",
        // Clipping
        "W", "W*",
        // Text
        "BT", "ET", "Tc", "Tw", "Tz", "TL", "Tf", "Tr", "Ts",
        "Td", "TD", "Tm", "T*", "Tj", "TJ", "'", "\"",
        // Color
        "CS", "cs", "SC", "SCN", "sc", "scn", "G", "g", "RG", "rg", "K", "k",
        // Shading
        "sh",
        // XObject/Images
        "Do", "BI", "ID", "EI",
        // Marked content
        "MP", "DP", "BMC", "BDC", "EMC", "BX", "EX"
    };

    private static bool IsOperator(string token) => Operators.Contains(token);

    #endregion

    /// <summary>
    /// Internal graphics state for tracking transformations.
    /// </summary>
    private class GraphicsState
    {
        public double Ctm_a = 1, Ctm_b, Ctm_c, Ctm_d = 1, Ctm_e, Ctm_f;

        public void MultiplyCtm(double a, double b, double c, double d, double e, double f)
        {
            var na = a * Ctm_a + b * Ctm_c;
            var nb = a * Ctm_b + b * Ctm_d;
            var nc = c * Ctm_a + d * Ctm_c;
            var nd = c * Ctm_b + d * Ctm_d;
            var ne = e * Ctm_a + f * Ctm_c + Ctm_e;
            var nf = e * Ctm_b + f * Ctm_d + Ctm_f;

            Ctm_a = na; Ctm_b = nb; Ctm_c = nc;
            Ctm_d = nd; Ctm_e = ne; Ctm_f = nf;
        }

        public GraphicsState Clone() => new()
        {
            Ctm_a = Ctm_a, Ctm_b = Ctm_b, Ctm_c = Ctm_c,
            Ctm_d = Ctm_d, Ctm_e = Ctm_e, Ctm_f = Ctm_f
        };
    }
}
