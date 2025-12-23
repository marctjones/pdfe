using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Redaction.ContentStream.Building;
using PdfEditor.Redaction.ContentStream.Parsing;
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
        _logger = logger;
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
                _logger.LogInformation("No occurrences of '{Text}' found", textToRedact);
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

                // Process this page
                var pageDetails = RedactPageContent(page, redactionAreas, options);
                details.AddRange(pageDetails.Select(d => new RedactionDetail
                {
                    PageNumber = pageNumber,
                    RedactedText = d.text,
                    Location = d.box
                }));

                if (pageDetails.Count > 0)
                {
                    affectedPages.Add(pageNumber);
                }
            }

            // Sanitize metadata if requested
            if (options.SanitizeMetadata)
            {
                SanitizeMetadata(pdfDoc);
            }

            // Save the modified PDF
            pdfDoc.Save(outputPath);

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

        int searchIndex = 0;

        while ((searchIndex = fullText.IndexOf(searchText, searchIndex, comparison)) != -1)
        {
            _logger.LogInformation("FindTextMatches: Found match at index {Index}", searchIndex);
            // Map text index back to letters
            var matchLetters = letters.Skip(searchIndex).Take(searchText.Length).ToList();
            _logger.LogInformation("FindTextMatches: Mapped to {Count} letters (expected {Expected})", matchLetters.Count, searchText.Length);
            if (matchLetters.Count == searchText.Length)
            {
                matches.Add(matchLetters);
            }
            searchIndex++;
        }

        return matches;
    }

    /// <summary>
    /// Calculate bounding box from a sequence of letters.
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
            if (rect.Left < left) left = rect.Left;
            if (rect.Bottom < bottom) bottom = rect.Bottom;
            if (rect.Right > right) right = rect.Right;
            if (rect.Top > top) top = rect.Top;
        }

        return new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>
    /// Redact content from a single page.
    /// Returns details of what was redacted.
    /// </summary>
    private List<(string text, PdfRectangle box)> RedactPageContent(
        PdfPage page,
        List<PdfRectangle> redactionAreas,
        RedactionOptions options)
    {
        var redactedItems = new List<(string text, PdfRectangle box)>();

        // Get page dimensions
        var mediaBox = page.MediaBox;
        var pageHeight = mediaBox.Height;

        // Get content stream bytes
        var contentBytes = GetContentStreamBytes(page);
        if (contentBytes == null || contentBytes.Length == 0)
        {
            _logger.LogDebug("Page has no content stream");
            return redactedItems;
        }

        RedactionLogger.LogParseStart(_logger, contentBytes.Length, pageHeight);

        // Parse content stream
        var operations = _parser.Parse(contentBytes, pageHeight);

        // Log redaction areas
        foreach (var area in redactionAreas)
        {
            RedactionLogger.LogRedactionArea(_logger, area, 0);
        }

        // Find operations to redact
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
                    redactedItems.Add((textOp.Text, textOp.BoundingBox));
                    break;
                }
            }
        }

        RedactionLogger.LogParseComplete(_logger, operations.Count, textOps.Count);

        if (opsToRemove.Count == 0)
        {
            _logger.LogDebug("No operations to redact on this page");
            return redactedItems;
        }

        _logger.LogDebug("Removing {Count} operations from content stream", opsToRemove.Count);

        // Build new content stream with redacted operations
        var newContentBytes = _builder.BuildWithRedactions(operations, redactionAreas);

        // Replace page content
        ReplacePageContent(page, newContentBytes);

        // Draw visual markers
        if (options.DrawVisualMarker)
        {
            DrawRedactionMarkers(page, redactionAreas, options.MarkerColor);
        }

        return redactedItems;
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
            // Clear existing content
            page.Contents.Elements.Clear();

            // Create new content stream
            var newStream = new PdfDictionary(page.Owner);
            newStream.CreateStream(newContent);

            // Add to document
            page.Owner.Internals.AddObject(newStream);

            // Set as page content
            if (newStream.Reference != null)
            {
                page.Contents.Elements.Add(newStream.Reference);
            }
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
}
