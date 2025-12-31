using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Redaction.ContentStream.Building;
using PdfEditor.Redaction.ContentStream.Parsing;
using PdfEditor.Redaction.GlyphLevel;
using PdfSharp.Pdf;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Redaction.ContentStream;

/// <summary>
/// Result of a Form XObject redaction operation.
/// </summary>
public class FormXObjectRedactionResult
{
    /// <summary>
    /// The XObject name (e.g., "/Fm1").
    /// </summary>
    public required string XObjectName { get; init; }

    /// <summary>
    /// The modified content stream bytes for this Form XObject.
    /// </summary>
    public required byte[] ModifiedContentBytes { get; init; }

    /// <summary>
    /// Details of what was redacted in this Form XObject.
    /// </summary>
    public List<RedactionDetail> Details { get; init; } = new();
}

/// <summary>
/// Core redaction engine that operates on PDF content stream bytes.
/// This is the foundation for both file-based and in-memory PdfPage APIs.
/// </summary>
internal class ContentStreamRedactor
{
    private readonly IContentStreamParser _parser;
    private readonly IContentStreamBuilder _builder;
    private readonly GlyphRemover? _glyphRemover;
    private readonly ILogger _logger;

    public ContentStreamRedactor(
        IContentStreamParser parser,
        IContentStreamBuilder builder,
        GlyphRemover? glyphRemover,
        ILogger logger)
    {
        _parser = parser;
        _builder = builder;
        _glyphRemover = glyphRemover;
        _logger = logger;
    }

    /// <summary>
    /// Redact content from a content stream.
    /// This is the core redaction logic extracted from TextRedactor.RedactPageContent().
    /// </summary>
    /// <param name="contentBytes">Original content stream bytes (decompressed).</param>
    /// <param name="pageHeight">Page height in points (for coordinate calculations).</param>
    /// <param name="redactionAreas">Areas to redact in content stream coordinates (for operation matching).</param>
    /// <param name="letters">PdfPig letters for glyph-level redaction (null for whole-operation).</param>
    /// <param name="options">Redaction options.</param>
    /// <param name="visualRedactionAreas">Areas to redact in visual coordinates (for letter matching). If null, uses redactionAreas.</param>
    /// <param name="pageRotation">Page rotation in degrees (0, 90, 180, 270). Default 0.</param>
    /// <param name="mediaBoxWidth">Page MediaBox width in points. Default 612 (US Letter).</param>
    /// <param name="mediaBoxHeight">Page MediaBox height in points. Default 792 (US Letter).</param>
    /// <returns>Modified content stream bytes and list of redaction details.</returns>
    public (byte[] modifiedContent, List<RedactionDetail> details) RedactContentStream(
        byte[] contentBytes,
        double pageHeight,
        List<PdfRectangle> redactionAreas,
        IReadOnlyList<Letter>? letters,
        RedactionOptions options,
        List<PdfRectangle>? visualRedactionAreas = null,
        int pageRotation = 0,
        double mediaBoxWidth = 612,
        double mediaBoxHeight = 792)
    {
        var details = new List<RedactionDetail>();

        if (contentBytes == null || contentBytes.Length == 0)
        {
            _logger.LogDebug("Content stream is empty");
            return (contentBytes ?? Array.Empty<byte>(), details);
        }

        RedactionLogger.LogParseStart(_logger, contentBytes.Length, pageHeight);

        // Parse content stream
        var operations = _parser.Parse(contentBytes, pageHeight);

        // Log redaction areas
        foreach (var area in redactionAreas)
        {
            RedactionLogger.LogRedactionArea(_logger, area, 0);
        }

        // Choose redaction strategy based on options
        byte[] newContentBytes;

        if (options.UseGlyphLevelRedaction && _glyphRemover != null && letters != null)
        {
            _logger.LogInformation("Using glyph-level redaction with {OpCount} operations and {LetterCount} letters",
                operations.Count, letters.Count);

            // Use visual areas for letter matching (if provided), content stream areas for operation matching
            var letterAreas = visualRedactionAreas ?? redactionAreas;

            // Use glyph-level redaction for each area
            var modifiedOps = new List<PdfOperation>(operations);

            for (int i = 0; i < redactionAreas.Count; i++)
            {
                var contentStreamArea = redactionAreas[i];
                var letterArea = letterAreas.Count > i ? letterAreas[i] : contentStreamArea;

                // Process operations with glyph-level redaction
                // letterArea is used for letter position matching (visual coordinates)
                // CRITICAL FIX (Issue #173): Pass rotation info for coordinate transformation
                modifiedOps = _glyphRemover.ProcessOperations(
                    modifiedOps, letters, letterArea, pageRotation, mediaBoxWidth, mediaBoxHeight);

                // Track redacted text (approximate - we don't have exact text anymore)
                // contentStreamArea is used for operation matching
                var textOps = operations.OfType<TextOperation>()
                    .Where(op => op.IntersectsWith(contentStreamArea))
                    .ToList();

                foreach (var textOp in textOps)
                {
                    details.Add(new RedactionDetail
                    {
                        PageNumber = 1,  // Page number must be set by caller
                        RedactedText = textOp.Text,
                        Location = textOp.BoundingBox
                    });
                }
            }

            // Build new content stream from modified operations
            newContentBytes = _builder.Build(modifiedOps);
        }
        else
        {
            _logger.LogInformation("Using whole-operation redaction");

            // Original whole-operation removal logic
            var textOps = operations.OfType<TextOperation>().ToList();
            var opsToRemove = new HashSet<int>();

            for (int i = 0; i < textOps.Count; i++)
            {
                var textOp = textOps[i];
                RedactionLogger.LogTextOperation(_logger, textOp, i);

                foreach (var area in redactionAreas)
                {
                    var intersects = textOp.IntersectsWith(area);
                    RedactionLogger.LogIntersection(_logger, textOp, area, intersects);

                    if (intersects)
                    {
                        opsToRemove.Add(textOp.StreamPosition);
                        details.Add(new RedactionDetail
                        {
                            PageNumber = 1,  // Page number must be set by caller
                            RedactedText = textOp.Text,
                            Location = textOp.BoundingBox
                        });
                        break;
                    }
                }
            }

            RedactionLogger.LogParseComplete(_logger, operations.Count, textOps.Count);

            if (opsToRemove.Count == 0)
            {
                _logger.LogDebug("No operations to redact");
                return (contentBytes, details);
            }

            _logger.LogDebug("Removing {Count} operations from content stream", opsToRemove.Count);

            // Build new content stream with redacted operations
            newContentBytes = _builder.BuildWithRedactions(operations, redactionAreas);
        }

        return (newContentBytes, details);
    }

    /// <summary>
    /// Redact content from a page including Form XObjects.
    /// </summary>
    /// <param name="contentBytes">Original page content stream bytes (decompressed).</param>
    /// <param name="pageHeight">Page height in points (for coordinate calculations).</param>
    /// <param name="redactionAreas">Areas to redact in content stream coordinates (for operation matching).</param>
    /// <param name="letters">PdfPig letters for glyph-level redaction (null for whole-operation).</param>
    /// <param name="options">Redaction options.</param>
    /// <param name="resources">Page resources for Form XObject resolution.</param>
    /// <param name="visualRedactionAreas">Areas to redact in visual coordinates (for letter matching). If null, uses redactionAreas.</param>
    /// <param name="pageRotation">Page rotation in degrees (0, 90, 180, 270). Default 0.</param>
    /// <param name="mediaBoxWidth">Page MediaBox width in points. Default 612 (US Letter).</param>
    /// <param name="mediaBoxHeight">Page MediaBox height in points. Default 792 (US Letter).</param>
    /// <returns>Modified content stream bytes, redaction details, and modified Form XObjects.</returns>
    public (byte[] modifiedContent, List<RedactionDetail> details, List<FormXObjectRedactionResult> formXObjects)
        RedactContentStreamWithFormXObjects(
            byte[] contentBytes,
            double pageHeight,
            List<PdfRectangle> redactionAreas,
            IReadOnlyList<Letter>? letters,
            RedactionOptions options,
            PdfDictionary? resources,
            List<PdfRectangle>? visualRedactionAreas = null,
            int pageRotation = 0,
            double mediaBoxWidth = 612,
            double mediaBoxHeight = 792)
    {
        var formXObjectResults = new List<FormXObjectRedactionResult>();

        if (contentBytes == null || contentBytes.Length == 0)
        {
            _logger.LogDebug("Content stream is empty");
            return (contentBytes ?? Array.Empty<byte>(), new List<RedactionDetail>(), formXObjectResults);
        }

        // Parse with Form XObject support
        var parser = _parser as ContentStreamParser;
        IReadOnlyList<PdfOperation> operations;

        if (parser != null && resources != null)
        {
            operations = parser.ParseWithResources(contentBytes, pageHeight, resources);
        }
        else
        {
            operations = _parser.Parse(contentBytes, pageHeight);
        }

        var allDetails = new List<RedactionDetail>();

        // Process Form XObjects first
        foreach (var op in operations.OfType<FormXObjectOperation>())
        {
            var formResult = RedactFormXObject(op, pageHeight, redactionAreas, letters, options);
            if (formResult != null)
            {
                formXObjectResults.Add(formResult);
                allDetails.AddRange(formResult.Details);
            }
        }

        // Then redact the main content stream
        // Pass visual areas for glyph-level letter matching (if provided)
        // CRITICAL FIX (Issue #173): Pass rotation info for coordinate transformation
        var (mainContent, mainDetails) = RedactContentStream(
            contentBytes, pageHeight, redactionAreas, letters, options, visualRedactionAreas,
            pageRotation, mediaBoxWidth, mediaBoxHeight);
        allDetails.AddRange(mainDetails);

        return (mainContent, allDetails, formXObjectResults);
    }

    /// <summary>
    /// Redact content within a Form XObject.
    /// </summary>
    private FormXObjectRedactionResult? RedactFormXObject(
        FormXObjectOperation formOp,
        double pageHeight,
        List<PdfRectangle> redactionAreas,
        IReadOnlyList<Letter>? letters,
        RedactionOptions options)
    {
        if (formOp.ContentStreamBytes == null || formOp.ContentStreamBytes.Length == 0)
            return null;

        // Check if any nested operations intersect with redaction areas
        var hasIntersection = false;
        foreach (var nestedOp in formOp.NestedOperations)
        {
            foreach (var area in redactionAreas)
            {
                if (nestedOp.IntersectsWith(area))
                {
                    hasIntersection = true;
                    break;
                }
            }
            if (hasIntersection) break;
        }

        if (!hasIntersection)
            return null;

        _logger.LogInformation("Redacting content in Form XObject: {Name}", formOp.XObjectName);

        // Redact the form's content stream
        // Note: Form XObjects use form coordinate space, but we apply the same redaction areas
        // because PdfPig extracts text positions in page coordinates after transformation.
        var (modifiedBytes, details) = RedactContentStream(
            formOp.ContentStreamBytes,
            pageHeight,  // Use page height for coordinate conversion
            redactionAreas,
            letters,  // Letters are already in page coordinates
            options);

        return new FormXObjectRedactionResult
        {
            XObjectName = formOp.XObjectName,
            ModifiedContentBytes = modifiedBytes,
            Details = details
        };
    }
}
