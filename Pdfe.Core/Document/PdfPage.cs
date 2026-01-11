using Pdfe.Core.Primitives;
using Pdfe.Core.Text;

namespace Pdfe.Core.Document;

/// <summary>
/// Represents a page in a PDF document.
/// </summary>
public class PdfPage
{
    private readonly PdfDocument _document;
    private readonly PdfDictionary _pageDict;

    /// <summary>
    /// The 1-based page number.
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// Creates a new page wrapper.
    /// </summary>
    internal PdfPage(PdfDocument document, PdfDictionary pageDict, int pageNumber)
    {
        _document = document;
        _pageDict = pageDict;
        PageNumber = pageNumber;
    }

    /// <summary>
    /// The underlying page dictionary.
    /// </summary>
    public PdfDictionary Dictionary => _pageDict;

    /// <summary>
    /// The document this page belongs to.
    /// </summary>
    public PdfDocument Document => _document;

    /// <summary>
    /// Get the extracted text content from the page.
    /// </summary>
    public string Text
    {
        get
        {
            var extractor = new TextExtractor(this);
            return extractor.ExtractText();
        }
    }

    /// <summary>
    /// Get all letters extracted from the page with position information.
    /// </summary>
    public IReadOnlyList<Letter> Letters
    {
        get
        {
            var extractor = new TextExtractor(this);
            return extractor.ExtractLetters();
        }
    }

    /// <summary>
    /// Page width in points.
    /// </summary>
    public double Width => MediaBox.Width;

    /// <summary>
    /// Page height in points.
    /// </summary>
    public double Height => MediaBox.Height;

    /// <summary>
    /// Page rotation in degrees (0, 90, 180, 270).
    /// </summary>
    public int Rotation => GetInheritedInt("Rotate", 0) % 360;

    /// <summary>
    /// The media box (page boundaries).
    /// </summary>
    public PdfRectangle MediaBox => GetInheritedRectangle("MediaBox")
        ?? throw new InvalidOperationException("Page has no MediaBox");

    /// <summary>
    /// The crop box (visible area). Falls back to MediaBox if not specified.
    /// </summary>
    public PdfRectangle CropBox => GetInheritedRectangle("CropBox") ?? MediaBox;

    /// <summary>
    /// The bleed box. Falls back to CropBox if not specified.
    /// </summary>
    public PdfRectangle BleedBox => GetRectangle("BleedBox") ?? CropBox;

    /// <summary>
    /// The trim box. Falls back to CropBox if not specified.
    /// </summary>
    public PdfRectangle TrimBox => GetRectangle("TrimBox") ?? CropBox;

    /// <summary>
    /// The art box. Falls back to CropBox if not specified.
    /// </summary>
    public PdfRectangle ArtBox => GetRectangle("ArtBox") ?? CropBox;

    /// <summary>
    /// Get the page resources dictionary.
    /// </summary>
    public PdfDictionary? Resources => GetInheritedDictionary("Resources");

    /// <summary>
    /// Get the raw content stream bytes (decoded).
    /// </summary>
    public byte[] GetContentStreamBytes()
    {
        var contentsObj = _pageDict.GetOptional("Contents");
        if (contentsObj == null)
            return Array.Empty<byte>();

        contentsObj = _document.Resolve(contentsObj);

        if (contentsObj is PdfStream stream)
        {
            return stream.DecodedData;
        }
        else if (contentsObj is PdfArray array)
        {
            // Multiple content streams - concatenate
            using var ms = new MemoryStream();
            foreach (var item in array)
            {
                var resolved = _document.Resolve(item);
                if (resolved is PdfStream s)
                {
                    ms.Write(s.DecodedData);
                    ms.WriteByte((byte)'\n'); // Separate streams with newline
                }
            }
            return ms.ToArray();
        }

        return Array.Empty<byte>();
    }

    /// <summary>
    /// Get a font from the page resources.
    /// </summary>
    public PdfDictionary? GetFont(string fontName)
    {
        var fonts = Resources?.GetDictionaryOrNull("Font");
        if (fonts == null)
            return null;

        var fontObj = fonts.GetOptional(fontName);
        if (fontObj == null)
            return null;

        return _document.Resolve(fontObj) as PdfDictionary;
    }

    /// <summary>
    /// Get all fonts used on this page.
    /// </summary>
    public IEnumerable<(string Name, PdfDictionary Font)> GetFonts()
    {
        var fonts = Resources?.GetDictionaryOrNull("Font");
        if (fonts == null)
            yield break;

        foreach (var kvp in fonts)
        {
            var fontDict = _document.Resolve(kvp.Value) as PdfDictionary;
            if (fontDict != null)
            {
                yield return (kvp.Key.Value, fontDict);
            }
        }
    }

    /// <summary>
    /// Get an XObject (form or image) from the page resources.
    /// </summary>
    public PdfObject? GetXObject(string name)
    {
        var xobjects = Resources?.GetDictionaryOrNull("XObject");
        if (xobjects == null)
            return null;

        var obj = xobjects.GetOptional(name);
        return obj != null ? _document.Resolve(obj) : null;
    }

    /// <summary>
    /// Get a graphics state from the page resources.
    /// </summary>
    public PdfDictionary? GetExtGState(string name)
    {
        var extGState = Resources?.GetDictionaryOrNull("ExtGState");
        if (extGState == null)
            return null;

        var obj = extGState.GetOptional(name);
        return obj != null ? _document.Resolve(obj) as PdfDictionary : null;
    }

    #region Inherited Properties

    /// <summary>
    /// Get an inherited integer value (walks up page tree).
    /// </summary>
    private int GetInheritedInt(string key, int defaultValue)
    {
        var current = _pageDict;
        while (current != null)
        {
            if (current.ContainsKey(key))
                return current.GetInt(key, defaultValue);

            var parentRef = current.GetReferenceOrNull("Parent");
            current = parentRef != null ? _document.GetObject(parentRef) as PdfDictionary : null;
        }
        return defaultValue;
    }

    /// <summary>
    /// Get an inherited dictionary value (walks up page tree).
    /// </summary>
    private PdfDictionary? GetInheritedDictionary(string key)
    {
        var current = _pageDict;
        while (current != null)
        {
            var obj = current.GetOptional(key);
            if (obj != null)
            {
                var resolved = _document.Resolve(obj);
                if (resolved is PdfDictionary dict)
                    return dict;
            }

            var parentRef = current.GetReferenceOrNull("Parent");
            current = parentRef != null ? _document.GetObject(parentRef) as PdfDictionary : null;
        }
        return null;
    }

    /// <summary>
    /// Get an inherited rectangle (walks up page tree).
    /// </summary>
    private PdfRectangle? GetInheritedRectangle(string key)
    {
        var current = _pageDict;
        while (current != null)
        {
            var rect = GetRectangleFromDict(current, key);
            if (rect.HasValue)
                return rect;

            var parentRef = current.GetReferenceOrNull("Parent");
            current = parentRef != null ? _document.GetObject(parentRef) as PdfDictionary : null;
        }
        return null;
    }

    /// <summary>
    /// Get a rectangle from this page's dictionary (non-inherited).
    /// </summary>
    private PdfRectangle? GetRectangle(string key)
    {
        return GetRectangleFromDict(_pageDict, key);
    }

    /// <summary>
    /// Get a rectangle from a dictionary.
    /// </summary>
    private PdfRectangle? GetRectangleFromDict(PdfDictionary dict, string key)
    {
        var obj = dict.GetOptional(key);
        if (obj == null)
            return null;

        var resolved = _document.Resolve(obj);
        if (resolved is not PdfArray arr || arr.Count != 4)
            return null;

        return new PdfRectangle(
            arr.GetNumber(0),
            arr.GetNumber(1),
            arr.GetNumber(2),
            arr.GetNumber(3)
        );
    }

    #endregion

    /// <inheritdoc />
    public override string ToString() => $"Page {PageNumber} ({Width}x{Height} pts)";
}

/// <summary>
/// A rectangle in PDF coordinates (bottom-left origin).
/// </summary>
public readonly record struct PdfRectangle(double Left, double Bottom, double Right, double Top)
{
    /// <summary>
    /// Width of the rectangle.
    /// </summary>
    public double Width => Math.Abs(Right - Left);

    /// <summary>
    /// Height of the rectangle.
    /// </summary>
    public double Height => Math.Abs(Top - Bottom);

    /// <summary>
    /// Create a rectangle from an array.
    /// </summary>
    public static PdfRectangle FromArray(PdfArray arr)
    {
        if (arr.Count != 4)
            throw new ArgumentException("Rectangle array must have 4 elements");

        return new PdfRectangle(
            arr.GetNumber(0),
            arr.GetNumber(1),
            arr.GetNumber(2),
            arr.GetNumber(3)
        );
    }

    /// <summary>
    /// Normalize the rectangle (ensure Left &lt; Right and Bottom &lt; Top).
    /// </summary>
    public PdfRectangle Normalize()
    {
        return new PdfRectangle(
            Math.Min(Left, Right),
            Math.Min(Bottom, Top),
            Math.Max(Left, Right),
            Math.Max(Bottom, Top)
        );
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Left:F2}, {Bottom:F2}, {Right:F2}, {Top:F2}]";
}
