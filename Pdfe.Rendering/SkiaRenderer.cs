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
    private string _currentFontEncoding;

    // Glyph widths parsed from the current font dictionary's /Widths array.
    // Null when unavailable (e.g. standard 14 fonts that omit /Widths), in which
    // case we fall back to Skia's MeasureText on the system typeface.
    private float[]? _currentFontWidths;
    private int _currentFontFirstChar;
    private float _currentFontMissingWidth;

    // Per-font character-code → Unicode map, built from /BaseEncoding +
    // /Differences when /Encoding is a dictionary. Null for the common case
    // of a simple name encoding (WinAnsiEncoding/MacRomanEncoding), in which
    // case DecodeTextBytes uses the raw codepage.
    private char[]? _currentCodeToUnicode;
    // Inverse map, populated whenever _currentCodeToUnicode is; lets
    // MeasurePdfAdvance go from Unicode text back to PDF byte codes for
    // indexing /Widths. When a Unicode char appears at multiple codes we
    // keep the first (lowest) to match the likely intent.
    private Dictionary<char, byte>? _currentUnicodeToCode;

    // Typefaces loaded from the PDF's own embedded font streams
    // (/FontFile2 = TrueType, /FontFile3 = OpenType/CFF). Keyed by resource
    // name (e.g. "TT0") so repeated SetFont calls for the same font within a
    // page don't re-parse. Disposed at the end of Render().
    private readonly Dictionary<string, SKTypeface> _embeddedTypefaces = new();

    // Type0 / CID font state. Type0 fonts use 2-byte-per-character codes and
    // index a descendant font's /W array for widths (different format from the
    // simple-font /Widths). When _currentFontIsType0 is true, content-stream
    // bytes must be parsed 2 at a time and rendered via glyph ID, not Unicode.
    private bool _currentFontIsType0;
    private Dictionary<int, float>? _currentCidWidths;
    private float _currentCidDefaultWidth = 1000f;

    public RenderContext(SKCanvas canvas, PdfPage page, RenderOptions options)
    {
        _canvas = canvas;
        _page = page;
        _options = options;
        _stateStack = new Stack<GraphicsState>();
        _state = new GraphicsState();
        _textState = new TextState();
        _inTextBlock = false;
        _currentFontEncoding = "WinAnsiEncoding"; // Default encoding

        // Register code pages encoding provider for Windows-1252, Mac Roman, etc.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Render()
    {
        try
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
        finally
        {
            foreach (var typeface in _embeddedTypefaces.Values)
                typeface.Dispose();
            _embeddedTypefaces.Clear();
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

        // Try to get the font from page resources to determine the base font and encoding
        var fontDict = _page.GetFont(fontName);
        var baseFont = fontDict?.GetNameOrNull("BaseFont") ?? "Helvetica";

        // /Encoding can be either a Name (e.g. /WinAnsiEncoding) or a Dictionary
        // with /BaseEncoding and /Differences. The dictionary form is how embedded
        // subset fonts remap small character codes to specific glyphs — without
        // handling it, text decodes as control characters and renders invisibly.
        // Must resolve the indirect reference; most real PDFs use `/Encoding N 0 R`.
        var encodingDict = fontDict != null ? ResolveDict(fontDict, "Encoding") : null;
        var encodingName = fontDict?.GetNameOrNull("Encoding")
                           ?? encodingDict?.GetNameOrNull("BaseEncoding")
                           ?? "WinAnsiEncoding";

        _currentFontEncoding = encodingName;
        _currentCodeToUnicode = null;
        _currentUnicodeToCode = null;
        if (encodingDict != null)
        {
            BuildEncodingMaps(encodingDict, encodingName);
        }

        // Prefer a typeface loaded from the PDF's own embedded font stream
        // (/FontFile2 = TrueType, /FontFile3 = OpenType/CFF). When no embedded
        // data is present or the format isn't SkiaSharp-loadable (e.g. /FontFile
        // is raw Type1 PostScript), fall through to the system-font mapping.
        _currentTypeface = TryLoadEmbeddedTypeface(fontName, fontDict) ?? GetTypeface(baseFont);

        // Type0 (composite CID) fonts need a completely different content-stream
        // parse (2 bytes per character, widths indexed via /W not /Widths).
        _currentFontIsType0 = fontDict?.GetNameOrNull("Subtype") == "Type0";
        _currentCidWidths = null;
        _currentCidDefaultWidth = 1000f;
        if (_currentFontIsType0 && fontDict != null)
        {
            var descendants = ResolveArray(fontDict, "DescendantFonts");
            if (descendants != null && descendants.Count > 0 &&
                _page.Document.Resolve(descendants[0]) is Pdfe.Core.Primitives.PdfDictionary cidFont)
            {
                _currentCidDefaultWidth = (float)cidFont.GetNumber("DW", 1000);
                var w = ResolveArray(cidFont, "W");
                if (w != null)
                    _currentCidWidths = ParseWArray(w);
            }
        }

        // Parse the font's glyph width table. When present, we'll use PDF widths
        // for cursor advance instead of Skia's MeasureText (which uses the
        // fallback system typeface's metrics — wrong for embedded subset fonts).
        _currentFontWidths = null;
        _currentFontFirstChar = 0;
        _currentFontMissingWidth = 0f;
        if (fontDict != null)
        {
            var widthsArray = ResolveArray(fontDict, "Widths");
            if (widthsArray != null && widthsArray.Count > 0)
            {
                _currentFontFirstChar = fontDict.GetInt("FirstChar", 0);
                var widths = new float[widthsArray.Count];
                for (int i = 0; i < widthsArray.Count; i++)
                    widths[i] = (float)widthsArray.GetNumber(i);
                _currentFontWidths = widths;
            }
            var descriptor = ResolveDict(fontDict, "FontDescriptor");
            if (descriptor != null)
                _currentFontMissingWidth = (float)descriptor.GetNumber("MissingWidth", 0);
        }
    }

    // Resolve `dict[key]` as a dictionary, following indirect references.
    // PdfDictionary.GetDictionaryOrNull does a direct type-check and misses the
    // common case where the value is a `N 0 R` reference — most FontDescriptor,
    // /Widths, and /Encoding entries in real PDFs are stored that way.
    private Pdfe.Core.Primitives.PdfDictionary? ResolveDict(
        Pdfe.Core.Primitives.PdfDictionary dict, string key)
    {
        var obj = dict.GetOptional(key);
        if (obj == null) return null;
        var resolved = _page.Document.Resolve(obj);
        return resolved as Pdfe.Core.Primitives.PdfDictionary;
    }

    private Pdfe.Core.Primitives.PdfArray? ResolveArray(
        Pdfe.Core.Primitives.PdfDictionary dict, string key)
    {
        var obj = dict.GetOptional(key);
        if (obj == null) return null;
        var resolved = _page.Document.Resolve(obj);
        return resolved as Pdfe.Core.Primitives.PdfArray;
    }

    // Parse the /W array of a CIDFont (PDF spec 9.7.4.3). Two forms are
    // interleaved in a single array:
    //   cid [w1 w2 w3 ...]     → assigns w1..wN to cid, cid+1, cid+2, ...
    //   cid_start cid_end w    → assigns w to every CID in [cid_start, cid_end]
    // Widths are in glyph units (1/1000 of the designed em).
    private static Dictionary<int, float> ParseWArray(Pdfe.Core.Primitives.PdfArray w)
    {
        var map = new Dictionary<int, float>();
        int i = 0;
        while (i < w.Count)
        {
            if (!IsNumber(w[i])) { i++; continue; }
            int cid = (int)w.GetNumber(i);
            i++;
            if (i >= w.Count) break;

            if (w[i] is Pdfe.Core.Primitives.PdfArray inner)
            {
                for (int j = 0; j < inner.Count; j++)
                    map[cid + j] = (float)inner.GetNumber(j);
                i++;
            }
            else if (IsNumber(w[i]) && i + 1 < w.Count && IsNumber(w[i + 1]))
            {
                int endCid = (int)w.GetNumber(i);
                float width = (float)w.GetNumber(i + 1);
                for (int c = cid; c <= endCid; c++)
                    map[c] = width;
                i += 2;
            }
            else
            {
                i++; // Malformed — skip and recover.
            }
        }
        return map;
    }

    private static bool IsNumber(Pdfe.Core.Primitives.PdfObject o) =>
        o is Pdfe.Core.Primitives.PdfInteger || o is Pdfe.Core.Primitives.PdfReal;

    // Load the font's embedded file (TrueType or OpenType/CFF) as an SKTypeface
    // so glyphs render in the face the PDF actually specifies, with the widths
    // and kerning the PDF's /Widths table was authored against. Cached per-font
    // for the life of this RenderContext; disposed at end of Render().
    private SKTypeface? TryLoadEmbeddedTypeface(string fontName, Pdfe.Core.Primitives.PdfDictionary? fontDict)
    {
        if (_embeddedTypefaces.TryGetValue(fontName, out var cached))
            return cached;

        if (fontDict == null) return null;

        // Handle both simple and Type0 (CID) fonts: Type0 carries the embedded
        // file inside its /DescendantFonts[0]/FontDescriptor, not on itself.
        var descriptor = ResolveDict(fontDict, "FontDescriptor");
        if (descriptor == null)
        {
            var descendants = ResolveArray(fontDict, "DescendantFonts");
            if (descendants != null && descendants.Count > 0)
            {
                var descendantObj = _page.Document.Resolve(descendants[0]);
                if (descendantObj is Pdfe.Core.Primitives.PdfDictionary cidFontDict)
                    descriptor = ResolveDict(cidFontDict, "FontDescriptor");
            }
        }
        if (descriptor == null) return null;

        // /FontFile2 (TrueType) → SkiaSharp loads directly.
        // /FontFile3 (OpenType/CFF) → if already SFNT-wrapped, Skia loads it;
        //   if it's raw Type1C/CIDFontType0C (more common in modern PDFs),
        //   we wrap it in a minimal OpenType container first.
        // /FontFile (raw Type1 PostScript) — Skia can't load directly. Skipped.
        var ff2 = descriptor.GetOptional("FontFile2");
        var ff3 = descriptor.GetOptional("FontFile3");

        byte[]? fontBytes = null;
        bool isCff = false;
        if (ff2 != null && _page.Document.Resolve(ff2) is Pdfe.Core.Primitives.PdfStream s2)
        {
            try { fontBytes = s2.DecodedData; } catch { }
        }
        else if (ff3 != null && _page.Document.Resolve(ff3) is Pdfe.Core.Primitives.PdfStream s3)
        {
            try { fontBytes = s3.DecodedData; } catch { }
            var subtype = s3.GetNameOrNull("Subtype");
            // Type1C and CIDFontType0C are raw CFF without SFNT wrapper; OpenType
            // is already SFNT-wrapped and passes through.
            isCff = subtype == "Type1C" || subtype == "CIDFontType0C";
        }
        if (fontBytes == null || fontBytes.Length == 0) return null;

        // For raw CFF (Type1C / CIDFontType0C), synthesize an OpenType container
        // so Skia can load it. The wrapper's cmap has been independently verified
        // (CffWrapperTests.Wrapped_CffSkiaCanResolveKnownGlyphs) — Skia resolves
        // every Unicode char to the correct CFF glyph index.
        byte[] loadableBytes = fontBytes;
        if (isCff)
        {
            var wrapped = TryWrapCffAsOpenType(fontBytes, fontDict, descriptor);
            if (wrapped != null) loadableBytes = wrapped;
        }

        SKTypeface? typeface;
        try
        {
            using var data = SKData.CreateCopy(loadableBytes);
            typeface = SKTypeface.FromData(data);
        }
        catch { typeface = null; }

        if (typeface == null) return null;
        _embeddedTypefaces[fontName] = typeface;
        return typeface;
    }

    private byte[]? TryWrapCffAsOpenType(
        byte[] cff,
        Pdfe.Core.Primitives.PdfDictionary fontDict,
        Pdfe.Core.Primitives.PdfDictionary descriptor)
    {
        var cffInfo = Fonts.CffParser.Parse(cff);
        if (cffInfo == null) return null;

        // Build Unicode → glyph-index map and glyph-index → PDF-width map.
        // Both derive from walking the PDF's character codes 0..255, resolving
        // each to (Unicode, glyph name) and then looking up the glyph index in
        // the CFF charset.
        var unicodeToGlyph = new Dictionary<char, int>(256);
        var glyphWidths = new Dictionary<int, ushort>(256);
        for (int code = 0; code < 256; code++)
        {
            char unicode = GetUnicodeForCode((byte)code);
            if (unicode == '\0') continue;
            if (!AdobeGlyphList.TryGetName(unicode, out var glyphName)) continue;
            if (!cffInfo.GlyphNameToIndex.TryGetValue(glyphName, out var glyphIndex)) continue;

            if (!unicodeToGlyph.ContainsKey(unicode))
                unicodeToGlyph[unicode] = glyphIndex;

            // If /Widths covers this code, use it as the per-glyph hmtx width.
            if (_currentFontWidths != null)
            {
                int idx = code - _currentFontFirstChar;
                if (idx >= 0 && idx < _currentFontWidths.Length)
                    glyphWidths[glyphIndex] = (ushort)Math.Clamp(_currentFontWidths[idx], 0, 65535);
            }
        }

        short xMin = cffInfo.XMin, yMin = cffInfo.YMin, xMax = cffInfo.XMax, yMax = cffInfo.YMax;
        var bbox = ResolveArray(descriptor, "FontBBox");
        if (bbox != null && bbox.Count >= 4)
        {
            xMin = (short)bbox.GetNumber(0);
            yMin = (short)bbox.GetNumber(1);
            xMax = (short)bbox.GetNumber(2);
            yMax = (short)bbox.GetNumber(3);
        }

        var info = new Fonts.CffToOpenType.PdfFontInfo
        {
            PsName = descriptor.GetNameOrNull("FontName")
                     ?? fontDict.GetNameOrNull("BaseFont")
                     ?? "Unknown",
            XMin = xMin, YMin = yMin, XMax = xMax, YMax = yMax,
            Ascent = (short)descriptor.GetNumber("Ascent", 800),
            Descent = (short)descriptor.GetNumber("Descent", -200),
            WeightClass = (ushort)Math.Clamp((int)descriptor.GetNumber("FontWeight", 400), 1, 1000),
            UnicodeToGlyph = unicodeToGlyph,
            GlyphWidths = glyphWidths.Count > 0 ? glyphWidths : null,
        };

        return Fonts.CffToOpenType.Wrap(cff, cffInfo.NumGlyphs, info);
    }

    // Decode a raw PDF character code to its Unicode char under the current
    // font's encoding. Prefers the /Differences-derived map when present,
    // otherwise falls back to the named base encoding (WinAnsi/MacRoman).
    private char GetUnicodeForCode(byte code)
    {
        if (_currentCodeToUnicode != null)
            return _currentCodeToUnicode[code];
        var encoding = _currentFontEncoding == "MacRomanEncoding"
            ? Encoding.GetEncoding(10000)
            : Encoding.GetEncoding(1252);
        var s = encoding.GetString(new[] { code });
        return s.Length > 0 ? s[0] : '\0';
    }

    // Build code→Unicode (and inverse) tables for a font whose /Encoding is a
    // dictionary. Seeds from the named base encoding (WinAnsi/MacRoman), then
    // overlays entries from the /Differences array. Per PDF spec 9.6.5:
    // Differences is a sequence of numbers (starting code) and names (glyph
    // names), e.g. [32 /space /exclam /quotedbl 39 /quoteright].
    private void BuildEncodingMaps(Pdfe.Core.Primitives.PdfDictionary encodingDict, string baseEncoding)
    {
        var map = BuildBaseEncodingTable(baseEncoding);

        var differences = ResolveArray(encodingDict, "Differences");
        if (differences != null)
        {
            int currentCode = 0;
            for (int i = 0; i < differences.Count; i++)
            {
                var item = differences[i];
                if (item is Pdfe.Core.Primitives.PdfName name)
                {
                    if (currentCode >= 0 && currentCode < 256 &&
                        AdobeGlyphList.TryGet(name.Value, out var ch))
                    {
                        map[currentCode] = ch;
                    }
                    currentCode++;
                }
                else if (item is Pdfe.Core.Primitives.PdfInteger intNum)
                {
                    currentCode = (int)intNum.Value;
                }
                else if (item is Pdfe.Core.Primitives.PdfReal realNum)
                {
                    currentCode = (int)realNum.Value;
                }
            }
        }

        _currentCodeToUnicode = map;
        _currentUnicodeToCode = new Dictionary<char, byte>(256);
        for (int b = 0; b < 256; b++)
        {
            var c = map[b];
            if (c != '\0' && !_currentUnicodeToCode.ContainsKey(c))
                _currentUnicodeToCode[c] = (byte)b;
        }
    }

    private static char[] BuildBaseEncodingTable(string encodingName)
    {
        var encoding = encodingName == "MacRomanEncoding"
            ? Encoding.GetEncoding(10000)
            : Encoding.GetEncoding(1252);

        var map = new char[256];
        var buffer = new byte[1];
        for (int b = 0; b < 256; b++)
        {
            buffer[0] = (byte)b;
            var decoded = encoding.GetString(buffer);
            map[b] = decoded.Length > 0 ? decoded[0] : '\0';
        }
        return map;
    }

    // Effective font size applied to glyph drawing: raw Tf size scaled by the
    // text matrix's Y-scale (handles the common `1 Tf` + `s 0 0 s ... Tm` idiom).
    private float GetEffectiveFontSize()
    {
        var c = _textState.TextMatrixC;
        var d = _textState.TextMatrixD;
        var yScale = (float)Math.Sqrt(c * c + d * d);
        if (yScale < 1e-6f) yScale = 1f;
        return _textState.FontSize * yScale;
    }

    // Horizontal-to-vertical aspect ratio of the text matrix. Most PDFs use a
    // uniform Tm (X-scale == Y-scale) so this is 1. When they don't — e.g. a
    // condensed heading like SCOTUS's `14.2001 0 0 15 ... Tm` for SUPREME COURT
    // — glyphs must render horizontally squeezed by this ratio and advance
    // must scale by this ratio too, otherwise accumulated per-glyph error
    // shows up as mid-word gaps.
    private float GetTextMatrixXYRatio()
    {
        var a = _textState.TextMatrixA;
        var b = _textState.TextMatrixB;
        var c = _textState.TextMatrixC;
        var d = _textState.TextMatrixD;
        var xScale = (float)Math.Sqrt(a * a + b * b);
        var yScale = (float)Math.Sqrt(c * c + d * d);
        if (xScale < 1e-6f || yScale < 1e-6f) return 1f;
        return xScale / yScale;
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
        // PDF spec 9.4.2: Td's (tx, ty) are in UNSCALED text space units; the
        // new text matrix is [1 0 0 1 tx ty] × TextLineMatrix. The translation
        // lives in the right-hand side, so after composition:
        //   new_e = a*tx + c*ty + e
        //   new_f = b*tx + d*ty + f
        // Previously we added tx/ty directly to device-space e/f, which under
        // any Tm scale (e.g. `1 Tf` + `10.02 0 0 10.02 Tm`) produced line
        // breaks ~10x too small and pulled subsequent text up under the
        // previous line.
        var a = _textState.TextMatrixA;
        var b = _textState.TextMatrixB;
        var c = _textState.TextMatrixC;
        var d = _textState.TextMatrixD;
        var dx = a * tx + c * ty;
        var dy = b * tx + d * ty;
        _textState.TextMatrixE = _textState.LineMatrixE + (float)dx;
        _textState.TextMatrixF = _textState.LineMatrixF + (float)dy;
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
        var bytes = ParsePdfStringBytes(textOperand);
        if (bytes.Length == 0) return;

        if (_currentFontIsType0)
            RenderCidBytes(bytes);
        else
            RenderText(DecodeTextBytes(bytes));
    }

    private void ShowTextArray(List<string> operands)
    {
        // TJ operator: array of strings and position adjustments.
        foreach (var operand in operands)
        {
            if (operand == "[" || operand == "]")
                continue;

            if (operand.StartsWith("(") || operand.StartsWith("<"))
            {
                var bytes = ParsePdfStringBytes(operand);
                if (bytes.Length == 0) continue;
                if (_currentFontIsType0)
                    RenderCidBytes(bytes);
                else
                    RenderText(DecodeTextBytes(bytes));
            }
            else if (double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var adjustment))
            {
                // TJ position adjustment is in thousandths of text-space units,
                // which map to device-space X via the text matrix's X-scale
                // (not Y-scale). For non-uniform Tm (e.g. SCOTUS "SUPREME COURT"
                // with 14.2001/15 ratio), using yScale instead of xScale
                // compounds a ~6% per-glyph error into visible mid-word gaps.
                var effectiveSize = GetEffectiveFontSize();
                var xyRatio = GetTextMatrixXYRatio();
                var xOffset = (float)(-adjustment * effectiveSize / 1000.0) * xyRatio;
                _textState.TextMatrixE += xOffset * _textState.HorizontalScale / 100.0f;
            }
        }
    }

    private void RenderText(string text)
    {
        if (!_inTextBlock || _currentTypeface == null)
            return;

        var effectiveSize = GetEffectiveFontSize();
        var xyRatio = GetTextMatrixXYRatio();

        using var font = new SKFont(_currentTypeface, effectiveSize);
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
        // When the text matrix has non-uniform scaling (xyRatio != 1), squeeze
        // glyphs horizontally so their on-screen width matches the text-matrix's
        // X-scale rather than the Y-scale we used for font height.
        _canvas.Save();
        _canvas.Translate(x, y);
        _canvas.Scale(xyRatio, -1);
        _canvas.DrawText(text, 0, 0, paint);
        _canvas.Restore();

        // Advance must match what Skia actually drew above, otherwise the
        // cursor ends up past (or before) the visible glyph extent and the
        // next Tj renders with a gap (or overlap). Always use paint.MeasureText
        // — when the embedded font is loaded it reports the real widths; when
        // we've substituted a system typeface, the system font's widths are
        // what Skia drew with, and mixing in PDF /Widths here would diverge
        // from the visible render. Multiply by xyRatio so the advance lives
        // in the same scaled coordinate system as the drawn glyphs.
        var width = paint.MeasureText(text) * xyRatio;
        var charCount = text.Length;
        var spaceCount = text.Count(c => c == ' ');

        // PDF spec 9.4.4: Tc and Tw are in UNSCALED text space units. Scale by
        // the text matrix's X-scale before adding to device-space advance,
        // otherwise Tw-heavy layouts overlap themselves (birth-cert form).
        var tmA = _textState.TextMatrixA;
        var tmB = _textState.TextMatrixB;
        var xScale = (float)Math.Sqrt(tmA * tmA + tmB * tmB);
        if (xScale < 1e-6f) xScale = 1f;
        width += charCount * _textState.CharSpacing * xScale;
        width += spaceCount * _textState.WordSpacing * xScale;
        width *= _textState.HorizontalScale / 100.0f;

        _textState.TextMatrixE += width;
    }

    // Type0 rendering path. Content-stream bytes come in 2-at-a-time as
    // big-endian CIDs under /Identity-H (the only CMap we currently handle).
    // CIDs are rendered as glyph IDs directly — correct for /CIDToGIDMap
    // /Identity (the default and most common case for /CIDFontType2 fonts).
    private void RenderCidBytes(byte[] bytes)
    {
        if (!_inTextBlock || _currentTypeface == null || bytes.Length < 2)
            return;

        var count = bytes.Length / 2;
        var cids = new ushort[count];
        for (int i = 0; i < count; i++)
            cids[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);

        var effectiveSize = GetEffectiveFontSize();
        var xyRatio = GetTextMatrixXYRatio();

        using var font = new SKFont(_currentTypeface, effectiveSize);
        using var paint = new SKPaint(font)
        {
            Color = _state.FillColor,
            IsAntialias = _options.AntiAlias,
            TextEncoding = SKTextEncoding.GlyphId,
        };

        _canvas.Save();
        _canvas.Translate(_textState.TextMatrixE, _textState.TextMatrixF + _textState.TextRise);
        _canvas.Scale(xyRatio, -1);

        // SKTextEncoding.GlyphId reads the byte buffer as native-endian ushort
        // glyph IDs. BlockCopy gives us exactly that on little-endian machines.
        var glyphBytes = new byte[cids.Length * 2];
        Buffer.BlockCopy(cids, 0, glyphBytes, 0, glyphBytes.Length);
        _canvas.DrawText(glyphBytes, 0, 0, paint);

        _canvas.Restore();

        // Advance by summed widths from /W (with /DW as fallback per CID).
        float sumThousandthsOfEm = 0f;
        foreach (var cid in cids)
        {
            sumThousandthsOfEm += (_currentCidWidths != null &&
                                   _currentCidWidths.TryGetValue(cid, out var w))
                ? w
                : _currentCidDefaultWidth;
        }
        var width = sumThousandthsOfEm * effectiveSize / 1000f * xyRatio;
        width *= _textState.HorizontalScale / 100.0f;
        _textState.TextMatrixE += width;
    }

    // Returns the raw PDF string bytes WITHOUT decoding via encoding. Simple
    // fonts route these through DecodeTextBytes → Unicode → RenderText; Type0
    // fonts interpret the bytes directly as 2-byte CIDs via RenderCidBytes.
    private byte[] ParsePdfStringBytes(string operand)
    {
        if (string.IsNullOrEmpty(operand))
            return Array.Empty<byte>();

        // Literal string: (text)
        if (operand.StartsWith("(") && operand.EndsWith(")"))
            return UnescapePdfStringBytes(operand.Substring(1, operand.Length - 2));

        // Hex string: <hexdata>
        if (operand.StartsWith("<") && operand.EndsWith(">"))
            return DecodeHexStringBytes(operand.Substring(1, operand.Length - 2));

        return Encoding.Latin1.GetBytes(operand);
    }

    private static byte[] UnescapePdfStringBytes(string s)
    {
        var unescaped = new List<byte>(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var next = s[i + 1];
                switch (next)
                {
                    case 'n': unescaped.Add((byte)'\n'); i += 2; break;
                    case 'r': unescaped.Add((byte)'\r'); i += 2; break;
                    case 't': unescaped.Add((byte)'\t'); i += 2; break;
                    case 'b': unescaped.Add((byte)'\b'); i += 2; break;
                    case 'f': unescaped.Add((byte)'\f'); i += 2; break;
                    case '(': unescaped.Add((byte)'('); i += 2; break;
                    case ')': unescaped.Add((byte)')'); i += 2; break;
                    case '\\': unescaped.Add((byte)'\\'); i += 2; break;
                    default:
                        if (char.IsDigit(next))
                        {
                            var octal = "";
                            i++;
                            while (i < s.Length && octal.Length < 3 && char.IsDigit(s[i]) && s[i] < '8')
                                octal += s[i++];
                            unescaped.Add((byte)Convert.ToInt32(octal, 8));
                        }
                        else
                        {
                            unescaped.Add((byte)next);
                            i += 2;
                        }
                        break;
                }
            }
            else
            {
                // The content stream was decoded as Latin1, so char = byte.
                unescaped.Add((byte)s[i++]);
            }
        }
        return unescaped.ToArray();
    }

    private static byte[] DecodeHexStringBytes(string hex)
    {
        hex = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (hex.Length % 2 != 0) hex += "0";

        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, null, out var b))
                bytes[i] = b;
        }
        return bytes;
    }

    private string DecodeTextBytes(byte[] bytes)
    {
        // If the current font has an /Encoding dictionary, use the
        // /BaseEncoding + /Differences-derived map. Without this, embedded
        // subset fonts (which remap codes like 3 → "N", 4 → "A" via
        // /Differences) decode as control characters and render invisibly.
        if (_currentCodeToUnicode != null)
        {
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                var c = _currentCodeToUnicode[b];
                if (c != '\0') sb.Append(c);
            }
            return sb.ToString();
        }

        // Named-encoding fast path. WinAnsiEncoding = cp1252 is the default
        // for most modern PDFs.
        if (_currentFontEncoding == "MacRomanEncoding")
            return Encoding.GetEncoding(10000).GetString(bytes);
        return Encoding.GetEncoding(1252).GetString(bytes);
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
