using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Redaction.ContentStream;
using PdfEditor.Redaction.ContentStream.Building;
using PdfEditor.Redaction.ContentStream.Parsing;
using PdfEditor.Redaction.GlyphLevel;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Redaction;

/// <summary>
/// Main implementation of TRUE glyph-level PDF text redaction.
/// Uses PdfPig for text position extraction and PDFsharp for PDF modification.
/// </summary>
public class TextRedactor : ITextRedactor
{
    private readonly IContentStreamParser _parser;
    private readonly IContentStreamBuilder _builder;
    private readonly GlyphRemover? _glyphRemover;
    private readonly ContentStreamRedactor _contentStreamRedactor;
    private readonly AnnotationRedactor _annotationRedactor;
    private readonly ILogger<TextRedactor> _logger;

    /// <summary>
    /// Create a TextRedactor with default components.
    /// </summary>
    public TextRedactor() : this(
        new ContentStreamParser(),
        new ContentStreamBuilder(),
        NullLogger<TextRedactor>.Instance)
    {
    }

    /// <summary>
    /// Create a TextRedactor with a logger.
    /// </summary>
    public TextRedactor(ILogger<TextRedactor> logger) : this(
        new ContentStreamParser(),
        new ContentStreamBuilder(),
        logger)
    {
    }

    /// <summary>
    /// Create a TextRedactor with custom components (for testing/DI).
    /// </summary>
    public TextRedactor(
        IContentStreamParser parser,
        IContentStreamBuilder builder,
        ILogger<TextRedactor> logger)
    {
        _parser = parser;
        _builder = builder;
        _glyphRemover = new GlyphRemover(builder);
        _logger = logger;
        _contentStreamRedactor = new ContentStreamRedactor(parser, builder, _glyphRemover, logger);
        _annotationRedactor = new AnnotationRedactor();
    }

    /// <summary>
    /// Create a TextRedactor with full logging for debugging (for testing).
    /// </summary>
    public TextRedactor(
        IContentStreamParser parser,
        IContentStreamBuilder builder,
        ILogger<TextRedactor> logger,
        ILogger<GlyphRemover> glyphRemoverLogger,
        ILogger<LetterFinder> letterFinderLogger,
        ILogger<TextSegmenter> textSegmenterLogger,
        ILogger<OperationReconstructor> operationReconstructorLogger)
    {
        _parser = parser;
        _builder = builder;
        _glyphRemover = new GlyphRemover(
            builder,
            glyphRemoverLogger,
            letterFinderLogger,
            textSegmenterLogger,
            operationReconstructorLogger);
        _logger = logger;
        _contentStreamRedactor = new ContentStreamRedactor(parser, builder, _glyphRemover, logger);
        _annotationRedactor = new AnnotationRedactor();
    }

    /// <inheritdoc />
    public RedactionResult RedactText(string inputPath, string outputPath, string textToRedact, RedactionOptions? options = null)
    {
        options ??= new RedactionOptions();

        try
        {
            _logger.LogDebug("Searching for text '{Text}' in {Input}", textToRedact, inputPath);

            // Find all occurrences using PdfPig
            var locations = FindTextLocations(inputPath, textToRedact, options.CaseSensitive);

            if (locations.Count == 0)
            {
                _logger.LogInformation("No occurrences of '{Text}' found, copying input to output", textToRedact);

                // Copy input to output so sequential redactions don't break
                // When no redactions are needed, the output should be identical to input
                File.Copy(inputPath, outputPath, overwrite: true);

                return RedactionResult.Succeeded(0, Array.Empty<int>());
            }

            _logger.LogDebug("Found {Count} occurrences of '{Text}'", locations.Count, textToRedact);

            // Perform redaction at found locations
            return RedactLocations(inputPath, outputPath, locations, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redact text '{Text}'", textToRedact);
            return RedactionResult.Failed($"Redaction failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public RedactionResult RedactLocations(string inputPath, string outputPath, IEnumerable<RedactionLocation> locations, RedactionOptions? options = null)
    {
        options ??= new RedactionOptions();
        var locationList = locations.ToList();

        if (locationList.Count == 0)
        {
            _logger.LogInformation("No redaction locations provided, copying input to output");

            // Copy input to output for consistency
            File.Copy(inputPath, outputPath, overwrite: true);

            return RedactionResult.Succeeded(0, Array.Empty<int>());
        }

        try
        {
            // Group locations by page
            var locationsByPage = locationList
                .GroupBy(l => l.PageNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            var details = new List<RedactionDetail>();
            var affectedPages = new HashSet<int>();

            // Open PDF with PDFsharp for modification
            using var pdfDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

            foreach (var (pageNumber, pageLocations) in locationsByPage)
            {
                if (pageNumber < 1 || pageNumber > pdfDoc.PageCount)
                {
                    _logger.LogWarning("Skipping invalid page number {Page}", pageNumber);
                    continue;
                }

                var page = pdfDoc.Pages[pageNumber - 1]; // 0-based index
                var redactionAreas = pageLocations.Select(l => l.BoundingBox).ToList();

                // Process this page (content stream)
                var pageDetails = RedactPageContent(inputPath, pageNumber, page, redactionAreas, options);
                details.AddRange(pageDetails.Select(d => new RedactionDetail
                {
                    PageNumber = pageNumber,
                    RedactedText = d.text,
                    Location = d.box
                }));

                // Redact annotations in the redaction areas
                if (options.RedactAnnotations)
                {
                    var annotationsRemoved = _annotationRedactor.RedactAnnotations(page, redactionAreas);
                    if (annotationsRemoved > 0)
                    {
                        _logger.LogDebug("Removed {Count} annotations from page {Page}", annotationsRemoved, pageNumber);
                    }
                }

                if (pageDetails.Count > 0)
                {
                    affectedPages.Add(pageNumber);
                }
            }

            // Detect PDF/A level before saving (for post-save metadata injection and transparency removal)
            PdfALevel pdfALevel = PdfALevel.None;
            if (options.PreservePdfAMetadata || options.RemovePdfATransparency)
            {
                pdfALevel = PdfADetector.Detect(inputPath);
                if (pdfALevel != PdfALevel.None)
                {
                    _logger.LogDebug("Detected PDF/A level {Level}, will preserve after save", PdfADetector.GetDisplayName(pdfALevel));
                }
            }

            // Sanitize metadata if requested
            if (options.SanitizeMetadata)
            {
                SanitizeMetadata(pdfDoc);
            }

            // Set modification date before save (required for PDF/A compliance)
            pdfDoc.Info.ModificationDate = DateTime.Now;

            // Save the modified PDF
            pdfDoc.Save(outputPath);

            // Post-save: Inject PDF/A metadata (PDFsharp overwrites XMP on save)
            if (pdfALevel != PdfALevel.None)
            {
                var preserved = PdfAMetadataPreserver.PreserveMetadataInFile(outputPath, pdfALevel);
                if (preserved)
                {
                    _logger.LogDebug("Successfully preserved PDF/A metadata");
                }
                else
                {
                    _logger.LogWarning("Failed to preserve PDF/A metadata");
                }
            }

            // Post-save: Remove transparency for PDF/A-1 compliance (if detected and requested)
            if (options.RemovePdfATransparency && pdfALevel != PdfALevel.None && IsPdfA1(pdfALevel))
            {
                RemoveTransparencyFromFile(outputPath);
            }

            var result = RedactionResult.Succeeded(details.Count, affectedPages, details);
            RedactionLogger.LogRedactionResult(_logger, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redact locations");
            return RedactionResult.Failed($"Redaction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Find all locations of a text string in the PDF using PdfPig.
    /// </summary>
    private List<RedactionLocation> FindTextLocations(string pdfPath, string searchText, bool caseSensitive)
    {
        var locations = new List<RedactionLocation>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);

        _logger.LogInformation("FindTextLocations: Searching for '{Text}' in {PageCount} pages", searchText, pdfDoc.NumberOfPages);

        for (int pageNum = 1; pageNum <= pdfDoc.NumberOfPages; pageNum++)
        {
            var page = pdfDoc.GetPage(pageNum);
            var letters = page.Letters.ToList();

            _logger.LogInformation("Page {Page}: Found {LetterCount} letters", pageNum, letters.Count);

            // Find matches in the letter sequence
            var matches = FindTextMatches(letters, searchText, comparison);

            _logger.LogInformation("Page {Page}: Found {MatchCount} matches for '{Text}'", pageNum, matches.Count, searchText);

            foreach (var match in matches)
            {
                var boundingBox = CalculateBoundingBox(match);
                locations.Add(new RedactionLocation
                {
                    PageNumber = pageNum,
                    BoundingBox = boundingBox
                });

                _logger.LogInformation(
                    "Found '{Text}' on page {Page} at ({Left:F2}, {Bottom:F2}, {Right:F2}, {Top:F2})",
                    searchText, pageNum,
                    boundingBox.Left, boundingBox.Bottom, boundingBox.Right, boundingBox.Top);
            }
        }

        _logger.LogInformation("FindTextLocations: Total {Count} locations found", locations.Count);
        return locations;
    }

    /// <summary>
    /// Normalize text for more robust matching.
    /// Handles Unicode apostrophes, quotes, and whitespace variations.
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalized = text
            // Replace various apostrophe/quote characters with ASCII apostrophe
            .Replace('\u2019', '\'')  // Right single quotation mark (')
            .Replace('\u2018', '\'')  // Left single quotation mark (')
            .Replace('\u02BC', '\'')  // Modifier letter apostrophe (ʼ)
            .Replace('\u2032', '\'')  // Prime (′)

            // Replace various dash characters with ASCII hyphen
            .Replace('\u2013', '-')   // En dash (–)
            .Replace('\u2014', '-')   // Em dash (—)
            .Replace('\u2212', '-')   // Minus sign (−)

            // Normalize whitespace
            .Trim();

        // Replace multiple consecutive spaces with single space
        return System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
    }

    /// <summary>
    /// Find text matches in a sequence of letters.
    /// </summary>
    private List<List<Letter>> FindTextMatches(List<Letter> letters, string searchText, StringComparison comparison)
    {
        var matches = new List<List<Letter>>();
        var textBuilder = new System.Text.StringBuilder();

        // Build full text string
        foreach (var letter in letters)
        {
            textBuilder.Append(letter.Value);
        }

        var fullText = textBuilder.ToString();
        _logger.LogInformation("FindTextMatches: Full text length = {Length}, searching for '{Text}'", fullText.Length, searchText);
        _logger.LogInformation("FindTextMatches: First 200 chars = '{Preview}'", fullText.Substring(0, Math.Min(200, fullText.Length)));

        // Normalize the search text for more robust matching
        var normalizedSearchText = NormalizeText(searchText);
        _logger.LogInformation("FindTextMatches: After normalization, searching for '{NormalizedText}'", normalizedSearchText);

        // Search character by character, building up normalized strings to compare
        int i = 0;
        while (i <= fullText.Length - normalizedSearchText.Length)
        {
            // Extract substring and normalize it
            var substring = fullText.Substring(i, Math.Min(normalizedSearchText.Length * 2, fullText.Length - i));
            var normalizedSubstring = NormalizeText(substring);

            // Check if this position matches
            if (normalizedSubstring.StartsWith(normalizedSearchText, comparison))
            {
                _logger.LogInformation("FindTextMatches: Found match at index {Index}", i);

                // Find the actual end position in the original text
                // We need to take enough characters to match the normalized search text
                int endIndex = i;
                var currentNormalized = "";

                while (endIndex < fullText.Length && currentNormalized.Length < normalizedSearchText.Length)
                {
                    currentNormalized = NormalizeText(fullText.Substring(i, endIndex - i + 1));
                    if (currentNormalized == normalizedSearchText)
                    {
                        break;
                    }
                    endIndex++;
                }

                var matchLength = endIndex - i + 1;
                var matchLetters = letters.Skip(i).Take(matchLength).ToList();

                _logger.LogInformation("FindTextMatches: Mapped to {Count} letters from index {Start} to {End}",
                    matchLetters.Count, i, endIndex);

                if (matchLetters.Count > 0)
                {
                    matches.Add(matchLetters);
                    // Skip past this match to avoid overlapping matches
                    i = endIndex + 1;
                    continue;
                }
            }

            i++;
        }

        return matches;
    }

    /// <summary>
    /// Calculate bounding box from a sequence of letters.
    /// Handles rotated text where PdfPig may return GlyphRectangles with swapped Left/Right or Bottom/Top.
    /// </summary>
    private static PdfRectangle CalculateBoundingBox(List<Letter> letters)
    {
        if (letters.Count == 0)
            return default;

        double left = double.MaxValue;
        double bottom = double.MaxValue;
        double right = double.MinValue;
        double top = double.MinValue;

        foreach (var letter in letters)
        {
            var rect = letter.GlyphRectangle;

            // Normalize coordinates - PdfPig can return swapped Left/Right or Bottom/Top for rotated text
            // For example, 90° rotation returns GlyphRectangle with Left > Right
            double letterLeft = Math.Min(rect.Left, rect.Right);
            double letterRight = Math.Max(rect.Left, rect.Right);
            double letterBottom = Math.Min(rect.Bottom, rect.Top);
            double letterTop = Math.Max(rect.Bottom, rect.Top);

            if (letterLeft < left) left = letterLeft;
            if (letterBottom < bottom) bottom = letterBottom;
            if (letterRight > right) right = letterRight;
            if (letterTop > top) top = letterTop;
        }

        return new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>
    /// Redact content from a single page.
    /// Returns details of what was redacted.
    /// </summary>
    private List<(string text, PdfRectangle box)> RedactPageContent(
        string pdfPath,
        int pageNumber,
        PdfPage page,
        List<PdfRectangle> redactionAreas,
        RedactionOptions options)
    {
        // Get page dimensions, accounting for rotation
        var mediaBox = page.MediaBox;
        var rotation = GetPageRotation(page);

        // For content stream parsing, use MediaBox height (unrotated)
        var contentStreamPageHeight = mediaBox.Height;

        _logger.LogDebug("Page {Page} rotation: {Rotation}°, MediaBox: {W}x{H}",
            pageNumber, rotation, mediaBox.Width, mediaBox.Height);

        // CRITICAL FIX for Issue #151 (rotated page redaction):
        // PdfPig returns letter coordinates in VISUAL space (post-rotation)
        // ContentStreamParser returns coordinates in CONTENT STREAM space (pre-rotation)
        // For rotated pages, we must transform redaction areas from visual to content stream space
        List<PdfRectangle> contentStreamAreas;
        if (rotation != 0)
        {
            _logger.LogDebug("Transforming {Count} redaction areas from visual to content stream space (rotation: {Rotation}°)",
                redactionAreas.Count, rotation);

            contentStreamAreas = redactionAreas
                .Select(area => RotationTransform.VisualToContentStream(area, rotation, mediaBox.Width, mediaBox.Height))
                .ToList();

            for (int i = 0; i < redactionAreas.Count; i++)
            {
                _logger.LogDebug("  Area {I}: Visual ({VL:F1},{VB:F1})-({VR:F1},{VT:F1}) → Content ({CL:F1},{CB:F1})-({CR:F1},{CT:F1})",
                    i,
                    redactionAreas[i].Left, redactionAreas[i].Bottom, redactionAreas[i].Right, redactionAreas[i].Top,
                    contentStreamAreas[i].Left, contentStreamAreas[i].Bottom, contentStreamAreas[i].Right, contentStreamAreas[i].Top);
            }
        }
        else
        {
            contentStreamAreas = redactionAreas;
        }

        // Get content stream bytes
        var contentBytes = GetContentStreamBytes(page);
        if (contentBytes == null || contentBytes.Length == 0)
        {
            _logger.LogDebug("Page has no content stream");
            return new List<(string text, PdfRectangle box)>();
        }

        // Extract letters if needed for glyph-level redaction
        IReadOnlyList<Letter>? letters = null;
        if (options.UseGlyphLevelRedaction && _glyphRemover != null)
        {
            letters = PdfPig.PdfPigHelper.ExtractLettersFromFile(pdfPath, pageNumber, _logger);
        }

        // Get page resources for Form XObject support
        var resources = page.Elements.GetDictionary("/Resources");

        // Delegate to ContentStreamRedactor for core redaction logic (with Form XObject support)
        // NOTE: Content stream areas are for operation-level matching, visual areas are for letter-level matching
        // CRITICAL FIX (Issue #173): Pass rotation info for coordinate transformation in glyph-level redaction
        var (newContentBytes, details, formXObjectResults) = _contentStreamRedactor.RedactContentStreamWithFormXObjects(
            contentBytes,
            contentStreamPageHeight,
            contentStreamAreas,
            letters,
            options,
            resources,
            redactionAreas,  // Pass original visual areas for glyph-level letter matching
            rotation,        // Page rotation for coordinate transformation
            mediaBox.Width,  // MediaBox width for coordinate transformation
            mediaBox.Height); // MediaBox height for coordinate transformation

        // Replace page content
        ReplacePageContent(page, newContentBytes);

        // Update any modified Form XObjects
        if (formXObjectResults.Count > 0 && resources != null)
        {
            UpdateFormXObjects(resources, formXObjectResults);
        }

        // Draw visual markers (using content stream coordinates so they appear correctly)
        if (options.DrawVisualMarker)
        {
            DrawRedactionMarkers(page, contentStreamAreas, options.MarkerColor);
        }

        // Convert details to return format
        return details.Select(d => (d.RedactedText, d.Location)).ToList();
    }

    /// <summary>
    /// Update Form XObject content streams with redacted content.
    /// </summary>
    private void UpdateFormXObjects(PdfDictionary resources, List<ContentStream.FormXObjectRedactionResult> formXObjectResults)
    {
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects == null)
            return;

        foreach (var result in formXObjectResults)
        {
            try
            {
                // Get the name without leading slash
                var name = result.XObjectName;
                if (name.StartsWith("/"))
                    name = name.Substring(1);

                // Try with and without leading slash
                var key = "/" + name;
                if (!xObjects.Elements.ContainsKey(key))
                {
                    key = name;
                    if (!xObjects.Elements.ContainsKey(key))
                    {
                        _logger.LogWarning("Could not find Form XObject to update: {Name}", result.XObjectName);
                        continue;
                    }
                }

                // Get the XObject
                PdfDictionary? xObject = null;
                var element = xObjects.Elements[key];
                if (element is PdfSharp.Pdf.Advanced.PdfReference pdfRef)
                {
                    xObject = pdfRef.Value as PdfDictionary;
                }
                else if (element is PdfDictionary dict)
                {
                    xObject = dict;
                }

                if (xObject == null)
                {
                    _logger.LogWarning("Could not resolve Form XObject: {Name}", result.XObjectName);
                    continue;
                }

                // Replace the Form XObject's content stream
                if (xObject.Stream != null)
                {
                    // Replace stream content
                    xObject.Stream.Value = result.ModifiedContentBytes;
                    _logger.LogInformation("Updated Form XObject {Name} with redacted content ({Bytes} bytes)",
                        result.XObjectName, result.ModifiedContentBytes.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Form XObject: {Name}", result.XObjectName);
            }
        }
    }

    /// <summary>
    /// Get the page rotation in degrees (0, 90, 180, or 270).
    /// </summary>
    private int GetPageRotation(PdfPage page)
    {
        try
        {
            if (page.Elements.ContainsKey("/Rotate"))
            {
                var rotation = page.Elements.GetInteger("/Rotate");
                // Normalize to 0, 90, 180, 270
                return ((rotation % 360) + 360) % 360;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get page rotation, assuming 0°");
        }
        return 0;
    }

    /// <summary>
    /// Get the content stream bytes from a page.
    /// Handles multiple content streams by concatenating them.
    /// </summary>
    private byte[]? GetContentStreamBytes(PdfPage page)
    {
        try
        {
            var contents = page.Contents;
            if (contents == null || contents.Elements.Count == 0)
                return null;

            using var ms = new MemoryStream();

            foreach (var element in contents.Elements)
            {
                if (element is PdfReference reference)
                {
                    var obj = reference.Value;
                    if (obj is PdfDictionary streamDict && streamDict.Stream != null)
                    {
                        var bytes = streamDict.Stream.UnfilteredValue;
                        if (bytes != null && bytes.Length > 0)
                        {
                            ms.Write(bytes, 0, bytes.Length);
                            ms.WriteByte((byte)'\n');
                        }
                    }
                }
                else if (element is PdfDictionary directDict && directDict.Stream != null)
                {
                    var bytes = directDict.Stream.UnfilteredValue;
                    if (bytes != null && bytes.Length > 0)
                    {
                        ms.Write(bytes, 0, bytes.Length);
                        ms.WriteByte((byte)'\n');
                    }
                }
            }

            var result = ms.ToArray();
            return result.Length > 0 ? result : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract content stream");
            return null;
        }
    }

    /// <summary>
    /// Replace the page content with new content stream bytes.
    /// </summary>
    private void ReplacePageContent(PdfPage page, byte[] newContent)
    {
        try
        {
            var oldSize = GetContentStreamBytes(page)?.Length ?? 0;

            // DIAGNOSTIC LOGGING
            _logger.LogWarning("[REPLACE-CONTENT] Replacing page content: {OldSize} → {NewSize} bytes (change: {Delta})",
                oldSize, newContent.Length, newContent.Length - oldSize);

            // Clear existing content
            page.Contents.Elements.Clear();

            // Create new content stream
            var newStream = new PdfDictionary(page.Owner);
            newStream.CreateStream(newContent);

            // Add to document
            page.Owner.Internals.AddObject(newStream);

            // Set as page content
            if (newStream.Reference == null)
            {
                _logger.LogError("[REPLACE-CONTENT] ❌ ERROR: newStream.Reference is NULL!");
                throw new InvalidOperationException("Failed to create PDF reference for new content stream");
            }
            page.Contents.Elements.Add(newStream.Reference);
            _logger.LogWarning("[REPLACE-CONTENT] ✅ Successfully added new stream reference");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace page content");
            throw;
        }
    }

    /// <summary>
    /// Draw visual redaction markers (black rectangles) on the page.
    /// </summary>
    private void DrawRedactionMarkers(PdfPage page, List<PdfRectangle> areas, (double R, double G, double B) color)
    {
        foreach (var area in areas)
        {
            // Create graphics commands to draw filled rectangle
            var commands = $"q {color.R} {color.G} {color.B} rg {area.Left:F2} {area.Bottom:F2} {area.Width:F2} {area.Height:F2} re f Q\n";
            var commandBytes = System.Text.Encoding.ASCII.GetBytes(commands);

            // Append to content stream
            AppendToContentStream(page, commandBytes);
        }
    }

    /// <summary>
    /// Append bytes to the page's content stream.
    /// </summary>
    private void AppendToContentStream(PdfPage page, byte[] appendBytes)
    {
        try
        {
            // Get existing content
            var existingBytes = GetContentStreamBytes(page) ?? Array.Empty<byte>();

            // Combine existing + new
            var combined = new byte[existingBytes.Length + appendBytes.Length];
            Array.Copy(existingBytes, 0, combined, 0, existingBytes.Length);
            Array.Copy(appendBytes, 0, combined, existingBytes.Length, appendBytes.Length);

            // Replace content
            ReplacePageContent(page, combined);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append to content stream");
        }
    }

    /// <summary>
    /// Sanitize document metadata.
    /// </summary>
    private void SanitizeMetadata(PdfSharp.Pdf.PdfDocument pdfDoc)
    {
        try
        {
            // Clear Info dictionary fields (Producer is read-only)
            pdfDoc.Info.Title = "";
            pdfDoc.Info.Author = "";
            pdfDoc.Info.Subject = "";
            pdfDoc.Info.Keywords = "";
            pdfDoc.Info.Creator = "";

            _logger.LogDebug("Sanitized document metadata");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sanitize metadata");
        }
    }

    // ======================================================================
    // PUBLIC API: In-Memory PdfPage Operations
    // ======================================================================

    /// <summary>
    /// Redact specific areas on a PdfPage in-place.
    /// See ITextRedactor.RedactPage() for full documentation.
    /// </summary>
    public PageRedactionResult RedactPage(
        PdfPage page,
        IEnumerable<PdfRectangle> areas,
        RedactionOptions? options = null,
        IReadOnlyList<UglyToad.PdfPig.Content.Letter>? pageLetters = null)
    {
        try
        {
            options ??= new RedactionOptions();
            var redactionAreas = areas.ToList();

            if (redactionAreas.Count == 0)
            {
                return PageRedactionResult.Succeeded(Array.Empty<RedactionDetail>());
            }

            // Get page dimensions
            var mediaBox = page.MediaBox;
            var pageHeight = mediaBox.Height;

            // Get content stream bytes
            var contentBytes = GetContentStreamBytes(page);
            if (contentBytes == null || contentBytes.Length == 0)
            {
                _logger.LogDebug("Page has no content stream");
                return PageRedactionResult.Succeeded(Array.Empty<RedactionDetail>());
            }

            // Determine page number for detail reporting
            int pageNumber = PdfPig.PdfPigHelper.GetPageNumber(page);

            // IMPORTANT: For glyph-level redaction, letters must be provided by caller
            // We cannot auto-extract because page.Owner.Save() marks document as "already saved"
            // If UseGlyphLevelRedaction=true but pageLetters=null, we log a warning and fall back
            IReadOnlyList<UglyToad.PdfPig.Content.Letter>? lettersToUse = pageLetters;
            if (options.UseGlyphLevelRedaction && lettersToUse == null)
            {
                _logger.LogWarning(
                    "Glyph-level redaction requested but no letters provided. " +
                    "Falling back to whole-operation redaction. " +
                    "For TRUE glyph-level redaction, use file-based API or provide letters via ExtractLettersFromPage().");
            }

            // Delegate to ContentStreamRedactor for core redaction logic
            var (newContentBytes, details) = _contentStreamRedactor.RedactContentStream(
                contentBytes,
                pageHeight,
                redactionAreas,
                lettersToUse,
                options);

            // Update page number in details
            var updatedDetails = details.Select(d => d with { PageNumber = pageNumber }).ToList();

            // Replace page content
            ReplacePageContent(page, newContentBytes);

            // Redact annotations in the redaction areas
            if (options.RedactAnnotations)
            {
                var annotationsRemoved = _annotationRedactor.RedactAnnotations(page, redactionAreas);
                if (annotationsRemoved > 0)
                {
                    _logger.LogDebug("Removed {Count} annotations from page", annotationsRemoved);
                }
            }

            // Draw visual markers
            if (options.DrawVisualMarker)
            {
                DrawRedactionMarkers(page, redactionAreas, options.MarkerColor);
            }

            return PageRedactionResult.Succeeded(updatedDetails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to redact page");
            return PageRedactionResult.Failed($"Redaction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extract PdfPig letters from a PdfPage for glyph-level redaction.
    /// See ITextRedactor.ExtractLettersFromPage() for full documentation.
    /// </summary>
    public IReadOnlyList<UglyToad.PdfPig.Content.Letter> ExtractLettersFromPage(PdfPage page)
    {
        try
        {
            int pageNumber = PdfPig.PdfPigHelper.GetPageNumber(page);

            // Save document to MemoryStream and extract letters
            using var memoryStream = new MemoryStream();
            page.Owner.Save(memoryStream, closeStream: false);
            memoryStream.Position = 0;

            // Open with PdfPig and extract letters
            using var pigDocument = UglyToad.PdfPig.PdfDocument.Open(memoryStream);
            var pigPage = pigDocument.GetPage(pageNumber);

            return pigPage.Letters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract letters from page");
            throw new InvalidOperationException(
                $"Failed to extract letters from page: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Sanitize document metadata after redactions.
    /// See ITextRedactor.SanitizeDocumentMetadata() for full documentation.
    /// </summary>
    public void SanitizeDocumentMetadata(PdfSharp.Pdf.PdfDocument document)
    {
        SanitizeMetadata(document);
    }

    /// <summary>
    /// Check if a PDF/A level is PDF/A-1 (which forbids transparency).
    /// </summary>
    private static bool IsPdfA1(PdfALevel level)
    {
        return level == PdfALevel.PdfA_1a || level == PdfALevel.PdfA_1b;
    }

    /// <summary>
    /// Remove transparency features from a saved PDF file for PDF/A-1 compliance.
    /// </summary>
    private void RemoveTransparencyFromFile(string filePath)
    {
        try
        {
            // Open, modify, and save
            using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify);
            var remover = new PdfATransparencyRemover();
            var removed = remover.RemoveTransparency(doc);

            if (removed > 0)
            {
                doc.Save(filePath);
                _logger.LogDebug("Removed {Count} transparency features for PDF/A-1 compliance", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove transparency from PDF/A-1 file");
        }
    }
}
