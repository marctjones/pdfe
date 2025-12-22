using Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Filters text operations at character level, splitting them into runs of
/// characters to keep vs remove based on redaction area.
/// </summary>
public class CharacterLevelTextFilter
{
    private readonly CharacterMatcher _matcher;
    private readonly TextOperationEmitter _emitter;
    private readonly ILogger<CharacterLevelTextFilter> _logger;

    public CharacterLevelTextFilter(
        CharacterMatcher matcher,
        TextOperationEmitter emitter,
        ILogger<CharacterLevelTextFilter> logger)
    {
        _matcher = matcher;
        _emitter = emitter;
        _logger = logger;
    }

    /// <summary>
    /// Filter a text operation at character level.
    /// </summary>
    /// <param name="textOp">The text operation to filter</param>
    /// <param name="letters">All letters on the page (from PdfPig)</param>
    /// <param name="redactionArea">Redaction area in Avalonia coordinates</param>
    /// <param name="pageHeight">Page height for coordinate conversion</param>
    /// <returns>Filter result with operations to keep and removed text</returns>
    public TextFilterResult FilterTextOperation(
        TextOperation textOp,
        List<Letter> letters,
        Rect redactionArea,
        double pageHeight)
    {
        var result = new TextFilterResult();

        // Step 1: Match letters to operation characters
        var letterMap = _matcher.MatchLettersToOperation(textOp, letters, pageHeight);

        if (letterMap == null)
        {
            // Character matching failed - fall back to operation-level check
            _logger.LogDebug("Character matching failed for operation '{Text}', using fallback",
                textOp.Text.Length > 20 ? textOp.Text.Substring(0, 20) + "..." : textOp.Text);

            result.FallbackToOperationLevel = true;
            result.Operations.Add(textOp);
            return result;
        }

        // Step 2: Build character runs (contiguous sequences of keep/remove)
        var runs = BuildCharacterRuns(textOp, letterMap, redactionArea, pageHeight);

        // Step 3: Check if any characters are in redaction area
        bool anyRemoved = runs.Any(r => !r.Keep);

        if (!anyRemoved)
        {
            // Nothing to redact - return original operation
            result.Operations.Add(textOp);
            return result;
        }

        // Step 4: Build removed text for clipboard history
        result.RemovedText = BuildRemovedText(runs);

        // Step 5: Use TextOperationEmitter to generate PDF bytes for runs to keep
        var partialOps = _emitter.EmitOperations(runs, textOp, letterMap, pageHeight);
        result.Operations.AddRange(partialOps);

        _logger.LogDebug("Filtered '{Text}' into {Kept} kept runs, {Removed} removed runs",
            textOp.Text.Length > 20 ? textOp.Text.Substring(0, 20) + "..." : textOp.Text,
            runs.Count(r => r.Keep),
            runs.Count(r => !r.Keep));

        return result;
    }

    /// <summary>
    /// Build character runs by grouping consecutive characters with same keep/remove status.
    /// </summary>
    private List<CharacterRun> BuildCharacterRuns(
        TextOperation textOp,
        Dictionary<int, Letter> letterMap,
        Rect redactionArea,
        double pageHeight)
    {
        var runs = new List<CharacterRun>();
        CharacterRun? currentRun = null;

        for (int i = 0; i < textOp.Text.Length; i++)
        {
            char c = textOp.Text[i];

            // Determine if this character should be kept or removed
            bool keep = true;

            if (letterMap.TryGetValue(i, out var letter))
            {
                // We have a letter match - check if it's in redaction area
                keep = !_matcher.IsLetterInRedactionArea(letter, redactionArea, pageHeight);
            }
            else
            {
                // No letter match (likely whitespace) - keep by default
                // Whitespace will be removed if surrounding text is removed
                keep = true;
            }

            // Skip whitespace at boundaries (leading/trailing spaces in runs)
            bool isWhitespace = char.IsWhiteSpace(c);

            // Start new run or continue current run?
            if (currentRun == null || currentRun.Keep != keep)
            {
                // Start new run
                if (currentRun != null && currentRun.EndIndex > currentRun.StartIndex)
                {
                    runs.Add(currentRun);
                }

                currentRun = new CharacterRun(textOp.Text)
                {
                    StartIndex = i,
                    EndIndex = i + 1,
                    Keep = keep
                };

                // Set position from letter if available
                if (letterMap.TryGetValue(i, out var startLetter))
                {
                    currentRun.StartPosition = new Point(
                        startLetter.GlyphRectangle.Left,
                        startLetter.GlyphRectangle.Bottom);
                    currentRun.Width = startLetter.GlyphRectangle.Width;
                }
            }
            else
            {
                // Continue current run
                currentRun.EndIndex = i + 1;

                // Extend width
                if (letterMap.TryGetValue(i, out var extendLetter))
                {
                    currentRun.Width = extendLetter.GlyphRectangle.Right - currentRun.StartPosition.X;
                }
            }
        }

        // Add final run
        if (currentRun != null && currentRun.EndIndex > currentRun.StartIndex)
        {
            runs.Add(currentRun);
        }

        // Clean up whitespace at run boundaries
        return CleanupWhitespace(runs);
    }

    /// <summary>
    /// Remove leading/trailing whitespace from runs.
    /// </summary>
    private List<CharacterRun> CleanupWhitespace(List<CharacterRun> runs)
    {
        foreach (var run in runs)
        {
            // Trim leading whitespace
            while (run.StartIndex < run.EndIndex &&
                   char.IsWhiteSpace(run.Text[0]))
            {
                run.StartIndex++;
            }

            // Trim trailing whitespace
            while (run.EndIndex > run.StartIndex &&
                   char.IsWhiteSpace(run.Text[run.Text.Length - 1]))
            {
                run.EndIndex--;
            }
        }

        // Remove empty runs
        return runs.Where(r => r.EndIndex > r.StartIndex).ToList();
    }

    /// <summary>
    /// Build removed text string from runs marked for removal.
    /// </summary>
    private string BuildRemovedText(List<CharacterRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs.Where(r => !r.Keep))
        {
            sb.Append(run.Text);
        }
        return sb.ToString();
    }

}
