using Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Matches PdfPig Letter objects to characters in a TextOperation.
/// Uses spatial proximity and character value to correlate extracted letter
/// positions with operation text.
/// </summary>
public class CharacterMatcher
{
    private readonly ILogger<CharacterMatcher> _logger;

    public CharacterMatcher(ILogger<CharacterMatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Match PdfPig letters to characters in a text operation.
    /// </summary>
    /// <param name="textOp">The text operation to match</param>
    /// <param name="letters">All letters on the page</param>
    /// <param name="pageHeight">Page height for coordinate conversion</param>
    /// <returns>
    /// Dictionary mapping character index to Letter, or null if matching failed.
    /// Some indices may be missing if no letter matches (e.g., spaces).
    /// </returns>
    public Dictionary<int, Letter>? MatchLettersToOperation(
        TextOperation textOp,
        List<Letter> letters,
        double pageHeight)
    {
        if (string.IsNullOrEmpty(textOp.Text))
            return null;

        var result = new Dictionary<int, Letter>();

        // CRITICAL: TextBoundsCalculator produces INACCURATE bounding boxes (can be 1000s of points wide!)
        // Strategy: Use a VERY GENEROUS spatial filter + text content matching
        //
        // Key insight: TextBoundsCalculator can be wildly wrong in WIDTH but is usually
        // reasonable in X-start position and Y position. So:
        // 1. Filter letters by Y position (vertical alignment) with generous tolerance
        // 2. Filter letters starting near the operation's X position
        // 3. Don't use bbox width/right edge at all - it's unreliable
        // 4. Within this spatial region, match by text content sequence
        //
        // This gives us spatial localization (to distinguish multiple lines) while using
        // actual PdfPig letter positions (ACCURATE) for the matching itself.

        var opBbox = textOp.BoundingBox;

        // Convert operation bbox to PDF coordinates
        var opLeft = opBbox.X;
        var opAvaloniaY = opBbox.Y;
        var opTop = pageHeight - opAvaloniaY;  // PDF Y coordinate
        var opBottom = pageHeight - (opAvaloniaY + opBbox.Height);

        // GENEROUS tolerances - we're just trying to get the right line/region
        var yTolerance = 20.0;  // 20 points = ~0.3 inches vertical
        var xTolerance = 50.0;  // 50 points = ~0.7 inches to the left of start position

        // Filter letters that are in the vertical vicinity and start reasonably close
        var candidateLetters = letters.Where(letter =>
        {
            var letterCenterY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2.0;
            var letterLeft = letter.GlyphRectangle.Left;

            // Must be on roughly the same line (Y within tolerance)
            bool yMatch = letterCenterY >= opBottom - yTolerance &&
                          letterCenterY <= opTop + yTolerance;

            // Must start reasonably close to operation's start position
            // (we don't check right edge because bbox width is unreliable)
            bool xMatch = letterLeft >= opLeft - xTolerance;

            return yMatch && xMatch;
        }).OrderBy(l => l.GlyphRectangle.Left).ToList();

        if (candidateLetters.Count == 0)
        {
            _logger.LogWarning("CharacterMatcher: No letters found in spatial vicinity. Operation '{Text}' @ BBox=({X:F1},{Y:F1},{W:F1}x{H:F1}), PDF Y range=({Bottom:F1} to {Top:F1})",
                textOp.Text.Length > 20 ? textOp.Text.Substring(0, 20) + "..." : textOp.Text,
                opBbox.X, opBbox.Y, opBbox.Width, opBbox.Height,
                opBottom, opTop);
            return null;
        }

        // Now search for operation text within these candidates
        var candidatesText = string.Join("", candidateLetters.Select(l => l.Value));
        var opTextNormalized = textOp.Text.Replace("\r", "").Replace("\n", "");

        _logger.LogDebug("CharacterMatcher: Searching for '{OpText}' in {CandCount} candidate letters. Candidates text: '{CandText}'",
            opTextNormalized.Length > 30 ? opTextNormalized.Substring(0, 30) + "..." : opTextNormalized,
            candidateLetters.Count,
            candidatesText.Length > 50 ? candidatesText.Substring(0, 50) + "..." : candidatesText);

        var startIndex = candidatesText.IndexOf(opTextNormalized, StringComparison.OrdinalIgnoreCase);

        if (startIndex < 0)
        {
            _logger.LogWarning("CharacterMatcher: Operation text NOT FOUND in candidates. Operation='{OpText}' ({OpLen} chars), Candidates='{CandText}' ({CandLen} chars from {LetterCount} letters)",
                opTextNormalized.Length > 30 ? opTextNormalized.Substring(0, 30) + "..." : opTextNormalized,
                opTextNormalized.Length,
                candidatesText.Length > 50 ? candidatesText.Substring(0, 50) + "..." : candidatesText,
                candidatesText.Length,
                candidateLetters.Count);
            return null;
        }

        // Extract the matching letters for this operation
        var matchingLetters = candidateLetters.Skip(startIndex).Take(opTextNormalized.Length).ToList();

        // Match letters to character indices
        // Strategy: Match by position order (left-to-right) and character value
        var letterIndex = 0;
        for (int charIndex = 0; charIndex < textOp.Text.Length && letterIndex < matchingLetters.Count; charIndex++)
        {
            var c = textOp.Text[charIndex];
            var letter = matchingLetters[letterIndex];

            // Verify character match (case-insensitive due to encoding variations)
            // Note: PdfPig DOES report spaces, so we match them too
            var charMatch = string.Equals(letter.Value, c.ToString(), StringComparison.OrdinalIgnoreCase) ||
                           (letter.Value.Length == 1 && char.ToUpperInvariant(letter.Value[0]) == char.ToUpperInvariant(c));

            // For whitespace, be more lenient - any whitespace matches any whitespace
            if (char.IsWhiteSpace(c) && string.IsNullOrWhiteSpace(letter.Value))
            {
                charMatch = true;
            }

            if (charMatch)
            {
                result[charIndex] = letter;
                letterIndex++;
            }
            else
            {
                // Mismatch - try to recover by checking if letter matches a later character
                _logger.LogDebug("Character mismatch at index {Index}: expected '{Expected}', found '{Found}'",
                    charIndex, c, letter.Value);

                // Still advance letterIndex to avoid getting stuck
                letterIndex++;
            }
        }

        _logger.LogDebug("Matched {Count}/{Total} characters for operation '{Text}'",
            result.Count, textOp.Text.Length,
            textOp.Text.Length > 20 ? textOp.Text.Substring(0, 20) + "..." : textOp.Text);

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Check if a letter's center point is inside a redaction area.
    /// </summary>
    /// <param name="letter">The PdfPig letter</param>
    /// <param name="area">Redaction area in Avalonia coordinates (top-left origin, PDF points)</param>
    /// <param name="pageHeight">Page height for coordinate conversion</param>
    public bool IsLetterInRedactionArea(Letter letter, Rect area, double pageHeight)
    {
        var centerX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2.0;
        var centerY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2.0;

        // Convert redaction area to PDF coordinates (bottom-left origin)
        var pdfAreaBottom = pageHeight - area.Y - area.Height;
        var pdfAreaTop = pageHeight - area.Y;

        return centerX >= area.X && centerX <= area.X + area.Width &&
               centerY >= pdfAreaBottom && centerY <= pdfAreaTop;
    }
}
