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
using PdfEditor.Redaction;

namespace PdfEditor.Services;

/// <summary>
/// Options for redaction operations
/// </summary>
public class RedactionOptions
{
    /// <summary>
    /// Whether to sanitize metadata after redaction (remove redacted terms from document info, outlines, etc.)
    /// Default: true (security best practice - prevents redacted text leaking via metadata)
    /// See issue #150: Metadata may contain redacted text - security concern
    /// </summary>
    public bool SanitizeMetadata { get; set; } = true;

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
    private readonly TextRedactor _textRedactor;
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

        // Use PdfEditor.Redaction library for TRUE glyph-level redaction
        _textRedactor = new TextRedactor(_loggerFactory.CreateLogger<TextRedactor>());
        _metadataSanitizer = new MetadataSanitizer(_loggerFactory.CreateLogger<MetadataSanitizer>());

        _logger.LogDebug("RedactionService created using PdfEditor.Redaction library for glyph-level redaction");
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
    /// <param name="pdfFilePath">Path to the PDF file (needed for glyph-level letter extraction)</param>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 72)</param>
    public void RedactArea(PdfPage page, Rect area, string pdfFilePath, int renderDpi = CoordinateConverter.PdfPointsPerInch)
    {
        _logger.LogInformation(
            "Starting redaction. Input area: ({X:F2},{Y:F2},{W:F2}x{H:F2}) at {Dpi} DPI [image pixels, top-left origin]",
            area.X, area.Y, area.Width, area.Height, renderDpi);

        // Convert from image pixels to PDF points using centralized converter
        // Both input and output use top-left origin (Avalonia convention)
        var scaledArea = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(area, renderDpi);

        // Check for page rotation
        var rotation = GetPageRotation(page);
        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;

        // IMPORTANT: scaledArea is in VISUAL coordinates (what the user sees)
        // For glyph-level redaction: PdfPig already provides letters in visual coordinates, so NO rotation transform
        // For drawing black rectangle: We need to transform to content stream coordinates

        // Calculate transformed area for drawing the black rectangle (content stream uses rotated coords)
        var transformedAreaForDrawing = scaledArea;
        if (rotation != 0)
        {
            _logger.LogInformation("Page has rotation: {Rotation}°. Will transform coordinates for drawing.", rotation);
            transformedAreaForDrawing = CoordinateConverter.TransformForRotation(scaledArea, rotation, pageWidth, pageHeight);
        }

        _logger.LogInformation(
            "Coordinate conversion via CoordinateConverter: " +
            "({X:F2},{Y:F2},{W:F2}x{H:F2}) → visual ({ScaledX:F2},{ScaledY:F2},{ScaledW:F2}x{ScaledH:F2}) [PDF points]",
            area.X, area.Y, area.Width, area.Height,
            scaledArea.X, scaledArea.Y, scaledArea.Width, scaledArea.Height);

        // Validate coordinates are reasonable for this page (use visual dimensions for rotated pages)
        var (effectiveWidth, effectiveHeight) = CoordinateConverter.GetRotatedPageDimensions(pageWidth, pageHeight, rotation);
        if (!CoordinateConverter.IsValidForPage(scaledArea, effectiveWidth, effectiveHeight))
        {
            _logger.LogWarning(
                "Selection area may be outside page bounds. Page: ({W}x{H}), Selection: ({X},{Y},{SW}x{SH})",
                pageWidth, pageHeight, scaledArea.X, scaledArea.Y, scaledArea.Width, scaledArea.Height);
        }

        var sw = Stopwatch.StartNew();

        // CRITICAL SECURITY REQUIREMENT:
        // Content removal MUST succeed for redaction to be valid.
        // We will NEVER do visual-only redaction as it creates a false sense of security.

        Models.RedactionResult result;

        try
        {
            // Step 1: Remove content within the area (TRUE REDACTION - REQUIRED)
            _logger.LogDebug("Step 1: Removing content within redaction area");
            result = RemoveContentInArea(page, scaledArea, pdfFilePath);

            sw.Stop();

            if (result.Mode == RedactionMode.TrueRedaction)
            {
                _logger.LogInformation("Content removed successfully in {ElapsedMs}ms. Redaction is secure.", sw.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogError("Redaction failed in {ElapsedMs}ms. Content removal threw exception.", sw.ElapsedMilliseconds);
            }

            // Step 2: Draw black rectangle over the area (OPTIONAL - visual confirmation only)
            // This is done by directly appending PDF operators to avoid XGraphics issues
            // IMPORTANT: For rotated pages, we use transformedAreaForDrawing which is in content stream coords
            _logger.LogDebug("Step 2: Drawing black rectangle for visual confirmation");
            try
            {
                DrawBlackRectangleDirectly(page, transformedAreaForDrawing);
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
            // IMPORTANT: Must create as indirect object, not direct dictionary
            // Adding a direct dictionary corrupts the PDF structure
            var newContent = new PdfDictionary(page.Owner);
            newContent.CreateStream(operatorBytes);
            page.Owner.Internals.AddObject(newContent);
            page.Contents.Elements.Add(newContent.Reference!);
        }

        _logger.LogDebug(
            "Drew black rectangle at PDF coords({X:F2},{Y:F2},{W:F2}x{H:F2}) [converted from Avalonia({AX:F2},{AY:F2})]",
            pdfX, pdfY, pdfWidth, pdfHeight, area.X, area.Y);
    }

    /// <summary>
    /// Remove content within the specified area using PdfEditor.Redaction library.
    /// This is TRUE glyph-level redaction - removes individual characters from PDF structure.
    /// </summary>
    /// <remarks>
    /// ⚠️ CRITICAL METHOD - USES PdfEditor.Redaction LIBRARY
    ///
    /// This method delegates to the proven PdfEditor.Redaction library for TRUE glyph-level removal:
    /// 1. Library uses PdfPig for accurate letter positions
    /// 2. Spatial matching finds exact characters in redaction area
    /// 3. Text operations are split at character boundaries
    /// 4. Kept segments are reconstructed with proper positioning (Tm operators)
    /// 5. Content stream is rebuilt without redacted characters
    ///
    /// After this method runs, text extraction tools CANNOT find the removed text.
    /// This is the security-critical part of redaction.
    ///
    /// The library is fully tested (208/209 tests passing) and handles Unicode normalization.
    /// </remarks>
    private Models.RedactionResult RemoveContentInArea(PdfPage page, Rect area, string pdfFilePath)
    {
        var result = new Models.RedactionResult();

        try
        {
            _logger.LogDebug("Using PdfEditor.Redaction library RedactPage() API for TRUE glyph-level redaction");
            var sw = Stopwatch.StartNew();

            // Convert Avalonia coordinates (top-left) to PDF coordinates (bottom-left)
            var pageHeight = page.Height.Point;
            var pdfLeft = area.X;
            var pdfBottom = pageHeight - area.Y - area.Height;  // Convert to bottom-left origin
            var pdfRight = area.X + area.Width;
            var pdfTop = pageHeight - area.Y;  // Convert to bottom-left origin

            var pdfRectangle = new PdfEditor.Redaction.PdfRectangle(pdfLeft, pdfBottom, pdfRight, pdfTop);

            _logger.LogInformation("Redacting area: Avalonia({X:F2},{Y:F2},{W:F2}x{H:F2}) → PDF({L:F2},{B:F2},{R:F2},{T:F2})",
                area.X, area.Y, area.Width, area.Height,
                pdfLeft, pdfBottom, pdfRight, pdfTop);

            // CRITICAL: Extract letters for TRUE glyph-level redaction
            // We extract from the ORIGINAL file (not the in-memory document) to avoid "already saved" issue
            int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
            var letters = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(
                pdfFilePath,
                pageNumber,
                _logger);

            _logger.LogDebug("Extracted {Count} letters for TRUE glyph-level redaction from page {PageNumber}", letters.Count, pageNumber);

            // Use RedactPage() API with TRUE glyph-level redaction
            var redactionOptions = new PdfEditor.Redaction.RedactionOptions
            {
                UseGlyphLevelRedaction = true,  // TRUE GLYPH-LEVEL!
                DrawVisualMarker = true,
                MarkerColor = (0, 0, 0)  // Black
            };

            var libraryResult = _textRedactor.RedactPage(page, new[] { pdfRectangle }, redactionOptions, pageLetters: letters);

            if (!libraryResult.Success)
            {
                throw new InvalidOperationException($"Library redaction failed: {libraryResult.ErrorMessage}");
            }

            sw.Stop();
            _logger.LogDebug("In-memory redaction completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            // Set result from library redaction
            // Issue #269: Now tracking image operations removed
            result.Mode = RedactionMode.TrueRedaction;
            result.ContentRemoved = libraryResult.RedactionCount > 0 || libraryResult.ImageRedactionCount > 0;
            result.TextOperationsRemoved = libraryResult.RedactionCount;
            result.ImageOperationsRemoved = libraryResult.ImageRedactionCount;
            result.GraphicsOperationsRemoved = 0;

            // Track redacted text from library details
            if (libraryResult.RedactionCount > 0)
            {
                foreach (var detail in libraryResult.Details)
                {
                    if (!string.IsNullOrWhiteSpace(detail.RedactedText))
                    {
                        // Split redacted text into words and track them
                        var words = detail.RedactedText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var word in words)
                        {
                            _redactedTerms.Add(word);
                        }
                    }
                }
            }

            // MANDATORY LOGGING
            Console.WriteLine($"[REDACTION-SECURITY] IN-MEMORY GLYPH-LEVEL REDACTION: Removed {libraryResult.RedactionCount} text segments, {libraryResult.ImageRedactionCount} images using PdfEditor.Redaction library");
            _logger.LogWarning("IN-MEMORY GLYPH-LEVEL REDACTION PERFORMED: {TextCount} text segments, {ImageCount} images removed via RedactPage() API in {ElapsedMs}ms",
                libraryResult.RedactionCount, libraryResult.ImageRedactionCount, sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            // CRITICAL SECURITY FAILURE
            result.Mode = RedactionMode.Failed;
            result.ContentRemoved = false;

            Console.WriteLine($"[REDACTION-CRITICAL-ERROR] Library redaction FAILED: {ex.Message}");
            _logger.LogError(ex, "CRITICAL: Library redaction failed");

            throw;
        }
    }

    /// <summary>
    /// Get content stream bytes from a page
    /// </summary>
    private byte[] GetPageContentBytes(PdfPage page)
    {
        if (page.Contents.Elements.Count == 0)
            return Array.Empty<byte>();

        using var ms = new MemoryStream();
        foreach (var item in page.Contents.Elements)
        {
            PdfDictionary? dict = null;
            if (item is PdfReference pdfRef)
                dict = pdfRef.Value as PdfDictionary;
            else if (item is PdfDictionary directDict)
                dict = directDict;

            if (dict?.Stream?.Value != null)
            {
                ms.Write(dict.Stream.Value, 0, dict.Stream.Value.Length);
                ms.WriteByte((byte)'\n');
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Extract text from a page for tracking redacted terms
    /// </summary>
    private string ExtractPageText(PdfPage page)
    {
        try
        {
            // Simple text extraction - just for tracking what was redacted
            // This is NOT security-critical, just for metadata sanitization
            using var tempFile = new TempFile();
            using (var doc = new PdfDocument())
            {
                doc.AddPage(page);
                doc.Save(tempFile.Path);
            }

            using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(tempFile.Path);
            var pdfPigPage = pdfPigDoc.GetPage(1);
            return pdfPigPage.Text;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Helper for temporary file management
    /// </summary>
    private class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile()
        {
            Path = System.IO.Path.GetTempFileName();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch { }
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

    // RemoveImagesInArea method removed - PdfEditor.Redaction library handles all content types
    // including images, paths, and text in a unified way

    /// <summary>
    /// Redact multiple areas on a page
    /// </summary>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 150)</param>
    public void RedactAreas(PdfPage page, IEnumerable<Rect> areas, string pdfFilePath, int renderDpi = 150)
    {
        foreach (var area in areas)
        {
            RedactArea(page, area, pdfFilePath, renderDpi);
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
    /// Redact all occurrences of specific text from a PDF file.
    /// This uses the proven TextRedactor.RedactText() API that bypasses coordinate conversion.
    /// Added to fix issue #190: GUI scripting fails on corpus PDFs due to coordinate issues.
    /// </summary>
    /// <param name="inputPath">Path to the input PDF file</param>
    /// <param name="outputPath">Path to save the redacted PDF</param>
    /// <param name="textToRedact">Text to search for and redact</param>
    /// <param name="caseSensitive">Whether to match case-sensitively (default: false)</param>
    /// <returns>Result indicating success and number of redactions</returns>
    public PdfEditor.Redaction.RedactionResult RedactText(string inputPath, string outputPath, string textToRedact, bool caseSensitive = false)
    {
        _logger.LogInformation("RedactText: Searching for '{Text}' in {Input}", textToRedact, inputPath);

        var options = new PdfEditor.Redaction.RedactionOptions
        {
            CaseSensitive = caseSensitive,
            UseGlyphLevelRedaction = true,
            DrawVisualMarker = true,
            MarkerColor = (0, 0, 0)  // Black
        };

        var result = _textRedactor.RedactText(inputPath, outputPath, textToRedact, options);

        if (result.Success)
        {
            _logger.LogInformation("RedactText: Successfully redacted {Count} occurrences of '{Text}'",
                result.RedactionCount, textToRedact);

            // Track redacted text for metadata sanitization
            if (result.RedactionCount > 0)
            {
                _redactedTerms.Add(textToRedact);
            }
        }
        else
        {
            _logger.LogError("RedactText: Failed to redact '{Text}': {Error}", textToRedact, result.ErrorMessage);
        }

        return result;
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
            RedactArea(page, area, document.FullPath ?? string.Empty, renderDpi);
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

    // DoesTextOperationContainRedactedCharacters method removed
    // PdfEditor.Redaction library handles character-level matching internally

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
