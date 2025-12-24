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
    public List<PdfOperation> ProcessOperations(
        List<PdfOperation> operations,
        IReadOnlyList<Letter> letters,
        PdfRectangle redactionArea)
    {
        _logger.LogInformation("=== STARTING OPERATION PROCESSING ===");
        _logger.LogInformation("Total operations: {Count}, Redaction area: ({L:F2},{B:F2})-({R:F2},{T:F2})",
            operations.Count, redactionArea.Left, redactionArea.Bottom, redactionArea.Right, redactionArea.Top);

        // PHASE 1: Identify text blocks and mark those with reconstructed text
        var textBlocks = IdentifyTextBlocks(operations, redactionArea);
        _logger.LogInformation("Identified {Count} text blocks, {ReconstructedCount} will be reconstructed",
            textBlocks.Count, textBlocks.Count(b => b.HasReconstructedText));

        // PHASE 2: Build output, filtering operations from reconstructed blocks
        var modifiedOperations = new List<PdfOperation>();
        int textOpsFound = 0;
        int operationsReconstructed = 0;
        int operationsSkippedFromReconstructedBlocks = 0;
        int operationsKeptAsIs = 0;

        foreach (var operation in operations)
        {
            // Find which text block (if any) contains this operation
            var containingBlock = textBlocks.FirstOrDefault(b => b.Contains(operation.StreamPosition));

            if (containingBlock != null && containingBlock.HasReconstructedText)
            {
                // This operation is inside a text block that will be reconstructed
                // We must reconstruct ALL TextOperations in this block, not just intersecting ones
                if (operation is TextOperation textOp)
                {
                    // Reconstruct this text operation
                    textOpsFound++;
                    var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

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
                        var reconstructed = _operationReconstructor.ReconstructWithPositioning(segments, textOp);
                        modifiedOperations.AddRange(reconstructed);
                        operationsReconstructed++;
                        _logger.LogInformation("Reconstructed TextOp '{Text}' into {Count} segments",
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
                    // Skip - this operation is part of a reconstructed block but not a TextOperation
                    // (Could be BT, ET, Tf, Tm, Tc, Tw, etc.)
                    operationsSkippedFromReconstructedBlocks++;
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

        return modifiedOperations;
    }

    /// <summary>
    /// Identify text blocks (BT...ET ranges) and mark those containing intersecting text.
    /// </summary>
    private List<TextBlockInfo> IdentifyTextBlocks(List<PdfOperation> operations, PdfRectangle redactionArea)
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

                    // Check if any TextOperation in this range intersects
                    var hasReconstructedText = operations
                        .Where(o => o.StreamPosition >= btPos && o.StreamPosition <= op.StreamPosition)
                        .OfType<TextOperation>()
                        .Any(to => to.IntersectsWith(redactionArea));

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
