using Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace PdfEditor.Services;

/// <summary>
/// Service for extracting text from PDF pages
/// Uses PdfPig (Apache 2.0 License) for text extraction
/// </summary>
public class PdfTextExtractionService
{
    private readonly ILogger<PdfTextExtractionService> _logger;

    public PdfTextExtractionService(ILogger<PdfTextExtractionService> logger)
    {
        _logger = logger;
        _logger.LogDebug("PdfTextExtractionService instance created");
    }

    /// <summary>
    /// Extract all text from a page (stream-based - primary method)
    /// </summary>
    /// <param name="pdfStream">PDF document stream (can be file stream or memory stream)</param>
    /// <param name="pageIndex">Zero-based page index</param>
    /// <param name="sourceName">Optional name for logging (e.g., filename)</param>
    public string ExtractTextFromPage(Stream pdfStream, int pageIndex, string sourceName = "PDF")
    {
        _logger.LogInformation("Extracting text from page {PageIndex} of {FileName}",
            pageIndex + 1, sourceName);

        try
        {
            using var document = PdfDocument.Open(pdfStream);

            if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
            {
                _logger.LogWarning("Invalid page index: {PageIndex}, total pages: {TotalPages}",
                    pageIndex, document.NumberOfPages);
                return string.Empty;
            }

            var page = document.GetPage(pageIndex + 1); // PdfPig uses 1-based indexing
            var text = page.Text;

            _logger.LogInformation("Extracted {Length} characters from page {PageIndex}",
                text.Length, pageIndex + 1);

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from page {PageIndex}", pageIndex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract all text from a page (file-based wrapper for backward compatibility)
    /// </summary>
    public string ExtractTextFromPage(string pdfPath, int pageIndex)
    {
        using var stream = File.OpenRead(pdfPath);
        return ExtractTextFromPage(stream, pageIndex, Path.GetFileName(pdfPath));
    }

    /// <summary>
    /// Extract all text from all pages in the PDF document
    /// </summary>
    /// <param name="pdfPath">Path to PDF file</param>
    /// <returns>Concatenated text from all pages</returns>
    public string ExtractAllText(string pdfPath)
    {
        _logger.LogInformation("Extracting all text from {FileName}", Path.GetFileName(pdfPath));

        try
        {
            using var document = PdfDocument.Open(pdfPath);
            var allText = new StringBuilder();

            for (int i = 0; i < document.NumberOfPages; i++)
            {
                var page = document.GetPage(i + 1); // PdfPig uses 1-based indexing
                var pageText = page.Text;

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    allText.Append(pageText);
                    allText.Append('\n'); // Separate pages with newline
                }
            }

            var result = allText.ToString();
            _logger.LogInformation("Extracted {Length} total characters from {PageCount} pages",
                result.Length, document.NumberOfPages);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting all text from {FileName}", pdfPath);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extract text from a specific area of the page (stream-based - primary method)
    /// </summary>
    /// <param name="pdfStream">PDF document stream (can be file stream or memory stream)</param>
    /// <param name="pageIndex">Zero-based page index</param>
    /// <param name="area">Selection area in screen coordinates</param>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 150). Used to scale screen coordinates to PDF points (72 DPI)</param>
    /// <param name="sourceName">Optional name for logging (e.g., filename)</param>
    public string ExtractTextFromArea(Stream pdfStream, int pageIndex, Rect area, int renderDpi = 150, string sourceName = "PDF")
    {
        _logger.LogInformation(
            "Extracting text from screen area ({X:F2},{Y:F2},{W:F2}x{H:F2}) on page {PageIndex} of {FileName} (rendered at {Dpi} DPI)",
            area.X, area.Y, area.Width, area.Height, pageIndex + 1, sourceName, renderDpi);

        try
        {
            using var document = PdfDocument.Open(pdfStream);

            if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
            {
                _logger.LogWarning("Invalid page index: {PageIndex}, total pages: {TotalPages}",
                    pageIndex, document.NumberOfPages);
                return string.Empty;
            }

            var page = document.GetPage(pageIndex + 1); // PdfPig uses 1-based indexing

            // Get words from page (we'll iterate their letters for character-level selection)
            var words = page.GetWords();
            var extractedText = new StringBuilder();

            // Use centralized CoordinateConverter for all coordinate transformations
            // This converts from image pixels (top-left origin) to PDF coordinates (bottom-left origin)
            var pageHeight = page.Height;
            var (left, bottom, right, top) = CoordinateConverter.ImageSelectionToPdfCoords(
                area, pageHeight, renderDpi);

            var pdfRect = new UglyToad.PdfPig.Core.PdfRectangle(left, bottom, right, top);

            _logger.LogInformation(
                "Coordinate conversion via CoordinateConverter.ImageSelectionToPdfCoords: " +
                "({X:F2},{Y:F2},{W:F2}x{H:F2}) → PDF ({Left:F2},{Bottom:F2}) to ({Right:F2},{Top:F2})",
                area.X, area.Y, area.Width, area.Height,
                pdfRect.Left, pdfRect.Bottom, pdfRect.Right, pdfRect.Top);

            // CHARACTER-LEVEL SELECTION: Find individual letters whose center point is inside the selection
            // This allows precise text selection (e.g., selecting "can therefore" from a sentence
            // without accidentally including adjacent words)
            var selectedLetters = new List<Letter>();

            foreach (var word in words)
            {
                foreach (var letter in word.Letters)
                {
                    if (IsLetterCenterInSelection(letter.GlyphRectangle, pdfRect))
                    {
                        selectedLetters.Add(letter);
                    }
                }
            }

            _logger.LogInformation("Found {LetterCount} letters in selection area (character-level)", selectedLetters.Count);

            if (selectedLetters.Count == 0)
            {
                _logger.LogWarning("No letters found in selection area");
                return string.Empty;
            }

            // Group letters into lines based on Y coordinate
            // Issue #105: Use StartBaseLine.Y for more reliable vertical positioning
            // which handles PDFs with complex text matrices better than GlyphRectangle
            const double lineHeightThreshold = 5.0;
            var lines = new List<List<Letter>>();

            foreach (var letter in selectedLetters)
            {
                // Use StartBaseLine.Y as the primary position indicator
                var letterY = letter.StartBaseLine.Y;
                var line = lines.FirstOrDefault(l =>
                {
                    var lineY = l[0].StartBaseLine.Y;
                    return Math.Abs(lineY - letterY) < lineHeightThreshold;
                });

                if (line == null)
                {
                    line = new List<Letter>();
                    lines.Add(line);
                }
                line.Add(letter);
            }

            // Sort lines top to bottom (higher Y = top in PDF coordinates)
            var sortedLines = lines.OrderByDescending(line => line[0].StartBaseLine.Y).ToList();

            // Within each line, sort letters left to right
            // Issue #105: Use StartBaseLine.X for sorting as it's more reliable than GlyphRectangle.Left
            // in PDFs with complex text positioning (Type 1 fonts, Tz scaling issues)
            foreach (var line in sortedLines)
            {
                line.Sort((a, b) =>
                {
                    // Prefer StartBaseLine.X as it represents the actual text rendering position
                    // Fall back to GlyphRectangle.Left for compatibility
                    var aX = a.StartBaseLine.X;
                    var bX = b.StartBaseLine.X;
                    return aX.CompareTo(bX);
                });
            }

            // Build text with proper spacing
            for (int i = 0; i < sortedLines.Count; i++)
            {
                var line = sortedLines[i];
                double lastRight = 0;

                for (int j = 0; j < line.Count; j++)
                {
                    var letter = line[j];

                    if (j > 0)
                    {
                        // Check gap from previous letter
                        var gap = letter.GlyphRectangle.Left - lastRight;
                        // If gap is larger than typical letter spacing, add a space
                        // Threshold: 2.5 points (typical word spacing is 3-4 points)
                        if (gap > 2.5)
                        {
                            extractedText.Append(' ');
                        }
                    }

                    extractedText.Append(letter.Value);
                    lastRight = letter.GlyphRectangle.Right;
                }

                // Add newline after line (except last)
                if (i < sortedLines.Count - 1)
                    extractedText.AppendLine();
            }

            var result = extractedText.ToString().Trim();
            _logger.LogInformation(
                "Text extraction complete: {LetterCount} letters extracted, {CharCount} characters in result",
                selectedLetters.Count, result.Length);

            // Log the actual extracted text (first 200 chars)
            if (result.Length > 0)
            {
                var preview = result.Length > 200 ? result.Substring(0, 200) + "..." : result;
                _logger.LogInformation("Extracted text preview: \"{Preview}\"", preview);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from area on page {PageIndex}", pageIndex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Check if two rectangles intersect (kept for potential future use)
    /// </summary>
    private bool Intersects(PdfRectangle a, PdfRectangle b)
    {
        return !(a.Right < b.Left || a.Left > b.Right ||
                 a.Top < b.Bottom || a.Bottom > b.Top);
    }

    /// <summary>
    /// Extract text from a specific area of the page (file-based wrapper for backward compatibility)
    /// </summary>
    /// <param name="pdfPath">Path to PDF file</param>
    /// <param name="pageIndex">Zero-based page index</param>
    /// <param name="area">Selection area in screen coordinates</param>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 150)</param>
    public string ExtractTextFromArea(string pdfPath, int pageIndex, Rect area, int renderDpi = 150)
    {
        using var stream = File.OpenRead(pdfPath);
        return ExtractTextFromArea(stream, pageIndex, area, renderDpi, Path.GetFileName(pdfPath));
    }

    /// <summary>
    /// Check if a letter's center point is inside the selection rectangle.
    /// This provides precise character-level selection - a letter is only selected
    /// if its center point falls within the selection bounds.
    /// </summary>
    /// <param name="letterBox">The bounding box of the letter (glyph rectangle)</param>
    /// <param name="selection">The selection rectangle in PDF coordinates</param>
    /// <returns>True if the letter's center is inside the selection</returns>
    private bool IsLetterCenterInSelection(PdfRectangle letterBox, PdfRectangle selection)
    {
        // Calculate the center point of the letter
        var centerX = (letterBox.Left + letterBox.Right) / 2.0;
        var centerY = (letterBox.Top + letterBox.Bottom) / 2.0;

        // Check if center point is inside the selection rectangle
        return centerX >= selection.Left && centerX <= selection.Right &&
               centerY >= selection.Bottom && centerY <= selection.Top;
    }
}
