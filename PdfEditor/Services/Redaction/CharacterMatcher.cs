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

        // Convert operation bbox to PDF coordinates for matching
        var opBbox = textOp.BoundingBox;
        var opLeft = opBbox.X;
        var opRight = opBbox.X + opBbox.Width;
        var opTop = pageHeight - opBbox.Y;  // Convert to PDF Y (bottom-left origin)
        var opBottom = pageHeight - (opBbox.Y + opBbox.Height);

        // Allow tolerance for font metrics approximation
        var tolerance = 5.0;

        // Filter letters that are within the operation's bounding box
        var matchingLetters = letters.Where(letter =>
        {
            var letterCenterX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2.0;
            var letterCenterY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2.0;

            return letterCenterX >= opLeft - tolerance &&
                   letterCenterX <= opRight + tolerance &&
                   letterCenterY >= opBottom - tolerance &&
                   letterCenterY <= opTop + tolerance;
        }).OrderBy(l => l.GlyphRectangle.Left).ToList();

        if (matchingLetters.Count == 0)
        {
            _logger.LogDebug("No letters matched for operation '{Text}'",
                textOp.Text.Length > 20 ? textOp.Text.Substring(0, 20) + "..." : textOp.Text);
            return null;
        }

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
