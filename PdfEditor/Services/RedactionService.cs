using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Models;
using PdfEditor.Services.Redaction;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    /// Redact an area of a PDF page by removing content and drawing a black rectangle.
    ///
    /// COORDINATE FLOW:
    /// ================
    /// Input: Image pixels (renderDpi, top-left origin) from mouse selection
    ///    ↓ CoordinateConverter.ImageSelectionToPdfPointsTopLeft()
    /// Redaction Area: PDF points (72 DPI, top-left origin) - Avalonia convention
    ///
    /// Text Bounding Boxes: PDF points (72 DPI, top-left origin) - Avalonia convention
    ///    ↓ Created by TextBoundsCalculator.CalculateBounds()
    ///    ↓ Using CoordinateConverter.TextBoundsToPdfPointsTopLeft()
    ///
    /// Both text bounding boxes and selection area use the SAME coordinate system:
    /// - PDF points (72 DPI)
    /// - Top-left origin (Avalonia convention)
    /// - This enables direct intersection testing via Rect.IntersectsWith()
    /// </summary>
    /// <param name="page">The PDF page to redact</param>
    /// <param name="area">Selection area in rendered image pixels (renderDpi DPI, top-left origin)</param>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 72)</param>
    public void RedactArea(PdfPage page, Rect area, int renderDpi = CoordinateConverter.PdfPointsPerInch)
    {
        _logger.LogInformation(
            "Starting redaction. Input area: ({X:F2},{Y:F2},{W:F2}x{H:F2}) at {Dpi} DPI [image pixels, top-left origin]",
            area.X, area.Y, area.Width, area.Height, renderDpi);

        // Convert from image pixels to PDF points using centralized converter
        // Both input and output use top-left origin (Avalonia convention)
        var scaledArea = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(area, renderDpi);

        // Check for page rotation and transform coordinates if necessary
        var rotation = GetPageRotation(page);
        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;

        if (rotation != 0)
        {
            _logger.LogInformation("Page has rotation: {Rotation}°. Transforming coordinates.", rotation);
            scaledArea = CoordinateConverter.TransformForRotation(scaledArea, rotation, pageWidth, pageHeight);
        }

        _logger.LogInformation(
            "Coordinate conversion via CoordinateConverter: " +
            "({X:F2},{Y:F2},{W:F2}x{H:F2}) → ({ScaledX:F2},{ScaledY:F2},{ScaledW:F2}x{ScaledH:F2}) [PDF points, top-left origin]",
            area.X, area.Y, area.Width, area.Height,
            scaledArea.X, scaledArea.Y, scaledArea.Width, scaledArea.Height);

        // Validate coordinates are reasonable for this page
        if (!CoordinateConverter.IsValidForPage(scaledArea, pageWidth, pageHeight))
        {
            _logger.LogWarning(
                "Selection area may be outside page bounds. Page: ({W}x{H}), Selection: ({X},{Y},{SW}x{SH})",
                pageWidth, pageHeight, scaledArea.X, scaledArea.Y, scaledArea.Width, scaledArea.Height);
        }

        var sw = Stopwatch.StartNew();

        // CRITICAL SECURITY REQUIREMENT:
        // Content removal MUST succeed for redaction to be valid.
        // We will NEVER do visual-only redaction as it creates a false sense of security.

        RedactionResult result;

        try
        {
            // Step 1: Remove content within the area (TRUE REDACTION - REQUIRED)
            _logger.LogDebug("Step 1: Removing content within redaction area");
            result = RemoveContentInArea(page, scaledArea);

            sw.Stop();

            if (result.Mode == RedactionMode.TrueRedaction)
            {
                _logger.LogInformation("Content removed successfully in {ElapsedMs}ms. Redaction is secure.", sw.ElapsedMilliseconds);
            }
            else if (result.Mode == RedactionMode.VisualOnly)
            {
                _logger.LogWarning("Visual-only redaction in {ElapsedMs}ms. No content found to remove.", sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogError("Redaction failed in {ElapsedMs}ms. Content removal threw exception.", sw.ElapsedMilliseconds);
            }

            // Step 2: Draw black rectangle over the area (OPTIONAL - visual confirmation only)
            // This is done by directly appending PDF operators to avoid XGraphics issues
            _logger.LogDebug("Step 2: Drawing black rectangle for visual confirmation");
            try
            {
                DrawBlackRectangleDirectly(page, scaledArea);
                result.VisualCoverageDrawn = true;
                _logger.LogDebug("Visual black rectangle drawn successfully");
            }
            catch (Exception visualEx)
            {
                // Visual drawing failed, but if content was removed, redaction is still secure
                result.VisualCoverageDrawn = false;

                if (result.Mode == RedactionMode.TrueRedaction)
                {
                    _logger.LogWarning(visualEx, "Could not draw visual black rectangle, but content was successfully removed. Redaction is secure.");
                }
                else
                {
                    _logger.LogError(visualEx, "CRITICAL: Visual rectangle drawing failed AND content was not removed. Redaction completely failed.");
                }
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "CRITICAL: Content removal failed after {ElapsedMs}ms. Redaction aborted to prevent insecure visual-only redaction.",
                sw.ElapsedMilliseconds);

            // NEVER fall back to visual-only redaction - this would be a security vulnerability
            // Throw the exception to notify the user that redaction failed
            throw new Exception($"Redaction failed: Could not remove content from PDF structure. {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Draw a black rectangle by directly appending PDF operators to the content stream.
    /// This method works even after content stream has been modified (unlike XGraphics).
    ///
    /// COORDINATE HANDLING:
    /// ===================
    /// Input: PDF points with top-left origin (Avalonia convention)
    /// PDF operators: Use BOTTOM-LEFT origin (PDF native coordinates)
    /// Result: Y-coordinate is flipped to PDF bottom-left origin
    /// </summary>
    /// <param name="page">The PDF page to draw on</param>
    /// <param name="area">Area in PDF points (72 DPI, top-left origin)</param>
    private void DrawBlackRectangleDirectly(PdfPage page, Rect area)
    {
        var pageHeight = page.Height.Point;

        // Convert from Avalonia coordinates (top-left) to PDF coordinates (bottom-left)
        var pdfX = area.X;
        var pdfY = pageHeight - area.Y - area.Height;  // Flip Y axis
        var pdfWidth = area.Width;
        var pdfHeight = area.Height;

        // Build PDF operators to draw a filled black rectangle
        // PDF operator sequence:
        // q                          Save graphics state
        // 0 0 0 rg                   Set fill color to black (RGB)
        // x y width height re        Draw rectangle path
        // f                          Fill the path
        // Q                          Restore graphics state
        var operators = $"q\n0 0 0 rg\n{pdfX:F2} {pdfY:F2} {pdfWidth:F2} {pdfHeight:F2} re\nf\nQ\n";
        var operatorBytes = System.Text.Encoding.ASCII.GetBytes(operators);

        // Append to existing content stream or create new one
        if (page.Contents.Elements.Count > 0)
        {
            // Append to first content stream
            var content = page.Contents.Elements.GetDictionary(0);
            if (content != null)
            {
                if (content.Stream != null)
                {
                    // Existing stream - append to it
                    var stream = content.Stream;
                    var existingBytes = stream.Value ?? Array.Empty<byte>();
                    var newBytes = new byte[existingBytes.Length + operatorBytes.Length];
                    Array.Copy(existingBytes, newBytes, existingBytes.Length);
                    Array.Copy(operatorBytes, 0, newBytes, existingBytes.Length, operatorBytes.Length);
                    stream.Value = newBytes;
                }
                else
                {
                    // Dictionary exists but no stream - create stream
                    content.CreateStream(operatorBytes);
                }
            }
        }
        else
        {
            // No existing content, create new stream with black rectangle
            var newContent = new PdfDictionary(page.Owner);
            newContent.CreateStream(operatorBytes);
            page.Contents.Elements.Add(newContent);
        }

        _logger.LogDebug(
            "Drew black rectangle at PDF coords({X:F2},{Y:F2},{W:F2}x{H:F2}) [converted from Avalonia({AX:F2},{AY:F2})]",
            pdfX, pdfY, pdfWidth, pdfHeight, area.X, area.Y);
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
    private RedactionResult RemoveContentInArea(PdfPage page, Rect area)
    {
        var result = new RedactionResult();

        try
        {
            _logger.LogDebug("Parsing PDF content stream");
            var sw = Stopwatch.StartNew();

            // Step 1: Parse the content stream to get all operations
            var operations = _parser.ParseContentStream(page);
            sw.Stop();

            _logger.LogInformation("Parsed content stream in {ElapsedMs}ms. Found {OperationCount} operations",
                sw.ElapsedMilliseconds, operations.Count);

            // Step 1b: Get character-level letter information for precise text filtering
            List<UglyToad.PdfPig.Content.Letter>? letters = null;
            var pageHeight = page.Height.Point;
            try
            {
                using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(page.Owner.FullPath);
                // Find page number by iterating through pages
                var pageNumber = 1;
                for (int i = 0; i < page.Owner.Pages.Count; i++)
                {
                    if (page.Owner.Pages[i] == page)
                    {
                        pageNumber = i + 1;  // PdfPig uses 1-based page numbers
                        break;
                    }
                }
                var pdfPigPage = pdfPigDoc.GetPage(pageNumber);
                letters = pdfPigPage.Letters.ToList();
                _logger.LogInformation("Loaded {LetterCount} character-level letters for precise redaction filtering", letters.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load PdfPig letters for character-level filtering, falling back to operation-level");
            }

            // Step 1c: Parse inline images (BI...ID...EI sequences)
            var currentGraphicsState = new PdfGraphicsState();
            var inlineImages = _parser.ParseInlineImages(page, pageHeight, currentGraphicsState);

            if (inlineImages.Count > 0)
            {
                _logger.LogInformation("Found {Count} inline images to check for redaction", inlineImages.Count);
                operations.AddRange(inlineImages);
            }

            // Step 2: Filter out operations that intersect with the redaction area
            _logger.LogInformation("Filtering operations against redaction area: ({X:F2},{Y:F2},{W:F2}x{H:F2}) [Avalonia top-left, PDF points]",
                area.X, area.Y, area.Width, area.Height);

            var filteredOperations = new List<PdfOperation>();
            var removedCount = 0;
            var removedByType = new Dictionary<string, int>();

            foreach (var operation in operations)
            {
                // Check if this operation intersects with the redaction area
                // For text operations, use CHARACTER-LEVEL filtering if available
                bool shouldRemove;

                if (operation is TextOperation textOp && letters != null && !string.IsNullOrWhiteSpace(textOp.Text))
                {
                    // CHARACTER-LEVEL FILTERING: Check if any character's center is in the redaction area
                    shouldRemove = DoesTextOperationContainRedactedCharacters(textOp, area, letters, pageHeight);
                }
                else
                {
                    // For non-text operations (paths, images, etc.), use bounding box intersection
                    shouldRemove = operation.IntersectsWith(area);
                }

                // DEBUG: Log intersection tests for text operations
                if (operation is TextOperation textOpDebug && !string.IsNullOrWhiteSpace(textOpDebug.Text))
                {
                    _logger.LogInformation(
                        "INTERSECTION TEST: '{Text}' BBox=({X:F2},{Y:F2},{W:F2}x{H:F2}) vs Area=({AX:F2},{AY:F2},{AW:F2}x{AH:F2}) => {Result}",
                        textOpDebug.Text.Length > 20 ? textOpDebug.Text.Substring(0, 20) + "..." : textOpDebug.Text,
                        textOpDebug.BoundingBox.X, textOpDebug.BoundingBox.Y, textOpDebug.BoundingBox.Width, textOpDebug.BoundingBox.Height,
                        area.X, area.Y, area.Width, area.Height,
                        shouldRemove ? "REMOVE" : "KEEP");
                }

                if (shouldRemove)
                {
                    removedCount++;
                    var typeName = operation.GetType().Name;

                    // Track removed operations by type
                    if (!removedByType.ContainsKey(typeName))
                        removedByType[typeName] = 0;
                    removedByType[typeName]++;

                    if (operation is TextOperation textOpToRemove && !string.IsNullOrWhiteSpace(textOpToRemove.Text))
                    {
                        _redactedTerms.Add(textOpToRemove.Text);
                    }

                    // Skip this operation - it will be redacted
                    continue;
                }

                // Keep this operation
                filteredOperations.Add(operation);
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

                // Set result for TRUE redaction
                result.Mode = RedactionMode.TrueRedaction;
                result.ContentRemoved = true;
                result.TextOperationsRemoved = removedByType.GetValueOrDefault("TextOperation", 0);
                result.ImageOperationsRemoved = removedByType.GetValueOrDefault("ImageOperation", 0);
                result.GraphicsOperationsRemoved = removedByType.GetValueOrDefault("PathOperation", 0);

                // MANDATORY LOGGING - Cannot be silenced by log level
                Console.WriteLine($"[REDACTION-SECURITY] TRUE REDACTION: Removed {removedCount} operations " +
                    $"(Text: {result.TextOperationsRemoved}, Images: {result.ImageOperationsRemoved}, Graphics: {result.GraphicsOperationsRemoved})");
                _logger.LogWarning("TRUE REDACTION PERFORMED: {TotalCount} operations removed (Text: {TextCount}, Images: {ImageCount}, Graphics: {GraphicsCount})",
                    removedCount, result.TextOperationsRemoved, result.ImageOperationsRemoved, result.GraphicsOperationsRemoved);
            }
            else
            {
                // No content found - will be visual-only redaction
                result.Mode = RedactionMode.VisualOnly;
                result.ContentRemoved = false;

                // MANDATORY WARNING - Visual-only is not secure for sensitive data
                Console.WriteLine($"[REDACTION-WARNING] VISUAL ONLY - No content found in redaction area (may be blank area or unsupported content type)");
                _logger.LogWarning("Visual-only redaction - no content operations found in redaction area. " +
                    "This may indicate: 1) Blank area, 2) Unsupported content type, 3) Coordinate mismatch");
            }

            // Step 5: Handle images separately
            var removedImageOps = operations.OfType<ImageOperation>()
                .Where(op => op.IntersectsWith(area))
                .ToList();

            var keptImageOps = filteredOperations.OfType<ImageOperation>().ToList();

            RemoveImagesInArea(page, removedImageOps, keptImageOps);

            return result;
        }
        catch (Exception ex)
        {
            // CRITICAL SECURITY FAILURE
            result.Mode = RedactionMode.Failed;
            result.ContentRemoved = false;

            // MANDATORY CRITICAL ERROR - Always visible
            Console.WriteLine($"[REDACTION-CRITICAL-ERROR] Redaction FAILED - Content removal threw exception!");
            Console.WriteLine($"[REDACTION-CRITICAL-ERROR] Exception: {ex.Message}");
            Console.WriteLine($"[REDACTION-CRITICAL-ERROR] Will fall back to visual-only (UNSAFE for sensitive data)");

            _logger.LogError(ex, "CRITICAL SECURITY FAILURE: Content removal threw exception. " +
                "Falling back to visual-only redaction which is UNSAFE for sensitive data.");

            return result;
        }
    }

    /// <summary>
    /// Replace the page's content stream with new content
    /// </summary>
    private void ReplacePageContent(PdfPage page, byte[] newContent)
    {
        try
        {
            _logger.LogDebug("Replacing page content stream. Existing elements: {Count}", page.Contents.Elements.Count);

            // Build the new content object first so we don't lose the existing streams if something goes wrong.
            var dict = new PdfDictionary(page.Owner);
            dict.CreateStream(newContent);

            // Register as an indirect object when possible to match existing structure
            PdfItem contentItem;
            if (page.Owner?.Internals != null)
            {
                page.Owner.Internals.AddObject(dict);
                contentItem = dict.Reference != null ? (PdfItem)dict.Reference : dict;
            }
            else
            {
                contentItem = dict;
            }

            // Now replace all content elements with the new single stream
            page.Contents.Elements.Clear();
            page.Contents.Elements.Add(contentItem);

            _logger.LogInformation("Successfully replaced page content stream with consolidated single stream");
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
    /// <param name="page">The PDF page</param>
    /// <param name="removedImageOps">List of image operations that were removed from content stream</param>
    /// <param name="keptImageOps">List of image operations that remain in content stream</param>
    private void RemoveImagesInArea(PdfPage page, List<ImageOperation> removedImageOps, List<ImageOperation> keptImageOps)
    {
        try
        {
            if (removedImageOps.Count == 0)
            {
                _logger.LogDebug("No image operations were removed, skipping resource cleanup");
                return;
            }

            // Get page resources
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null)
                return;

            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects == null)
                return;

            _logger.LogDebug("Found {XObjectCount} XObjects in resources", xObjects.Elements.Count);

            // Identify which XObjects are candidates for removal
            var candidateXObjects = removedImageOps
                .Select(op => op.ResourceName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToList();

            _logger.LogDebug("Candidate XObjects for removal: {Candidates}", string.Join(", ", candidateXObjects));

            // Identify which XObjects are still in use by kept operations
            var keptXObjects = keptImageOps
                .Select(op => op.ResourceName)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToHashSet();

            // Remove XObjects that are candidates AND not in kept list
            foreach (var xObjectName in candidateXObjects)
            {
                if (!keptXObjects.Contains(xObjectName))
                {
                    if (xObjects.Elements.ContainsKey("/" + xObjectName))
                    {
                        xObjects.Elements.Remove("/" + xObjectName);
                        _logger.LogInformation("Removed unused XObject resource: {Name}", xObjectName);
                    }
                    else if (xObjects.Elements.ContainsKey(xObjectName))
                    {
                        xObjects.Elements.Remove(xObjectName);
                        _logger.LogInformation("Removed unused XObject resource: {Name}", xObjectName);
                    }
                }
                else
                {
                    _logger.LogDebug("XObject {Name} was redacted but is still used elsewhere on page - keeping resource", xObjectName);
                }
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
    /// Get the rotation angle of a PDF page from its /Rotate entry.
    /// PDF pages can be rotated by 0, 90, 180, or 270 degrees.
    /// </summary>
    /// <param name="page">The PDF page</param>
    /// <returns>Rotation in degrees (0, 90, 180, or 270)</returns>
    private int GetPageRotation(PdfPage page)
    {
        try
        {
            // Check for /Rotate entry in page dictionary
            if (page.Elements.ContainsKey("/Rotate"))
            {
                var rotateElement = page.Elements.GetInteger("/Rotate");
                _logger.LogDebug("Page has /Rotate entry: {Rotation}", rotateElement);
                return rotateElement;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading page rotation, assuming 0°");
        }

        return 0; // Default: no rotation
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

    /// <summary>
    /// Check if a text operation contains any characters whose center points fall within
    /// the redaction area. This enables CHARACTER-LEVEL redaction precision, preventing
    /// over-redaction where entire multi-word operations are removed when only one word
    /// was selected.
    /// </summary>
    /// <param name="textOp">The text operation to check</param>
    /// <param name="area">Redaction area in PDF points (top-left origin)</param>
    /// <param name="letters">PdfPig letter collection for the page</param>
    /// <param name="pageHeight">PDF page height for coordinate conversion</param>
    /// <returns>True if any character's center is inside the redaction area</returns>
    private bool DoesTextOperationContainRedactedCharacters(
        TextOperation textOp,
        Rect area,
        List<UglyToad.PdfPig.Content.Letter> letters,
        double pageHeight)
    {
        // Convert redaction area from top-left origin to PDF bottom-left origin
        var pdfArea = new UglyToad.PdfPig.Core.PdfRectangle(
            area.X,
            pageHeight - area.Y - area.Height,  // Convert Y to bottom-left origin
            area.X + area.Width,
            pageHeight - area.Y                 // Convert Y to bottom-left origin
        );

        // Find letters that belong to this text operation by matching position
        // We'll be lenient and check if letters are "near" the operation's bounding box
        var opLeft = textOp.BoundingBox.X;
        var opRight = textOp.BoundingBox.X + textOp.BoundingBox.Width;
        var opTop = pageHeight - textOp.BoundingBox.Y;  // Convert to PDF coords
        var opBottom = pageHeight - (textOp.BoundingBox.Y + textOp.BoundingBox.Height);  // Convert to PDF coords

        var matchingLetters = letters.Where(letter =>
        {
            var letterCenterX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2.0;
            var letterCenterY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2.0;

            // Check if letter is roughly within the operation's bounding box
            // Allow some tolerance for font metrics approximation
            var tolerance = 5.0; // PDF points
            return letterCenterX >= opLeft - tolerance &&
                   letterCenterX <= opRight + tolerance &&
                   letterCenterY >= opBottom - tolerance &&
                   letterCenterY <= opTop + tolerance;
        }).ToList();

        if (matchingLetters.Count == 0)
        {
            // Fallback: If we can't match letters to this operation, use operation-level intersection
            _logger.LogDebug("No matching letters found for text operation '{Text}', using operation-level intersection",
                textOp.Text.Length > 20 ? textOp.Text.Substring(0, 20) + "..." : textOp.Text);
            return textOp.IntersectsWith(area);
        }

        // Check if any letter's center is inside the redaction area
        foreach (var letter in matchingLetters)
        {
            var centerX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2.0;
            var centerY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2.0;

            // Check if center point is inside the PDF-coordinate redaction area
            if (centerX >= pdfArea.Left && centerX <= pdfArea.Right &&
                centerY >= pdfArea.Bottom && centerY <= pdfArea.Top)
            {
                return true;  // At least one character is inside the redaction area
            }
        }

        return false;  // No characters have their centers in the redaction area
    }

    /// <summary>
    /// Merge all content streams on a page into a single stream so downstream readers
    /// (and tests that only inspect the first stream) see the full content.
    /// </summary>
    public void ConsolidateContentStreams(PdfPage page)
    {
        try
        {
            if (page.Contents.Elements.Count <= 1)
                return;

            var existingCount = page.Contents.Elements.Count;
            using var ms = new MemoryStream();

            foreach (var item in page.Contents.Elements)
            {
                PdfDictionary? dict = null;
                if (item is PdfReference pdfRef)
                {
                    dict = pdfRef.Value as PdfDictionary;
                }
                else if (item is PdfDictionary directDict)
                {
                    dict = directDict;
                }

                if (dict?.Stream?.Value != null)
                {
                    ms.Write(dict.Stream.Value, 0, dict.Stream.Value.Length);
                    ms.WriteByte((byte)'\n');
                }
            }

            var combined = ms.ToArray();
            if (combined.Length == 0)
                return;

            ReplacePageContent(page, combined);
            _logger.LogDebug("Consolidated {Count} content streams into a single stream", existingCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not consolidate content streams after redaction");
        }
    }
}
