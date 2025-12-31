namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// Represents a single glyph with its Unicode value and encoding information.
/// Used to preserve encoding fidelity during redaction - we store both the
/// decoded Unicode (for text matching) and raw bytes (for reconstruction).
/// </summary>
public class GlyphInfo
{
    /// <summary>
    /// The Unicode character(s) this glyph represents.
    /// Used for text matching during redaction.
    /// May be multiple characters for ligatures (e.g., "fi", "fl").
    /// </summary>
    public string UnicodeValue { get; init; } = string.Empty;

    /// <summary>
    /// The raw bytes as they appear in the PDF content stream.
    /// For Western fonts: typically 1 byte.
    /// For CID fonts: typically 2 bytes (big-endian CID).
    /// </summary>
    public byte[] RawBytes { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// The Character ID (CID) for CID-keyed fonts.
    /// For Western fonts, this is typically the single byte value.
    /// For CID fonts, this is the 2-byte big-endian value.
    /// </summary>
    public int CidValue { get; init; }

    /// <summary>
    /// Reference to the font this glyph belongs to.
    /// Used to determine encoding during reconstruction.
    /// </summary>
    public FontInfo? Font { get; init; }

    /// <summary>
    /// Whether this glyph came from a CID-keyed font.
    /// </summary>
    public bool IsCidGlyph => Font?.IsCidFont ?? false;

    /// <summary>
    /// The width of this glyph in text space units (if known).
    /// Used for positioning calculations.
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// The position of this glyph in the original string (0-based index).
    /// Used for mapping back to source positions after parsing.
    /// </summary>
    public int SourceIndex { get; init; }

    /// <summary>
    /// Whether this glyph should be kept or removed during redaction.
    /// Set by the redaction pipeline based on text matching.
    /// </summary>
    public bool ShouldRemove { get; set; }

    /// <summary>
    /// Create a GlyphInfo for a Western (single-byte) font glyph.
    /// </summary>
    public static GlyphInfo FromSingleByte(byte value, char unicode, FontInfo? font, int sourceIndex = 0)
    {
        return new GlyphInfo
        {
            UnicodeValue = unicode.ToString(),
            RawBytes = new[] { value },
            CidValue = value,
            Font = font,
            SourceIndex = sourceIndex
        };
    }

    /// <summary>
    /// Create a GlyphInfo for a CID font glyph (2-byte encoding).
    /// </summary>
    public static GlyphInfo FromCid(int cid, string unicode, FontInfo? font, int sourceIndex = 0)
    {
        return new GlyphInfo
        {
            UnicodeValue = unicode,
            RawBytes = new[] { (byte)(cid >> 8), (byte)(cid & 0xFF) },
            CidValue = cid,
            Font = font,
            SourceIndex = sourceIndex
        };
    }

    /// <summary>
    /// Create a GlyphInfo from raw bytes with Unicode mapping.
    /// </summary>
    public static GlyphInfo FromBytes(byte[] bytes, string unicode, FontInfo? font, int sourceIndex = 0)
    {
        int cid = bytes.Length switch
        {
            1 => bytes[0],
            2 => (bytes[0] << 8) | bytes[1],
            _ => 0
        };

        return new GlyphInfo
        {
            UnicodeValue = unicode,
            RawBytes = bytes,
            CidValue = cid,
            Font = font,
            SourceIndex = sourceIndex
        };
    }

    /// <summary>
    /// Get the hex representation of the raw bytes.
    /// Used for reconstructing hex strings in content streams.
    /// </summary>
    public string ToHexString()
    {
        return BitConverter.ToString(RawBytes).Replace("-", "");
    }

    public override string ToString()
    {
        var cidStr = IsCidGlyph ? $"CID={CidValue:X4}" : $"Byte={CidValue:X2}";
        return $"GlyphInfo('{UnicodeValue}', {cidStr}, Remove={ShouldRemove})";
    }
}

/// <summary>
/// Represents a sequence of glyphs from a single text operation.
/// Provides methods for segmenting based on redaction targets.
/// </summary>
public class GlyphSequence
{
    private readonly List<GlyphInfo> _glyphs = new();

    /// <summary>
    /// The glyphs in this sequence.
    /// </summary>
    public IReadOnlyList<GlyphInfo> Glyphs => _glyphs;

    /// <summary>
    /// The font used for this sequence.
    /// </summary>
    public FontInfo? Font { get; init; }

    /// <summary>
    /// Whether this sequence is from a CID font.
    /// </summary>
    public bool IsCidSequence => Font?.IsCidFont ?? false;

    /// <summary>
    /// Add a glyph to the sequence.
    /// </summary>
    public void Add(GlyphInfo glyph)
    {
        _glyphs.Add(glyph);
    }

    /// <summary>
    /// Add multiple glyphs to the sequence.
    /// </summary>
    public void AddRange(IEnumerable<GlyphInfo> glyphs)
    {
        _glyphs.AddRange(glyphs);
    }

    /// <summary>
    /// Get the Unicode string represented by all glyphs.
    /// </summary>
    public string ToUnicodeString()
    {
        return string.Concat(_glyphs.Select(g => g.UnicodeValue));
    }

    /// <summary>
    /// Get the raw bytes for all glyphs concatenated.
    /// </summary>
    public byte[] ToRawBytes()
    {
        return _glyphs.SelectMany(g => g.RawBytes).ToArray();
    }

    /// <summary>
    /// Get the hex string representation for reconstruction.
    /// </summary>
    public string ToHexString()
    {
        return string.Concat(_glyphs.Select(g => g.ToHexString()));
    }

    /// <summary>
    /// Mark glyphs for removal based on a predicate.
    /// </summary>
    public void MarkForRemoval(Func<GlyphInfo, bool> predicate)
    {
        foreach (var glyph in _glyphs)
        {
            if (predicate(glyph))
            {
                glyph.ShouldRemove = true;
            }
        }
    }

    /// <summary>
    /// Get glyphs that should be kept (not removed).
    /// </summary>
    public IEnumerable<GlyphInfo> GetKeptGlyphs()
    {
        return _glyphs.Where(g => !g.ShouldRemove);
    }

    /// <summary>
    /// Get glyphs that should be removed.
    /// </summary>
    public IEnumerable<GlyphInfo> GetRemovedGlyphs()
    {
        return _glyphs.Where(g => g.ShouldRemove);
    }

    /// <summary>
    /// Split the sequence into segments based on the ShouldRemove flag.
    /// Returns alternating keep/remove segments.
    /// </summary>
    public IEnumerable<GlyphSegment> SplitIntoSegments()
    {
        if (_glyphs.Count == 0)
            yield break;

        var currentSegment = new List<GlyphInfo>();
        bool currentIsRemove = _glyphs[0].ShouldRemove;

        foreach (var glyph in _glyphs)
        {
            if (glyph.ShouldRemove != currentIsRemove)
            {
                // Segment boundary - yield current and start new
                if (currentSegment.Count > 0)
                {
                    yield return new GlyphSegment(currentSegment.ToList(), currentIsRemove, Font);
                }
                currentSegment.Clear();
                currentIsRemove = glyph.ShouldRemove;
            }
            currentSegment.Add(glyph);
        }

        // Yield final segment
        if (currentSegment.Count > 0)
        {
            yield return new GlyphSegment(currentSegment.ToList(), currentIsRemove, Font);
        }
    }

    /// <summary>
    /// Whether any glyphs are marked for removal.
    /// </summary>
    public bool HasRemovals => _glyphs.Any(g => g.ShouldRemove);

    /// <summary>
    /// Whether all glyphs are marked for removal.
    /// </summary>
    public bool AllRemoved => _glyphs.All(g => g.ShouldRemove);

    /// <summary>
    /// The number of glyphs in the sequence.
    /// </summary>
    public int Count => _glyphs.Count;
}

/// <summary>
/// A contiguous segment of glyphs that are either all kept or all removed.
/// </summary>
public class GlyphSegment
{
    /// <summary>
    /// The glyphs in this segment.
    /// </summary>
    public IReadOnlyList<GlyphInfo> Glyphs { get; }

    /// <summary>
    /// Whether this segment should be removed.
    /// </summary>
    public bool IsRemoved { get; }

    /// <summary>
    /// The font for this segment.
    /// </summary>
    public FontInfo? Font { get; }

    public GlyphSegment(IReadOnlyList<GlyphInfo> glyphs, bool isRemoved, FontInfo? font)
    {
        Glyphs = glyphs;
        IsRemoved = isRemoved;
        Font = font;
    }

    /// <summary>
    /// Get the Unicode string for this segment.
    /// </summary>
    public string ToUnicodeString()
    {
        return string.Concat(Glyphs.Select(g => g.UnicodeValue));
    }

    /// <summary>
    /// Get the raw bytes for this segment.
    /// </summary>
    public byte[] ToRawBytes()
    {
        return Glyphs.SelectMany(g => g.RawBytes).ToArray();
    }

    /// <summary>
    /// Get the hex string for reconstruction.
    /// </summary>
    public string ToHexString()
    {
        return string.Concat(Glyphs.Select(g => g.ToHexString()));
    }

    /// <summary>
    /// The number of glyphs in this segment.
    /// </summary>
    public int Count => Glyphs.Count;
}
