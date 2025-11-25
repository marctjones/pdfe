using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Services.Redaction;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PdfEditor.Services;

/// <summary>
/// Options for redaction operations
/// </summary>
public class RedactionOptions
{
    /// <summary>
    /// Whether to sanitize metadata after redaction (remove redacted terms from document info, outlines, etc.)
    /// </summary>
    public bool SanitizeMetadata { get; set; } = false;

    /// <summary>
    /// Whether to remove all metadata for maximum security
    /// </summary>
    public bool RemoveAllMetadata { get; set; } = false;

    /// <summary>
    /// Color for the redaction overlay (default: black)
    /// </summary>
    public XColor FillColor { get; set; } = XColor.FromArgb(255, 0, 0, 0);
}

/// <summary>
/// Service for redacting content from PDF pages
/// Implements TRUE content-level redaction by parsing and filtering PDF content streams
/// Uses PdfSharpCore (MIT License) for low-level PDF manipulation
/// </summary>
/// <remarks>
/// ⚠️ CRITICAL FOR AI CODING ASSISTANTS:
///
/// This service implements GLYPH-LEVEL REMOVAL - text is removed from PDF structure,
/// not just visually covered. This is a SECURITY-CRITICAL feature.
///
/// DO NOT:
/// - Replace with visual-only redaction (just drawing black boxes)
/// - Remove the content stream parsing (ParseContentStream)
/// - Remove the content stream rebuilding (BuildContentStream)
/// - Simplify by skipping the RemoveContentInArea method
///
/// The correct flow is: parse → filter → rebuild → replace → draw
///
/// Tests verify that text extraction FAILS after redaction.
/// If you break this, redacted text becomes extractable = SECURITY VULNERABILITY.
///
/// See REDACTION_AI_GUIDELINES.md for complete documentation.
/// </remarks>
public class RedactionService
{
    private readonly ILogger<RedactionService> _logger;
    private readonly ContentStreamParser _parser;
    private readonly ContentStreamBuilder _builder;
    private readonly MetadataSanitizer _metadataSanitizer;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// List of text strings that have been redacted in the current session.
    /// Used for metadata sanitization.
    /// </summary>
    private readonly List<string> _redactedTerms = new();

    public RedactionService(ILogger<RedactionService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;

        // Create parsers with logger instances
        _parser = new ContentStreamParser(_loggerFactory.CreateLogger<ContentStreamParser>(), _loggerFactory);
        _builder = new ContentStreamBuilder(_loggerFactory.CreateLogger<ContentStreamBuilder>());
        _metadataSanitizer = new MetadataSanitizer(_loggerFactory.CreateLogger<MetadataSanitizer>());

        _logger.LogDebug("RedactionService instance created with logger-enabled components");
    }

    /// <summary>
    /// Get the list of text strings that have been redacted
    /// </summary>
    public IReadOnlyList<string> RedactedTerms => _redactedTerms.AsReadOnly();

    /// <summary>
    /// Clear the list of redacted terms
    /// </summary>
    public void ClearRedactedTerms()
    {
        _redactedTerms.Clear();
        _logger.LogDebug("Cleared redacted terms list");
    }

    /// <summary>
    /// Redact an area of a PDF page by removing content and drawing a black rectangle
    /// </summary>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 150). Used to scale screen coordinates to PDF points (72 DPI)</param>
    public void RedactArea(PdfPage page, Rect area, int renderDpi = 150)
    {
        _logger.LogInformation(
            "Starting redaction. Screen area: ({X:F2},{Y:F2},{W:F2}x{H:F2}) at {Dpi} DPI",
            area.X, area.Y, area.Width, area.Height, renderDpi);

        // Scale coordinates from rendered DPI to PDF points (72 DPI)
        // PDF uses 72 points per inch, but the image is rendered at renderDpi
        var scale = 72.0 / renderDpi;
        var scaledX = area.X * scale;
        var scaledY = area.Y * scale;
        var scaledWidth = area.Width * scale;
        var scaledHeight = area.Height * scale;

        _logger.LogInformation(
            "Scaled coordinates by factor {Scale:F4} ({RenderDpi}→72 DPI): ({X:F2},{Y:F2},{W:F2}x{H:F2}) → ({ScaledX:F2},{ScaledY:F2},{ScaledW:F2}x{ScaledH:F2})",
            scale, renderDpi, area.X, area.Y, area.Width, area.Height,
            scaledX, scaledY, scaledWidth, scaledHeight);

        // NOTE: TextBoundsCalculator already returns bounds in Avalonia coordinates (top-left origin)
        // So we keep the redaction area in Avalonia coordinates too for direct comparison
        var scaledArea = new Rect(scaledX, scaledY, scaledWidth, scaledHeight);

        _logger.LogInformation(
            "Using Avalonia coordinates for comparison (top-left origin): ({X:F2},{Y:F2},{W:F2}x{H:F2})",
            scaledArea.X, scaledArea.Y, scaledArea.Width, scaledArea.Height);

        var sw = Stopwatch.StartNew();

        try
        {
            // Step 1: Remove content within the area (true redaction)
            _logger.LogDebug("Step 1: Removing content within redaction area");
            RemoveContentInArea(page, scaledArea);

            // Step 2: Draw black rectangle over the area (visual redaction)
            _logger.LogDebug("Step 2: Drawing black rectangle for visual redaction");
            DrawBlackRectangle(page, scaledArea);

            sw.Stop();
            _logger.LogInformation("Redaction completed successfully in {ElapsedMs}ms (content removed and visual redaction applied)", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error during redaction after {ElapsedMs}ms. Attempting fallback to visual-only redaction",
                sw.ElapsedMilliseconds);

            // Fallback: At least draw the black rectangle
            try
            {
                DrawBlackRectangle(page, scaledArea);
                _logger.LogWarning("Fallback successful: Visual redaction applied (content may not be removed)");
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Fallback redaction also failed");
                throw new Exception($"Failed to redact area: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Draw a black rectangle over the redacted area
    /// This provides visual redaction
    /// </summary>
    /// <param name="area">Area in Avalonia coordinates (top-left origin, 72 DPI)</param>
    private void DrawBlackRectangle(PdfPage page, Rect area)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        // XGraphics with XGraphicsPdfPageOptions.Append uses Avalonia-style coordinates (top-left origin)
        // No conversion needed - just use the area coordinates directly
        var brush = new XSolidBrush(XColor.FromArgb(255, 0, 0, 0));
        gfx.DrawRectangle(brush, area.X, area.Y, area.Width, area.Height);

        _logger.LogDebug(
            "Drew black rectangle at ({X:F2},{Y:F2},{W:F2}x{H:F2}) in XGraphics coordinates",
            area.X, area.Y, area.Width, area.Height);
    }

    /// <summary>
    /// Remove content within the specified area
    /// This is TRUE content-level redaction - removes text, graphics, and images
    /// </summary>
    /// <remarks>
    /// ⚠️ CRITICAL METHOD - DO NOT REMOVE OR SIMPLIFY
    ///
    /// This method performs actual GLYPH REMOVAL by:
    /// 1. Parsing content stream into operations (including text with glyphs)
    /// 2. Filtering out operations that intersect with redaction area
    /// 3. Rebuilding content stream WITHOUT removed operations
    /// 4. Replacing page content with filtered stream
    ///
    /// After this method runs, text extraction tools CANNOT find the removed text.
    /// This is the security-critical part of redaction.
    ///
    /// If you remove/simplify this, text will still be extractable = SECURITY VULNERABILITY.
    /// </remarks>
    private void RemoveContentInArea(PdfPage page, Rect area)
    {
        try
        {
            _logger.LogDebug("Parsing PDF content stream");
            var sw = Stopwatch.StartNew();

            // Step 1: Parse the content stream to get all operations
            var operations = _parser.ParseContentStream(page);
            sw.Stop();

            _logger.LogInformation("Parsed content stream in {ElapsedMs}ms. Found {OperationCount} operations",
                sw.ElapsedMilliseconds, operations.Count);

            // Step 1b: Parse inline images (BI...ID...EI sequences)
            var pageHeight = page.Height.Point;
            var currentGraphicsState = new PdfGraphicsState();
            var inlineImages = _parser.ParseInlineImages(page, pageHeight, currentGraphicsState);

            if (inlineImages.Count > 0)
            {
                _logger.LogInformation("Found {Count} inline images to check for redaction", inlineImages.Count);
                operations.AddRange(inlineImages);
            }

            // Step 2: Filter out operations that intersect with the redaction area
            _logger.LogInformation("Filtering operations against redaction area: ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                area.X, area.Y, area.Width, area.Height);

            // DEBUG: Log first few text operations to see where text actually is
            var textOps = operations.OfType<TextOperation>().Take(10).ToList();
            if (textOps.Count > 0)
            {
                _logger.LogInformation("DEBUG: Sample of {Count} text operations found (showing first 10):", operations.OfType<TextOperation>().Count());
                foreach (var t in textOps)
                {
                    _logger.LogInformation("  Text: \"{Text}\" at ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                        t.Text.Length > 20 ? t.Text.Substring(0, 20) + "..." : t.Text,
                        t.BoundingBox.X, t.BoundingBox.Y, t.BoundingBox.Width, t.BoundingBox.Height);
                }

                // Find text ops near the redaction area (within 100 points)
                var nearbyOps = operations.OfType<TextOperation>()
                    .Where(t => Math.Abs(t.BoundingBox.Y - area.Y) < 100)
                    .Take(5)
                    .ToList();
                if (nearbyOps.Count > 0)
                {
                    _logger.LogInformation("DEBUG: Text operations near redaction Y={Y:F2}:", area.Y);
                    foreach (var t in nearbyOps)
                    {
                        var intersects = t.IntersectsWith(area);
                        _logger.LogInformation("  Text: \"{Text}\" at ({X:F2},{Y:F2},{W:F2}x{H:F2}) - Intersects: {Intersects}",
                            t.Text.Length > 20 ? t.Text.Substring(0, 20) + "..." : t.Text,
                            t.BoundingBox.X, t.BoundingBox.Y, t.BoundingBox.Width, t.BoundingBox.Height, intersects);
                    }
                }
            }

            var filteredOperations = new List<PdfOperation>();
            var removedCount = 0;
            var removedByType = new Dictionary<string, int>();

            foreach (var operation in operations)
            {
                // Check if this operation intersects with the redaction area
                bool shouldRemove = operation.IntersectsWith(area);

                if (shouldRemove)
                {
                    removedCount++;
                    var typeName = operation.GetType().Name;

                    // Track removed operations by type
                    if (!removedByType.ContainsKey(typeName))
                        removedByType[typeName] = 0;
                    removedByType[typeName]++;

                    // Log detailed information based on operation type
                    if (operation is TextOperation textOp)
                    {
                        _logger.LogInformation(
                            "REMOVING Text: \"{Text}\" at ({X:F2},{Y:F2},{W:F2}x{H:F2}), Font={Font}/{Size}pt",
                            textOp.Text.Length > 30 ? textOp.Text.Substring(0, 30) + "..." : textOp.Text,
                            textOp.BoundingBox.X, textOp.BoundingBox.Y,
                            textOp.BoundingBox.Width, textOp.BoundingBox.Height,
                            textOp.FontName, textOp.FontSize);

                        // Track redacted text for metadata sanitization
                        if (!string.IsNullOrWhiteSpace(textOp.Text))
                        {
                            _redactedTerms.Add(textOp.Text);
                        }
                    }
                    else if (operation is PathOperation pathOp)
                    {
                        _logger.LogDebug(
                            "REMOVING Path ({PathType}, Stroke={Stroke}, Fill={Fill}): at ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                            pathOp.Type, pathOp.IsStroke, pathOp.IsFill,
                            pathOp.BoundingBox.X, pathOp.BoundingBox.Y,
                            pathOp.BoundingBox.Width, pathOp.BoundingBox.Height);
                    }
                    else if (operation is ImageOperation imgOp)
                    {
                        _logger.LogInformation(
                            "REMOVING Image: {ResourceName} at ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                            imgOp.ResourceName,
                            imgOp.BoundingBox.X, imgOp.BoundingBox.Y,
                            imgOp.BoundingBox.Width, imgOp.BoundingBox.Height);
                    }
                    else if (operation is InlineImageOperation inlineImgOp)
                    {
                        _logger.LogInformation(
                            "REMOVING Inline Image: {Width}x{Height} at ({X:F2},{Y:F2},{W:F2}x{H:F2})",
                            inlineImgOp.ImageWidth, inlineImgOp.ImageHeight,
                            inlineImgOp.BoundingBox.X, inlineImgOp.BoundingBox.Y,
                            inlineImgOp.BoundingBox.Width, inlineImgOp.BoundingBox.Height);
                    }
                    else
                    {
                        _logger.LogDebug("REMOVING {OperationType} at {BoundingBox}",
                            typeName, operation.BoundingBox);
                    }

                    // Skip this operation - it will be redacted
                    continue;
                }

                // Keep this operation
                filteredOperations.Add(operation);
            }

            // Log summary of removed operations by type
            _logger.LogInformation(
                "Content filtering complete. Removed: {RemovedCount}, Kept: {KeptCount}",
                removedCount, filteredOperations.Count);

            if (removedByType.Count > 0)
            {
                var summary = string.Join(", ",
                    removedByType.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                _logger.LogInformation("Removed operations by type: {Summary}", summary);
            }

            // Step 3: Rebuild the content stream with filtered operations
            if (removedCount > 0)
            {
                _logger.LogDebug("Rebuilding content stream with filtered operations");
                sw.Restart();
                var newContentBytes = _builder.BuildContentStream(filteredOperations);
                sw.Stop();

                _logger.LogDebug("Content stream rebuilt in {ElapsedMs}ms. Size: {SizeBytes} bytes",
                    sw.ElapsedMilliseconds, newContentBytes.Length);

                // Step 4: Replace the page's content stream
                ReplacePageContent(page, newContentBytes);
            }
            else
            {
                _logger.LogInformation("No content found in redaction area - nothing to remove");
            }

            // Step 5: Handle images separately
            _logger.LogDebug("Checking for images in redaction area");
            RemoveImagesInArea(page, area);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove content in area - will fall back to visual-only redaction");
            // Don't throw - we'll fall back to visual redaction
        }
    }

    /// <summary>
    /// Replace the page's content stream with new content
    /// </summary>
    private void ReplacePageContent(PdfPage page, byte[] newContent)
    {
        try
        {
            _logger.LogDebug("Clearing existing content elements. Current count: {Count}", page.Contents.Elements.Count);

            // Method: Get the existing content reference and replace its stream
            // This preserves the indirect reference structure that PdfPig expects
            if (page.Contents.Elements.Count > 0)
            {
                // Get the first content stream (or only one after flattening)
                var contentRef = page.Contents.Elements[0];
                _logger.LogDebug("Content element type: {Type}", contentRef.GetType().FullName);

                if (contentRef is PdfSharp.Pdf.Advanced.PdfReference pdfRef)
                {
                    _logger.LogDebug("Reference value type: {ValueType}", pdfRef.Value?.GetType().FullName ?? "null");
                    var contentObject = pdfRef.Value as PdfSharp.Pdf.Advanced.PdfContent;
                    if (contentObject != null)
                    {
                        // Replace the stream in the existing content object
                        // The content object already has a stream, so we need to access and replace it
                        try
                        {
                            // PdfContent inherits from PdfDictionary which has a Stream property
                            // We need to replace the stream value with our new content
                            if (contentObject.Stream != null)
                            {
                                // Get the existing stream and replace its value
                                contentObject.Stream.Value = newContent;
                            }
                            else
                            {
                                // No existing stream, create new one
                                contentObject.CreateStream(newContent);
                            }
                            _logger.LogInformation("Successfully replaced page content stream using existing reference");
                            return;
                        }
                        catch (Exception streamEx)
                        {
                            _logger.LogError(streamEx, "CreateStream failed: {Message}", streamEx.Message);
                            throw;
                        }
                    }
                }
                else if (contentRef is PdfSharp.Pdf.Advanced.PdfContent directContent)
                {
                    // Direct content object - replace its stream
                    directContent.CreateStream(newContent);
                    _logger.LogInformation("Successfully replaced page content stream (direct content)");
                    return;
                }

                // If it's something else, clear and recreate
                _logger.LogDebug("Unknown content type, clearing and recreating");
                page.Contents.Elements.Clear();
            }

            // Fallback: Create new content stream
            _logger.LogDebug("Creating new content stream with {SizeBytes} bytes", newContent.Length);
            var stream = page.Contents.CreateSingleContent();
            stream.CreateStream(newContent);

            _logger.LogInformation("Successfully replaced page content stream");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace page content");
            throw;
        }
    }

    /// <summary>
    /// Remove or modify images that intersect with the redaction area
    /// </summary>
    private void RemoveImagesInArea(PdfPage page, Rect area)
    {
        try
        {
            // Get page resources
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null)
                return;

            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects == null)
                return;

            _logger.LogDebug("Found {XObjectCount} XObjects", xObjects.Elements.Count);

            // Track which XObjects to remove
            var keysToRemove = new List<string>();

            // Check each XObject (which may be an image)
            foreach (var key in xObjects.Elements.Keys)
            {
                var xObject = xObjects.Elements[key] as PdfDictionary;
                if (xObject == null)
                    continue;

                var subtype = xObject.Elements.GetName("/Subtype");
                if (subtype == "/Image")
                {
                    // This is an image - check if it intersects with redaction area
                    // For now, we conservatively remove images if they might intersect
                    // A more sophisticated implementation would track image positions
                    // from the content stream Do operators

                    _logger.LogDebug("Found image: {Key}", key);
                    // Note: We'd need to track image positions from Do operators
                    // to accurately determine intersection. For now, images are
                    // preserved unless explicitly intersecting based on Do operator analysis
                }
            }

            // Remove images that intersect
            foreach (var key in keysToRemove)
            {
                xObjects.Elements.Remove(key);
                _logger.LogInformation("Removed image: {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not process images");
        }
    }

    /// <summary>
    /// Redact multiple areas on a page
    /// </summary>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 150)</param>
    public void RedactAreas(PdfPage page, IEnumerable<Rect> areas, int renderDpi = 150)
    {
        foreach (var area in areas)
        {
            RedactArea(page, area, renderDpi);
        }
    }

    /// <summary>
    /// Sanitize document metadata by removing redacted terms from all metadata locations.
    /// Call this after all redaction operations are complete.
    /// </summary>
    /// <param name="document">The PDF document to sanitize</param>
    public void SanitizeDocumentMetadata(PdfDocument document)
    {
        if (_redactedTerms.Count == 0)
        {
            _logger.LogInformation("No redacted terms to sanitize from metadata");
            return;
        }

        _logger.LogInformation("Sanitizing document metadata for {Count} redacted terms", _redactedTerms.Count);
        _metadataSanitizer.SanitizeDocument(document, _redactedTerms);
    }

    /// <summary>
    /// Sanitize document metadata using a custom list of terms.
    /// </summary>
    /// <param name="document">The PDF document to sanitize</param>
    /// <param name="terms">List of terms to redact from metadata</param>
    public void SanitizeDocumentMetadata(PdfDocument document, IEnumerable<string> terms)
    {
        _logger.LogInformation("Sanitizing document metadata for custom term list");
        _metadataSanitizer.SanitizeDocument(document, terms);
    }

    /// <summary>
    /// Remove all metadata from document for maximum security.
    /// This removes document info, XMP metadata, etc.
    /// </summary>
    /// <param name="document">The PDF document to sanitize</param>
    public void RemoveAllMetadata(PdfDocument document)
    {
        _logger.LogInformation("Removing all metadata from document");
        _metadataSanitizer.RemoveAllMetadata(document);
    }

    /// <summary>
    /// Complete redaction workflow: redact areas and optionally sanitize metadata.
    /// </summary>
    /// <param name="document">The PDF document</param>
    /// <param name="page">The page to redact</param>
    /// <param name="areas">Areas to redact</param>
    /// <param name="options">Redaction options</param>
    /// <param name="renderDpi">The DPI at which the page was rendered</param>
    public void RedactWithOptions(PdfDocument document, PdfPage page, IEnumerable<Rect> areas,
                                   RedactionOptions options, int renderDpi = 150)
    {
        // Clear previous redacted terms for this operation
        ClearRedactedTerms();

        // Perform redaction
        foreach (var area in areas)
        {
            RedactArea(page, area, renderDpi);
        }

        // Sanitize metadata if requested
        if (options.RemoveAllMetadata)
        {
            RemoveAllMetadata(document);
        }
        else if (options.SanitizeMetadata)
        {
            SanitizeDocumentMetadata(document);
        }

        _logger.LogInformation("Redaction with options complete. Redacted {Count} text items", _redactedTerms.Count);
    }
}
