using Pdfe.Core.Parsing;
using Pdfe.Core.Primitives;
using Pdfe.Core.Writing;

namespace Pdfe.Core.Document;

/// <summary>
/// Represents a PDF document.
/// Main entry point for reading and manipulating PDFs.
/// </summary>
public class PdfDocument : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly Dictionary<int, XRefEntry> _xref;
    private readonly Dictionary<int, PdfObject> _objectCache;
    private readonly PdfParser _parser;
    private readonly StreamDecompressor _decompressor;
    private PageCollection? _pages;

    /// <summary>
    /// The trailer dictionary.
    /// </summary>
    public PdfDictionary Trailer { get; }

    /// <summary>
    /// The document catalog.
    /// </summary>
    public PdfDictionary Catalog { get; }

    /// <summary>
    /// Number of pages in the document.
    /// </summary>
    public int PageCount { get; }

    /// <summary>
    /// PDF version (e.g., "1.4", "1.7", "2.0").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Whether this document is encrypted.
    /// </summary>
    public bool IsEncrypted => Trailer.ContainsKey("Encrypt");

    /// <summary>
    /// Information dictionary (metadata).
    /// </summary>
    public PdfDictionary? Info { get; }

    /// <summary>
    /// Collection of pages in the document.
    /// Provides methods for adding, removing, and reordering pages.
    /// </summary>
    public PageCollection Pages
    {
        get
        {
            _pages ??= new PageCollection(this);
            return _pages;
        }
    }

    private PdfDocument(
        Stream stream,
        bool ownsStream,
        Dictionary<int, XRefEntry> xref,
        PdfDictionary trailer,
        string version)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _xref = xref;
        _objectCache = new Dictionary<int, PdfObject>();
        _parser = new PdfParser(new PdfLexer(stream, ownsStream: false));
        _decompressor = new StreamDecompressor();

        Trailer = trailer;
        Version = version;

        // Load catalog
        var catalogRef = trailer.Get<PdfReference>("Root");
        Catalog = GetObject(catalogRef) as PdfDictionary
            ?? throw new PdfParseException("Could not load document catalog");

        // Get page count from page tree
        var pagesRef = Catalog.GetReferenceOrNull("Pages");
        if (pagesRef != null)
        {
            var pages = GetObject(pagesRef) as PdfDictionary;
            PageCount = pages?.GetInt("Count", 0) ?? 0;
        }

        // Get info dictionary
        var infoRef = trailer.GetReferenceOrNull("Info");
        if (infoRef != null)
        {
            Info = GetObject(infoRef) as PdfDictionary;
        }
    }

    /// <summary>
    /// Open a PDF document from a file.
    /// </summary>
    public static PdfDocument Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Open(stream, ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Open a PDF document from a stream.
    /// </summary>
    public static PdfDocument Open(Stream stream, bool ownsStream = false)
    {
        // Read PDF version from header
        string version = ReadVersion(stream);

        // Find and parse xref
        var xrefParser = new XRefParser(stream);
        long startXRef = xrefParser.FindStartXRef();
        var (trailer, xref) = xrefParser.ParseXRef(startXRef);

        // Handle incremental updates (Prev pointer)
        var fullXRef = new Dictionary<int, XRefEntry>(xref);
        var currentTrailer = trailer;

        while (currentTrailer.GetReferenceOrNull("Prev") != null || currentTrailer.ContainsKey("Prev"))
        {
            var prevObj = currentTrailer.GetOptional("Prev");
            if (prevObj == null) break;

            long prevXRef = prevObj.GetLong();
            var (prevTrailer, prevXRefEntries) = xrefParser.ParseXRef(prevXRef);

            // Merge with previous xref (older entries don't override newer)
            foreach (var kvp in prevXRefEntries)
            {
                if (!fullXRef.ContainsKey(kvp.Key))
                    fullXRef[kvp.Key] = kvp.Value;
            }

            currentTrailer = prevTrailer;
        }

        // Create document (loads catalog internally)
        return new PdfDocument(stream, ownsStream, fullXRef, trailer, version);
    }

    /// <summary>
    /// Open a PDF document from a byte array.
    /// </summary>
    public static PdfDocument Open(byte[] data)
    {
        return Open(new MemoryStream(data, writable: false), ownsStream: true);
    }

    /// <summary>
    /// Read the PDF version from the header.
    /// </summary>
    private static string ReadVersion(Stream stream)
    {
        stream.Position = 0;
        var buffer = new byte[20];
        int read = stream.Read(buffer, 0, buffer.Length);

        var header = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
        if (!header.StartsWith("%PDF-"))
            throw new PdfParseException("Invalid PDF header");

        // Extract version (e.g., "1.4", "1.7", "2.0")
        int idx = 5;
        while (idx < header.Length && (char.IsDigit(header[idx]) || header[idx] == '.'))
            idx++;

        return header.Substring(5, idx - 5);
    }

    /// <summary>
    /// Get a page by number (1-based).
    /// </summary>
    public PdfPage GetPage(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), $"Page number must be between 1 and {PageCount}");

        var pagesRef = Catalog.GetReference("Pages");
        var pagesDict = GetObject(pagesRef) as PdfDictionary
            ?? throw new PdfParseException("Invalid page tree");

        var pageDict = FindPage(pagesDict, pageNumber - 1, 0);
        return new PdfPage(this, pageDict, pageNumber);
    }

    /// <summary>
    /// Get all pages.
    /// </summary>
    public IEnumerable<PdfPage> GetPages()
    {
        for (int i = 1; i <= PageCount; i++)
        {
            yield return GetPage(i);
        }
    }

    /// <summary>
    /// Find a page in the page tree by index.
    /// </summary>
    private PdfDictionary FindPage(PdfDictionary node, int targetIndex, int currentIndex)
    {
        var type = node.GetNameOrNull("Type");

        if (type == "Page")
        {
            if (currentIndex == targetIndex)
                return node;
            throw new PdfParseException($"Page index mismatch: expected {targetIndex}, at {currentIndex}");
        }

        // It's a Pages node
        var kids = node.GetArray("Kids");
        int index = currentIndex;

        foreach (var kidRef in kids)
        {
            if (kidRef is not PdfReference kr)
                throw new PdfParseException("Invalid page tree: kid is not a reference");

            var kid = GetObject(kr) as PdfDictionary
                ?? throw new PdfParseException("Invalid page tree: kid is not a dictionary");

            var kidType = kid.GetNameOrNull("Type");

            if (kidType == "Page")
            {
                if (index == targetIndex)
                    return kid;
                index++;
            }
            else
            {
                // Pages node
                int count = kid.GetInt("Count");
                if (targetIndex >= index && targetIndex < index + count)
                {
                    return FindPage(kid, targetIndex, index);
                }
                index += count;
            }
        }

        throw new PdfParseException($"Could not find page {targetIndex}");
    }

    /// <summary>
    /// Get an object by reference.
    /// </summary>
    public PdfObject GetObject(PdfReference reference)
    {
        return GetObject(reference.ObjectNum);
    }

    /// <summary>
    /// Get an object by object number.
    /// </summary>
    public PdfObject GetObject(int objectNumber)
    {
        // Check cache
        if (_objectCache.TryGetValue(objectNumber, out var cached))
            return cached;

        // Find in xref
        if (!_xref.TryGetValue(objectNumber, out var entry))
            throw new PdfParseException($"Object {objectNumber} not found in xref");

        if (!entry.InUse)
            return PdfNull.Instance;

        PdfObject obj;

        if (entry.IsCompressed)
        {
            // Object is in an object stream
            obj = GetObjectFromStream(entry.ObjectStreamNumber!.Value, entry.IndexInStream!.Value);
        }
        else
        {
            // Regular object
            _parser.Seek(entry.Offset);
            var indirectObj = _parser.ParseIndirectObject();
            obj = indirectObj.Value;

            // Decompress streams
            if (obj is PdfStream stream && stream.IsFiltered)
            {
                try
                {
                    _decompressor.Decompress(stream);
                }
                catch
                {
                    // Some streams can't be decompressed (images, etc.) - that's OK
                }
            }
        }

        _objectCache[objectNumber] = obj;
        return obj;
    }

    /// <summary>
    /// Get an object from an object stream.
    /// </summary>
    private PdfObject GetObjectFromStream(int streamNumber, int index)
    {
        // Get the object stream
        var streamObj = GetObject(streamNumber) as PdfStream
            ?? throw new PdfParseException($"Object stream {streamNumber} not found");

        // Ensure it's decoded
        if (!streamObj.IsDecoded)
        {
            _decompressor.Decompress(streamObj);
        }

        var data = streamObj.DecodedData;
        int n = streamObj.GetInt("N"); // Number of objects
        int first = streamObj.GetInt("First"); // Offset to first object

        // Parse the index (pairs of object number and byte offset)
        using var parser = new PdfParser(data);
        var offsets = new (int ObjNum, int Offset)[n];

        for (int i = 0; i < n; i++)
        {
            var objNumToken = parser.Lexer.NextToken();
            var offsetToken = parser.Lexer.NextToken();

            if (objNumToken.Type != PdfTokenType.Integer || offsetToken.Type != PdfTokenType.Integer)
                throw new PdfParseException("Invalid object stream index");

            offsets[i] = (
                int.Parse(objNumToken.Value),
                int.Parse(offsetToken.Value)
            );
        }

        // Find and parse the requested object
        if (index < 0 || index >= n)
            throw new PdfParseException($"Index {index} out of range in object stream {streamNumber}");

        int offset = first + offsets[index].Offset;
        parser.Seek(offset);
        return parser.ParseObject();
    }

    /// <summary>
    /// Resolve a reference to its actual object.
    /// If the object is a reference, follows it. Otherwise returns the object itself.
    /// </summary>
    public PdfObject Resolve(PdfObject obj)
    {
        while (obj is PdfReference reference)
        {
            obj = GetObject(reference);
        }
        return obj;
    }

    /// <summary>
    /// Get document metadata title.
    /// </summary>
    public string? Title => Info?.GetStringOrNull("Title");

    /// <summary>
    /// Get document metadata author.
    /// </summary>
    public string? Author => Info?.GetStringOrNull("Author");

    /// <summary>
    /// Get document metadata subject.
    /// </summary>
    public string? Subject => Info?.GetStringOrNull("Subject");

    /// <summary>
    /// Get document metadata keywords.
    /// </summary>
    public string? Keywords => Info?.GetStringOrNull("Keywords");

    /// <summary>
    /// Get document metadata creator.
    /// </summary>
    public string? Creator => Info?.GetStringOrNull("Creator");

    /// <summary>
    /// Get document metadata producer.
    /// </summary>
    public string? Producer => Info?.GetStringOrNull("Producer");

    #region Save Methods

    /// <summary>
    /// Save the document to a stream.
    /// </summary>
    public void Save(Stream outputStream)
    {
        var writer = new PdfDocumentWriter(this);
        writer.Write(outputStream);
    }

    /// <summary>
    /// Save the document to a byte array.
    /// </summary>
    public byte[] SaveToBytes()
    {
        using var ms = new MemoryStream();
        Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Save the document to a file.
    /// </summary>
    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(fs);
    }

    /// <summary>
    /// Get all objects in the document (for writing).
    /// </summary>
    internal IEnumerable<(int ObjectNumber, int Generation, PdfObject Object)> GetAllObjects()
    {
        foreach (var kvp in _xref)
        {
            if (kvp.Value.InUse)
            {
                var obj = GetObject(kvp.Key);
                yield return (kvp.Key, kvp.Value.Generation, obj);
            }
        }
    }

    /// <summary>
    /// Get the catalog reference for writing.
    /// </summary>
    internal PdfReference GetCatalogReference()
    {
        return Trailer.Get<PdfReference>("Root");
    }

    #endregion

    /// <inheritdoc />
    public void Dispose()
    {
        _parser.Dispose();
        if (_ownsStream)
            _stream.Dispose();
    }
}
