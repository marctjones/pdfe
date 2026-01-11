using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Redaction.ContentStream.Building;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Orchestrates glyph-level redaction by coordinating LetterFinder, TextSegmenter,
/// and OperationReconstructor to remove individual characters from text operations.
/// </summary>
public class GlyphRemover
{
    private readonly LetterFinder _letterFinder;
    private readonly TextSegmenter _textSegmenter;
    private readonly OperationReconstructor _operationReconstructor;
    private readonly IContentStreamBuilder _contentStreamBuilder;
    private readonly ILogger<GlyphRemover> _logger;

    public GlyphRemover(
        IContentStreamBuilder contentStreamBuilder,
        ILogger<GlyphRemover>? logger = null,
        ILogger<LetterFinder>? letterFinderLogger = null,
        ILogger<TextSegmenter>? textSegmenterLogger = null,
        ILogger<OperationReconstructor>? operationReconstructorLogger = null)
    {
        _letterFinder = new LetterFinder(letterFinderLogger ?? NullLogger<LetterFinder>.Instance);
        _textSegmenter = new TextSegmenter(textSegmenterLogger ?? NullLogger<TextSegmenter>.Instance);
        _operationReconstructor = new OperationReconstructor(operationReconstructorLogger ?? NullLogger<OperationReconstructor>.Instance);
        _contentStreamBuilder = contentStreamBuilder;
        _logger = logger ?? NullLogger<GlyphRemover>.Instance;
    }

    /// <summary>
    /// Process text operations to perform glyph-level redaction using block-aware filtering.
    /// </summary>
    /// <param name="operations">PDF operations to process.</param>
    /// <param name="letters">PdfPig letters for position matching.</param>
    /// <param name="redactionArea">Area to redact in visual coordinates.</param>
    /// <param name="pageRotation">Page rotation in degrees (0, 90, 180, 270). Default 0.</param>
    /// <param name="mediaBoxWidth">Page MediaBox width in points. Default 612 (US Letter).</param>
    /// <param name="mediaBoxHeight">Page MediaBox height in points. Default 792 (US Letter).</param>
    /// <returns>List of modified operations with redacted text removed.</returns>
    public List<PdfOperation> ProcessOperations(
        List<PdfOperation> operations,
        IReadOnlyList<Letter> letters,
        PdfRectangle redactionArea,
        int pageRotation = 0,
        double mediaBoxWidth = 612,
        double mediaBoxHeight = 792)
    {
        _logger.LogInformation("=== STARTING OPERATION PROCESSING ===");
        _logger.LogInformation("Total operations: {Count}, Redaction area: ({L:F2},{B:F2})-({R:F2},{T:F2})",
            operations.Count, redactionArea.Left, redactionArea.Bottom, redactionArea.Right, redactionArea.Top);

        // PHASE 1: Identify text blocks and mark those with reconstructed text
        // Pass letters for accurate intersection testing using actual PdfPig positions
        var textBlocks = IdentifyTextBlocks(operations, redactionArea, letters);
        _logger.LogInformation("Identified {Count} text blocks, {ReconstructedCount} will be reconstructed",
            textBlocks.Count, textBlocks.Count(b => b.HasReconstructedText));

        // PHASE 2: Pre-analyze blocks to determine:
        // 1. Which blocks will have remaining content (not entirely redacted)
        // 2. Which blocks have kept-as-is TextOps (need original state ops)
        var blocksWithContent = new HashSet<TextBlockInfo>();
        var blocksWithKeptAsIsTextOps = new HashSet<TextBlockInfo>();

        foreach (var block in textBlocks.Where(b => b.HasReconstructedText))
        {
            var textOpsInBlock = operations
                .Where(o => block.Contains(o.StreamPosition))
                .OfType<TextOperation>()
                .ToList();

            foreach (var textOp in textOpsInBlock)
            {
                var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

                // Check if this TextOp has any letters NOT in the redaction area
                bool hasKeptAsIsLetters = letterMatches.Any(match =>
                {
                    var rect = match.Letter.GlyphRectangle;
                    double centerX = (rect.Left + rect.Right) / 2.0;
                    double centerY = (rect.Bottom + rect.Top) / 2.0;
                    return !redactionArea.Contains(centerX, centerY);
                });

                // Check if this TextOp doesn't intersect at all (entirely kept-as-is)
                bool hasIntersectingLetters = letterMatches.Any(match =>
                {
                    var rect = match.Letter.GlyphRectangle;
                    double centerX = (rect.Left + rect.Right) / 2.0;
                    double centerY = (rect.Bottom + rect.Top) / 2.0;
                    return redactionArea.Contains(centerX, centerY);
                });

                if (hasKeptAsIsLetters)
                {
                    // This block has remaining content
                    blocksWithContent.Add(block);
                }

                if (!hasIntersectingLetters)
                {
                    // This TextOp is entirely kept-as-is (no intersection)
                    // We need original state ops for it
                    blocksWithKeptAsIsTextOps.Add(block);
                    blocksWithContent.Add(block);
                }
            }
        }

        // PHASE 3: Build output, filtering operations from reconstructed blocks
        // Key insight for Issue #270 fix:
        // - TextOperations that DON'T intersect redaction area should be kept as-is
        //   (with their original positioning) to avoid content shift
        // - TextOperations that DO intersect are reconstructed with new BT/ET blocks
        // - For mixed blocks: keep original structure for non-intersecting text,
        //   then append reconstructed segments AFTER the original ET
        var modifiedOperations = new List<PdfOperation>();
        int textOpsFound = 0;
        int operationsReconstructed = 0;
        int operationsSkippedFromReconstructedBlocks = 0;
        int operationsKeptAsIs = 0;

        // Track deferred reconstructed operations per block (output after block's ET)
        var deferredReconstructedOps = new Dictionary<TextBlockInfo, List<PdfOperation>>();

        foreach (var operation in operations)
        {
            // Find which text block (if any) contains this operation
            var containingBlock = textBlocks.FirstOrDefault(b => b.Contains(operation.StreamPosition));

            if (containingBlock != null && containingBlock.HasReconstructedText)
            {
                // Check if this block will have any remaining content
                if (!blocksWithContent.Contains(containingBlock))
                {
                    // This entire block will be empty after redaction - skip all operations
                    operationsSkippedFromReconstructedBlocks++;
                    continue;
                }

                // This operation is inside a text block that has SOME text needing redaction
                if (operation is TextOperation textOp)
                {
                    textOpsFound++;
                    var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

                    // CRITICAL FIX (Issue #270): Check if THIS specific TextOperation has letters in redaction area
                    // If not, we should NOT reconstruct it - preserve original positioning to avoid content shift
                    bool hasIntersectingLetters = letterMatches.Any(match =>
                    {
                        var rect = match.Letter.GlyphRectangle;
                        double centerX = (rect.Left + rect.Right) / 2.0;
                        double centerY = (rect.Bottom + rect.Top) / 2.0;
                        return redactionArea.Contains(centerX, centerY);
                    });

                    if (!hasIntersectingLetters)
                    {
                        // This TextOperation doesn't intersect the redaction area
                        // Keep it as-is to preserve original positioning (avoid content shift)
                        modifiedOperations.Add(operation);
                        operationsKeptAsIs++;
                        _logger.LogDebug("Keeping TextOp '{Text}' as-is (no intersecting letters)",
                            textOp.Text.Length > 30 ? textOp.Text.Substring(0, 30) + "..." : textOp.Text);
                        continue;
                    }

                    // This TextOperation intersects the redaction area - needs reconstruction
                    // Update bbox with real positions if we have letter matches
                    if (letterMatches.Count > 0)
                    {
                        var realBBox = CalculateBoundingBoxFromLetters(letterMatches);
                        textOp = new TextOperation
                        {
                            Operator = textOp.Operator,
                            Operands = textOp.Operands,
                            StreamPosition = textOp.StreamPosition,
                            Text = textOp.Text,
                            Glyphs = textOp.Glyphs,
                            FontName = textOp.FontName,
                            FontSize = textOp.FontSize,
                            BoundingBox = realBBox
                        };
                    }

                    var segments = _textSegmenter.BuildSegments(textOp, letterMatches, redactionArea);

                    if (segments.Count > 0)
                    {
                        // CRITICAL FIX (Issue #173): Pass rotation info for coordinate transformation
                        var reconstructed = _operationReconstructor.ReconstructWithPositioning(
                            segments, textOp, pageRotation, mediaBoxWidth, mediaBoxHeight);

                        // CRITICAL FIX (Issue #270): Defer reconstructed ops until after ET
                        // to avoid outputting BT inside existing BT block
                        if (!deferredReconstructedOps.ContainsKey(containingBlock))
                        {
                            deferredReconstructedOps[containingBlock] = new List<PdfOperation>();
                        }
                        deferredReconstructedOps[containingBlock].AddRange(reconstructed);

                        operationsReconstructed++;
                        _logger.LogInformation("Deferred reconstruction of TextOp '{Text}' into {Count} segments",
                            textOp.Text.Length > 30 ? textOp.Text.Substring(0, 30) + "..." : textOp.Text,
                            segments.Count);
                    }
                    else
                    {
                        _logger.LogInformation("All segments removed (complete redaction) for '{Text}'",
                            textOp.Text.Length > 30 ? textOp.Text.Substring(0, 30) + "..." : textOp.Text);
                    }
                }
                else
                {
                    // State operation (BT, ET, Tf, Tm, etc.)
                    // Only keep original state ops if block has kept-as-is TextOps that need them
                    if (blocksWithKeptAsIsTextOps.Contains(containingBlock))
                    {
                        modifiedOperations.Add(operation);
                        operationsKeptAsIs++;

                        // If this is ET and we have deferred reconstructed ops for this block,
                        // output them now (after the original block ends)
                        if (operation is TextStateOperation tso && tso.Operator == "ET")
                        {
                            if (deferredReconstructedOps.TryGetValue(containingBlock, out var deferred) && deferred.Count > 0)
                            {
                                modifiedOperations.AddRange(deferred);
                                _logger.LogDebug("Output {Count} deferred reconstructed operations after ET", deferred.Count);
                                deferred.Clear();
                            }
                        }
                    }
                    else
                    {
                        // No kept-as-is TextOps - skip original state ops
                        // But if this is ET and we have deferred ops, output them now
                        operationsSkippedFromReconstructedBlocks++;

                        if (operation is TextStateOperation tso && tso.Operator == "ET")
                        {
                            if (deferredReconstructedOps.TryGetValue(containingBlock, out var deferred) && deferred.Count > 0)
                            {
                                modifiedOperations.AddRange(deferred);
                                _logger.LogDebug("Output {Count} deferred reconstructed operations (no kept-as-is TextOps)", deferred.Count);
                                deferred.Clear();
                            }
                        }
                    }
                }
            }
            else
            {
                // Not in a reconstructed block - keep as-is
                operationsKeptAsIs++;
                modifiedOperations.Add(operation);
            }
        }

        _logger.LogInformation("=== OPERATION PROCESSING COMPLETE ===");
        _logger.LogInformation("Text operations reconstructed: {Reconstructed}", operationsReconstructed);
        _logger.LogInformation("Operations skipped from reconstructed blocks: {Skipped}", operationsSkippedFromReconstructedBlocks);
        _logger.LogInformation("Operations kept as-is: {Kept}", operationsKeptAsIs);
        _logger.LogInformation("Total operations in output: {Output} (vs {Input} input)",
            modifiedOperations.Count, operations.Count);

        // Validate reconstructed content stream (issue #126)
        var validator = new ContentStreamValidator();
        var validationResult = validator.Validate(modifiedOperations);

        if (!validationResult.IsValid)
        {
            _logger.LogError("Content stream validation failed after reconstruction:");
            foreach (var error in validationResult.Errors)
            {
                _logger.LogError("  - {Error}", error);
            }
            // Log errors but don't throw - let the PDF be saved and fail at render time if necessary
            // This allows partial success and helps with debugging
        }

        foreach (var warning in validationResult.Warnings)
        {
            _logger.LogWarning("Content stream validation warning: {Warning}", warning);
        }

        return modifiedOperations;
    }

    /// <summary>
    /// Identify text blocks (BT...ET ranges) and mark those containing intersecting text.
    /// Uses actual letter positions from PdfPig (not parsed bounding boxes) for accurate intersection.
    /// </summary>
    private List<TextBlockInfo> IdentifyTextBlocks(
        List<PdfOperation> operations,
        PdfRectangle redactionArea,
        IReadOnlyList<Letter> letters)
    {
        var blocks = new List<TextBlockInfo>();
        var btStack = new Stack<int>();  // Track BT positions (handles nested BT in malformed PDFs)

        for (int i = 0; i < operations.Count; i++)
        {
            var op = operations[i];

            if (op is TextStateOperation tso && tso.Operator == "BT")
            {
                btStack.Push(op.StreamPosition);
            }
            else if (op is TextStateOperation tso2 && tso2.Operator == "ET")
            {
                if (btStack.Count > 0)
                {
                    var btPos = btStack.Pop();

                    // Get all TextOperations in this block
                    var textOpsInBlock = operations
                        .Where(o => o.StreamPosition >= btPos && o.StreamPosition <= op.StreamPosition)
                        .OfType<TextOperation>()
                        .ToList();

                    // Check if any TextOperation has letters intersecting the redaction area
                    // Use actual letter positions from PdfPig, not the parsed bounding boxes
                    bool hasReconstructedText = false;
                    foreach (var textOp in textOpsInBlock)
                    {
                        // Find matching letters for this operation
                        var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

                        // Check if any matched letter's center is in the redaction area
                        foreach (var match in letterMatches)
                        {
                            var rect = match.Letter.GlyphRectangle;
                            double centerX = (rect.Left + rect.Right) / 2.0;
                            double centerY = (rect.Bottom + rect.Top) / 2.0;

                            if (redactionArea.Contains(centerX, centerY))
                            {
                                hasReconstructedText = true;
                                _logger.LogDebug("Letter '{Letter}' at ({X:F2},{Y:F2}) intersects redaction area",
                                    match.Letter.Value, centerX, centerY);
                                break;
                            }
                        }

                        if (hasReconstructedText)
                            break;
                    }

                    blocks.Add(new TextBlockInfo
                    {
                        BtStreamPosition = btPos,
                        EtStreamPosition = op.StreamPosition,
                        HasReconstructedText = hasReconstructedText
                    });

                    _logger.LogDebug("Text block [{Bt},{Et}]: HasReconstructedText={Has}",
                        btPos, op.StreamPosition, hasReconstructedText);
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// Calculate accurate bounding box from PdfPig letter positions.
    /// </summary>
    private PdfRectangle CalculateBoundingBoxFromLetters(List<LetterMatch> letterMatches)
    {
        if (letterMatches.Count == 0)
            return new PdfRectangle();

        double left = double.MaxValue;
        double bottom = double.MaxValue;
        double right = double.MinValue;
        double top = double.MinValue;

        foreach (var match in letterMatches)
        {
            var rect = match.Letter.GlyphRectangle;
            if (rect.Left < left) left = rect.Left;
            if (rect.Bottom < bottom) bottom = rect.Bottom;
            if (rect.Right > right) right = rect.Right;
            if (rect.Top > top) top = rect.Top;
        }

        return new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>
    /// Perform glyph-level redaction on a page and return new content stream bytes.
    /// </summary>
    public byte[] RedactPage(
        string pdfPath,
        int pageNumber,
        List<PdfOperation> operations,
        PdfRectangle redactionArea)
    {
        // Open PDF with PdfPig to get letter positions
        using var document = PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageNumber);
        var letters = page.Letters;

        _logger.LogInformation("Redacting page {Page} with {LetterCount} letters in area ({L},{B})-({R},{T})",
            pageNumber, letters.Count, redactionArea.Left, redactionArea.Bottom, redactionArea.Right, redactionArea.Top);

        // Process operations
        var modifiedOperations = ProcessOperations(operations, letters, redactionArea);

        // Rebuild content stream
        var contentBytes = _contentStreamBuilder.Build(modifiedOperations);

        _logger.LogInformation("Generated new content stream: {Size} bytes", contentBytes.Length);

        return contentBytes;
    }
}

/// <summary>
/// Information about a text block (BT...ET range).
/// </summary>
internal class TextBlockInfo
{
    public required int BtStreamPosition { get; init; }
    public required int EtStreamPosition { get; init; }
    public required bool HasReconstructedText { get; init; }

    public bool Contains(int streamPosition) =>
        streamPosition >= BtStreamPosition && streamPosition <= EtStreamPosition;
}

