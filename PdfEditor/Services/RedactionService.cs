using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Models;
using PdfEditor.Services.Redaction;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PdfeCoreRect = Pdfe.Core.Document.PdfRectangle;
using PdfeStrategy = Pdfe.Core.Text.Segmentation.GlyphRemovalStrategy;
using Pdfe.Core.Text.Segmentation; // for PdfPageRedactionExtensions.RedactArea

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
/// GUI-facing redaction orchestrator. Delegates glyph removal to Pdfe.Core
/// and mirrors the rewritten content stream onto the PDFsharp page that the
/// rest of the app is editing.
/// </summary>
/// <remarks>
/// ⚠️ CRITICAL FOR AI CODING ASSISTANTS:
///
/// Redaction is TRUE GLYPH-LEVEL REMOVAL — glyphs are deleted from the PDF
/// content stream, not just visually covered. Text-extraction tools must not
/// be able to recover redacted text. Do not replace the content-stream
/// rewrite with a visual-only black box; that is a security regression.
///
/// Pipeline: find glyphs in area → rewrite content stream without them →
/// append black-rectangle overlay → mirror bytes onto PDFsharp page.
///
/// Covered by ContentRemovalVerificationTests. If those fail, redacted text
/// can be recovered by downstream tools = SECURITY VULNERABILITY.
///
/// See REDACTION_AI_GUIDELINES.md for complete documentation.
/// </remarks>
public class RedactionService
{
    private readonly ILogger<RedactionService> _logger;
    private readonly MetadataSanitizer _metadataSanitizer;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// List of text strings that have been redacted in the current session.
    /// Used for metadata sanitization.
    /// </summary>
    private readonly List<string> _redactedTerms = new();

    // Cache of the most recently opened Pdfe.Core document, keyed by file path.
    // RedactArea needs this across sequential calls on the same page so each
    // redaction accumulates on the Core doc's in-memory content stream —
    // re-opening from disk would drop prior redactions. PDFsharp can't be
    // re-serialized after Save, so snapshot-per-call isn't an option either.
    private string? _cachedCoreDocPath;
    private Pdfe.Core.Document.PdfDocument? _cachedCoreDoc;

    public RedactionService(ILogger<RedactionService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _metadataSanitizer = new MetadataSanitizer(_loggerFactory.CreateLogger<MetadataSanitizer>());

        _logger.LogDebug("RedactionService created (area + text redaction share one Pdfe.Core pipeline)");
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

        // Convert from image pixels to PDF points (both top-left origin).
        var scaledArea = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(area, renderDpi);

        // Sanity-check that the selection overlaps the page — a noisy log
        // rather than a hard failure, because UI rubber-band selections can
        // slightly overrun page bounds and still want to redact.
        var rotation = GetPageRotation(page);
        var (effectiveWidth, effectiveHeight) = CoordinateConverter.GetRotatedPageDimensions(
            page.Width.Point, page.Height.Point, rotation);
        if (!CoordinateConverter.IsValidForPage(scaledArea, effectiveWidth, effectiveHeight))
        {
            _logger.LogWarning(
                "Selection area may be outside page bounds. Page: ({W}x{H}), Selection: ({X},{Y},{SW}x{SH})",
                page.Width.Point, page.Height.Point,
                scaledArea.X, scaledArea.Y, scaledArea.Width, scaledArea.Height);
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

            // Visual black rectangle is appended inside RemoveContentInArea,
            // in the same content-stream rewrite as the glyph removal, so
            // sequential redactions accumulate overlays correctly.
            result.VisualCoverageDrawn = result.Mode == RedactionMode.TrueRedaction;
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
    /// Open the Pdfe.Core document for <paramref name="path"/>, or return the
    /// cached instance from a prior call so sequential RedactArea calls on the
    /// same file accumulate their redactions on one Core document. Disposes
    /// the previous cache entry when the path changes.
    /// </summary>
    private Pdfe.Core.Document.PdfDocument GetOrOpenCoreDoc(string path)
    {
        if (_cachedCoreDocPath == path && _cachedCoreDoc != null)
            return _cachedCoreDoc;

        _cachedCoreDoc?.Dispose();
        var bytes = File.ReadAllBytes(path);
        _cachedCoreDoc = Pdfe.Core.Document.PdfDocument.Open(bytes);
        _cachedCoreDocPath = path;
        return _cachedCoreDoc;
    }

    /// <summary>
    /// Release any cached Pdfe.Core document. Callers should invoke this
    /// after the containing PDFsharp document has been saved / closed so the
    /// stale Core-side copy isn't reused for a future redaction session on
    /// the same path.
    /// </summary>
    public void ClearCoreDocCache()
    {
        _cachedCoreDoc?.Dispose();
        _cachedCoreDoc = null;
        _cachedCoreDocPath = null;
    }

    /// <summary>
    /// 0-based index of <paramref name="page"/> within its owning document.
    /// </summary>
    private static int IndexOfPdfSharpPage(PdfPage page)
    {
        var pages = page.Owner.Pages;
        for (int i = 0; i < pages.Count; i++)
            if (pages[i] == page) return i;
        throw new InvalidOperationException("Page not found in its owning document");
    }

    /// <summary>
    /// Append the visual-confirmation black rectangle as a fill op in the
    /// Pdfe.Core page's content stream. Emits the standard
    /// <c>q 0 0 0 rg X Y W H re f Q</c> sequence in bottom-left PDF coords.
    /// </summary>
    private static void AppendBlackRectangleToCorePage(
        Pdfe.Core.Document.PdfPage corePage, PdfeCoreRect rect)
    {
        var content = corePage.GetContentStream();
        var ops = content.Operators.ToList();
        ops.Add(Pdfe.Core.Content.ContentOperator.SaveState());
        ops.Add(Pdfe.Core.Content.ContentOperator.SetFillRgb(0, 0, 0));
        ops.Add(Pdfe.Core.Content.ContentOperator.Rectangle(
            rect.Left, rect.Bottom, rect.Right - rect.Left, rect.Top - rect.Bottom));
        ops.Add(Pdfe.Core.Content.ContentOperator.Fill());
        ops.Add(Pdfe.Core.Content.ContentOperator.RestoreState());
        corePage.SetContentStream(new Pdfe.Core.Content.ContentStream(ops));
    }

    /// <summary>
    /// Replace a PDFsharp page's content stream with the given bytes. Used to
    /// mirror a Pdfe.Core-rewritten content stream onto the PDFsharp page so
    /// downstream <c>document.Save()</c> captures the redaction.
    /// </summary>
    private static void ReplacePdfSharpContentStream(PdfPage page, byte[] newContent)
    {
        page.Contents.Elements.Clear();
        var newStream = new PdfDictionary(page.Owner);
        newStream.CreateStream(newContent);
        page.Owner.Internals.AddObject(newStream);
        if (newStream.Reference == null)
            throw new InvalidOperationException(
                "Failed to create indirect reference for rewritten content stream");
        page.Contents.Elements.Add(newStream.Reference);
    }

    private static bool MatchesStrategy(
        PdfeCoreRect glyph, PdfeCoreRect area, PdfeStrategy strategy)
    {
        var g = glyph.Normalize();
        var a = area.Normalize();
        if (!g.IntersectsWith(a)) return false;

        bool fullyContained =
            a.Contains(g.Left, g.Bottom) && a.Contains(g.Right, g.Top) &&
            a.Contains(g.Left, g.Top) && a.Contains(g.Right, g.Bottom);

        return strategy switch
        {
            PdfeStrategy.FullyContained => fullyContained,
            PdfeStrategy.CenterPoint => a.Contains(
                (g.Left + g.Right) * 0.5, (g.Bottom + g.Top) * 0.5),
            _ => true,
        };
    }

    /// <summary>
    /// Remove the glyphs inside <paramref name="area"/> from the page's
    /// content stream via Pdfe.Core, then append a black-rectangle overlay
    /// for visual confirmation. The rewritten content stream is mirrored
    /// onto the PDFsharp page so the eventual document-level Save captures it.
    /// </summary>
    /// <remarks>
    /// This is TRUE glyph-level redaction: after this method runs, text-
    /// extraction tools cannot find the removed characters in the PDF
    /// structure. Don't replace the content-stream rewrite with a visual-
    /// only black box — that's a security regression.
    /// </remarks>
    private Models.RedactionResult RemoveContentInArea(PdfPage page, Rect area, string pdfFilePath)
    {
        var result = new Models.RedactionResult();

        try
        {
            var sw = Stopwatch.StartNew();

            // Convert Avalonia coordinates (top-left) to PDF coordinates (bottom-left)
            var pageHeight = page.Height.Point;
            var pdfLeft = area.X;
            var pdfBottom = pageHeight - area.Y - area.Height;  // Convert to bottom-left origin
            var pdfRight = area.X + area.Width;
            var pdfTop = pageHeight - area.Y;  // Convert to bottom-left origin

            _logger.LogInformation("Redacting area: Avalonia({X:F2},{Y:F2},{W:F2}x{H:F2}) → PDF({L:F2},{B:F2},{R:F2},{T:F2})",
                area.X, area.Y, area.Width, area.Height,
                pdfLeft, pdfBottom, pdfRight, pdfTop);

            // #235 migrated path: use Pdfe.Core's glyph-level redaction.
            int pageIndex = IndexOfPdfSharpPage(page);
            var coreRect = new PdfeCoreRect(pdfLeft, pdfBottom, pdfRight, pdfTop);
            var coreDoc = GetOrOpenCoreDoc(pdfFilePath);
            var corePage = coreDoc.GetPage(pageIndex + 1);

            // Snapshot the words about to be removed for metadata sanitization.
            // After RedactArea rewrites the content stream the letters are gone,
            // so extraction has to happen first.
            var removed = corePage.Letters
                .Where(l => MatchesStrategy(l.GlyphRectangle, coreRect, PdfeStrategy.AnyOverlap))
                .Select(l => l.Value)
                .ToList();

            corePage.RedactArea(coreRect, PdfeStrategy.AnyOverlap);

            // Append the visual black rectangle INSIDE the Core page's content
            // stream (not via PDFsharp afterwards). If we drew it on the
            // PDFsharp page, the next RedactArea call would mirror the Core
            // doc's bytes back over it and erase the rectangle — sequential
            // redactions would end up with only the last rect visible. Keeping
            // the rect inside Core means the cached doc accumulates all
            // overlays across calls.
            AppendBlackRectangleToCorePage(corePage, coreRect);

            // Mirror the rewritten /Contents onto the PDFsharp page.
            var newContent = corePage.GetContentStreamBytes();
            ReplacePdfSharpContentStream(page, newContent);

            sw.Stop();
            _logger.LogDebug("In-memory redaction completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            result.Mode = RedactionMode.TrueRedaction;
            result.ContentRemoved = removed.Count > 0;
            result.TextOperationsRemoved = removed.Count;
            result.ImageOperationsRemoved = 0; // #279: image redaction not yet ported to Pdfe.Core
            result.GraphicsOperationsRemoved = 0;

            if (removed.Count > 0)
            {
                var joined = string.Concat(removed);
                foreach (var word in joined.Split(
                    new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    _redactedTerms.Add(word);
                }
            }

            _logger.LogInformation(
                "Glyph-level redaction via Pdfe.Core removed {Count} characters in {Ms}ms",
                removed.Count, sw.ElapsedMilliseconds);

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
    /// Redact all occurrences of <paramref name="textToRedact"/> in the PDF at
    /// <paramref name="inputPath"/>, writing the result to
    /// <paramref name="outputPath"/>.
    /// </summary>
    /// <remarks>
    /// Shares the same Pdfe.Core glyph-removal pipeline as
    /// <see cref="RedactArea(PdfPage, Rect, string, int)"/> — find glyphs,
    /// rewrite content stream without them, append a black rectangle overlay.
    /// The only thing the scripting path adds is page-level text search to
    /// derive bounding boxes from a search string.
    /// </remarks>
    public TextRedactionResult RedactText(string inputPath, string outputPath, string textToRedact, bool caseSensitive = false)
    {
        _logger.LogInformation("RedactText: Searching for '{Text}' in {Input}", textToRedact, inputPath);

        // Drop any cached area-redaction state for a different file path; the
        // Core doc we're about to load is fresh and self-contained.
        ClearCoreDocCache();

        try
        {
            var bytes = File.ReadAllBytes(inputPath);
            using var doc = Pdfe.Core.Document.PdfDocument.Open(bytes);

            int totalMatches = doc.RedactText(textToRedact, caseSensitive);

            doc.Save(outputPath);

            if (totalMatches > 0)
                _redactedTerms.Add(textToRedact);

            _logger.LogInformation(
                "RedactText: redacted {Count} occurrences of '{Text}'",
                totalMatches, textToRedact);

            return TextRedactionResult.Succeeded(totalMatches);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RedactText failed for '{Text}'", textToRedact);
            return TextRedactionResult.Failed($"Redaction failed: {ex.Message}");
        }
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
