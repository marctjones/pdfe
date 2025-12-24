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

        // Strategy: Use Y-range filtering + proximity to operation start + text length limit
        // This handles cases where bbox estimates are wrong but Y-position is approximately correct

        // Get starting position from glyphs if available, otherwise use bbox
        double startX = opBox.Left;
        double startY = opBox.Bottom;
        if (textOperation.Glyphs.Count > 0)
        {
            startX = textOperation.Glyphs[0].BoundingBox.Left;
            startY = textOperation.Glyphs[0].BoundingBox.Bottom;
        }

        // Find letters in the same Y-range (with tolerance for height differences)
        const double YTolerance = 5.0;
        var candidateLetters = allLetters
            .Where(l => {
                bool yNearby = Math.Abs(l.GlyphRectangle.Bottom - startY) <= YTolerance;
                return yNearby;
            })
            .OrderBy(l => l.GlyphRectangle.Left)
            .ToList();

        // Now find the sequence starting closest to our start position
        // Take letters that form a continuous sequence
        var nearbyLetters = new List<Letter>();

        if (candidateLetters.Count > 0)
        {
            // Find the letter closest to our start X position
            var closestLetter = candidateLetters
                .OrderBy(l => Math.Abs(l.GlyphRectangle.Left - startX))
                .First();

            var startIndex = candidateLetters.IndexOf(closestLetter);

            // Take letters from this point, up to the text length (with some tolerance for ligatures)
            var maxLetters = Math.Min(candidateLetters.Count - startIndex, textOperation.Text.Length + 5);
            nearbyLetters = candidateLetters.Skip(startIndex).Take(maxLetters).ToList();
        }

        _logger.LogDebug("Found {Count} letters for operation at ({X:F2},{Y:F2}) with text '{Text}'",
            nearbyLetters.Count, opBox.Left, opBox.Bottom,
            textOperation.Text.Length > 20 ? textOperation.Text.Substring(0, 20) + "..." : textOperation.Text);

        // Now try to match the nearby letters to our text operation
        // Use combined spatial + textual matching
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
