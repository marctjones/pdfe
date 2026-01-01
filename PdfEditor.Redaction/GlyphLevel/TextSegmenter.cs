using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Splits text operations into keep/remove segments based on letter positions and redaction area.
/// </summary>
public class TextSegmenter
{
    private readonly ILogger<TextSegmenter> _logger;

    public TextSegmenter() : this(NullLogger<TextSegmenter>.Instance)
    {
    }

    public TextSegmenter(ILogger<TextSegmenter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build segments from letter matches, determining which to keep and which to remove.
    /// </summary>
    /// <param name="textOperation">The original text operation.</param>
    /// <param name="letterMatches">Letters matched to this operation.</param>
    /// <param name="redactionArea">Area to redact.</param>
    /// <returns>List of segments to KEEP (segments to remove are excluded).</returns>
    public List<TextSegment> BuildSegments(
        TextOperation textOperation,
        List<LetterMatch> letterMatches,
        PdfRectangle redactionArea)
    {
        var allSegments = new List<TextSegment>();
        TextSegment? currentSegment = null;

        // If no letter matches, fall back to checking whole operation bounding box
        if (letterMatches.Count == 0)
        {
            _logger.LogDebug("No letter matches for '{Text}', using whole-operation check",
                textOperation.Text.Length > 50
                    ? textOperation.Text.Substring(0, 50) + "..."
                    : textOperation.Text);

            bool wholeOperationInRedactionArea = textOperation.BoundingBox.IntersectsWith(redactionArea);

            if (!wholeOperationInRedactionArea)
            {
                // Keep entire operation
                return new List<TextSegment>
                {
                    new TextSegment
                    {
                        StartIndex = 0,
                        EndIndex = textOperation.Text.Length,
                        Keep = true,
                        StartX = textOperation.BoundingBox.Left,
                        StartY = textOperation.BoundingBox.Bottom,
                        Width = textOperation.BoundingBox.Width,
                        Height = textOperation.BoundingBox.Height,
                        OriginalText = textOperation.Text
                    }
                };
            }
            else
            {
                // Remove entire operation
                return new List<TextSegment>();
            }
        }

        // Process each character, building segments
        for (int i = 0; i < textOperation.Text.Length; i++)
        {
            // Find matching letter for this character index
            var match = letterMatches.FirstOrDefault(m => m.CharacterIndex == i);

            bool keep;
            double glyphX, glyphY, glyphWidth, glyphHeight;

            GlyphOverlapType overlapType = GlyphOverlapType.None;

            if (match != null)
            {
                // We have letter position info - get detailed overlap info
                var (shouldRemove, overlap) = GetLetterOverlapInfo(match.Letter.GlyphRectangle, redactionArea);
                keep = !shouldRemove;
                overlapType = overlap;

                // Normalize coordinates - PdfPig can return swapped Left/Right or Bottom/Top for rotated text
                var rect = match.Letter.GlyphRectangle;
                glyphX = Math.Min(rect.Left, rect.Right);
                glyphY = Math.Min(rect.Bottom, rect.Top);
                glyphWidth = Math.Abs(rect.Right - rect.Left);
                glyphHeight = Math.Abs(rect.Top - rect.Bottom);
            }
            else
            {
                // No letter match for this index - use FIXED width estimate
                // This happens for unmapped characters (trailing spaces, etc.)
                // CRITICAL: Don't use operation bbox width - for reconstructed operations,
                // this is a placeholder that causes massive width accumulation!
                keep = true;  // Conservative: keep if unknown
                glyphX = textOperation.BoundingBox.Left;
                glyphY = textOperation.BoundingBox.Bottom;
                glyphWidth = 5.0;   // Fixed approximate space width
                glyphHeight = 12.0; // Fixed approximate text height

                _logger.LogDebug("No letter match for character index {Index} in '{Text}', using fixed width {Width}",
                    i, textOperation.Text, glyphWidth);
            }

            // Start new segment if keep status or overlap type changed
            // (we want separate segments for partial vs full overlap)
            if (currentSegment == null || currentSegment.Keep != keep ||
                (!keep && currentSegment.OverlapType != overlapType))
            {
                if (currentSegment != null)
                {
                    allSegments.Add(currentSegment);
                }

                currentSegment = new TextSegment
                {
                    StartIndex = i,
                    EndIndex = i + 1,
                    Keep = keep,
                    OverlapType = keep ? GlyphOverlapType.None : overlapType,
                    StartX = glyphX,
                    StartY = glyphY,
                    Width = glyphWidth,
                    Height = glyphHeight,
                    OriginalText = textOperation.Text,
                    // CJK support (Issue #174)
                    IsCidFont = textOperation.IsCidFont,
                    WasHexString = textOperation.WasHexString
                };

                // Add letter match to new segment
                if (match != null)
                {
                    currentSegment.LetterMatches.Add(match);
                }
            }
            else
            {
                // Extend current segment
                currentSegment.EndIndex = i + 1;
                currentSegment.Width += glyphWidth;

                // Add letter match to current segment
                if (match != null)
                {
                    currentSegment.LetterMatches.Add(match);
                }
            }
        }

        // Add final segment
        if (currentSegment != null)
        {
            allSegments.Add(currentSegment);
        }

        // Return only segments to keep
        var segmentsToKeep = allSegments.Where(s => s.Keep).ToList();

        _logger.LogDebug("Segmented '{Text}' into {Total} segments, keeping {Keep}",
            textOperation.Text.Length > 50
                ? textOperation.Text.Substring(0, 50) + "..."
                : textOperation.Text,
            allSegments.Count,
            segmentsToKeep.Count);

        return segmentsToKeep;
    }

    /// <summary>
    /// Check if a letter's center point is within the redaction area.
    /// Handles rotated text where PdfPig may return swapped Left/Right or Bottom/Top.
    /// </summary>
    private bool IsLetterInRedactionArea(UglyToad.PdfPig.Core.PdfRectangle glyphRect, PdfRectangle redactionArea)
    {
        var (shouldRemove, _) = GetLetterOverlapInfo(glyphRect, redactionArea);
        return shouldRemove;
    }

    /// <summary>
    /// Get detailed overlap information for a letter.
    /// Returns whether to remove the letter and what type of overlap exists.
    /// </summary>
    /// <param name="glyphRect">The glyph's bounding box from PdfPig.</param>
    /// <param name="redactionArea">The redaction area.</param>
    /// <returns>Tuple of (shouldRemove, overlapType).</returns>
    private (bool ShouldRemove, GlyphOverlapType OverlapType) GetLetterOverlapInfo(
        UglyToad.PdfPig.Core.PdfRectangle glyphRect,
        PdfRectangle redactionArea)
    {
        // Normalize coordinates for rotated text (PdfPig can return Left > Right for 90° rotation)
        var normalizedGlyph = PdfRectangle.FromPdfPig(glyphRect);

        // Get overlap type
        var overlapType = redactionArea.GetOverlapType(normalizedGlyph);

        // Current behavior: Use center point for determination
        // This maintains backward compatibility
        double centerX = (normalizedGlyph.Left + normalizedGlyph.Right) / 2.0;
        double centerY = (normalizedGlyph.Bottom + normalizedGlyph.Top) / 2.0;
        bool centerInArea = redactionArea.Contains(centerX, centerY);

        // If center is in area, should remove (Full or Partial)
        // If center is NOT in area but there's intersection, it's Partial but we currently keep it
        // Future: Configuration option to control this behavior
        return (centerInArea, overlapType);
    }
}

/// <summary>
/// Represents a segment of text that should be kept or removed.
/// Enhanced for CJK support with raw byte preservation.
/// </summary>
public class TextSegment
{
    /// <summary>
    /// Start index in the original text (inclusive).
    /// </summary>
    public required int StartIndex { get; init; }

    /// <summary>
    /// End index in the original text (exclusive).
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    /// Whether to keep this segment (false means remove).
    /// </summary>
    public required bool Keep { get; init; }

    /// <summary>
    /// Type of overlap with redaction area.
    /// For Keep=true segments, this is GlyphOverlapType.None.
    /// For Keep=false segments, this is Full or Partial depending on coverage.
    /// </summary>
    public GlyphOverlapType OverlapType { get; set; } = GlyphOverlapType.None;

    /// <summary>
    /// Whether this segment is a candidate for rasterization (partial overlap).
    /// When true, the text should be removed but the visible portion preserved as image.
    /// </summary>
    public bool IsPartialOverlap => OverlapType == GlyphOverlapType.Partial;

    /// <summary>
    /// X position of first character in this segment (PDF coordinates).
    /// </summary>
    public required double StartX { get; init; }

    /// <summary>
    /// Y position (baseline) of this segment (PDF coordinates).
    /// </summary>
    public required double StartY { get; init; }

    /// <summary>
    /// Total width of this segment in points.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Height of this segment in points.
    /// </summary>
    public required double Height { get; set; }

    /// <summary>
    /// The full original text (for extracting substring).
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// The text of this segment.
    /// </summary>
    public string Text => OriginalText.Substring(StartIndex, EndIndex - StartIndex);

    /// <summary>
    /// Get the bounding box for this segment in PDF coordinates.
    /// Useful for partial overlap rasterization.
    /// </summary>
    public PdfRectangle BoundingBox => new PdfRectangle(
        StartX,
        StartY,
        StartX + Width,
        StartY + Height
    );

    #region CJK Support (Issue #174)

    /// <summary>
    /// The letter matches for this segment's characters.
    /// Used to access raw bytes and glyph positions for reconstruction.
    /// </summary>
    public List<LetterMatch> LetterMatches { get; set; } = new();

    /// <summary>
    /// Whether this segment is from a CID-keyed font.
    /// </summary>
    public bool IsCidFont { get; set; }

    /// <summary>
    /// Whether the font has a ToUnicode CMap.
    /// When true, raw bytes must be used for reconstruction to preserve encoding.
    /// </summary>
    public bool HasToUnicode { get; set; }

    /// <summary>
    /// Whether the original operand was a hex string.
    /// </summary>
    public bool WasHexString { get; set; }

    /// <summary>
    /// Get the raw bytes for this segment (for CJK reconstruction).
    /// </summary>
    public byte[] GetRawBytes()
    {
        if (LetterMatches.Count == 0)
            return Array.Empty<byte>();

        return LetterMatches
            .Where(m => m.RawBytes != null)
            .SelectMany(m => m.RawBytes!)
            .ToArray();
    }

    #endregion
}
