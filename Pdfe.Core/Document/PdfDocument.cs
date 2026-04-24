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
    public int PageCount => Pages.Count;

    /// <summary>
    /// PDF version (e.g., "1.4", "1.7", "2.0").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Register <paramref name="obj"/> as a new indirect object in this
    /// document. Allocates the next free object number, wires it into
    /// the xref and object cache, and returns a reference callers can
    /// drop into other dictionaries or arrays.
    /// </summary>
    /// <remarks>
    /// Used by mutation paths (AddBlank, SetContentStreamBytes, …) that
    /// need to produce objects the writer can serialize at the top level
    /// with a real <c>N 0 obj … endobj</c> frame — critical for stream
    /// objects which are not valid inline in PDF syntax.
    /// </remarks>
    internal PdfReference AddIndirectObject(PdfObject obj)
    {
        int next = _xref.Count == 0 ? 1 : _xref.Keys.Max() + 1;
        _xref[next] = new Pdfe.Core.Parsing.XRefEntry
        {
            Offset = 0, // filled in by the writer at serialize time
            Generation = 0,
            InUse = true,
        };
        _objectCache[next] = obj;
        return new PdfReference(next, 0);
    }

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
    /// Create a new empty in-memory PDF document. The returned document
    /// has a <c>/Catalog</c>, an empty <c>/Pages</c> tree, and no pages.
    /// Use <see cref="Pages"/>.<see cref="PageCollection.AddBlank"/> to
    /// append pages.
    /// </summary>
    /// <remarks>
    /// Implementation goes through a <c>Open(bytes)</c> round-trip so the
    /// new document is fully initialized with parser / xref / object
    /// cache in the same shape as a document loaded from disk — mutation
    /// paths then work identically on freshly-created and loaded docs.
    /// </remarks>
    public static PdfDocument CreateNew(string version = "1.7")
    {
        return Open(BuildMinimalEmptyPdfBytes(version));
    }

    /// <summary>
    /// Raw-bytes writer that produces a minimal valid empty PDF: header,
    /// catalog object, empty pages object, xref, trailer. Just enough
    /// for the parser to accept and for AddBlank to latch onto.
    /// </summary>
    private static byte[] BuildMinimalEmptyPdfBytes(string version)
    {
        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, new System.Text.UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };

        w.WriteLine($"%PDF-{version}");
        w.Flush();

        var offsets = new long[3];

        offsets[1] = ms.Position;
        w.WriteLine("1 0 obj");
        w.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        w.WriteLine("endobj");
        w.Flush();

        offsets[2] = ms.Position;
        w.WriteLine("2 0 obj");
        w.WriteLine("<< /Type /Pages /Kids [] /Count 0 >>");
        w.WriteLine("endobj");
        w.Flush();

        long xrefPos = ms.Position;
        w.WriteLine("xref");
        w.WriteLine("0 3");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 2; i++)
            w.WriteLine($"{offsets[i]:D10} 00000 n ");
        w.Flush();
        w.WriteLine("trailer");
        w.WriteLine("<< /Root 1 0 R /Size 3 >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefPos.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
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

        // Delegate to the PageCollection, which already handles both
        // indirect-reference and inline-dictionary kids uniformly.
        return Pages[pageNumber - 1];
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
