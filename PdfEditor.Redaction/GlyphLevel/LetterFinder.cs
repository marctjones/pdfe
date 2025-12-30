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

        // Get the operation bounding box and glyphs
        var opBox = textOperation.BoundingBox;

        if (opBox.Width <= 0 || opBox.Height <= 0)
        {
            _logger.LogDebug("Operation has invalid bounding box, cannot match letters");
            return matches;
        }

        // Strategy: Use Y-range filtering + TEXT MATCHING to find the sequence
        // Issue #90: Parsed glyph positions can differ from PdfPig by 3-6 points,
        // so we can't rely solely on X proximity. Instead, search for the text within the Y band.

        // Get starting position from glyphs if available, otherwise use bbox
        double startX = opBox.Left;
        double startY = opBox.Bottom;
        if (textOperation.Glyphs.Count > 0)
        {
            startX = textOperation.Glyphs[0].BoundingBox.Left;
            startY = textOperation.Glyphs[0].BoundingBox.Bottom;
        }

        // Find letters in the same Y-range (with tolerance for height differences)
        // Issue #125: Adaptive Y-tolerance based on font size
        // Issue #90: Parsed glyph positions can differ from PdfPig by 3-6 points
        // Small fonts: use minimum tolerance (5.0 points)
        // Large fonts: scale with font size (40% of font size)
        double fontSize = textOperation.FontSize > 0 ? textOperation.FontSize : 12.0;
        double yTolerance = Math.Max(5.0, fontSize * 0.4);

        var candidateLetters = allLetters
            .Where(l => {
                bool yNearby = Math.Abs(l.GlyphRectangle.Bottom - startY) <= yTolerance;
                return yNearby;
            })
            .OrderBy(l => l.GlyphRectangle.Left)
            .ToList();

        if (candidateLetters.Count == 0)
        {
            _logger.LogDebug("No candidate letters in Y band for operation '{Text}'",
                textOperation.Text.Length > 30 ? textOperation.Text.Substring(0, 30) + "..." : textOperation.Text);
            return matches;
        }

        // Build full text from candidate letters
        var candidateText = string.Join("", candidateLetters.Select(l => l.Value));
        var operationText = textOperation.Text;

        _logger.LogDebug("Searching for '{OpText}' in Y-band text '{CandidateText}' ({Count} letters)",
            operationText.Length > 30 ? operationText.Substring(0, 30) + "..." : operationText,
            candidateText.Length > 50 ? candidateText.Substring(0, 50) + "..." : candidateText,
            candidateLetters.Count);

        // Strategy 1: Find exact text match within the Y band
        int matchIndex = FindTextInCandidates(candidateText, operationText, candidateLetters, startX);

        if (matchIndex >= 0)
        {
            // Found exact match - use the letters at this position
            int count = Math.Min(operationText.Length, candidateLetters.Count - matchIndex);
            for (int i = 0; i < count; i++)
            {
                matches.Add(new LetterMatch
                {
                    CharacterIndex = i,
                    Letter = candidateLetters[matchIndex + i],
                    OperationText = operationText
                });
            }
            _logger.LogDebug("Text match found at index {Index}: {Count} letters matched", matchIndex, matches.Count);
            return matches;
        }

        // Strategy 2: Fallback to old proximity-based matching for non-text operations
        // (keeping this for backwards compatibility with edge cases)
        var nearbyLetters = new List<Letter>();
        var closestLetter = candidateLetters
            .OrderBy(l => Math.Abs(l.GlyphRectangle.Left - startX))
            .First();

        var startIndex = candidateLetters.IndexOf(closestLetter);
        var maxLetters = Math.Min(candidateLetters.Count - startIndex, textOperation.Text.Length + 5);
        nearbyLetters = candidateLetters.Skip(startIndex).Take(maxLetters).ToList();

        _logger.LogDebug("Fallback to proximity: {Count} letters starting from '{First}'",
            nearbyLetters.Count, nearbyLetters.FirstOrDefault()?.Value ?? "?");

        // Try to match with relaxed rules
        matches = MatchLettersToText(nearbyLetters, textOperation.Text, textOperation);

        if (matches.Count == 0)
        {
            _logger.LogWarning("Could not match any letters for operation '{Text}' at ({X:F2},{Y:F2})",
                textOperation.Text.Length > 50 ? textOperation.Text.Substring(0, 50) + "..." : textOperation.Text,
                opBox.Left, opBox.Bottom);
        }

        return matches;
    }

    /// <summary>
    /// Find the operation text within the candidate letters, preferring matches closer to startX.
    /// </summary>
    private int FindTextInCandidates(string candidateText, string operationText, List<Letter> candidateLetters, double startX)
    {
        // Find all occurrences of operation text in candidate text
        var foundIndices = new List<int>();
        int searchStart = 0;
        while (true)
        {
            int idx = candidateText.IndexOf(operationText, searchStart, StringComparison.Ordinal);
            if (idx < 0)
            {
                // Try case-insensitive
                idx = candidateText.IndexOf(operationText, searchStart, StringComparison.OrdinalIgnoreCase);
            }
            if (idx < 0) break;
            foundIndices.Add(idx);
            searchStart = idx + 1;
        }

        if (foundIndices.Count == 0)
            return -1;

        if (foundIndices.Count == 1)
            return foundIndices[0];

        // Multiple matches found - pick the one closest to startX
        int bestIndex = foundIndices[0];
        double bestDistance = double.MaxValue;

        foreach (var idx in foundIndices)
        {
            if (idx < candidateLetters.Count)
            {
                double letterX = candidateLetters[idx].GlyphRectangle.Left;
                double distance = Math.Abs(letterX - startX);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = idx;
                }
            }
        }

        _logger.LogDebug("Found {Count} matches, chose index {Index} (X distance: {Distance:F2})",
            foundIndices.Count, bestIndex, bestDistance);

        return bestIndex;
    }

    /// <summary>
    /// Match nearby letters to text operation using combined spatial + textual matching.
    /// </summary>
    private List<LetterMatch> MatchLettersToText(
        List<Letter> nearbyLetters,
        string operationText,
        TextOperation textOperation)
    {
        var matches = new List<LetterMatch>();

        if (nearbyLetters.Count == 0 || string.IsNullOrEmpty(operationText))
            return matches;

        // Strategy: Find a contiguous sequence of letters that matches our text
        // Handle the fact that letter count may not equal text length due to:
        // - Ligatures (fi, fl rendered as single glyphs)
        // - Encoding differences
        // - Whitespace handling

        // Build the text from nearby letters
        var letterText = string.Join("", nearbyLetters.Select(l => l.Value));

        _logger.LogDebug("Letter text: '{LetterText}' vs Operation text: '{OpText}'",
            letterText.Length > 50 ? letterText.Substring(0, 50) + "..." : letterText,
            operationText.Length > 50 ? operationText.Substring(0, 50) + "..." : operationText);

        // Try exact match first
        if (letterText.StartsWith(operationText) && nearbyLetters.Count >= operationText.Length)
        {
            // Perfect match - map first N letters to operation text
            for (int i = 0; i < operationText.Length; i++)
            {
                matches.Add(new LetterMatch
                {
                    CharacterIndex = i,
                    Letter = nearbyLetters[i],
                    OperationText = operationText
                });
            }

            _logger.LogDebug("Exact match: {Count} letters matched", matches.Count);
            return matches;
        }

        // Try case-insensitive match
        if (letterText.StartsWith(operationText, StringComparison.OrdinalIgnoreCase) && nearbyLetters.Count >= operationText.Length)
        {
            for (int i = 0; i < operationText.Length; i++)
            {
                matches.Add(new LetterMatch
                {
                    CharacterIndex = i,
                    Letter = nearbyLetters[i],
                    OperationText = operationText
                });
            }

            _logger.LogDebug("Case-insensitive match: {Count} letters matched", matches.Count);
            return matches;
        }

        // Try fuzzy match - handle encoding differences
        // Only if counts are VERY similar (not just within 3)
        if (nearbyLetters.Count == operationText.Length ||
            (nearbyLetters.Count == operationText.Length + 1) ||  // Allow 1 extra letter (ligature, etc.)
            (nearbyLetters.Count == operationText.Length - 1))    // Allow 1 missing letter
        {
            int count = Math.Min(nearbyLetters.Count, operationText.Length);
            for (int i = 0; i < count; i++)
            {
                matches.Add(new LetterMatch
                {
                    CharacterIndex = i,
                    Letter = nearbyLetters[i],
                    OperationText = operationText
                });
            }

            _logger.LogDebug("Fuzzy match: {Count} letters matched (letter count: {LetterCount}, text length: {TextLength})",
                matches.Count, nearbyLetters.Count, operationText.Length);
            return matches;
        }

        _logger.LogWarning("Could not match letters to text - letter text: '{LetterText}', operation text: '{OpText}'",
            letterText, operationText);

        return matches;
    }
}

/// <summary>
/// Represents a match between a PdfPig letter and a character in a text operation.
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
}
