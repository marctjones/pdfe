using System.Globalization;
using System.Text;
using Pdfe.Core.Document;
using SkiaSharp;

namespace Pdfe.Rendering;

/// <summary>
/// Renders PDF pages to SkiaSharp bitmaps.
/// </summary>
public class SkiaRenderer
{
    /// <summary>
    /// Render a PDF page to a bitmap with default options (150 DPI).
    /// </summary>
    public SKBitmap RenderPage(PdfPage page)
    {
        return RenderPage(page, new RenderOptions());
    }

    /// <summary>
    /// Render a PDF page to a bitmap with specified options.
    /// </summary>
    public SKBitmap RenderPage(PdfPage page, RenderOptions options)
    {
        // Calculate pixel dimensions
        var scale = options.Dpi / 72.0;
        var width = (int)Math.Round(page.Width * scale);
        var height = (int)Math.Round(page.Height * scale);

        // Create bitmap
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);

        // Fill background
        canvas.Clear(options.BackgroundColor);

        // Set up coordinate transformation:
        // PDF: origin at bottom-left, Y increases upward
        // Skia: origin at top-left, Y increases downward
        // We need to: scale, then flip Y, then translate
        canvas.Scale((float)scale, -(float)scale);
        canvas.Translate(0, -(float)page.Height);

        // Render content
        var context = new RenderContext(canvas, page, options);
        context.Render();

        return bitmap;
    }
}

/// <summary>
/// Context for rendering PDF content stream operators.
/// </summary>
internal class RenderContext
{
    private readonly SKCanvas _canvas;
    private readonly PdfPage _page;
    private readonly RenderOptions _options;
    private readonly Stack<GraphicsState> _stateStack;
    private GraphicsState _state;
    private SKPath? _currentPath;

    public RenderContext(SKCanvas canvas, PdfPage page, RenderOptions options)
    {
        _canvas = canvas;
        _page = page;
        _options = options;
        _stateStack = new Stack<GraphicsState>();
        _state = new GraphicsState();
    }

    public void Render()
    {
        var contentBytes = _page.GetContentStreamBytes();
        if (contentBytes.Length == 0)
            return;

        var content = Encoding.Latin1.GetString(contentBytes);
        var tokens = Tokenize(content);
        var operands = new List<string>();

        foreach (var token in tokens)
        {
            if (IsOperator(token))
            {
                ExecuteOperator(token, operands);
                operands.Clear();
            }
            else
            {
                operands.Add(token);
            }
        }
    }

    private void ExecuteOperator(string op, List<string> operands)
    {
        switch (op)
        {
            // Graphics state
            case "q":
                SaveState();
                break;
            case "Q":
                RestoreState();
                break;
            case "cm":
                if (operands.Count >= 6)
                    ApplyTransform(operands);
                break;
            case "w":
                if (operands.Count >= 1)
                    _state.LineWidth = ParseNumber(operands[0]);
                break;

            // Color (grayscale)
            case "g":
                if (operands.Count >= 1)
                    _state.FillColor = GrayToColor(ParseNumber(operands[0]));
                break;
            case "G":
                if (operands.Count >= 1)
                    _state.StrokeColor = GrayToColor(ParseNumber(operands[0]));
                break;

            // Color (RGB)
            case "rg":
                if (operands.Count >= 3)
                    _state.FillColor = RgbToColor(
                        ParseNumber(operands[0]),
                        ParseNumber(operands[1]),
                        ParseNumber(operands[2]));
                break;
            case "RG":
                if (operands.Count >= 3)
                    _state.StrokeColor = RgbToColor(
                        ParseNumber(operands[0]),
                        ParseNumber(operands[1]),
                        ParseNumber(operands[2]));
                break;

            // Path construction
            case "m":
                if (operands.Count >= 2)
                    MoveTo(ParseNumber(operands[0]), ParseNumber(operands[1]));
                break;
            case "l":
                if (operands.Count >= 2)
                    LineTo(ParseNumber(operands[0]), ParseNumber(operands[1]));
                break;
            case "c":
                if (operands.Count >= 6)
                    CurveTo(
                        ParseNumber(operands[0]), ParseNumber(operands[1]),
                        ParseNumber(operands[2]), ParseNumber(operands[3]),
                        ParseNumber(operands[4]), ParseNumber(operands[5]));
                break;
            case "v":
                if (operands.Count >= 4)
                    CurveToV(
                        ParseNumber(operands[0]), ParseNumber(operands[1]),
                        ParseNumber(operands[2]), ParseNumber(operands[3]));
                break;
            case "y":
                if (operands.Count >= 4)
                    CurveToY(
                        ParseNumber(operands[0]), ParseNumber(operands[1]),
                        ParseNumber(operands[2]), ParseNumber(operands[3]));
                break;
            case "h":
                ClosePath();
                break;
            case "re":
                if (operands.Count >= 4)
                    Rectangle(
                        ParseNumber(operands[0]), ParseNumber(operands[1]),
                        ParseNumber(operands[2]), ParseNumber(operands[3]));
                break;

            // Path painting
            case "S":
                StrokePath();
                break;
            case "s":
                ClosePath();
                StrokePath();
                break;
            case "f":
            case "F":
                FillPath(false);
                break;
            case "f*":
                FillPath(true);
                break;
            case "B":
                FillAndStroke(false);
                break;
            case "B*":
                FillAndStroke(true);
                break;
            case "b":
                ClosePath();
                FillAndStroke(false);
                break;
            case "b*":
                ClosePath();
                FillAndStroke(true);
                break;
            case "n":
                // End path without fill or stroke (no-op)
                _currentPath?.Dispose();
                _currentPath = null;
                break;

            // Text (basic - just skip for now)
            case "BT":
            case "ET":
            case "Tf":
            case "Td":
            case "TD":
            case "Tm":
            case "T*":
            case "Tj":
            case "TJ":
            case "'":
            case "\"":
            case "Tc":
            case "Tw":
            case "Tz":
            case "TL":
            case "Tr":
            case "Ts":
                // Text operators - skip for basic path rendering
                break;

            // Ignore unknown operators
            default:
                break;
        }
    }

    #region State Management

    private void SaveState()
    {
        _stateStack.Push(_state.Clone());
        _canvas.Save();
    }

    private void RestoreState()
    {
        if (_stateStack.Count > 0)
        {
            _state = _stateStack.Pop();
            _canvas.Restore();
        }
    }

    private void ApplyTransform(List<string> operands)
    {
        var a = (float)ParseNumber(operands[0]);
        var b = (float)ParseNumber(operands[1]);
        var c = (float)ParseNumber(operands[2]);
        var d = (float)ParseNumber(operands[3]);
        var e = (float)ParseNumber(operands[4]);
        var f = (float)ParseNumber(operands[5]);

        var matrix = new SKMatrix(a, c, e, b, d, f, 0, 0, 1);
        _canvas.Concat(ref matrix);
    }

    #endregion

    #region Path Construction

    private void MoveTo(double x, double y)
    {
        _currentPath ??= new SKPath();
        _currentPath.MoveTo((float)x, (float)y);
    }

    private void LineTo(double x, double y)
    {
        _currentPath ??= new SKPath();
        _currentPath.LineTo((float)x, (float)y);
    }

    private void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        _currentPath ??= new SKPath();
        _currentPath.CubicTo((float)x1, (float)y1, (float)x2, (float)y2, (float)x3, (float)y3);
    }

    private void CurveToV(double x2, double y2, double x3, double y3)
    {
        // v operator: current point replicated as first control point
        if (_currentPath == null) return;
        var last = _currentPath.LastPoint;
        _currentPath.CubicTo(last.X, last.Y, (float)x2, (float)y2, (float)x3, (float)y3);
    }

    private void CurveToY(double x1, double y1, double x3, double y3)
    {
        // y operator: endpoint replicated as second control point
        _currentPath ??= new SKPath();
        _currentPath.CubicTo((float)x1, (float)y1, (float)x3, (float)y3, (float)x3, (float)y3);
    }

    private void ClosePath()
    {
        _currentPath?.Close();
    }

    private void Rectangle(double x, double y, double w, double h)
    {
        _currentPath ??= new SKPath();
        _currentPath.AddRect(new SKRect((float)x, (float)y, (float)(x + w), (float)(y + h)));
    }

    #endregion

    #region Path Painting

    private void StrokePath()
    {
        if (_currentPath == null) return;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = _state.StrokeColor,
            StrokeWidth = (float)_state.LineWidth,
            IsAntialias = _options.AntiAlias
        };

        _canvas.DrawPath(_currentPath, paint);
        _currentPath.Dispose();
        _currentPath = null;
    }

    private void FillPath(bool evenOdd)
    {
        if (_currentPath == null) return;

        _currentPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = _state.FillColor,
            IsAntialias = _options.AntiAlias
        };

        _canvas.DrawPath(_currentPath, paint);
        _currentPath.Dispose();
        _currentPath = null;
    }

    private void FillAndStroke(bool evenOdd)
    {
        if (_currentPath == null) return;

        _currentPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        // Fill first
        using (var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = _state.FillColor,
            IsAntialias = _options.AntiAlias
        })
        {
            _canvas.DrawPath(_currentPath, fillPaint);
        }

        // Then stroke
        using (var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = _state.StrokeColor,
            StrokeWidth = (float)_state.LineWidth,
            IsAntialias = _options.AntiAlias
        })
        {
            _canvas.DrawPath(_currentPath, strokePaint);
        }

        _currentPath.Dispose();
        _currentPath = null;
    }

    #endregion

    #region Color Conversion

    private static SKColor GrayToColor(double gray)
    {
        var g = (byte)Math.Clamp(gray * 255, 0, 255);
        return new SKColor(g, g, g);
    }

    private static SKColor RgbToColor(double r, double g, double b)
    {
        return new SKColor(
            (byte)Math.Clamp(r * 255, 0, 255),
            (byte)Math.Clamp(g * 255, 0, 255),
            (byte)Math.Clamp(b * 255, 0, 255));
    }

    #endregion

    #region Tokenizer

    private static List<string> Tokenize(string content)
    {
        var tokens = new List<string>();
        var i = 0;
        var len = content.Length;

        while (i < len)
        {
            // Skip whitespace
            while (i < len && char.IsWhiteSpace(content[i]))
                i++;

            if (i >= len)
                break;

            var c = content[i];

            // Skip comments
            if (c == '%')
            {
                while (i < len && content[i] != '\n' && content[i] != '\r')
                    i++;
                continue;
            }

            // String literal
            if (c == '(')
            {
                var start = i;
                var depth = 1;
                i++;
                while (i < len && depth > 0)
                {
                    if (content[i] == '\\' && i + 1 < len)
                    {
                        i += 2; // Skip escape
                        continue;
                    }
                    if (content[i] == '(') depth++;
                    else if (content[i] == ')') depth--;
                    i++;
                }
                tokens.Add(content[start..i]);
                continue;
            }

            // Hex string
            if (c == '<' && i + 1 < len && content[i + 1] != '<')
            {
                var start = i;
                i++;
                while (i < len && content[i] != '>')
                    i++;
                i++; // Skip '>'
                tokens.Add(content[start..i]);
                continue;
            }

            // Dictionary start/end
            if (c == '<' && i + 1 < len && content[i + 1] == '<')
            {
                tokens.Add("<<");
                i += 2;
                continue;
            }
            if (c == '>' && i + 1 < len && content[i + 1] == '>')
            {
                tokens.Add(">>");
                i += 2;
                continue;
            }

            // Array delimiters
            if (c == '[' || c == ']')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            // Name
            if (c == '/')
            {
                var start = i;
                i++;
                while (i < len && !IsDelimiterOrWhitespace(content[i]))
                    i++;
                tokens.Add(content[start..i]);
                continue;
            }

            // Number or operator
            var tokenStart = i;
            while (i < len && !IsDelimiterOrWhitespace(content[i]))
                i++;

            if (i > tokenStart)
                tokens.Add(content[tokenStart..i]);
        }

        return tokens;
    }

    private static bool IsDelimiterOrWhitespace(char c)
    {
        return char.IsWhiteSpace(c) ||
               c == '(' || c == ')' ||
               c == '<' || c == '>' ||
               c == '[' || c == ']' ||
               c == '/' || c == '%';
    }

    private static bool IsOperator(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        // If it starts with a digit, minus, or period, it's likely a number
        var c = token[0];
        if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
            return false;

        // Names start with /
        if (c == '/')
            return false;

        // Strings
        if (c == '(' || c == '<')
            return false;

        // Arrays/dicts
        if (c == '[' || c == ']')
            return false;
        if (token == "<<" || token == ">>")
            return false;

        return true;
    }

    private static double ParseNumber(string s)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return result;
        return 0;
    }

    #endregion
}

/// <summary>
/// Graphics state for rendering.
/// </summary>
internal class GraphicsState
{
    public SKColor FillColor { get; set; } = SKColors.Black;
    public SKColor StrokeColor { get; set; } = SKColors.Black;
    public double LineWidth { get; set; } = 1;

    public GraphicsState Clone()
    {
        return new GraphicsState
        {
            FillColor = FillColor,
            StrokeColor = StrokeColor,
            LineWidth = LineWidth
        };
    }
}
