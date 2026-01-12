using Pdfe.Core.Content;
using Pdfe.Core.Graphics;
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
    private PdfGraphics? _graphics;

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
    /// Get all words extracted from the page.
    /// A word is a sequence of letters separated by whitespace.
    /// </summary>
    /// <returns>List of words with their letters and bounding boxes.</returns>
    public IReadOnlyList<Word> GetWords()
    {
        var extractor = new TextExtractor(this);
        return extractor.ExtractWords();
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
    public int Rotation
    {
        get => GetInheritedInt("Rotate", 0) % 360;
        set
        {
            // Normalize to 0, 90, 180, or 270
            value = ((value % 360) + 360) % 360;
            if (value != 0 && value != 90 && value != 180 && value != 270)
                throw new ArgumentException("Rotation must be 0, 90, 180, or 270 degrees", nameof(value));

            if (value == 0)
                _pageDict.Remove("Rotate");
            else
                _pageDict.SetInt("Rotate", value);
        }
    }

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
    /// Sets the content stream bytes for this page.
    /// </summary>
    public void SetContentStreamBytes(byte[] data)
    {
        var contentsObj = _pageDict.GetOptional("Contents");

        if (contentsObj == null)
        {
            // Create a new content stream
            var newStream = new PdfStream(data);
            // For now, store directly in page dictionary (simplified)
            // A full implementation would create a new indirect object
            _pageDict["Contents"] = newStream;
            return;
        }

        contentsObj = _document.Resolve(contentsObj);

        if (contentsObj is PdfStream stream)
        {
            // Update existing stream (also updates encoded data and length)
            stream.DecodedData = data;
        }
        else if (contentsObj is PdfArray array && array.Count > 0)
        {
            // Update first stream in array
            var firstRef = array[0];
            var resolved = _document.Resolve(firstRef);
            if (resolved is PdfStream firstStream)
            {
                // Update first stream (removes filters too)
                firstStream.DecodedData = data;
                // Clear other streams in the array if present
                while (array.Count > 1)
                    array.RemoveAt(array.Count - 1);
            }
        }
    }

    /// <summary>
    /// Gets a graphics context for drawing on this page.
    /// </summary>
    public PdfGraphics GetGraphics()
    {
        _graphics ??= new PdfGraphics(this);
        return _graphics;
    }

    /// <summary>
    /// Get the content stream as a parsed ContentStream object.
    /// </summary>
    public ContentStream GetContentStream()
    {
        var bytes = GetContentStreamBytes();
        if (bytes.Length == 0)
            return new ContentStream();

        var parser = new ContentStreamParser(bytes, this);
        return parser.Parse();
    }

    /// <summary>
    /// Set the content stream from a ContentStream object.
    /// </summary>
    public void SetContentStream(ContentStream content)
    {
        var writer = new ContentStreamWriter();
        var bytes = writer.Write(content);
        SetContentStreamBytes(bytes);
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
    /// Adds a font to the page resources if not already present.
    /// Returns the font resource name (e.g., "F1", "F2").
    /// </summary>
    public string AddFont(PdfFont font)
    {
        // Get or create Resources dictionary
        var resources = EnsureResources();

        // Get or create Font dictionary within Resources
        if (!resources.TryGetValue("Font", out var fontDictObj) || fontDictObj is not PdfDictionary fontDict)
        {
            fontDict = new PdfDictionary();
            resources["Font"] = fontDict;
        }

        // Check if this font is already registered by base font name
        foreach (var kvp in fontDict)
        {
            var existingFont = _document.Resolve(kvp.Value) as PdfDictionary;
            if (existingFont != null)
            {
                var existingBaseFont = existingFont.GetNameOrNull("BaseFont");
                if (existingBaseFont == font.BaseFont)
                {
                    return kvp.Key.Value; // Return existing name
                }
            }
        }

        // Find next available font name
        var fontName = font.Name;
        int counter = 1;
        while (fontDict.ContainsKey(fontName))
        {
            fontName = $"F{counter++}";
        }

        // Add the font dictionary
        fontDict[fontName] = font.CreateFontDictionary();

        return fontName;
    }

    /// <summary>
    /// Ensures the page has a Resources dictionary, creating one if needed.
    /// </summary>
    private PdfDictionary EnsureResources()
    {
        var resources = Resources;
        if (resources != null)
            return resources;

        // Create a new Resources dictionary
        resources = new PdfDictionary();
        _pageDict["Resources"] = resources;
        return resources;
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

    /// <summary>
    /// Get a shading dictionary from the page resources.
    /// </summary>
    public PdfDictionary? GetShading(string name)
    {
        var shadings = Resources?.GetDictionaryOrNull("Shading");
        if (shadings == null)
            return null;

        var obj = shadings.GetOptional(name);
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

/// <summary>
/// A point in PDF coordinates (bottom-left origin).
/// </summary>
public readonly record struct PdfPoint(double X, double Y)
{
    /// <inheritdoc />
    public override string ToString() => $"({X:F2}, {Y:F2})";
}
