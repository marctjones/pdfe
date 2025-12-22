using Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Generates raw PDF bytes for partial text operations.
/// Bypasses PdfSharp's COperator limitations by directly emitting PDF syntax.
/// </summary>
public class TextOperationEmitter
{
    private readonly ILogger<TextOperationEmitter> _logger;

    public TextOperationEmitter(ILogger<TextOperationEmitter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Emit PDF bytes for a character run that should be kept.
    /// Generates positioning operator (Td) followed by text operator (Tj).
    /// </summary>
    /// <param name="run">The character run to emit</param>
    /// <param name="textOp">Original text operation (for font/size context)</param>
    /// <param name="letterMap">Mapping of character indices to letters</param>
    /// <param name="pageHeight">Page height for coordinate conversion</param>
    /// <returns>Raw PDF bytes representing this partial operation</returns>
    public byte[] EmitPartialOperation(
        CharacterRun run,
        TextOperation textOp,
        Dictionary<int, Letter> letterMap,
        double pageHeight)
    {
        if (run.Text.Length == 0)
            return Array.Empty<byte>();

        var sb = new StringBuilder();

        // If we have position information, emit Tm (set text matrix) operator
        // Tm provides absolute positioning, avoiding coordinate accumulation issues
        if (run.StartPosition.X > 0 || run.StartPosition.Y > 0)
        {
            // Tm operator: a b c d e f Tm (sets text matrix)
            // For simple positioning without rotation/scaling: 1 0 0 1 x y Tm
            // where x, y is the absolute position in PDF coordinates
            sb.Append($"1 0 0 1 {run.StartPosition.X:F2} {run.StartPosition.Y:F2} Tm ");
        }

        // Emit text operator: (text) Tj
        // Need to escape special characters in PDF strings
        var escapedText = EscapePdfString(run.Text);
        sb.Append($"({escapedText}) Tj");

        var bytes = Encoding.ASCII.GetBytes(sb.ToString());

        _logger.LogDebug("Emitted {Length} bytes for '{Text}' at ({X:F1}, {Y:F1})",
            bytes.Length,
            run.Text.Length > 20 ? run.Text.Substring(0, 20) + "..." : run.Text,
            run.StartPosition.X,
            run.StartPosition.Y);

        return bytes;
    }

    /// <summary>
    /// Emit complete operations for a list of character runs.
    /// Each run gets its own PartialTextOperation with raw PDF bytes.
    /// </summary>
    /// <param name="runs">Character runs to emit (only Keep=true runs should be passed)</param>
    /// <param name="textOp">Original text operation</param>
    /// <param name="letterMap">Mapping of character indices to letters</param>
    /// <param name="pageHeight">Page height</param>
    /// <returns>List of PartialTextOperations with populated RawBytes</returns>
    public List<PartialTextOperation> EmitOperations(
        List<CharacterRun> runs,
        TextOperation textOp,
        Dictionary<int, Letter> letterMap,
        double pageHeight)
    {
        var operations = new List<PartialTextOperation>();

        foreach (var run in runs.Where(r => r.Keep))
        {
            var rawBytes = EmitPartialOperation(run, textOp, letterMap, pageHeight);

            if (rawBytes.Length > 0)
            {
                var partialOp = new PartialTextOperation
                {
                    RawBytes = rawBytes,
                    DisplayText = run.Text,
                    OperatorType = "Tj",
                    BoundingBox = new Rect(
                        run.StartPosition.X,
                        pageHeight - run.StartPosition.Y - textOp.BoundingBox.Height,
                        run.Width,
                        textOp.BoundingBox.Height)
                };

                operations.Add(partialOp);
            }
        }

        _logger.LogDebug("Emitted {Count} partial operations from {Total} runs",
            operations.Count, runs.Count);

        return operations;
    }

    /// <summary>
    /// Escape special characters in PDF strings.
    /// PDF strings use parentheses as delimiters and backslash for escaping.
    /// </summary>
    /// <param name="text">Text to escape</param>
    /// <returns>Escaped text safe for PDF string literals</returns>
    private string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length + 10);

        foreach (char c in text)
        {
            switch (c)
            {
                case '(':  // Left parenthesis - must escape
                    sb.Append("\\(");
                    break;
                case ')':  // Right parenthesis - must escape
                    sb.Append("\\)");
                    break;
                case '\\': // Backslash - must escape
                    sb.Append("\\\\");
                    break;
                case '\n': // Newline - escape as \n
                    sb.Append("\\n");
                    break;
                case '\r': // Carriage return - escape as \r
                    sb.Append("\\r");
                    break;
                case '\t': // Tab - escape as \t
                    sb.Append("\\t");
                    break;
                default:
                    // For characters outside printable ASCII range, use octal escaping
                    if (c < 32 || c > 126)
                    {
                        sb.Append($"\\{Convert.ToString(c, 8).PadLeft(3, '0')}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Calculate text positioning based on original operation and character offset.
    /// Used when we need to position a partial text operation within the original bbox.
    /// </summary>
    /// <param name="textOp">Original text operation</param>
    /// <param name="charOffset">Character offset (index) within the text</param>
    /// <param name="letterMap">Letter mapping from CharacterMatcher</param>
    /// <returns>Position point in PDF coordinates</returns>
    public Point CalculateCharacterPosition(
        TextOperation textOp,
        int charOffset,
        Dictionary<int, Letter> letterMap)
    {
        // If we have a letter match, use its position
        if (letterMap.TryGetValue(charOffset, out var letter))
        {
            return new Point(
                letter.GlyphRectangle.Left,
                letter.GlyphRectangle.Bottom);
        }

        // Fallback: use original operation position
        // This happens for whitespace characters
        return textOp.Position;
    }
}
