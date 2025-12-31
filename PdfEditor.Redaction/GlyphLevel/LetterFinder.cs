using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Finds which PdfPig letters correspond to a text operation using SPATIAL matching.
/// This is the foundation for glyph-level redaction.
/// </summary>
/// <remarks>
/// KEY INSIGHT: Use spatial overlap, NOT text matching!
/// Text matching fails due to encoding differences between PDF and extracted text.
/// PdfPig's letter positions are 100% accurate - trust them!
/// </remarks>
public class LetterFinder
{
    private readonly ILogger<LetterFinder> _logger;

    public LetterFinder() : this(NullLogger<LetterFinder>.Instance)
    {
    }

    public LetterFinder(ILogger<LetterFinder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find PdfPig letters that spatially correspond to this text operation.
    /// </summary>
    /// <param name="textOperation">The text operation from content stream parsing.</param>
    /// <param name="allLetters">All PdfPig letters from the page.</param>
    /// <returns>List of letter matches in reading order (left to right).</returns>
    public List<LetterMatch> FindOperationLetters(
        TextOperation textOperation,
        IReadOnlyList<Letter> allLetters)
    {
        var matches = new List<LetterMatch>();
        var operationText = textOperation.Text;

        if (string.IsNullOrEmpty(operationText))
        {
            _logger.LogDebug("Operation has empty text, cannot match letters");
            return matches;
        }

        // Strategy: TEXT CONTENT MATCHING (rotation-independent)
        // Issue #151: Parsed operation positions are in unrotated content stream coordinates,
        // but PdfPig letters are in visual/rotated coordinates. For rotated pages (90°, 180°, 270°),
        // these coordinate systems don't match, so Y-band filtering fails.
        //
        // Solution: Match by text content first (which is rotation-independent), then use
        // spatial position only as a tiebreaker when multiple matches exist.
        //
        // IMPORTANT: Use PdfPig's original letter order - it preserves reading order regardless
        // of page rotation. DO NOT re-sort letters as this breaks text matching for rotated pages.

        // Use PdfPig's original letter order (preserves reading order for all rotations)
        var orderedLetters = allLetters.ToList();
        var fullPageText = string.Join("", orderedLetters.Select(l => l.Value));

        _logger.LogDebug("Searching for '{OpText}' in page text ({TotalLetters} letters)",
            operationText.Length > 30 ? operationText.Substring(0, 30) + "..." : operationText,
            orderedLetters.Count);

        // Find all occurrences of operation text in the full page text
        var foundIndices = FindAllTextOccurrences(fullPageText, operationText);

        // ISSUE #172 FIX: If exact match fails, try matching just the meaningful part (before repeated underscores/dashes)
        // This handles form fields like "FULL NAME AT BIRTH: _______________" where the parser and PdfPig
        // report different numbers of underscores due to TJ array kerning differences.
        if (foundIndices.Count == 0 && (operationText.Contains("_") || operationText.Contains("-")))
        {
            // Extract the meaningful part before repeated fill characters
            var meaningfulPart = ExtractMeaningfulText(operationText);
            if (!string.IsNullOrEmpty(meaningfulPart) && meaningfulPart.Length >= 3)
            {
                _logger.LogDebug("Exact match failed, trying meaningful part: '{Part}'", meaningfulPart);
                foundIndices = FindAllTextOccurrences(fullPageText, meaningfulPart);

                if (foundIndices.Count > 0)
                {
                    _logger.LogDebug("Found meaningful part at {Count} location(s)", foundIndices.Count);
                    // Adjust match to only cover the meaningful part, not the underscores
                    operationText = meaningfulPart;
                }
            }
        }

        if (foundIndices.Count == 0)
        {
            _logger.LogDebug("Text '{OpText}' not found in page letters",
                operationText.Length > 30 ? operationText.Substring(0, 30) + "..." : operationText);
            return matches;
        }

        // If only one match, use it
        int matchIndex;
        if (foundIndices.Count == 1)
        {
            matchIndex = foundIndices[0];
        }
        else
        {
            // Multiple matches - need to disambiguate
            // For rotated pages, we can't use parsed coordinates directly.
            // Use letter cluster detection: find the match whose letters form a spatially coherent group.
            matchIndex = FindBestMatchByCoherence(foundIndices, orderedLetters, operationText.Length);
            _logger.LogDebug("Found {Count} matches, chose index {Index} by spatial coherence",
                foundIndices.Count, matchIndex);
        }

        // Build matches from the found position
        int count = Math.Min(operationText.Length, orderedLetters.Count - matchIndex);
        for (int i = 0; i < count; i++)
        {
            var match = new LetterMatch
            {
                CharacterIndex = i,
                Letter = orderedLetters[matchIndex + i],
                OperationText = operationText
            };

            // Populate CJK fields from corresponding GlyphPosition (Issue #174)
            if (i < textOperation.Glyphs.Count)
            {
                var glyph = textOperation.Glyphs[i];
                match.RawBytes = glyph.RawBytes;
                match.CidValue = glyph.CidValue;
                match.IsCidGlyph = glyph.IsCidGlyph;
                match.WasHexString = glyph.WasHexString;
                match.GlyphPosition = glyph;
            }

            matches.Add(match);
        }
        _logger.LogDebug("Text match found at index {Index}: {Count} letters matched", matchIndex, matches.Count);

        return matches;
    }

    /// <summary>
    /// Find all occurrences of text in candidates.
    /// </summary>
    private List<int> FindAllTextOccurrences(string candidateText, string searchText)
    {
        var indices = new List<int>();
        int searchStart = 0;

        while (true)
        {
            int idx = candidateText.IndexOf(searchText, searchStart, StringComparison.Ordinal);
            if (idx < 0)
            {
                // Try case-insensitive as fallback
                idx = candidateText.IndexOf(searchText, searchStart, StringComparison.OrdinalIgnoreCase);
            }
            if (idx < 0) break;
            indices.Add(idx);
            searchStart = idx + 1;
        }

        return indices;
    }

    /// <summary>
    /// Find the best match by spatial coherence - letters that form a tight cluster.
    /// For rotated pages, coordinates are already in visual space from PdfPig.
    /// </summary>
    private int FindBestMatchByCoherence(List<int> indices, List<Letter> letters, int textLength)
    {
        if (indices.Count == 0) return -1;
        if (indices.Count == 1) return indices[0];

        int bestIndex = indices[0];
        double bestScore = double.MaxValue;

        foreach (var idx in indices)
        {
            if (idx + textLength > letters.Count) continue;

            // Calculate spatial spread of this match's letters
            var matchLetters = letters.Skip(idx).Take(textLength).ToList();
            if (matchLetters.Count == 0) continue;

            // Compute bounding box of the match
            // Normalize coordinates - PdfPig can return swapped Left/Right or Bottom/Top for rotated text
            double minX = matchLetters.Min(l => Math.Min(l.GlyphRectangle.Left, l.GlyphRectangle.Right));
            double maxX = matchLetters.Max(l => Math.Max(l.GlyphRectangle.Left, l.GlyphRectangle.Right));
            double minY = matchLetters.Min(l => Math.Min(l.GlyphRectangle.Bottom, l.GlyphRectangle.Top));
            double maxY = matchLetters.Max(l => Math.Max(l.GlyphRectangle.Bottom, l.GlyphRectangle.Top));

            // Score by total spread (smaller is better - more coherent cluster)
            double spread = (maxX - minX) + (maxY - minY);

            // Prefer matches with smaller Y spread (letters on same line)
            double ySpread = maxY - minY;
            double score = spread + ySpread * 10; // Weight Y spread more heavily

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = idx;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Extract the meaningful text portion from a form field operation.
    /// Handles cases like "FULL NAME AT BIRTH: _______________" by returning "FULL NAME AT BIRTH: "
    /// This fixes Issue #172 where TJ array kerning causes underscore count mismatches.
    /// </summary>
    private static string ExtractMeaningfulText(string operationText)
    {
        if (string.IsNullOrEmpty(operationText))
            return operationText;

        // Find the first occurrence of repeated fill characters (3+ underscores or dashes)
        int fillStart = -1;
        for (int i = 0; i < operationText.Length - 2; i++)
        {
            char c = operationText[i];
            if ((c == '_' || c == '-') &&
                i + 2 < operationText.Length &&
                operationText[i + 1] == c &&
                operationText[i + 2] == c)
            {
                fillStart = i;
                break;
            }
        }

        if (fillStart <= 0)
            return operationText;  // No fill pattern found, or it's at the start

        // Return text up to the fill pattern (including any trailing space/colon)
        return operationText.Substring(0, fillStart).TrimEnd();
    }

}

/// <summary>
/// Represents a match between a PdfPig letter and a character in a text operation.
/// Enhanced for CJK support with raw byte preservation.
/// </summary>
public class LetterMatch
{
    /// <summary>
    /// Index of this character in the text operation string.
    /// </summary>
    public required int CharacterIndex { get; init; }

    /// <summary>
    /// The PdfPig letter with accurate position information.
    /// </summary>
    public required Letter Letter { get; init; }

    /// <summary>
    /// The full text of the operation (for debugging).
    /// </summary>
    public required string OperationText { get; init; }

    /// <summary>
    /// Whether this letter falls within a redaction area (set by TextSegmenter).
    /// </summary>
    public bool InRedactionArea { get; set; }

    #region CJK Support (Issue #174)

    /// <summary>
    /// Raw bytes as they appear in the PDF content stream.
    /// For Western fonts: 1 byte. For CID fonts: 2 bytes (big-endian).
    /// Set when correlating with GlyphPosition from TextOperation.
    /// </summary>
    public byte[]? RawBytes { get; set; }

    /// <summary>
    /// Character ID (CID) for CID-keyed fonts.
    /// </summary>
    public int CidValue { get; set; }

    /// <summary>
    /// Whether this glyph came from a CID-keyed font.
    /// </summary>
    public bool IsCidGlyph { get; set; }

    /// <summary>
    /// Whether the original operand was a hex string.
    /// </summary>
    public bool WasHexString { get; set; }

    /// <summary>
    /// Reference to the corresponding GlyphPosition from the TextOperation.
    /// Provides access to all glyph details.
    /// </summary>
    public GlyphPosition? GlyphPosition { get; set; }

    #endregion
}
