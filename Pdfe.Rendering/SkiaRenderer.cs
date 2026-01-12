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
    private TextState _textState;
    private bool _inTextBlock;
    private SKTypeface? _currentTypeface;

    public RenderContext(SKCanvas canvas, PdfPage page, RenderOptions options)
    {
        _canvas = canvas;
        _page = page;
        _options = options;
        _stateStack = new Stack<GraphicsState>();
        _state = new GraphicsState();
        _textState = new TextState();
        _inTextBlock = false;
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
            case "J":
                if (operands.Count >= 1)
                    _state.LineCap = (int)ParseNumber(operands[0]);
                break;
            case "j":
                if (operands.Count >= 1)
                    _state.LineJoin = (int)ParseNumber(operands[0]);
                break;
            case "M":
                if (operands.Count >= 1)
                    _state.MiterLimit = (float)ParseNumber(operands[0]);
                break;
            case "d":
                // Dash pattern - for now just ignore (implement later if needed)
                break;
            case "ri":
                // Rendering intent - no effect on rendering for now
                break;
            case "i":
                // Flatness tolerance - no effect on rendering for now
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

            // Color (CMYK)
            case "k":
                if (operands.Count >= 4)
                    _state.FillColor = CmykToColor(
                        ParseNumber(operands[0]),
                        ParseNumber(operands[1]),
                        ParseNumber(operands[2]),
                        ParseNumber(operands[3]));
                break;
            case "K":
                if (operands.Count >= 4)
                    _state.StrokeColor = CmykToColor(
                        ParseNumber(operands[0]),
                        ParseNumber(operands[1]),
                        ParseNumber(operands[2]),
                        ParseNumber(operands[3]));
                break;

            // Extended graphics state
            case "gs":
                if (operands.Count >= 1)
                    ApplyExtGState(operands[0]);
                break;

            // XObject rendering (images and forms)
            case "Do":
                if (operands.Count >= 1)
                    RenderXObject(operands[0]);
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

            // Clipping path operators (#295)
            case "W":
                SetClippingPath(false);
                break;
            case "W*":
                SetClippingPath(true);
                break;

            // Marked content operators (#298)
            case "BMC":
                // Begin marked content - no visual effect
                break;
            case "BDC":
                // Begin marked content with property list - no visual effect
                break;
            case "EMC":
                // End marked content - no visual effect
                break;
            case "MP":
                // Marked content point - no visual effect
                break;
            case "DP":
                // Marked content point with property list - no visual effect
                break;

            // Shading operator (#300)
            case "sh":
                if (operands.Count >= 1)
                    RenderShading(operands[0]);
                break;

            // Type 3 font operators (#301)
            case "d0":
                // Set glyph width - only affects metrics, not rendering
                break;
            case "d1":
                // Set glyph width and bounding box - only affects metrics
                break;

            // Color space operators
            case "CS":
                // Set stroking color space - store for later use with SC/SCN
                if (operands.Count >= 1)
                    _state.StrokeColorSpace = operands[0].TrimStart('/');
                break;
            case "cs":
                // Set non-stroking color space
                if (operands.Count >= 1)
                    _state.FillColorSpace = operands[0].TrimStart('/');
                break;
            case "SC":
            case "SCN":
                // Set stroking color
                SetStrokingColor(operands);
                break;
            case "sc":
            case "scn":
                // Set non-stroking (fill) color
                SetNonStrokingColor(operands);
                break;

            // Text state operators
            case "BT":
                BeginText();
                break;
            case "ET":
                EndText();
                break;
            case "Tf":
                if (operands.Count >= 2)
                    SetFont(operands[0], ParseNumber(operands[1]));
                break;
            case "Td":
                if (operands.Count >= 2)
                    TextMove(ParseNumber(operands[0]), ParseNumber(operands[1]));
                break;
            case "TD":
                if (operands.Count >= 2)
                {
                    _textState.TextLeading = -(float)ParseNumber(operands[1]);
                    TextMove(ParseNumber(operands[0]), ParseNumber(operands[1]));
                }
                break;
            case "Tm":
                if (operands.Count >= 6)
                    SetTextMatrix(
                        ParseNumber(operands[0]), ParseNumber(operands[1]),
                        ParseNumber(operands[2]), ParseNumber(operands[3]),
                        ParseNumber(operands[4]), ParseNumber(operands[5]));
                break;
            case "T*":
                TextNewLine();
                break;
            case "Tc":
                if (operands.Count >= 1)
                    _textState.CharSpacing = (float)ParseNumber(operands[0]);
                break;
            case "Tw":
                if (operands.Count >= 1)
                    _textState.WordSpacing = (float)ParseNumber(operands[0]);
                break;
            case "Tz":
                if (operands.Count >= 1)
                    _textState.HorizontalScale = (float)ParseNumber(operands[0]);
                break;
            case "TL":
                if (operands.Count >= 1)
                    _textState.TextLeading = (float)ParseNumber(operands[0]);
                break;
            case "Tr":
                if (operands.Count >= 1)
                    _textState.RenderMode = (int)ParseNumber(operands[0]);
                break;
            case "Ts":
                if (operands.Count >= 1)
                    _textState.TextRise = (float)ParseNumber(operands[0]);
                break;

            // Text showing operators
            case "Tj":
                if (operands.Count >= 1)
                    ShowText(operands[0]);
                break;
            case "TJ":
                ShowTextArray(operands);
                break;
            case "'":
                TextNewLine();
                if (operands.Count >= 1)
                    ShowText(operands[0]);
                break;
            case "\"":
                if (operands.Count >= 3)
                {
                    _textState.WordSpacing = (float)ParseNumber(operands[0]);
                    _textState.CharSpacing = (float)ParseNumber(operands[1]);
                    TextNewLine();
                    ShowText(operands[2]);
                }
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
            Color = _state.StrokeColor.WithAlpha((byte)(_state.StrokeAlpha * 255)),
            StrokeWidth = (float)_state.LineWidth,
            StrokeCap = _state.LineCap switch
            {
                1 => SKStrokeCap.Round,
                2 => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt
            },
            StrokeJoin = _state.LineJoin switch
            {
                1 => SKStrokeJoin.Round,
                2 => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Miter
            },
            StrokeMiter = _state.MiterLimit,
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
            Color = _state.FillColor.WithAlpha((byte)(_state.FillAlpha * 255)),
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
            Color = _state.FillColor.WithAlpha((byte)(_state.FillAlpha * 255)),
            IsAntialias = _options.AntiAlias
        })
        {
            _canvas.DrawPath(_currentPath, fillPaint);
        }

        // Then stroke
        using (var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = _state.StrokeColor.WithAlpha((byte)(_state.StrokeAlpha * 255)),
            StrokeWidth = (float)_state.LineWidth,
            StrokeCap = _state.LineCap switch
            {
                1 => SKStrokeCap.Round,
                2 => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt
            },
            StrokeJoin = _state.LineJoin switch
            {
                1 => SKStrokeJoin.Round,
                2 => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Miter
            },
            StrokeMiter = _state.MiterLimit,
            IsAntialias = _options.AntiAlias
        })
        {
            _canvas.DrawPath(_currentPath, strokePaint);
        }

        _currentPath.Dispose();
        _currentPath = null;
    }

    #endregion

    #region Text Rendering

    private void BeginText()
    {
        _inTextBlock = true;
        _textState.Reset();
    }

    private void EndText()
    {
        _inTextBlock = false;
    }

    private void SetFont(string fontName, double fontSize)
    {
        // Remove leading / if present
        if (fontName.StartsWith("/"))
            fontName = fontName.Substring(1);

        _textState.FontName = fontName;
        _textState.FontSize = (float)fontSize;

        // Try to get the font from page resources to determine the base font
        var fontDict = _page.GetFont(fontName);
        var baseFont = fontDict?.GetNameOrNull("BaseFont") ?? "Helvetica";

        // Map PDF font names to SkiaSharp typefaces
        _currentTypeface = GetTypeface(baseFont);
    }

    private SKTypeface GetTypeface(string baseFont)
    {
        // Map standard PDF fonts to system fonts
        var family = baseFont switch
        {
            "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique"
                => "Helvetica",
            "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic"
                => "Times New Roman",
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique"
                => "Courier New",
            "Symbol" => "Symbol",
            "ZapfDingbats" => "Wingdings",
            _ => "Sans-Serif" // Default fallback
        };

        var style = SKFontStyle.Normal;
        if (baseFont.Contains("Bold") && baseFont.Contains("Italic"))
            style = SKFontStyle.BoldItalic;
        else if (baseFont.Contains("Bold"))
            style = SKFontStyle.Bold;
        else if (baseFont.Contains("Italic") || baseFont.Contains("Oblique"))
            style = SKFontStyle.Italic;

        return SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
    }

    private void TextMove(double tx, double ty)
    {
        // Td operator: Move to start of next line, offset by (tx, ty)
        _textState.TextMatrixE = _textState.LineMatrixE + (float)tx;
        _textState.TextMatrixF = _textState.LineMatrixF + (float)ty;
        _textState.LineMatrixE = _textState.TextMatrixE;
        _textState.LineMatrixF = _textState.TextMatrixF;
    }

    private void SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
        _textState.TextMatrixA = (float)a;
        _textState.TextMatrixB = (float)b;
        _textState.TextMatrixC = (float)c;
        _textState.TextMatrixD = (float)d;
        _textState.TextMatrixE = (float)e;
        _textState.TextMatrixF = (float)f;
        _textState.LineMatrixE = (float)e;
        _textState.LineMatrixF = (float)f;
    }

    private void TextNewLine()
    {
        // T* operator: Move to start of next line using leading
        TextMove(0, -_textState.TextLeading);
    }

    private void ShowText(string textOperand)
    {
        // Parse the string operand (removes parentheses and handles escapes)
        var text = ParsePdfString(textOperand);
        if (string.IsNullOrEmpty(text))
            return;

        RenderText(text);
    }

    private void ShowTextArray(List<string> operands)
    {
        // TJ operator: array of strings and position adjustments
        // The operands come as tokens: [, string1, number, string2, ], etc.
        foreach (var operand in operands)
        {
            if (operand == "[" || operand == "]")
                continue;

            if (operand.StartsWith("(") || operand.StartsWith("<"))
            {
                // It's a string
                var text = ParsePdfString(operand);
                if (!string.IsNullOrEmpty(text))
                    RenderText(text);
            }
            else if (double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var adjustment))
            {
                // It's a position adjustment (in thousandths of em)
                // Negative values move right, positive move left
                var xOffset = (float)(-adjustment * _textState.FontSize / 1000.0);
                _textState.TextMatrixE += xOffset * _textState.HorizontalScale / 100.0f;
            }
        }
    }

    private void RenderText(string text)
    {
        if (!_inTextBlock || _currentTypeface == null)
            return;

        using var font = new SKFont(_currentTypeface, _textState.FontSize);
        using var paint = new SKPaint(font)
        {
            Color = _state.FillColor,
            IsAntialias = _options.AntiAlias
        };

        // Calculate position in PDF coordinates
        var x = _textState.TextMatrixE;
        var y = _textState.TextMatrixF + _textState.TextRise;

        // The canvas has been transformed with Scale(scale, -scale) to flip Y for paths.
        // For text, we need to un-flip it so text appears right-side up.
        // Save state, apply local transform to flip text back, draw, restore.
        _canvas.Save();

        // Move to text position, then flip Y locally for this text
        _canvas.Translate(x, y);
        _canvas.Scale(1, -1); // Flip back for text

        // Draw text at origin (we've already translated)
        _canvas.DrawText(text, 0, 0, paint);

        _canvas.Restore();

        // Advance the text position
        var width = paint.MeasureText(text);
        var charCount = text.Length;
        var spaceCount = text.Count(c => c == ' ');

        // Apply character and word spacing
        width += charCount * _textState.CharSpacing;
        width += spaceCount * _textState.WordSpacing;
        width *= _textState.HorizontalScale / 100.0f;

        _textState.TextMatrixE += width;
    }

    private static string ParsePdfString(string operand)
    {
        if (string.IsNullOrEmpty(operand))
            return "";

        // Literal string: (text)
        if (operand.StartsWith("(") && operand.EndsWith(")"))
        {
            var content = operand.Substring(1, operand.Length - 2);
            return UnescapePdfString(content);
        }

        // Hex string: <hexdata>
        if (operand.StartsWith("<") && operand.EndsWith(">"))
        {
            var hex = operand.Substring(1, operand.Length - 2);
            return DecodeHexString(hex);
        }

        return operand;
    }

    private static string UnescapePdfString(string s)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var next = s[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i += 2; break;
                    case 'r': sb.Append('\r'); i += 2; break;
                    case 't': sb.Append('\t'); i += 2; break;
                    case 'b': sb.Append('\b'); i += 2; break;
                    case 'f': sb.Append('\f'); i += 2; break;
                    case '(': sb.Append('('); i += 2; break;
                    case ')': sb.Append(')'); i += 2; break;
                    case '\\': sb.Append('\\'); i += 2; break;
                    default:
                        // Octal escape \ddd
                        if (char.IsDigit(next))
                        {
                            var octal = "";
                            i++;
                            while (i < s.Length && octal.Length < 3 && char.IsDigit(s[i]) && s[i] < '8')
                            {
                                octal += s[i++];
                            }
                            if (int.TryParse(octal, out var code))
                                sb.Append((char)Convert.ToInt32(octal, 8));
                        }
                        else
                        {
                            sb.Append(next);
                            i += 2;
                        }
                        break;
                }
            }
            else
            {
                sb.Append(s[i++]);
            }
        }
        return sb.ToString();
    }

    private static string DecodeHexString(string hex)
    {
        // Remove whitespace
        hex = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Pad with 0 if odd length
        if (hex.Length % 2 != 0)
            hex += "0";

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, null, out var b))
                bytes[i] = b;
        }

        return Encoding.Latin1.GetString(bytes);
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

    private static SKColor CmykToColor(double c, double m, double y, double k)
    {
        // Simple CMYK to RGB conversion (not color-managed)
        // R = 255 × (1-C) × (1-K)
        // G = 255 × (1-M) × (1-K)
        // B = 255 × (1-Y) × (1-K)
        var r = (byte)Math.Clamp(255 * (1 - c) * (1 - k), 0, 255);
        var g = (byte)Math.Clamp(255 * (1 - m) * (1 - k), 0, 255);
        var b = (byte)Math.Clamp(255 * (1 - y) * (1 - k), 0, 255);
        return new SKColor(r, g, b);
    }

    #endregion

    #region Extended Graphics State (gs operator)

    private void ApplyExtGState(string nameOperand)
    {
        // Remove leading / if present
        var name = nameOperand.TrimStart('/');
        var extGState = _page.GetExtGState(name);
        if (extGState == null)
            return;

        // CA - Stroking alpha
        if (extGState.ContainsKey("CA"))
        {
            var alpha = extGState.GetNumber("CA", 1.0);
            _state.StrokeAlpha = (float)Math.Clamp(alpha, 0, 1);
        }

        // ca - Non-stroking (fill) alpha
        if (extGState.ContainsKey("ca"))
        {
            var alpha = extGState.GetNumber("ca", 1.0);
            _state.FillAlpha = (float)Math.Clamp(alpha, 0, 1);
        }

        // LW - Line width
        if (extGState.ContainsKey("LW"))
        {
            _state.LineWidth = extGState.GetNumber("LW", 1.0);
        }

        // LC - Line cap style
        if (extGState.ContainsKey("LC"))
        {
            _state.LineCap = (int)extGState.GetNumber("LC", 0);
        }

        // LJ - Line join style
        if (extGState.ContainsKey("LJ"))
        {
            _state.LineJoin = (int)extGState.GetNumber("LJ", 0);
        }

        // ML - Miter limit
        if (extGState.ContainsKey("ML"))
        {
            _state.MiterLimit = (float)extGState.GetNumber("ML", 10.0);
        }
    }

    #endregion

    #region XObject Rendering (Do operator)

    private void RenderXObject(string nameOperand)
    {
        // Remove leading / if present
        var name = nameOperand.TrimStart('/');
        var xobj = _page.GetXObject(name);
        if (xobj == null)
            return;

        if (xobj is not Pdfe.Core.Primitives.PdfStream stream)
            return;

        var subtype = stream.GetNameOrNull("Subtype");
        switch (subtype)
        {
            case "Image":
                RenderImageXObject(stream);
                break;
            case "Form":
                RenderFormXObject(stream);
                break;
        }
    }

    private void RenderImageXObject(Pdfe.Core.Primitives.PdfStream imageStream)
    {
        var width = imageStream.GetInt("Width", 0);
        var height = imageStream.GetInt("Height", 0);
        if (width <= 0 || height <= 0)
            return;

        var bitsPerComponent = imageStream.GetInt("BitsPerComponent", 8);
        var colorSpace = imageStream.GetNameOrNull("ColorSpace") ?? "DeviceRGB";
        var imageData = imageStream.DecodedData;

        // Try to decode image
        SKBitmap? bitmap = null;
        try
        {
            // Check if it's a DCT (JPEG) encoded image
            var filters = imageStream.Filters;
            if (filters.Contains("DCTDecode"))
            {
                // JPEG data - decode directly
                bitmap = SKBitmap.Decode(imageStream.EncodedData);
            }
            else
            {
                // Raw image data - create bitmap based on color space
                bitmap = CreateBitmapFromRawData(imageData, width, height, bitsPerComponent, colorSpace, imageStream);
            }

            if (bitmap == null)
                return;

            // Draw the image at unit square (0,0)-(1,1), the CTM handles positioning
            _canvas.Save();

            // Images are drawn into a 1x1 unit square, scaled by the CTM
            // We need to flip Y because images have origin at top-left
            _canvas.Scale(1.0f / width, -1.0f / height);
            _canvas.Translate(0, -height);

            using var paint = new SKPaint { IsAntialias = _options.AntiAlias };
            if (_state.FillAlpha < 1.0f)
            {
                paint.Color = paint.Color.WithAlpha((byte)(_state.FillAlpha * 255));
            }

            _canvas.DrawBitmap(bitmap, 0, 0, paint);
            _canvas.Restore();
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    private SKBitmap? CreateBitmapFromRawData(byte[] data, int width, int height, int bitsPerComponent, string colorSpace, Pdfe.Core.Primitives.PdfStream stream)
    {
        // Handle different color spaces
        int componentsPerPixel = colorSpace switch
        {
            "DeviceGray" => 1,
            "DeviceRGB" => 3,
            "DeviceCMYK" => 4,
            "CalGray" => 1,
            "CalRGB" => 3,
            _ => 3 // Default to RGB
        };

        // Check for indexed color space
        var csObj = stream.GetOptional("ColorSpace");
        if (csObj is Pdfe.Core.Primitives.PdfArray csArray && csArray.Count >= 1)
        {
            var csName = (csArray[0] as Pdfe.Core.Primitives.PdfName)?.Value;
            if (csName == "Indexed")
            {
                componentsPerPixel = 1; // Indexed uses palette lookup
            }
        }

        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var pixels = new byte[width * height * 4];

        try
        {
            int srcIndex = 0;
            int dstIndex = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r = 0, g = 0, b = 0, a = 255;

                    if (bitsPerComponent == 8)
                    {
                        switch (componentsPerPixel)
                        {
                            case 1: // Grayscale
                                if (srcIndex < data.Length)
                                {
                                    r = g = b = data[srcIndex++];
                                }
                                break;
                            case 3: // RGB
                                if (srcIndex + 2 < data.Length)
                                {
                                    r = data[srcIndex++];
                                    g = data[srcIndex++];
                                    b = data[srcIndex++];
                                }
                                break;
                            case 4: // CMYK
                                if (srcIndex + 3 < data.Length)
                                {
                                    var c = data[srcIndex++] / 255.0;
                                    var m = data[srcIndex++] / 255.0;
                                    var yy = data[srcIndex++] / 255.0;
                                    var k = data[srcIndex++] / 255.0;
                                    r = (byte)Math.Clamp(255 * (1 - c) * (1 - k), 0, 255);
                                    g = (byte)Math.Clamp(255 * (1 - m) * (1 - k), 0, 255);
                                    b = (byte)Math.Clamp(255 * (1 - yy) * (1 - k), 0, 255);
                                }
                                break;
                        }
                    }
                    else if (bitsPerComponent == 1)
                    {
                        // 1-bit monochrome
                        int byteIndex = srcIndex / 8;
                        int bitIndex = 7 - (srcIndex % 8);
                        if (byteIndex < data.Length)
                        {
                            int bit = (data[byteIndex] >> bitIndex) & 1;
                            r = g = b = (byte)(bit == 0 ? 0 : 255);
                        }
                        srcIndex++;
                    }

                    // RGBA format
                    pixels[dstIndex++] = r;
                    pixels[dstIndex++] = g;
                    pixels[dstIndex++] = b;
                    pixels[dstIndex++] = a;
                }

                // Handle row padding for 1-bit images
                if (bitsPerComponent == 1)
                {
                    srcIndex = ((srcIndex + 7) / 8) * 8; // Align to byte boundary
                }
            }

            // Copy pixels to bitmap
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                bitmap.SetPixels(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private void RenderFormXObject(Pdfe.Core.Primitives.PdfStream formStream)
    {
        // Form XObjects contain their own content stream
        // Get the form's content and render it recursively
        var formContent = formStream.DecodedData;
        if (formContent.Length == 0)
            return;

        _canvas.Save();

        // Apply the form's transformation matrix if present
        var matrixArray = formStream.GetOptional("Matrix") as Pdfe.Core.Primitives.PdfArray;
        if (matrixArray != null && matrixArray.Count >= 6)
        {
            var a = (float)matrixArray.GetNumber(0);
            var b = (float)matrixArray.GetNumber(1);
            var c = (float)matrixArray.GetNumber(2);
            var d = (float)matrixArray.GetNumber(3);
            var e = (float)matrixArray.GetNumber(4);
            var f = (float)matrixArray.GetNumber(5);
            var matrix = new SKMatrix(a, c, e, b, d, f, 0, 0, 1);
            _canvas.Concat(ref matrix);
        }

        // Parse and render the form's content stream
        var content = Encoding.Latin1.GetString(formContent);
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

        _canvas.Restore();
    }

    #endregion

    #region Clipping Path (W, W* operators) - Issue #295

    private void SetClippingPath(bool evenOdd)
    {
        if (_currentPath == null) return;

        _currentPath.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;

        // Apply the clipping path to the canvas
        _canvas.ClipPath(_currentPath, SKClipOperation.Intersect, _options.AntiAlias);

        // Note: The path is NOT disposed here - it will be used by the following
        // path-painting operator (like n, S, f) which will dispose it
    }

    #endregion

    #region Shading (sh operator) - Issue #300

    private void RenderShading(string nameOperand)
    {
        // Remove leading / if present
        var name = nameOperand.TrimStart('/');

        // Get the shading dictionary from page resources
        var shading = _page.GetShading(name);
        if (shading == null)
            return;

        var shadingType = shading.GetInt("ShadingType", 0);

        // Handle different shading types
        switch (shadingType)
        {
            case 2: // Axial shading (linear gradient)
                RenderAxialShading(shading);
                break;
            case 3: // Radial shading (radial gradient)
                RenderRadialShading(shading);
                break;
            // Types 1, 4-7 are more complex (function-based, mesh-based)
            // For now, just fill with background color as fallback
            default:
                // Shading fills the current clipping path
                break;
        }
    }

    private void RenderAxialShading(Pdfe.Core.Primitives.PdfDictionary shading)
    {
        // Get the coordinate array [x0, y0, x1, y1]
        var coords = shading.GetOptional("Coords") as Pdfe.Core.Primitives.PdfArray;
        if (coords == null || coords.Count < 4)
            return;

        var x0 = (float)coords.GetNumber(0);
        var y0 = (float)coords.GetNumber(1);
        var x1 = (float)coords.GetNumber(2);
        var y1 = (float)coords.GetNumber(3);

        // Get colors from the color space and function
        // For simplicity, use black to white gradient as fallback
        var startColor = SKColors.Black;
        var endColor = SKColors.White;

        // Try to get colors from the function if available
        var colorSpace = shading.GetNameOrNull("ColorSpace") ?? "DeviceGray";
        var function = shading.GetOptional("Function");

        // Create the gradient shader
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(x0, y0),
            new SKPoint(x1, y1),
            new[] { startColor, endColor },
            null,
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = _options.AntiAlias
        };

        // Fill the current clipping area
        var clipBounds = _canvas.LocalClipBounds;
        _canvas.DrawRect(clipBounds, paint);
    }

    private void RenderRadialShading(Pdfe.Core.Primitives.PdfDictionary shading)
    {
        // Get the coordinate array [x0, y0, r0, x1, y1, r1]
        var coords = shading.GetOptional("Coords") as Pdfe.Core.Primitives.PdfArray;
        if (coords == null || coords.Count < 6)
            return;

        var x0 = (float)coords.GetNumber(0);
        var y0 = (float)coords.GetNumber(1);
        var r0 = (float)coords.GetNumber(2);
        var x1 = (float)coords.GetNumber(3);
        var y1 = (float)coords.GetNumber(4);
        var r1 = (float)coords.GetNumber(5);

        // For simplicity, use black to white gradient as fallback
        var startColor = SKColors.Black;
        var endColor = SKColors.White;

        // Create the two-point conical gradient
        using var shader = SKShader.CreateTwoPointConicalGradient(
            new SKPoint(x0, y0), r0,
            new SKPoint(x1, y1), r1,
            new[] { startColor, endColor },
            null,
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = _options.AntiAlias
        };

        // Fill the current clipping area
        var clipBounds = _canvas.LocalClipBounds;
        _canvas.DrawRect(clipBounds, paint);
    }

    #endregion

    #region Color Space Operators (SC, SCN, sc, scn)

    private void SetStrokingColor(List<string> operands)
    {
        var color = ParseColorFromOperands(operands, _state.StrokeColorSpace);
        if (color.HasValue)
            _state.StrokeColor = color.Value;
    }

    private void SetNonStrokingColor(List<string> operands)
    {
        var color = ParseColorFromOperands(operands, _state.FillColorSpace);
        if (color.HasValue)
            _state.FillColor = color.Value;
    }

    private SKColor? ParseColorFromOperands(List<string> operands, string colorSpace)
    {
        // Filter out pattern names (start with /)
        var values = operands.Where(o => !o.StartsWith("/")).ToList();

        return colorSpace switch
        {
            "DeviceGray" or "CalGray" when values.Count >= 1 =>
                GrayToColor(ParseNumber(values[0])),

            "DeviceRGB" or "CalRGB" when values.Count >= 3 =>
                RgbToColor(
                    ParseNumber(values[0]),
                    ParseNumber(values[1]),
                    ParseNumber(values[2])),

            "DeviceCMYK" when values.Count >= 4 =>
                CmykToColor(
                    ParseNumber(values[0]),
                    ParseNumber(values[1]),
                    ParseNumber(values[2]),
                    ParseNumber(values[3])),

            // Pattern color space - the pattern name is handled separately
            "Pattern" when operands.Any(o => o.StartsWith("/")) =>
                null, // Pattern fills are handled by pattern rendering

            // ICCBased, Lab, Indexed, Separation, DeviceN - fallback behavior
            _ when values.Count >= 3 =>
                RgbToColor(
                    ParseNumber(values[0]),
                    ParseNumber(values[1]),
                    ParseNumber(values[2])),

            _ when values.Count >= 1 =>
                GrayToColor(ParseNumber(values[0])),

            _ => null
        };
    }

    #endregion

    #region Inline Images (BI, ID, EI operators) - Issue #297

    private void RenderInlineImage(string content, ref int tokenIndex, List<string> tokens)
    {
        // Inline images are complex to parse from tokenized content
        // The format is: BI <dict entries> ID <image data> EI
        // For now, we'll handle this by looking for the pattern in raw content

        // This is a stub - inline images require special handling during tokenization
        // because the image data between ID and EI is binary and not tokenized
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
    public float FillAlpha { get; set; } = 1.0f;
    public float StrokeAlpha { get; set; } = 1.0f;
    public int LineCap { get; set; } = 0;  // 0=Butt, 1=Round, 2=Square
    public int LineJoin { get; set; } = 0; // 0=Miter, 1=Round, 2=Bevel
    public float MiterLimit { get; set; } = 10.0f;
    public string FillColorSpace { get; set; } = "DeviceGray";
    public string StrokeColorSpace { get; set; } = "DeviceGray";

    public GraphicsState Clone()
    {
        return new GraphicsState
        {
            FillColor = FillColor,
            StrokeColor = StrokeColor,
            LineWidth = LineWidth,
            FillAlpha = FillAlpha,
            StrokeAlpha = StrokeAlpha,
            LineCap = LineCap,
            LineJoin = LineJoin,
            MiterLimit = MiterLimit,
            FillColorSpace = FillColorSpace,
            StrokeColorSpace = StrokeColorSpace
        };
    }
}

/// <summary>
/// Text state for rendering text operators.
/// </summary>
internal class TextState
{
    public string FontName { get; set; } = "";
    public float FontSize { get; set; } = 12;
    public float CharSpacing { get; set; } = 0;
    public float WordSpacing { get; set; } = 0;
    public float HorizontalScale { get; set; } = 100;
    public float TextLeading { get; set; } = 0;
    public float TextRise { get; set; } = 0;
    public int RenderMode { get; set; } = 0; // 0 = fill, 1 = stroke, 2 = fill+stroke

    // Text matrix components (Tm operator sets this)
    public float TextMatrixA { get; set; } = 1;
    public float TextMatrixB { get; set; } = 0;
    public float TextMatrixC { get; set; } = 0;
    public float TextMatrixD { get; set; } = 1;
    public float TextMatrixE { get; set; } = 0; // X position
    public float TextMatrixF { get; set; } = 0; // Y position

    // Line matrix (start of current line)
    public float LineMatrixE { get; set; } = 0;
    public float LineMatrixF { get; set; } = 0;

    public void Reset()
    {
        TextMatrixA = 1;
        TextMatrixB = 0;
        TextMatrixC = 0;
        TextMatrixD = 1;
        TextMatrixE = 0;
        TextMatrixF = 0;
        LineMatrixE = 0;
        LineMatrixF = 0;
    }

    public TextState Clone()
    {
        return new TextState
        {
            FontName = FontName,
            FontSize = FontSize,
            CharSpacing = CharSpacing,
            WordSpacing = WordSpacing,
            HorizontalScale = HorizontalScale,
            TextLeading = TextLeading,
            TextRise = TextRise,
            RenderMode = RenderMode,
            TextMatrixA = TextMatrixA,
            TextMatrixB = TextMatrixB,
            TextMatrixC = TextMatrixC,
            TextMatrixD = TextMatrixD,
            TextMatrixE = TextMatrixE,
            TextMatrixF = TextMatrixF,
            LineMatrixE = LineMatrixE,
            LineMatrixF = LineMatrixF
        };
    }
}
