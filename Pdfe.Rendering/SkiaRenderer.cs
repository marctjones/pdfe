using System.Globalization;
using System.Text;
using Pdfe.Core.ColorSpaces;
using Pdfe.Core.Document;
using Pdfe.Rendering.Fonts;
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
    // SkiaSharp's font subsystem is not safe under concurrent typeface creation:
    // SKTypeface.FromData and SKTypeface.FromFamilyName both reach into a process-wide
    // native font manager whose cache can corrupt or deadlock when two managed threads
    // call them simultaneously. Visual tests previously failed under xUnit parallelism
    // because of this; we serialize all typeface acquisition with a process-wide lock.
    private static readonly object _typefaceLoadLock = new();

    private readonly SKCanvas _canvas;
    private readonly PdfPage _page;
    private readonly RenderOptions _options;
    private readonly Stack<GraphicsState> _stateStack;
    private GraphicsState _state;
    private SKPath? _currentPath;
    private TextState _textState;
    private bool _inTextBlock;
    private bool _inCompatibilitySection;
    private SKTypeface? _currentTypeface;
    private string _currentFontEncoding;

    // Form-XObject recursion guards. PDF allows Form XObjects to invoke
    // each other via the `Do` operator, and a malformed file can have
    // a cycle (form A → form B → form A). Without protection that
    // recurses until the stack overflows and SIGABRTs the process —
    // observed on a pdf.js corpus fixture during the differential
    // run on 2026-05-01. The visited-set tracks the call stack so
    // a Do-cycle skips the recursive call; the depth counter is a
    // backstop for pathologically deep but acyclic nests.
    private readonly HashSet<Pdfe.Core.Primitives.PdfStream> _formXObjectStack =
        new(ReferenceEqualityComparer.Instance);
    private int _formXObjectDepth;
    // Form XObject nesting cap. Has to be high enough to satisfy PDF/A-1
    // §6.1.12's "implementation limits" conformance fixtures — those
    // specifically chain a long ladder of Form XObjects to test that a
    // reader supports deep nesting (the spec says 28+ levels of graphic
    // state nesting must work). 64 is still well below any plausible
    // .NET stack overflow point and is the same neighbourhood that
    // mutool / Poppler use; cycle detection via _formXObjectStack
    // catches genuine self-reference loops independently.
    private const int MaxFormXObjectDepth = 64;

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
    // (/FontFile2 = TrueType, /FontFile3 = OpenType/CFF). Keyed by the
    // resolved /Font dictionary's reference identity (PdfDocument.Resolve
    // caches by object number, so two ResolveFontFromActiveResources calls
    // for the same indirect ref return the same C# instance). Keying by
    // the dict instead of the resource name correctly distinguishes two
    // different physical fonts that share the same logical name (e.g.
    // /F0) in different /Resources scopes — common in widget annotation
    // appearances where each appearance dict's /Resources defines its
    // own /F0. Disposed at the end of Render().
    private readonly Dictionary<Pdfe.Core.Primitives.PdfDictionary, SKTypeface> _embeddedTypefaces = new();

    // Per-font byte→glyphId map extracted from a format-0 cmap subtable when
    // the typeface has no Unicode-mapped subtable Skia's shaper can use
    // (Mac Roman / format-0 subsets from veraPDF Test Builder, LibreOffice
    // and Office). When non-null for the active font, RenderText draws via
    // SKTextEncoding.GlyphId with explicit glyph IDs. Otherwise text would
    // shape to all-.notdef and the page would render blank, even though the
    // parser correctly extracts the Unicode text via /ToUnicode.
    // Cache value of null = "checked, not needed" so we don't re-probe.
    // Keyed by the same fontDict reference as _embeddedTypefaces so two
    // appearance-scoped /F0 entries that point to different fonts get
    // independent byte-cmap probes.
    private readonly Dictionary<Pdfe.Core.Primitives.PdfDictionary, ushort[]?> _embeddedTypefaceByteToGlyph = new();
    private ushort[]? _currentByteToGlyph;

    // Stack of /Resources dictionaries currently active. The page's own
    // /Resources is the bottom; entering a Form XObject pushes its own
    // /Resources (or null when absent — we still push so push/pop pair).
    // Font and XObject lookups walk top-down, falling back to page
    // resources at the bottom of the stack. Without this, annotation
    // appearances and nested Form XObjects can't see the fonts and
    // images defined in their own /Resources, so text in /AP /N streams
    // either rendered with wrong fonts or not at all.
    private readonly Stack<Pdfe.Core.Primitives.PdfDictionary?> _resourcesStack = new();

    // Type0 / CID font state. Type0 fonts use 2-byte-per-character codes and
    // index a descendant font's /W array for widths (different format from the
    // simple-font /Widths). When _currentFontIsType0 is true, content-stream
    // bytes must be parsed 2 at a time and rendered via glyph ID, not Unicode.
    private bool _currentFontIsType0;
    // True when /FontFile, /FontFile2, or /FontFile3 produced a usable
    // SKTypeface — i.e. Skia is rendering with the actual PDF font and
    // its MeasureText reports correct advances. False means we
    // substituted a system typeface; in that case PDF /Widths (if present)
    // are the source of truth for cursor advance, not Skia metrics.
    private bool _currentFontHasEmbeddedProgram;
    private Dictionary<int, float>? _currentCidWidths;
    private float _currentCidDefaultWidth = 1000f;

    // CID → glyph-id mapping for the active CIDFontType2 font, when a
    // non-identity /CIDToGIDMap stream is present. ushort.MaxValue at
    // index N means "this CID is unmapped". Null means /CIDToGIDMap is
    // absent or /Identity, so CID == GID and RenderCidBytes draws CIDs
    // straight. Subset CIDFontType2 fonts (NotoSans, Noto* family) ship
    // a stream remapping each used CID to its small subset glyph index;
    // skipping the remap draws .notdef for every glyph and the page
    // appears blank.
    private ushort[]? _currentCidToGidMap;

    // CID → glyph-index mapping for the active CIDFontType0 (CFF-based)
    // font, derived from the /ROS-marked CFF's charset table. Same
    // purpose as _currentCidToGidMap, but for CFF-keyed CID fonts where
    // the mapping lives inside the embedded CFF rather than alongside
    // it as /CIDToGIDMap. Subset Adobe-Japan1 fonts (KozMinPro,
    // YuMincho, Source Han Sans, etc.) reach this path; without it,
    // every CJK glyph resolves to .notdef and Japanese / Chinese /
    // Korean PDFs render as blank pages.
    private Dictionary<int, int>? _currentCffCidToGlyph;
    // Per-fontDict cache for the above, keyed the same way as
    // _embeddedTypefaces so two different /Font dicts with the same
    // resource name but different physical fonts don't collide.
    private readonly Dictionary<Pdfe.Core.Primitives.PdfDictionary, Dictionary<int, int>?> _embeddedCffCidToGlyph = new();

    // Side-table for inline images. The tokenizer captures the dict
    // header text and the raw data bytes between BI/ID/EI as we scan
    // (where the bytes can be binary and not safe to round-trip through
    // a token string), and emits a numeric operand pointing into this
    // list. RenderInlineImage looks up the entry and rasterizes.
    private readonly List<(string HeaderText, byte[] DataBytes)> _inlineImages = new();

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
            // Page resources sit at the bottom of the stack. Form XObjects
            // (incl. annotation appearances) push their own /Resources on
            // top of this when entered; lookups fall back to the page when
            // a name isn't defined locally.
            _resourcesStack.Push(_page.Resources);

            var contentBytes = _page.GetContentStreamBytes();
            if (contentBytes.Length > 0)
            {
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

            // Annotations render on top of page content — sticky notes,
            // FreeText callouts, Widget appearances, etc. live in the
            // page's /Annots array as separate Form XObjects rather than
            // in the content stream. Without this pass, pages where the
            // visible text is entirely in annotations (a common veraPDF
            // PDF/UA fixture pattern) come out blank.
            RenderAnnotations();
        }
        finally
        {
            _resourcesStack.Clear();
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

            // Inline image operator. The tokenizer captured the dict
            // header text and binary data into _inlineImages and emitted
            // the side-table index as our operand; we pull the entry,
            // build a synthetic stream, and reuse the existing image
            // renderer.
            case "BI":
                if (operands.Count >= 1 &&
                    int.TryParse(operands[0], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var inlineIdx) &&
                    inlineIdx >= 0 && inlineIdx < _inlineImages.Count)
                {
                    var entry = _inlineImages[inlineIdx];
                    RenderInlineImage(entry.HeaderText, entry.DataBytes);
                }
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

            // Compatibility operators
            case "BX":
                _inCompatibilitySection = true;
                break;
            case "EX":
                _inCompatibilitySection = false;
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
            BlendMode = _state.BlendMode,
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
            BlendMode = _state.BlendMode,
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
            BlendMode = _state.BlendMode,
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
            BlendMode = _state.BlendMode,
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

        // Try to get the font from the active resources (innermost Form
        // XObject's /Resources, falling back to the page) to determine the
        // base font and encoding.
        var fontDict = ResolveFontFromActiveResources(fontName);
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

        // Parse the font's glyph width table FIRST. The CFF→OpenType wrapper
        // (called inside TryLoadEmbeddedTypeface below) reads these to build
        // hmtx — without populating them first, every embedded font would be
        // wrapped with stale widths from the previously-active font, producing
        // visibly wrong layout (mid-word gaps and overlaps).
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

        // Prefer a typeface loaded from the PDF's own embedded font stream
        // (/FontFile2 = TrueType, /FontFile3 = OpenType/CFF). When no embedded
        // data is present or the format isn't SkiaSharp-loadable (e.g. /FontFile
        // is raw Type1 PostScript), fall through to the system-font mapping.
        var embedded = TryLoadEmbeddedTypeface(fontDict);
        _currentFontHasEmbeddedProgram = embedded != null;
        _currentTypeface = embedded ?? GetTypeface(baseFont);
        _currentByteToGlyph = embedded != null && fontDict != null
            && _embeddedTypefaceByteToGlyph.TryGetValue(fontDict, out var btg)
            ? btg : null;
        _currentCffCidToGlyph = embedded != null && fontDict != null
            && _embeddedCffCidToGlyph.TryGetValue(fontDict, out var cffMap)
            ? cffMap : null;

        // Type0 (composite CID) fonts need a completely different content-stream
        // parse (2 bytes per character, widths indexed via /W not /Widths).
        _currentFontIsType0 = fontDict?.GetNameOrNull("Subtype") == "Type0";
        _currentCidWidths = null;
        _currentCidDefaultWidth = 1000f;
        _currentCidToGidMap = null;
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

                // /CIDToGIDMap is /Identity (or absent) for most modern Type0
                // fonts and CID == GID. Subset CIDFontType2 fonts produced by
                // veraPDF Test Builder, Word, etc. ship a remapping stream
                // (2-byte big-endian uint16 per CID); without applying it,
                // glyph IDs miss every glyph in the subset and pages render
                // .notdef-only blanks.
                var cidToGidObj = cidFont.GetOptional("CIDToGIDMap");
                if (cidToGidObj != null)
                {
                    var resolved = _page.Document.Resolve(cidToGidObj);
                    if (resolved is Pdfe.Core.Primitives.PdfStream cidStream)
                    {
                        try
                        {
                            var data = cidStream.DecodedData;
                            int count = data.Length / 2;
                            var map = new ushort[count];
                            for (int i = 0; i < count; i++)
                                map[i] = (ushort)((data[i * 2] << 8) | data[i * 2 + 1]);
                            _currentCidToGidMap = map;
                        }
                        catch { _currentCidToGidMap = null; }
                    }
                }
            }
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
    // and kerning the PDF's /Widths table was authored against. Cached per-
    // dict for the life of this RenderContext; disposed at end of Render().
    private SKTypeface? TryLoadEmbeddedTypeface(Pdfe.Core.Primitives.PdfDictionary? fontDict)
    {
        if (fontDict == null) return null;
        if (_embeddedTypefaces.TryGetValue(fontDict, out var cached))
            return cached;

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
        // For CID-keyed CFF (Adobe-Japan1 etc.) the wrapper produces a minimal
        // cmap and returns the CID → glyph index map via cffCidToGlyph; the
        // renderer threads that through SetFont so RenderCidBytes can dispatch
        // glyph IDs directly.
        byte[] loadableBytes = fontBytes;
        Dictionary<int, int>? cffCidToGlyph = null;
        if (isCff)
        {
            var wrapped = TryWrapCffAsOpenType(fontBytes, fontDict, descriptor, out cffCidToGlyph);
            if (wrapped != null) loadableBytes = wrapped;
        }

        SKTypeface? typeface;
        lock (_typefaceLoadLock)
        {
            try
            {
                using var data = SKData.CreateCopy(loadableBytes);
                typeface = SKTypeface.FromData(data);
            }
            catch { typeface = null; }

            if (typeface == null) return null;

            // Sanity-probe the wrapped font — for some CFF subsets (most commonly
            // dingbat fonts produced by the XEP toolchain) Skia loads our wrapper
            // and resolves the cmap, but its CFF interpreter finds no charstring
            // outlines and silently draws nothing. Detect that and fall back to
            // the system-font path so the user at least sees *some* glyph for the
            // codepoint instead of empty whitespace.
            if (isCff && !ProducesGlyphOutlines(typeface))
            {
                typeface.Dispose();
                return null;
            }
        }

        _embeddedTypefaces[fontDict] = typeface;
        _embeddedTypefaceByteToGlyph[fontDict] = ResolveByteCodeCmap(typeface, fontDict);
        _embeddedCffCidToGlyph[fontDict] = cffCidToGlyph;
        return typeface;
    }

    /// <summary>
    /// Detect typefaces that need the byte-coded glyph-ID draw path because
    /// Skia's shaper can't read their cmap, and pre-compute the
    /// byte→glyphId lookup once.
    ///
    /// We probe Skia first: if <c>SKFont.GetGlyph((int)c)</c> resolves
    /// common Unicode codepoints to real glyphs, the font has Unicode
    /// coverage Skia can shape and we don't need the workaround. Otherwise
    /// we read any format-0 subtable from the cmap. Returns null when no
    /// override is needed (the common case for modern Type 0 / CID fonts
    /// with Identity-H or Unicode-mapped cmaps).
    /// </summary>
    private static ushort[]? ResolveByteCodeCmap(
        SKTypeface typeface,
        Pdfe.Core.Primitives.PdfDictionary? fontDict)
    {
        // Type0 (CID) fonts go through a separate draw path that already
        // walks bytes 2 at a time and resolves through the descendant font;
        // the format-0 workaround would double-encode.
        if (fontDict?.GetNameOrNull("Subtype") == "Type0") return null;

        using var probe = new SKFont(typeface, 12f);
        int[] unicodeProbe = { 'A', 'a', 'M', 'e', '0', ' ', 'i' };
        foreach (var cp in unicodeProbe)
        {
            if (probe.GetGlyph(cp) != 0)
            {
                // Skia can shape this font directly via its cmap.
                return null;
            }
        }

        // No Unicode coverage Skia can see — fall back to the format-0
        // subtable if present.
        return CmapFormat0Table.TryRead(typeface);
    }

    private static bool ProducesGlyphOutlines(SKTypeface typeface)
    {
        // Sample up to 16 evenly-distributed glyph indices; if none have an
        // outline, the CFF program is unreadable for our purposes.
        int n = typeface.GlyphCount;
        if (n <= 1) return false;
        int probes = Math.Min(16, n - 1);
        int step = Math.Max(1, (n - 1) / probes);
        using var font = new SKFont(typeface, 100f);
        for (int i = 1; i <= probes; i++)
        {
            ushort gid = (ushort)Math.Min(n - 1, i * step);
            using var p = font.GetGlyphPath(gid);
            if (p != null && p.PointCount > 0) return true;
        }
        return false;
    }

    private byte[]? TryWrapCffAsOpenType(
        byte[] cff,
        Pdfe.Core.Primitives.PdfDictionary fontDict,
        Pdfe.Core.Primitives.PdfDictionary descriptor,
        out Dictionary<int, int>? cffCidToGlyph)
    {
        cffCidToGlyph = null;
        var cffInfo = Fonts.CffParser.Parse(cff);
        if (cffInfo == null) return null;

        var unicodeToGlyph = new Dictionary<char, int>(256);
        var glyphWidths = new Dictionary<int, ushort>(256);

        if (cffInfo.IsCidKeyed)
        {
            // CID-keyed CFF (Adobe-Japan1 / Adobe-CNS1 / Adobe-Korea1). The
            // CFF charset stores CIDs, not glyph names, so the AdobeGlyphList
            // path doesn't apply — there's no Unicode → name → glyph chain
            // to walk. Skip cmap construction and rely on the renderer
            // dispatching glyphs via SKTextEncoding.GlyphId; CFF glyph
            // ordering is preserved by the wrapper, so the OpenType glyph
            // index Skia ultimately uses == the CFF glyph index ==
            // CidToGlyph[cid] from the descendant font's CFF.
            cffCidToGlyph = cffInfo.CidToGlyph;
        }
        else
        {
            // Build Unicode → glyph-index map and glyph-index → PDF-width map.
            // Both derive from walking the PDF's character codes 0..255, resolving
            // each to (Unicode, glyph name) and then looking up the glyph index in
            // the CFF charset.
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
        // PDF subset fonts wear a 6-letter+'+' prefix (e.g. GFEDCB+MyriadPro-Semibold).
        // Strip it before matching — otherwise even "ZapfDingbats" subsets fall
        // through to Sans-Serif and the glyphs come out as missing-glyph boxes.
        var bareName = baseFont;
        if (bareName.Length >= 8 && bareName[6] == '+')
            bareName = bareName.Substring(7);

        // Match standard PDF base fonts. Allow both exact and prefix matches so
        // family-named subsets ("ZapfDingbatsStd", "MyriadPro-Semibold", etc.)
        // route to the right system substitute.
        string family;
        if (Starts(bareName, "Helvetica") || Starts(bareName, "Arial"))
            family = "Helvetica";
        else if (Starts(bareName, "Times") || Starts(bareName, "Bookman"))
            family = "Times New Roman";
        else if (Starts(bareName, "Courier"))
            family = "Courier New";
        else if (Starts(bareName, "Symbol"))
            family = "Symbol";
        else if (bareName.Contains("Dingbat") || bareName.Contains("Wingding"))
            // Linux ships NotoSansSymbols2 / OpenSymbol that cover U+27A4 etc.;
            // Skia's family-name lookup falls through to whichever is installed.
            family = "Noto Sans Symbols2";
        else
            family = "Sans-Serif";

        var style = SKFontStyle.Normal;
        if (bareName.Contains("Bold") && (bareName.Contains("Italic") || bareName.Contains("Oblique")))
            style = SKFontStyle.BoldItalic;
        else if (bareName.Contains("Bold") || bareName.Contains("Semibold") || bareName.Contains("Medium"))
            style = SKFontStyle.Bold;
        else if (bareName.Contains("Italic") || bareName.Contains("Oblique"))
            style = SKFontStyle.Italic;

        lock (_typefaceLoadLock)
        {
            return SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
        }
    }

    private static bool Starts(string s, string prefix) =>
        s.StartsWith(prefix, StringComparison.Ordinal);

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
            RenderText(DecodeTextBytes(bytes), bytes);
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
                    RenderText(DecodeTextBytes(bytes), bytes);
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

    private void RenderText(string text, byte[]? sourceBytes = null)
    {
        if (!_inTextBlock || _currentTypeface == null)
            return;

        var effectiveSize = GetEffectiveFontSize();
        var xyRatio = GetTextMatrixXYRatio();

        using var font = new SKFont(_currentTypeface, effectiveSize);
        using var paint = new SKPaint(font)
        {
            Color = _state.FillColor,
            BlendMode = _state.BlendMode,
            IsAntialias = _options.AntiAlias
        };

        // Calculate position in PDF coordinates
        var x = _textState.TextMatrixE;
        var y = _textState.TextMatrixF + _textState.TextRise;

        // The canvas has been transformed with Scale(scale, -scale) to flip
        // Y for paths. Un-flip for text. When the text matrix has non-uniform
        // X/Y scaling, squeeze glyphs horizontally to match the X-scale.
        //
        // For Y direction, follow the *sign* of Tm.d:
        //   d > 0 (typical PDF, Y-up text-space) → -1 cancels outer Y-flip
        //   d < 0 (browser-style Tm `1 0 0 -1`, e.g. WeasyPrint, Word, Word-derived
        //         and most CJK-producing toolchains) → +1 keeps Skia's natural
        //         Y-down glyph drawing, which the outer flip turns into Y-up
        //         on screen exactly as the PDF intends.
        // Without this, browser-flipped text (and most CJK) renders upside-down.
        //
        // Per PDF 32000-2 §9.4.4 the text rendering matrix multiplies the
        // X axis by Th (= Tz / 100). Tz=100 is the default and the no-op,
        // but conformance fixtures use Tz=300, Tz=30000 etc. to test wide
        // / condensed text. Without folding Th into this Scale the glyph
        // itself is drawn at native width while the cursor advances by
        // Th×width, leaving microscopic letters with huge gaps between
        // them (visible as pdfe rendering only ~5% of the expected pixel
        // count on TWG A001 / 6-1-12-t02 fixtures).
        float ySign = _textState.TextMatrixD >= 0 ? -1f : 1f;
        float th = _textState.HorizontalScale / 100.0f;
        _canvas.Save();
        _canvas.Translate(x, y);
        _canvas.Scale(xyRatio * th, ySign);

        bool drawWithPdfWidths =
            !_currentFontHasEmbeddedProgram &&
            _currentFontWidths != null &&
            sourceBytes != null &&
            text.Length == sourceBytes.Length;

        if (drawWithPdfWidths)
        {
            // Walk the bytes in lock-step with the decoded characters,
            // drawing each glyph at the cumulative PDF-/Widths position
            // *plus* the character/word spacing the PDF asked for.
            // Visible layout matches what the PDF author authored
            // against Times/Helvetica, regardless of the system font we
            // substituted for the actual glyphs.
            //
            // Per-glyph cursor advance after drawing byte b:
            //     /Widths[b]/1000 * fontSize    (intended glyph width)
            //   + Tc                             (character spacing)
            //   + (b == 0x20 ? Tw : 0)           (word spacing on space)
            //
            // Multiplied by the horizontal-scaling factor Tz (Th) per
            // PDF spec 9.4.4.
            //
            // We're inside a canvas that's already been scaled by xyRatio
            // for the X axis, so cursor is in the pre-xyRatio frame.
            // Tc / Tw are unscaled; we don't apply Tm's xScale here
            // because the canvas transform handles it.
            // Per-glyph advance per PDF spec 9.4.4:
            //   tx = (w0/1000 + Tc + (b == 0x20 ? Tw : 0)) * Tm_scale * Th
            // With Tf=1 and Tm scale = effectiveSize, Tm_scale = effectiveSize.
            // Multiplying everything together puts cursor in the canvas frame
            // we just set up with Scale(xyRatio, -1).
            // The outer Scale already folded Th into the canvas X axis, so
            // cursor advances in the *pre-Th* frame: (w/1000 + spacing) * Tfs.
            // Multiplying by Th again here would double-apply the horizontal
            // scale and over-shoot per-glyph spacing under any non-default Tz.
            float cursor = 0f;
            float tc = _textState.CharSpacing;
            float tw = _textState.WordSpacing;
            for (int i = 0; i < sourceBytes!.Length; i++)
            {
                _canvas.DrawText(text[i].ToString(), cursor, 0, paint);
                int idx = sourceBytes[i] - _currentFontFirstChar;
                float w = idx >= 0 && idx < _currentFontWidths!.Length
                    ? _currentFontWidths[idx]
                    : _currentFontMissingWidth;
                float spacing = tc + (sourceBytes[i] == 0x20 ? tw : 0f);
                cursor += (w / 1000f + spacing) * effectiveSize;
            }
        }
        else if (_currentByteToGlyph != null && sourceBytes != null)
        {
            // The active typeface's cmap is byte-coded (Mac Roman / format-0)
            // and Skia's shaper can't read it. Look each PDF byte code up in
            // the parsed cmap and dispatch via SKTextEncoding.GlyphId so Skia
            // draws the explicit glyph indices we already resolved. Without
            // this branch every glyph would render as .notdef and the page
            // would be blank.
            paint.TextEncoding = SKTextEncoding.GlyphId;
            var glyphBytes = BuildGlyphIdBytes(sourceBytes, _currentByteToGlyph);
            _canvas.DrawText(glyphBytes, 0, 0, paint);
        }
        else
        {
            _canvas.DrawText(text, 0, 0, paint);
        }
        _canvas.Restore();

        // Advance the cursor by what the PDF *intended*, which is not
        // always what Skia just drew.
        //   - Embedded font program → Skia loaded the real font, its
        //     MeasureText is correct.
        //   - No embedded program but PDF supplies /Widths → trust the
        //     PDF's explicit widths; the substituted system typeface's
        //     metrics differ and would compound per-glyph drift into
        //     visible mid-word gaps (the birth-cert form is the canary).
        //   - Otherwise fall back to Skia's MeasureText.
        float widthInFontUnits;
        bool advanceFromPdfWidths =
            !_currentFontHasEmbeddedProgram &&
            _currentFontWidths != null &&
            sourceBytes != null;

        if (advanceFromPdfWidths)
        {
            widthInFontUnits = SumPdfWidths(sourceBytes!) * effectiveSize;
        }
        else if (_currentByteToGlyph != null && sourceBytes != null)
        {
            // Same byte-coded glyph-ID path as the draw branch above. The
            // paint already has TextEncoding=GlyphId from the draw call;
            // measuring the same bytes returns Skia's hmtx-based advance.
            var glyphBytes = BuildGlyphIdBytes(sourceBytes, _currentByteToGlyph);
            widthInFontUnits = paint.MeasureText(glyphBytes);
        }
        else
        {
            widthInFontUnits = paint.MeasureText(text);
        }

        var width = widthInFontUnits * xyRatio;
        var charCount = sourceBytes?.Length ?? text.Length;
        var spaceCount = sourceBytes != null
            ? sourceBytes.Count(b => b == 0x20)
            : text.Count(c => c == ' ');

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

    /// <summary>
    /// Total advance for <paramref name="bytes"/> in the current simple
    /// font, expressed as a fraction of the font's em (multiply by font
    /// size to get points). Indexes <c>_currentFontWidths</c> by
    /// (byte − FirstChar); falls back to /MissingWidth or 0 for codes
    /// outside the table.
    /// </summary>
    private float SumPdfWidths(byte[] bytes)
    {
        if (_currentFontWidths == null || _currentFontWidths.Length == 0) return 0f;
        float total = 0f;
        var widths = _currentFontWidths;
        int firstChar = _currentFontFirstChar;
        for (int i = 0; i < bytes.Length; i++)
        {
            int idx = bytes[i] - firstChar;
            float w = idx >= 0 && idx < widths.Length
                ? widths[idx]
                : _currentFontMissingWidth;
            // PDF /Widths are in 1/1000 of em.
            total += w / 1000f;
        }
        return total;
    }

    // Pack PDF byte codes into a native-endian ushort buffer of glyph IDs
    // for SKTextEncoding.GlyphId. Used for simple fonts whose embedded
    // typeface has only a format-0 cmap; the byte→glyph map was parsed
    // once at typeface-load time. Same encoding the Type0 path uses
    // below (Buffer.BlockCopy on little-endian).
    private static byte[] BuildGlyphIdBytes(byte[] sourceBytes, ushort[] byteToGlyph)
    {
        var gids = new ushort[sourceBytes.Length];
        for (int i = 0; i < sourceBytes.Length; i++)
            gids[i] = byteToGlyph[sourceBytes[i]];
        var glyphBytes = new byte[gids.Length * 2];
        Buffer.BlockCopy(gids, 0, glyphBytes, 0, glyphBytes.Length);
        return glyphBytes;
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
        // Two parallel arrays: CIDs (used for /W width lookup, which is
        // keyed by CID per spec) and GIDs (what Skia actually draws). The
        // CID → GID resolution depends on the descendant font subtype:
        //   - CIDFontType2 with /CIDToGIDMap stream → _currentCidToGidMap
        //     is the array indexed by CID (handles Word / NotoSans / Office
        //     subsets).
        //   - CIDFontType0 (CFF-keyed, Adobe-Japan1 etc.) → the mapping is
        //     inside the embedded CFF charset; CffCidToGlyph holds it.
        //   - CIDFontType2 with /CIDToGIDMap = /Identity (or absent) → CID
        //     equals GID and we draw straight through.
        var cids = new ushort[count];
        var gids = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            ushort cid = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
            cids[i] = cid;
            ushort gid;
            if (_currentCidToGidMap != null && cid < _currentCidToGidMap.Length)
                gid = _currentCidToGidMap[cid];
            else if (_currentCffCidToGlyph != null
                     && _currentCffCidToGlyph.TryGetValue(cid, out var cffGid))
                gid = (ushort)cffGid;
            else
                gid = cid;
            gids[i] = gid;
        }

        var effectiveSize = GetEffectiveFontSize();
        var xyRatio = GetTextMatrixXYRatio();

        using var font = new SKFont(_currentTypeface, effectiveSize);
        using var paint = new SKPaint(font)
        {
            Color = _state.FillColor,
            BlendMode = _state.BlendMode,
            IsAntialias = _options.AntiAlias,
            TextEncoding = SKTextEncoding.GlyphId,
        };

        // Match RenderText's Tm.d-aware Y-flip — without this, all CJK text
        // and any other content authored with a browser-style flipped Tm
        // (`1 0 0 -1`) renders upside-down.
        float ySign = _textState.TextMatrixD >= 0 ? -1f : 1f;
        _canvas.Save();
        _canvas.Translate(_textState.TextMatrixE, _textState.TextMatrixF + _textState.TextRise);
        _canvas.Scale(xyRatio, ySign);

        // SKTextEncoding.GlyphId reads the byte buffer as native-endian ushort
        // glyph IDs. BlockCopy gives us exactly that on little-endian machines.
        // Pass the GID array (already remapped through /CIDToGIDMap when
        // present), not the raw CIDs.
        var glyphBytes = new byte[gids.Length * 2];
        Buffer.BlockCopy(gids, 0, glyphBytes, 0, glyphBytes.Length);
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

    internal static byte[] UnescapePdfStringBytes(string s)
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
                    case '\r':
                    case '\n':
                        // PDF spec 7.3.4.2: backslash followed by an EOL
                        // marker — both shall be ignored. Treat CRLF as a
                        // single EOL.
                        i += 2;
                        if (next == '\r' && i < s.Length && s[i] == '\n')
                            i++;
                        break;
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
                            // Backslash before unknown char — spec 7.3.4.2:
                            // backslash is ignored; emit just `next`.
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

        if (extGState.ContainsKey("BM"))
        {
            var bm = extGState.GetNameOrNull("BM") ?? "Normal";
            _state.BlendMode = MapBlendMode(bm);
        }

        if (extGState.ContainsKey("SMask"))
        {
            var smaskObj = extGState.GetOptional("SMask");
            if (smaskObj is Pdfe.Core.Primitives.PdfName n && n.Value == "None")
            {
                // Clear soft mask - no visual effect needed
            }
            // Note: full soft mask (transparency group) rendering not yet supported
        }
    }

    private static SKBlendMode MapBlendMode(string pdfName) => pdfName switch
    {
        "Multiply"   => SKBlendMode.Multiply,
        "Screen"     => SKBlendMode.Screen,
        "Overlay"    => SKBlendMode.Overlay,
        "Darken"     => SKBlendMode.Darken,
        "Lighten"    => SKBlendMode.Lighten,
        "ColorDodge" => SKBlendMode.ColorDodge,
        "ColorBurn"  => SKBlendMode.ColorBurn,
        "HardLight"  => SKBlendMode.HardLight,
        "SoftLight"  => SKBlendMode.SoftLight,
        "Difference" => SKBlendMode.Difference,
        "Exclusion"  => SKBlendMode.Exclusion,
        "Hue"        => SKBlendMode.Hue,
        "Saturation" => SKBlendMode.Saturation,
        "Color"      => SKBlendMode.Color,
        "Luminosity" => SKBlendMode.Luminosity,
        _            => SKBlendMode.SrcOver,
    };

    #endregion

    #region XObject Rendering (Do operator)

    private void RenderXObject(string nameOperand)
    {
        // Remove leading / if present
        var name = nameOperand.TrimStart('/');
        var xobj = ResolveXObjectFromActiveResources(name);
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
                // JPEG data - decode directly. SafeDecode null-guards
                // the encoded bytes and swallows SkiaSharp internal
                // exceptions (ArgumentNullException 'codec', etc.) on
                // malformed inputs so a single bad image doesn't kill
                // the whole page.
                bitmap = SafeDecode(imageStream.EncodedData);
            }
            else if (filters.Contains("JPXDecode"))
            {
                // JPEG 2000 data — SkiaSharp's stock build has no JPX
                // codec on Linux, so SKBitmap.Decode returns null or
                // throws ArgumentNullException with codec=null. Either
                // way we fall back to the placeholder.
                bitmap = SafeDecode(imageStream.EncodedData);
                if (bitmap == null && width > 0 && height > 0)
                    bitmap = CreatePlaceholderBitmap(width, height);
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

            using var paint = new SKPaint
            {
                BlendMode = _state.BlendMode,
                IsAntialias = _options.AntiAlias
            };
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
        PdfColorSpace? pdfColorSpace = null;
        int componentsPerPixel = 3;

        var csObj = stream.GetOptional("ColorSpace");
        if (csObj != null)
        {
            pdfColorSpace = PdfColorSpace.Parse(csObj, _page.Document);
            componentsPerPixel = pdfColorSpace.Components;
        }
        else
        {
            pdfColorSpace = PdfColorSpace.FromName(colorSpace);
            componentsPerPixel = pdfColorSpace.Components;
        }

        if (componentsPerPixel == 0)
            componentsPerPixel = 3;

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

                    if (bitsPerComponent == 8 && pdfColorSpace != null)
                    {
                        var pixelValues = new double[componentsPerPixel];
                        if (srcIndex + componentsPerPixel <= data.Length)
                        {
                            for (int i = 0; i < componentsPerPixel; i++)
                                pixelValues[i] = data[srcIndex + i] / 255.0;
                            srcIndex += componentsPerPixel;

                            var (rd, gd, bd) = pdfColorSpace.ToRgb(pixelValues);
                            r = (byte)Math.Clamp(rd * 255, 0, 255);
                            g = (byte)Math.Clamp(gd * 255, 0, 255);
                            b = (byte)Math.Clamp(bd * 255, 0, 255);
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

    /// <summary>
    /// Wrap SKBitmap.Decode so any exception (ArgumentNullException
    /// when SkiaSharp can't find a codec for this image format,
    /// AccessViolationException on truncated/corrupt input, etc.)
    /// returns null instead of propagating up and crashing the
    /// page render. Found by the pdf.js corpus differential —
    /// 8 fixtures with JPEG2000 inline images caused
    /// "Value cannot be null. (Parameter 'codec')" because
    /// SkiaSharp's Linux build ships without a JPX codec.
    /// </summary>
    private static SKBitmap? SafeDecode(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            return SKBitmap.Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap CreatePlaceholderBitmap(int width, int height)
    {
        var bmp = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(192, 192, 192, 255));
        return bmp;
    }

    /// <summary>
    /// Render every visible annotation on the page on top of the main
    /// content. Each annotation's <c>/AP /N</c> stream is a Form XObject
    /// in the appearance's own coordinate space; we compute the matrix
    /// that maps its <c>/BBox</c> (transformed by /Matrix) onto the
    /// annotation's <c>/Rect</c> per ISO 32000-2 §12.5.5, then dispatch
    /// the appearance through the existing Form XObject pipeline.
    /// Annotations without an /AP entry are skipped — synthesizing a
    /// default appearance from /Subtype-specific properties (sticky-note
    /// icon, link rectangles, etc.) is handled separately, if at all.
    /// </summary>
    private void RenderAnnotations()
    {
        IReadOnlyList<Pdfe.Core.Document.PdfAnnotation> annots;
        try { annots = _page.GetAnnotations(); }
        catch { return; }
        if (annots.Count == 0) return;

        foreach (var annot in annots)
        {
            // Skip annotations the spec says shouldn't be displayed.
            // Print=4 is fine — that's an opt-in for *also* including
            // the annotation in printed output, not a "screen only" flag.
            var f = annot.Flags;
            if ((f & (Pdfe.Core.Document.PdfAnnotationFlags.Hidden
                    | Pdfe.Core.Document.PdfAnnotationFlags.NoView
                    | Pdfe.Core.Document.PdfAnnotationFlags.Invisible)) != 0)
                continue;

            var appearance = ResolveAppearanceN(annot);
            if (appearance == null)
            {
                // No baked /AP /N stream — synthesize a minimal default
                // appearance for the subtypes commercial viewers
                // routinely show (interactive widgets, link rectangles,
                // shape annotations). Without this, signature widgets
                // and unfilled form fields are invisible and PDFs look
                // visibly less complete than in Acrobat / Preview /
                // Chrome.
                RenderDefaultAppearance(annot);
                continue;
            }

            // Appearance bbox + matrix.
            if (appearance.GetOptional("BBox") is not Pdfe.Core.Primitives.PdfArray bboxArr ||
                bboxArr.Count < 4) continue;
            float bx1 = (float)bboxArr.GetNumber(0);
            float by1 = (float)bboxArr.GetNumber(1);
            float bx2 = (float)bboxArr.GetNumber(2);
            float by2 = (float)bboxArr.GetNumber(3);
            float bMinX = Math.Min(bx1, bx2);
            float bMinY = Math.Min(by1, by2);
            float bMaxX = Math.Max(bx1, bx2);
            float bMaxY = Math.Max(by1, by2);

            var formMatrix = SKMatrix.Identity;
            if (appearance.GetOptional("Matrix") is Pdfe.Core.Primitives.PdfArray mArr && mArr.Count >= 6)
            {
                formMatrix = new SKMatrix(
                    (float)mArr.GetNumber(0), (float)mArr.GetNumber(2), (float)mArr.GetNumber(4),
                    (float)mArr.GetNumber(1), (float)mArr.GetNumber(3), (float)mArr.GetNumber(5),
                    0, 0, 1);
            }

            // Transform the four bbox corners through the form's /Matrix
            // and take the axis-aligned bounding box of the result. Spec
            // step from §12.5.5: "a quadrilateral whose corners are the
            // four corners of BBox transformed by Matrix … then the
            // smallest rectangle enclosing those four points."
            var p1 = formMatrix.MapPoint(new SKPoint(bMinX, bMinY));
            var p2 = formMatrix.MapPoint(new SKPoint(bMaxX, bMinY));
            var p3 = formMatrix.MapPoint(new SKPoint(bMaxX, bMaxY));
            var p4 = formMatrix.MapPoint(new SKPoint(bMinX, bMaxY));
            float bbMinX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
            float bbMinY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
            float bbMaxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
            float bbMaxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
            if (bbMaxX <= bbMinX || bbMaxY <= bbMinY) continue;

            // Annotation /Rect (PDF stores [llx lly urx ury], but some
            // producers swap pairs — normalize both ways).
            float rx1 = (float)Math.Min(annot.Rect.Left, annot.Rect.Right);
            float ry1 = (float)Math.Min(annot.Rect.Bottom, annot.Rect.Top);
            float rx2 = (float)Math.Max(annot.Rect.Left, annot.Rect.Right);
            float ry2 = (float)Math.Max(annot.Rect.Bottom, annot.Rect.Top);
            if (rx2 <= rx1 || ry2 <= ry1) continue;

            // A = scale + translate that maps the AABB of the transformed
            // bbox onto Rect. RenderFormXObject will additionally concat
            // the form's own Matrix, so the final on-page transform is
            // A · Matrix, which by construction takes BBox → Rect.
            float sx = (rx2 - rx1) / (bbMaxX - bbMinX);
            float sy = (ry2 - ry1) / (bbMaxY - bbMinY);
            float tx = rx1 - bbMinX * sx;
            float ty = ry1 - bbMinY * sy;
            var fitMatrix = new SKMatrix(sx, 0, tx, 0, sy, ty, 0, 0, 1);

            _canvas.Save();
            try
            {
                _canvas.Concat(ref fitMatrix);
                RenderFormXObject(appearance);
            }
            catch
            {
                // Never let one malformed annotation kill the rest of
                // the page; it's strictly an overlay on top of content
                // we've already successfully rendered.
            }
            finally
            {
                _canvas.Restore();
            }
        }
    }

    /// <summary>
    /// Resolve <paramref name="annot"/>'s normal appearance to a Form
    /// XObject stream. <c>/AP /N</c> is either:
    /// <list type="bullet">
    /// <item>a single stream — used regardless of state, or</item>
    /// <item>a dictionary keyed by state name (Off / Yes / etc.) where
    ///   <c>/AS</c> picks the active entry — Widget annotations and
    ///   appearance-stateful ones use this.</item>
    /// </list>
    /// Returns null when no usable appearance is present.
    /// </summary>
    private Pdfe.Core.Primitives.PdfStream? ResolveAppearanceN(Pdfe.Core.Document.PdfAnnotation annot)
    {
        var apObj = annot.RawDictionary.GetOptional("AP");
        if (apObj == null) return null;
        if (_page.Document.Resolve(apObj) is not Pdfe.Core.Primitives.PdfDictionary ap) return null;
        var nObj = ap.GetOptional("N");
        if (nObj == null) return null;
        var resolved = _page.Document.Resolve(nObj);

        if (resolved is Pdfe.Core.Primitives.PdfStream stream)
            return stream;

        if (resolved is Pdfe.Core.Primitives.PdfDictionary stateDict)
        {
            var stateName = annot.RawDictionary.GetNameOrNull("AS");
            if (stateName != null)
            {
                var stateObj = stateDict.GetOptional(stateName);
                if (stateObj != null &&
                    _page.Document.Resolve(stateObj) is Pdfe.Core.Primitives.PdfStream s)
                    return s;
            }
            // No /AS or unknown state — fall through to first usable entry.
            foreach (var kvp in stateDict)
            {
                if (_page.Document.Resolve(kvp.Value) is Pdfe.Core.Primitives.PdfStream s)
                    return s;
            }
        }
        return null;
    }

    /// <summary>
    /// Synthesize a minimum-viable visual for an annotation without
    /// <c>/AP /N</c>. Modeled after what Acrobat / Preview / Chrome show
    /// for interactive PDFs — a colored rectangle around the field —
    /// not a full reproduction of the field's would-be value (we don't
    /// interpret /DA + /V here; that's a substantial separate feature).
    /// Covers:
    /// <list type="bullet">
    /// <item><c>/Widget</c>: form-field highlight rectangle (background
    ///   from <c>/MK /BG</c> if present, border from <c>/MK /BC</c>
    ///   plus the <c>/BS</c> width, falling back to a neutral
    ///   light-blue field highlight similar to Acrobat's default).</item>
    /// <item><c>/Link</c>: thin border using the annotation's <c>/C</c>
    ///   color when present (links without /C are intentionally
    ///   invisible in print, matching every commercial viewer).</item>
    /// <item><c>/Square</c> / <c>/Circle</c>: stroked rectangle / ellipse
    ///   using <c>/C</c> + <c>/BS</c>.</item>
    /// </list>
    /// </summary>
    private void RenderDefaultAppearance(Pdfe.Core.Document.PdfAnnotation annot)
    {
        // PDF Y-up Rect; normalize so min < max.
        float rx1 = (float)Math.Min(annot.Rect.Left, annot.Rect.Right);
        float ry1 = (float)Math.Min(annot.Rect.Bottom, annot.Rect.Top);
        float rx2 = (float)Math.Max(annot.Rect.Left, annot.Rect.Right);
        float ry2 = (float)Math.Max(annot.Rect.Bottom, annot.Rect.Top);
        if (rx2 - rx1 < 0.5f || ry2 - ry1 < 0.5f) return;

        var rect = new SKRect(rx1, ry1, rx2, ry2);

        switch (annot.Subtype)
        {
            case Pdfe.Core.Document.PdfAnnotationSubtype.Widget:
                RenderWidgetDefault(annot, rect);
                break;
            case Pdfe.Core.Document.PdfAnnotationSubtype.Link:
                RenderLinkDefault(annot, rect);
                break;
            case Pdfe.Core.Document.PdfAnnotationSubtype.Square:
                RenderShapeDefault(annot, rect, isEllipse: false);
                break;
            case Pdfe.Core.Document.PdfAnnotationSubtype.Circle:
                RenderShapeDefault(annot, rect, isEllipse: true);
                break;
        }
    }

    /// <summary>
    /// Render a default appearance for a Widget annotation that lacks
    /// <c>/AP</c>. Two distinct cases:
    ///
    /// <list type="number">
    /// <item><b>Signature widgets (<c>/FT /Sig</c>):</b> draw a visible
    ///   placeholder border so the user can see "sign here." This
    ///   matches mutool's behaviour and what every commercial viewer
    ///   does for unsigned signature fields. Color comes from
    ///   <c>/MK /BC</c> when set, falling back to a neutral border
    ///   tone that's visible against white but not jarring.</item>
    /// <item><b>Other widgets (<c>/Tx</c>, <c>/Btn</c>, <c>/Ch</c>) with
    ///   <c>/MK</c> styling:</b> render background and/or border using
    ///   the explicitly-supplied colors. Skip when no /MK is set —
    ///   text fields in unfilled forms (IRS-1040, passport renewals,
    ///   etc.) are intentionally invisible at print time and adding
    ///   our own borders here makes pdfe's output diverge from mutool
    ///   by ~10% on real-world form PDFs.</item>
    /// </list>
    /// </summary>
    private void RenderWidgetDefault(Pdfe.Core.Document.PdfAnnotation annot, SKRect rect)
    {
        var fieldType = annot.RawDictionary.GetNameOrNull("FT");
        var mk = annot.RawDictionary.GetOptional("MK") is { } mkObj
            ? _page.Document.Resolve(mkObj) as Pdfe.Core.Primitives.PdfDictionary
            : null;

        var bgColor = mk != null ? ParseColorArray(mk.GetOptional("BG")) : null;
        var bcColor = mk != null ? ParseColorArray(mk.GetOptional("BC")) : null;
        bool isSignature = fieldType == "Sig";
        bool hasExplicitStyle = bgColor.HasValue || bcColor.HasValue;

        // Only signature fields get a synthesized "sign here" placeholder
        // border. Text / button / choice widgets in unfilled forms are
        // routinely emitted without /AP and are intentionally invisible
        // until filled — mutool, Poppler and Foxit all leave them blank
        // unless the author opted into /MK styling.
        if (!isSignature && !hasExplicitStyle) return;

        float borderWidth = (float)(annot.BorderWidth ?? 1.0);
        _canvas.Save();
        try
        {
            using var paint = new SKPaint { IsAntialias = _options.AntiAlias };

            if (bgColor.HasValue)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = bgColor.Value;
                _canvas.DrawRect(rect, paint);
            }

            // Border: use /MK /BC when supplied. For signature fields
            // without /MK, fall back to a neutral medium-blue tone —
            // the goal is "user can see the field exists," not pixel
            // parity with any specific viewer.
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = borderWidth;
            paint.Color = bcColor ?? new SKColor(0x66, 0x99, 0xFF, 0xFF);
            _canvas.DrawRect(rect, paint);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    /// <summary>
    /// Stroke a thin border around a /Link annotation when /C is set —
    /// mimics Acrobat's "show all link borders" default. Without /C the
    /// link is invisible in print, which matches every commercial viewer.
    /// </summary>
    private void RenderLinkDefault(Pdfe.Core.Document.PdfAnnotation annot, SKRect rect)
    {
        if (annot.Color is not { } color) return;
        var (r, g, b) = color;
        float borderWidth = (float)(annot.BorderWidth ?? 1.0);

        using var paint = new SKPaint
        {
            IsAntialias = _options.AntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = borderWidth,
            Color = RgbToColor(r, g, b),
        };
        _canvas.DrawRect(rect, paint);
    }

    /// <summary>
    /// Stroke a Square or Circle annotation outline using its /C color
    /// and /BS width. These annotations are rare without /AP — most
    /// authoring tools bake an appearance — but the few that don't
    /// fall back here.
    /// </summary>
    private void RenderShapeDefault(
        Pdfe.Core.Document.PdfAnnotation annot, SKRect rect, bool isEllipse)
    {
        if (annot.Color is not { } color) return;
        var (r, g, b) = color;
        float borderWidth = (float)(annot.BorderWidth ?? 1.0);

        using var paint = new SKPaint
        {
            IsAntialias = _options.AntiAlias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = borderWidth,
            Color = RgbToColor(r, g, b),
        };
        if (isEllipse) _canvas.DrawOval(rect, paint);
        else _canvas.DrawRect(rect, paint);
    }

    /// <summary>
    /// Parse a PDF color array (1, 3, or 4 components — gray / RGB /
    /// CMYK) into an SKColor. Returns null when the value isn't a valid
    /// array of numbers.
    /// </summary>
    private SKColor? ParseColorArray(Pdfe.Core.Primitives.PdfObject? obj)
    {
        if (obj == null) return null;
        var resolved = _page.Document.Resolve(obj);
        if (resolved is not Pdfe.Core.Primitives.PdfArray arr || arr.Count == 0) return null;
        try
        {
            switch (arr.Count)
            {
                case 1:
                    return GrayToColor(arr.GetNumber(0));
                case 3:
                    return RgbToColor(arr.GetNumber(0), arr.GetNumber(1), arr.GetNumber(2));
                case 4:
                    return CmykToColor(
                        arr.GetNumber(0), arr.GetNumber(1),
                        arr.GetNumber(2), arr.GetNumber(3));
            }
        }
        catch { }
        return null;
    }

    private void RenderFormXObject(Pdfe.Core.Primitives.PdfStream formStream)
    {
        // Cycle detection: a Form XObject that ends up invoking itself
        // (transitively) would otherwise recurse until the .NET stack
        // overflows, which is uncatchable and aborts the whole process.
        if (!_formXObjectStack.Add(formStream)) return;
        if (_formXObjectDepth >= MaxFormXObjectDepth)
        {
            _formXObjectStack.Remove(formStream);
            return;
        }
        _formXObjectDepth++;

        try
        {
            RenderFormXObjectInner(formStream);
        }
        finally
        {
            _formXObjectStack.Remove(formStream);
            _formXObjectDepth--;
        }
    }

    private void RenderFormXObjectInner(Pdfe.Core.Primitives.PdfStream formStream)
    {
        // Form XObjects contain their own content stream
        // Get the form's content and render it recursively
        var formContent = formStream.DecodedData;
        if (formContent.Length == 0)
            return;

        _canvas.Save();

        // Push the form's own /Resources so font / XObject lookups inside
        // its content stream resolve against the form's resource dict
        // first (with fallback to outer scopes via the resources stack).
        // PDF 32000-2 §7.8.3: a Form XObject inherits resources from its
        // page, so falling through is required for forms that omit names
        // their content references.
        var formResources = formStream.GetOptional("Resources") is { } resObj
            ? _page.Document.Resolve(resObj) as Pdfe.Core.Primitives.PdfDictionary
            : null;
        _resourcesStack.Push(formResources);

        try
        {
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
        }
        finally
        {
            _resourcesStack.Pop();
            _canvas.Restore();
        }
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
            case 1: // Function-based shading
                RenderFunctionShading(shading);
                break;
            case 2: // Axial shading (linear gradient)
                RenderAxialShading(shading);
                break;
            case 3: // Radial shading (radial gradient)
                RenderRadialShading(shading);
                break;
            // Types 4-7 are more complex (mesh-based)
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

        var (startColor, endColor, stops, positions) = ResolveGradientColors(shading);

        // Create the gradient shader
        using var shader = stops != null && stops.Length > 2
            ? SKShader.CreateLinearGradient(
                new SKPoint(x0, y0),
                new SKPoint(x1, y1),
                stops,
                positions,
                SKShaderTileMode.Clamp)
            : SKShader.CreateLinearGradient(
                new SKPoint(x0, y0),
                new SKPoint(x1, y1),
                new[] { startColor, endColor },
                null,
                SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = shader,
            BlendMode = _state.BlendMode,
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

        var (startColor, endColor, stops, positions) = ResolveGradientColors(shading);

        // Create the two-point conical gradient
        using var shader = stops != null && stops.Length > 2
            ? SKShader.CreateTwoPointConicalGradient(
                new SKPoint(x0, y0), r0,
                new SKPoint(x1, y1), r1,
                stops,
                positions,
                SKShaderTileMode.Clamp)
            : SKShader.CreateTwoPointConicalGradient(
                new SKPoint(x0, y0), r0,
                new SKPoint(x1, y1), r1,
                new[] { startColor, endColor },
                null,
                SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            Shader = shader,
            BlendMode = _state.BlendMode,
            IsAntialias = _options.AntiAlias
        };

        // Fill the current clipping area
        var clipBounds = _canvas.LocalClipBounds;
        _canvas.DrawRect(clipBounds, paint);
    }

    private void RenderFunctionShading(Pdfe.Core.Primitives.PdfDictionary shading)
    {
        var funcObj = shading.GetOptional("Function");
        var colorSpaceName = shading.GetNameOrNull("ColorSpace") ?? "DeviceGray";
        var comps = PdfFunctionEvaluator.Evaluate(funcObj, 0.5) ?? new[] { 0.0 };
        var color = ComponentsToSkColor(comps, colorSpaceName);

        using var paint = new SKPaint
        {
            Color = color,
            BlendMode = _state.BlendMode,
            IsAntialias = _options.AntiAlias
        };

        var clipBounds = _canvas.LocalClipBounds;
        _canvas.DrawRect(clipBounds, paint);
    }

    private (SKColor start, SKColor end, SKColor[]? stops, float[]? positions) ResolveGradientColors(
        Pdfe.Core.Primitives.PdfDictionary shading)
    {
        var colorSpaceName = shading.GetNameOrNull("ColorSpace") ?? "DeviceGray";
        var funcObj = shading.GetOptional("Function");

        var c0 = PdfFunctionEvaluator.Evaluate(funcObj, 0.0) ?? new[] { 0.0 };
        var c1 = PdfFunctionEvaluator.Evaluate(funcObj, 1.0) ?? new[] { 1.0 };

        var startColor = ComponentsToSkColor(c0, colorSpaceName);
        var endColor = ComponentsToSkColor(c1, colorSpaceName);

        SKColor[]? stops = null;
        float[]? positions = null;

        if (funcObj is Pdfe.Core.Primitives.PdfDictionary fd && fd.GetInt("FunctionType", -1) == 3)
        {
            var boundsObj = fd.GetOptional("Bounds") as Pdfe.Core.Primitives.PdfArray;
            if (boundsObj != null && boundsObj.Count > 0)
            {
                var pts = new List<float> { 0f };
                for (int i = 0; i < boundsObj.Count; i++)
                    pts.Add((float)boundsObj.GetNumber(i));
                pts.Add(1f);

                var colors = new List<SKColor>();
                foreach (var pt in pts)
                {
                    var c = PdfFunctionEvaluator.Evaluate(funcObj, pt) ?? new[] { 0.0 };
                    colors.Add(ComponentsToSkColor(c, colorSpaceName));
                }

                stops = colors.ToArray();
                positions = pts.ToArray();
            }
        }

        return (startColor, endColor, stops, positions);
    }

    private static SKColor ComponentsToSkColor(double[] comps, string colorSpace)
    {
        return colorSpace switch
        {
            "DeviceGray" or "G" =>
                comps.Length >= 1 ? ToGray(comps[0]) : SKColors.Black,
            "DeviceRGB" or "RGB" =>
                comps.Length >= 3 ? ToRGB(comps[0], comps[1], comps[2]) : SKColors.Black,
            "DeviceCMYK" or "CMYK" =>
                comps.Length >= 4 ? CmykToRgbColor(comps[0], comps[1], comps[2], comps[3]) : SKColors.Black,
            _ => comps.Length >= 3 ? ToRGB(comps[0], comps[1], comps[2])
               : comps.Length >= 1 ? ToGray(comps[0]) : SKColors.Black
        };
    }

    private static SKColor ToGray(double g)
    {
        byte v = (byte)Math.Clamp(g * 255, 0, 255);
        return new SKColor(v, v, v);
    }

    private static SKColor ToRGB(double r, double g, double b) =>
        new SKColor(
            (byte)Math.Clamp(r * 255, 0, 255),
            (byte)Math.Clamp(g * 255, 0, 255),
            (byte)Math.Clamp(b * 255, 0, 255));

    private static SKColor CmykToRgbColor(double c, double m, double y, double k)
    {
        double r = (1 - c) * (1 - k);
        double g = (1 - m) * (1 - k);
        double b = (1 - y) * (1 - k);
        return ToRGB(r, g, b);
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
        var values = operands.Where(o => !o.StartsWith("/")).ToList();

        if (values.Count == 0)
            return null;

        var doubleValues = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
            doubleValues[i] = ParseNumber(values[i]);

        var cs = ResolveColorSpace(colorSpace);
        if (cs != null && cs.Type != PdfColorSpaceType.Pattern)
        {
            var (r, g, b) = cs.ToRgb(doubleValues);
            return RgbToColor(r, g, b);
        }

        return colorSpace switch
        {
            "Pattern" when operands.Any(o => o.StartsWith("/")) =>
                null,

            _ => null
        };
    }

    private PdfColorSpace? ResolveColorSpace(string name)
    {
        var cs = PdfColorSpace.FromName(name);
        if (cs.Type != PdfColorSpaceType.Unknown)
            return cs;

        var csObj = _page.GetColorSpaceObject(name);
        if (csObj != null)
            return PdfColorSpace.Parse(csObj, _page.Document);

        return null;
    }

    /// <summary>
    /// Walk the resources stack top-down looking for a font definition.
    /// The innermost Form XObject's /Resources wins; we fall through to
    /// outer XObjects and finally the page when the name isn't defined
    /// locally — matches the "inherit if not found" rule from PDF 32000-2
    /// §7.8.3 for Form XObject resource resolution.
    /// </summary>
    private Pdfe.Core.Primitives.PdfDictionary? ResolveFontFromActiveResources(string fontName)
    {
        foreach (var resources in _resourcesStack)
        {
            if (resources == null) continue;
            var fontsObj = resources.GetOptional("Font");
            if (fontsObj == null) continue;
            if (_page.Document.Resolve(fontsObj) is not Pdfe.Core.Primitives.PdfDictionary fonts)
                continue;
            var fontObj = fonts.GetOptional(fontName);
            if (fontObj == null) continue;
            return _page.Document.Resolve(fontObj) as Pdfe.Core.Primitives.PdfDictionary;
        }
        return null;
    }

    /// <summary>
    /// Stack-aware XObject lookup, same fallback rule as
    /// <see cref="ResolveFontFromActiveResources"/>. Returns the resolved
    /// XObject (typically a stream); caller checks /Subtype.
    /// </summary>
    private Pdfe.Core.Primitives.PdfObject? ResolveXObjectFromActiveResources(string name)
    {
        foreach (var resources in _resourcesStack)
        {
            if (resources == null) continue;
            var xobjsObj = resources.GetOptional("XObject");
            if (xobjsObj == null) continue;
            if (_page.Document.Resolve(xobjsObj) is not Pdfe.Core.Primitives.PdfDictionary xobjs)
                continue;
            var x = xobjs.GetOptional(name);
            if (x == null) continue;
            return _page.Document.Resolve(x);
        }
        return null;
    }

    #endregion

    #region Inline Images (BI, ID, EI operators) - Issue #297

    /// <summary>
    /// Rasterize an inline image (§8.9.7) from the dict header text the
    /// tokenizer captured between BI and ID, and the raw data bytes
    /// captured between ID and EI. The dict uses abbreviated keys per
    /// Table 91 (W/H/CS/F/BPC/D/DP/IM/I/L) and abbreviated filter /
    /// colorspace values per Tables 92 and 90; we resolve them all to
    /// their full forms and dispatch through a synthetic
    /// <see cref="Pdfe.Core.Primitives.PdfStream"/> so the existing
    /// image-XObject rasterizer does the actual decoding and drawing.
    /// </summary>
    private void RenderInlineImage(string headerText, byte[] dataBytes)
    {
        var dict = ParseInlineImageDict(headerText);
        if (dict == null) return;

        // Inline image data may be filter-encoded the same way an
        // image XObject's stream is (FlateDecode for raw RGB, DCTDecode
        // for JPEG, etc.). Build a synthetic PdfStream so the existing
        // RenderImageXObject pipeline handles colour-space resolution
        // and rasterization uniformly.
        var stream = new Pdfe.Core.Primitives.PdfStream(dict, dataBytes);
        try
        {
            // PdfStream constructed in-process has no cached decoded
            // bytes; PdfStream.DecodedData throws InvalidOperationException
            // until something runs the filter chain. Run it now —
            // RenderImageXObject reads DecodedData when the filter is
            // FlateDecode / RunLength / etc., and EncodedData when it's
            // DCTDecode / JPXDecode (JPEG path stays pass-through).
            if (stream.IsFiltered)
                new Pdfe.Core.Parsing.StreamDecompressor().Decompress(stream);
            RenderImageXObject(stream);
        }
        catch
        {
            // Single bad inline image shouldn't kill the page.
        }
    }

    /// <summary>
    /// Parse the dict-header text between BI and ID into a
    /// <see cref="Pdfe.Core.Primitives.PdfDictionary"/> with abbreviated
    /// keys/values normalized to their full forms (Table 91, Table 92,
    /// Table 90). Returns null when the header is empty or unparseable.
    /// </summary>
    private static Pdfe.Core.Primitives.PdfDictionary? ParseInlineImageDict(string header)
    {
        var dict = new Pdfe.Core.Primitives.PdfDictionary();

        // Tokenize the header just like the rest of the content stream.
        // The dict body is just key/value pairs (`/Key value /Key value`)
        // with no surrounding `<<` `>>`.
        var toks = new List<string>();
        int i = 0, len = header.Length;
        while (i < len)
        {
            while (i < len && char.IsWhiteSpace(header[i])) i++;
            if (i >= len) break;
            char c = header[i];
            if (c == '/' || c == '(' || c == '<' || c == '[')
            {
                // Use the same delimiter logic as the main tokenizer
                // for names / strings / hex strings / arrays.
                int start = i;
                if (c == '/')
                {
                    i++;
                    while (i < len && !IsDelimiterOrWhitespace(header[i])) i++;
                    toks.Add(header[start..i]);
                }
                else if (c == '[')
                {
                    int depth = 1; i++;
                    while (i < len && depth > 0)
                    {
                        if (header[i] == '[') depth++;
                        else if (header[i] == ']') depth--;
                        i++;
                    }
                    toks.Add(header[start..i]);
                }
                else if (c == '<')
                {
                    if (i + 1 < len && header[i + 1] == '<')
                    {
                        // Inline images don't normally contain nested dicts,
                        // but be defensive — match >> to swallow and skip.
                        int depth = 1; i += 2;
                        while (i < len - 1 && depth > 0)
                        {
                            if (header[i] == '<' && header[i + 1] == '<') { depth++; i += 2; }
                            else if (header[i] == '>' && header[i + 1] == '>') { depth--; i += 2; }
                            else i++;
                        }
                        toks.Add(header[start..i]);
                    }
                    else
                    {
                        // Hex string </…>
                        i++;
                        while (i < len && header[i] != '>') i++;
                        if (i < len) i++;
                        toks.Add(header[start..i]);
                    }
                }
                else if (c == '(')
                {
                    int depth = 1; i++;
                    while (i < len && depth > 0)
                    {
                        if (header[i] == '\\' && i + 1 < len) i += 2;
                        else if (header[i] == '(') { depth++; i++; }
                        else if (header[i] == ')') { depth--; i++; }
                        else i++;
                    }
                    toks.Add(header[start..i]);
                }
            }
            else
            {
                int start = i;
                while (i < len && !IsDelimiterOrWhitespace(header[i])) i++;
                if (i > start) toks.Add(header[start..i]);
            }
        }

        // Walk pairs (/Name value). Map abbreviated keys/values to their
        // full forms before storing.
        for (int p = 0; p < toks.Count - 1; p++)
        {
            var nameTok = toks[p];
            if (!nameTok.StartsWith("/")) continue;
            string key = NormalizeInlineKey(nameTok.Substring(1));
            string valueTok = toks[p + 1];
            p++; // consumed the value

            var value = ParseInlineImageValue(key, valueTok);
            if (value != null)
                dict[new Pdfe.Core.Primitives.PdfName(key)] = value;
        }

        // Reject if no Width / Height — RenderImageXObject would early-out anyway.
        if (!dict.ContainsKey("Width") || !dict.ContainsKey("Height"))
            return null;
        return dict;
    }

    /// <summary>
    /// Per Table 91, inline image dicts may use one-or-two-letter
    /// abbreviations in place of the full names — normalize to full so
    /// downstream code (which expects /Width, /Filter, etc.) works.
    /// </summary>
    private static string NormalizeInlineKey(string abbr) => abbr switch
    {
        "W"   => "Width",
        "H"   => "Height",
        "CS"  => "ColorSpace",
        "BPC" => "BitsPerComponent",
        "F"   => "Filter",
        "DP"  => "DecodeParms",
        "D"   => "Decode",
        "IM"  => "ImageMask",
        "I"   => "Interpolate",
        "L"   => "Length",
        _     => abbr,
    };

    /// <summary>
    /// Convert an inline-image dict value token into the corresponding
    /// PdfObject. For /Filter and /ColorSpace we additionally expand
    /// abbreviated single-token values per Tables 92 and 90 so
    /// StreamDecompressor / PdfColorSpace see /FlateDecode rather than
    /// /Fl, /DeviceRGB rather than /RGB.
    /// </summary>
    private static Pdfe.Core.Primitives.PdfObject? ParseInlineImageValue(string key, string token)
    {
        if (token.StartsWith("/"))
        {
            string name = token.Substring(1);
            if (key == "Filter")        name = ExpandInlineFilter(name);
            else if (key == "ColorSpace") name = ExpandInlineColorSpace(name);
            return new Pdfe.Core.Primitives.PdfName(name);
        }
        if (token == "true")  return Pdfe.Core.Primitives.PdfBoolean.True;
        if (token == "false") return Pdfe.Core.Primitives.PdfBoolean.False;
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
            return new Pdfe.Core.Primitives.PdfInteger(iv);
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
            return new Pdfe.Core.Primitives.PdfReal(dv);
        // Arrays, strings, etc. fall through unhandled — rare in inline
        // image dicts (most use simple name/integer/bool values).
        return null;
    }

    private static string ExpandInlineFilter(string abbr) => abbr switch
    {
        "A"   => "ASCIIHexDecode",
        "A85" => "ASCII85Decode",
        "LZW" => "LZWDecode",
        "Fl"  => "FlateDecode",
        "RL"  => "RunLengthDecode",
        "CCF" => "CCITTFaxDecode",
        "DCT" => "DCTDecode",
        _     => abbr,
    };

    private static string ExpandInlineColorSpace(string abbr) => abbr switch
    {
        "G"    => "DeviceGray",
        "RGB"  => "DeviceRGB",
        "CMYK" => "DeviceCMYK",
        "I"    => "Indexed",
        _      => abbr,
    };

    #endregion

    #region Tokenizer

    private List<string> Tokenize(string content)
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
            {
                var tok = content[tokenStart..i];
                tokens.Add(tok);

                // Inline image fast-skip: when we see the BI operator,
                // scan forward to the matching EI marker (after the ID
                // separator) and discard the binary image bytes between
                // them. Without this, the tokenizer turns the raw image
                // data into thousands of garbage tokens that the
                // dispatcher then iterates over — visible as a
                // multi-minute hang on PDFs with large inline images
                // (e.g. SafeDocs issue14256.pdf, which has eight 20×10
                // hex-encoded images totaling 13 KB of "operators"
                // that aren't actually operators).
                //
                // Capture the inline image dict header (between BI and
                // ID) and the binary image data (between ID and EI) into
                // _inlineImages, then re-emit as a numeric operand
                // pointing into that side-table followed by the BI
                // operator. RenderInlineImage looks the entry up. We
                // can't pass binary bytes through the token list
                // safely (Latin1 round-trip works, but binary 0x00 etc.
                // bytes inside operand strings break later parsing).
                if (tok == "BI")
                {
                    // Find the ID marker (single token "ID" followed
                    // by exactly one whitespace per spec).
                    int idIdx = content.IndexOf("\nID", i);
                    if (idIdx < 0) idIdx = content.IndexOf(" ID", i);
                    if (idIdx < 0) idIdx = content.IndexOf("\rID", i);
                    if (idIdx >= 0)
                    {
                        // Header text spans from end of "BI " to start of "ID"
                        // (excluding the BI/ID markers themselves).
                        int headerStart = i; // i is just past "BI"
                        int headerEnd = idIdx; // before the surrounding whitespace + "ID"
                        string header = content.Substring(headerStart, headerEnd - headerStart);

                        // Skip over the dict entries up to and including
                        // ID + the one separator byte.
                        int after = idIdx + 3;
                        if (after < len && (content[after] == '\n' || content[after] == '\r' || content[after] == ' '))
                            after++;
                        // Now scan for "EI" at a word boundary.
                        int eiIdx = -1;
                        for (int j = after; j + 1 < len; j++)
                        {
                            if (content[j] == 'E' && content[j + 1] == 'I')
                            {
                                bool leftBoundary = j == 0 || char.IsWhiteSpace(content[j - 1]);
                                bool rightBoundary = j + 2 >= len ||
                                    char.IsWhiteSpace(content[j + 2]) ||
                                    content[j + 2] == '/' || content[j + 2] == '<';
                                if (leftBoundary && rightBoundary)
                                {
                                    eiIdx = j + 2;
                                    break;
                                }
                            }
                        }
                        if (eiIdx > 0)
                        {
                            // Data bytes are content[after..(eiIdx-2)]
                            // (eiIdx-2 = start of "EI"). Trim a single
                            // trailing whitespace separator before EI
                            // if present, per spec.
                            int dataEnd = eiIdx - 2;
                            if (dataEnd > after && (content[dataEnd - 1] == '\n' || content[dataEnd - 1] == '\r' || content[dataEnd - 1] == ' '))
                                dataEnd--;
                            // The renderer received content as a Latin1
                            // string of original bytes; reverse the
                            // mapping char-by-char to get the raw bytes
                            // back without re-encoding.
                            int dataLen = Math.Max(0, dataEnd - after);
                            var dataBytes = new byte[dataLen];
                            for (int k = 0; k < dataLen; k++)
                                dataBytes[k] = (byte)content[after + k];

                            int idx = _inlineImages.Count;
                            _inlineImages.Add((header, dataBytes));
                            // The "BI" was already pushed at line 2776;
                            // insert the side-table index *before* it so
                            // the dispatcher sees it as an operand of
                            // BI when the BI token fires.
                            tokens.Insert(tokens.Count - 1, idx.ToString(CultureInfo.InvariantCulture));
                            i = eiIdx;
                            continue;
                        }
                    }
                    // Couldn't parse — leave BI alone (no operand) so the
                    // dispatcher treats it as a no-op.
                }
            }
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
    public SKBlendMode BlendMode { get; set; } = SKBlendMode.SrcOver;

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
            StrokeColorSpace = StrokeColorSpace,
            BlendMode = BlendMode
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
