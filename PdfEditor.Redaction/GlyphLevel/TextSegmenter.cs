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

            if (match != null)
            {
                // We have letter position info - check if in redaction area
                keep = !IsLetterInRedactionArea(match.Letter.GlyphRectangle, redactionArea);

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

            // Start new segment if keep status changed
            if (currentSegment == null || currentSegment.Keep != keep)
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
        // Normalize coordinates for rotated text (PdfPig can return Left > Right for 90° rotation)
        double left = Math.Min(glyphRect.Left, glyphRect.Right);
        double right = Math.Max(glyphRect.Left, glyphRect.Right);
        double bottom = Math.Min(glyphRect.Bottom, glyphRect.Top);
        double top = Math.Max(glyphRect.Bottom, glyphRect.Top);

        // Use letter center point for determination
        double centerX = (left + right) / 2.0;
        double centerY = (bottom + top) / 2.0;

        return redactionArea.Contains(centerX, centerY);
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
