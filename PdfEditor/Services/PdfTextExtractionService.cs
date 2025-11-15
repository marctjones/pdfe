using Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

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
    /// Extract all text from a page
    /// </summary>
    public string ExtractTextFromPage(string pdfPath, int pageIndex)
    {
        _logger.LogInformation("Extracting text from page {PageIndex} of {FileName}",
            pageIndex + 1, Path.GetFileName(pdfPath));

        try
        {
            using var document = PdfDocument.Open(pdfPath);

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
    /// Extract text from a specific area of the page
    /// </summary>
    /// <param name="renderDpi">The DPI at which the page was rendered (default 150). Used to scale screen coordinates to PDF points (72 DPI)</param>
    public string ExtractTextFromArea(string pdfPath, int pageIndex, Rect area, int renderDpi = 150)
    {
        _logger.LogInformation(
            "Extracting text from screen area ({X:F2},{Y:F2},{W:F2}x{H:F2}) on page {PageIndex} (rendered at {Dpi} DPI)",
            area.X, area.Y, area.Width, area.Height, pageIndex + 1, renderDpi);

        try
        {
            using var document = PdfDocument.Open(pdfPath);

            if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
            {
                _logger.LogWarning("Invalid page index: {PageIndex}, total pages: {TotalPages}",
                    pageIndex, document.NumberOfPages);
                return string.Empty;
            }

            var page = document.GetPage(pageIndex + 1); // PdfPig uses 1-based indexing

            // Get words from page
            var words = page.GetWords();
            var extractedText = new StringBuilder();
            var matchedWords = new List<Word>();

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

            // Convert Avalonia coordinates (top-left origin) to PDF coordinates (bottom-left origin)
            var pageHeight = page.Height;
            var pdfY = pageHeight - scaledY - scaledHeight;
            var pdfRect = new UglyToad.PdfPig.Core.PdfRectangle(
                scaledX,
                pdfY,
                scaledX + scaledWidth,
                pdfY + scaledHeight);

            _logger.LogInformation(
                "Final PDF coordinates (bottom-left origin): ({Left:F2},{Bottom:F2}) to ({Right:F2},{Top:F2})",
                pdfRect.Left, pdfRect.Bottom, pdfRect.Right, pdfRect.Top);

            // Find words that intersect with the selection area
            foreach (var word in words)
            {
                var wordBox = word.BoundingBox;
                if (Intersects(wordBox, pdfRect))
                {
                    matchedWords.Add(word);
                }
            }

            _logger.LogInformation("Found {WordCount} words in selection area", matchedWords.Count);

            if (matchedWords.Count == 0)
            {
                _logger.LogWarning("No words found in selection area");
                return string.Empty;
            }

            // Group words into lines based on Y coordinate
            const double lineHeightThreshold = 5.0;
            var lines = new List<List<Word>>();

            foreach (var word in matchedWords)
            {
                var wordMid = (word.BoundingBox.Top + word.BoundingBox.Bottom) / 2.0;
                var line = lines.FirstOrDefault(l =>
                {
                    var lineMid = (l[0].BoundingBox.Top + l[0].BoundingBox.Bottom) / 2.0;
                    return Math.Abs(lineMid - wordMid) < lineHeightThreshold;
                });

                if (line == null)
                {
                    line = new List<Word>();
                    lines.Add(line);
                }
                line.Add(word);
            }

            // Sort lines top to bottom
            var sortedLines = lines.OrderByDescending(line =>
                (line[0].BoundingBox.Top + line[0].BoundingBox.Bottom) / 2.0).ToList();

            // Within each line, sort words left to right
            foreach (var line in sortedLines)
            {
                line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            }

            // Build text with proper spacing
            for (int i = 0; i < sortedLines.Count; i++)
            {
                var line = sortedLines[i];
                double lastRight = 0;

                for (int j = 0; j < line.Count; j++)
                {
                    var word = line[j];
                    var wordText = word.Text;

                    // Add spaces within concatenated words
                    // Check each letter to see if we need to insert spaces
                    var letters = word.Letters.ToList();
                    for (int k = 0; k < letters.Count; k++)
                    {
                        var letter = letters[k];
                        if (k > 0)
                        {
                            var prevLetter = letters[k - 1];
                            var gap = letter.GlyphRectangle.Left - prevLetter.GlyphRectangle.Right;
                            // If gap is larger than typical letter spacing, add a space
                            // Threshold: 2.5 points (typical word spacing is 3-4 points)
                            if (gap > 2.5)
                            {
                                extractedText.Append(' ');
                            }
                        }
                        else if (lastRight > 0)
                        {
                            // Check gap from previous word
                            var gap = letter.GlyphRectangle.Left - lastRight;
                            if (gap > 2.5)
                            {
                                extractedText.Append(' ');
                            }
                        }

                        extractedText.Append(letter.Value);
                        lastRight = letter.GlyphRectangle.Right;
                    }
                }

                // Add newline after line (except last)
                if (i < sortedLines.Count - 1)
                    extractedText.AppendLine();
            }

            var result = extractedText.ToString().Trim();
            _logger.LogInformation(
                "Text extraction complete: {WordCount} words extracted, {CharCount} characters in result",
                matchedWords.Count, result.Length);

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
    /// Check if two rectangles intersect
    /// </summary>
    private bool Intersects(UglyToad.PdfPig.Core.PdfRectangle a, UglyToad.PdfPig.Core.PdfRectangle b)
    {
        return !(a.Right < b.Left || a.Left > b.Right ||
                 a.Top < b.Bottom || a.Bottom > b.Top);
    }
}
