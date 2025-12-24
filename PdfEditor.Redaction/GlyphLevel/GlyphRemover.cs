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
    /// Process text operations to perform glyph-level redaction.
    /// </summary>
    /// <param name="operations">All operations from the page content stream.</param>
    /// <param name="letters">PdfPig letters from the page (for spatial matching).</param>
    /// <param name="redactionArea">Area to redact.</param>
    /// <returns>Modified list of operations with glyphs removed.</returns>
    public List<PdfOperation> ProcessOperations(
        List<PdfOperation> operations,
        IReadOnlyList<Letter> letters,
        PdfRectangle redactionArea)
    {
        var modifiedOperations = new List<PdfOperation>();
        int operationsProcessed = 0;
        int operationsModified = 0;
        int textOpsFound = 0;

        foreach (var operation in operations)
        {
            // Only process text operations
            if (operation is not TextOperation textOp)
            {
                // Keep ALL non-text operations (including TextStateOperations)
                // This preserves the original PDF structure for unmodified text
                modifiedOperations.Add(operation);
                continue;
            }

            textOpsFound++;

            operationsProcessed++;

            // Check if operation intersects with redaction area
            bool intersects = textOp.IntersectsWith(redactionArea);

            if (!intersects)
            {
                // No intersection - keep as-is
                modifiedOperations.Add(operation);
                continue;
            }

            // Operation intersects - perform glyph-level redaction
            _logger.LogDebug("Processing text operation: Text='{Text}', FontName='{Font}', FontSize={Size}",
                textOp.Text.Length > 50 ? textOp.Text.Substring(0, 50) + "..." : textOp.Text,
                textOp.FontName,
                textOp.FontSize);

            var letterMatches = _letterFinder.FindOperationLetters(textOp, letters);

            // Update the text operation's bounding box with REAL positions from PdfPig
            if (letterMatches.Count > 0)
            {
                var realBoundingBox = CalculateBoundingBoxFromLetters(letterMatches);
                // Create a new TextOperation with updated bounding box
                textOp = new TextOperation
                {
                    Operator = textOp.Operator,
                    Operands = textOp.Operands,
                    StreamPosition = textOp.StreamPosition,
                    Text = textOp.Text,
                    Glyphs = textOp.Glyphs,
                    FontName = textOp.FontName,
                    FontSize = textOp.FontSize,
                    BoundingBox = realBoundingBox  // Use REAL bounding box from PdfPig
                };

                _logger.LogDebug("Updated bounding box from ({OldL:F2},{OldB:F2},{OldR:F2},{OldT:F2}) to ({NewL:F2},{NewB:F2},{NewR:F2},{NewT:F2})",
                    operation is TextOperation oldOp ? oldOp.BoundingBox.Left : 0,
                    operation is TextOperation oldOp2 ? oldOp2.BoundingBox.Bottom : 0,
                    operation is TextOperation oldOp3 ? oldOp3.BoundingBox.Right : 0,
                    operation is TextOperation oldOp4 ? oldOp4.BoundingBox.Top : 0,
                    realBoundingBox.Left, realBoundingBox.Bottom, realBoundingBox.Right, realBoundingBox.Top);

                // Re-check intersection with REAL bounding box
                intersects = textOp.IntersectsWith(redactionArea);
                if (!intersects)
                {
                    _logger.LogDebug("After updating bounding box, operation no longer intersects - keeping as-is");
                    modifiedOperations.Add(textOp);
                    continue;
                }
            }

            if (letterMatches.Count == 0)
            {
                // No letter matches - fall back to whole-operation removal
                _logger.LogDebug("No letter matches for operation '{Text}', removing entire operation",
                    textOp.Text.Length > 50 ? textOp.Text.Substring(0, 50) + "..." : textOp.Text);
                // Don't add to modifiedOperations (removes entire operation)
                operationsModified++;
                continue;
            }

            // Build segments
            var segments = _textSegmenter.BuildSegments(textOp, letterMatches, redactionArea);

            if (segments.Count == 0)
            {
                // All text is in redaction area - remove entire operation
                _logger.LogDebug("All segments removed for operation '{Text}'",
                    textOp.Text.Length > 50 ? textOp.Text.Substring(0, 50) + "..." : textOp.Text);
                operationsModified++;
                continue;
            }

            // Reconstruct operations for kept segments
            var reconstructedOps = _operationReconstructor.ReconstructWithPositioning(segments, textOp);

            modifiedOperations.AddRange(reconstructedOps);
            operationsModified++;

            _logger.LogDebug("Redacted operation '{Original}' into {Count} segments",
                textOp.Text.Length > 50 ? textOp.Text.Substring(0, 50) + "..." : textOp.Text,
                segments.Count);
        }

        _logger.LogInformation("Processed {Total} text operations, modified {Modified}",
            operationsProcessed, operationsModified);

        return modifiedOperations;
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
    /// <param name="pdfPath">Path to PDF file.</param>
    /// <param name="pageNumber">Page number (1-indexed).</param>
    /// <param name="operations">Parsed operations from the page.</param>
    /// <param name="redactionArea">Area to redact.</param>
    /// <returns>New content stream bytes with glyphs removed.</returns>
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
