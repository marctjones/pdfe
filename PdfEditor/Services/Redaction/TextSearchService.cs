using Avalonia;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Service for searching text within PDF documents with position information
/// </summary>
public class TextSearchService
{
    private readonly ILogger<TextSearchService> _logger;

    public TextSearchService(ILogger<TextSearchService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Find all occurrences of text in document with their bounding boxes
    /// </summary>
    public List<TextMatch> FindText(PdfSharp.Pdf.PdfDocument document, string searchText, SearchOptions options)
    {
        if (string.IsNullOrEmpty(searchText))
            return new List<TextMatch>();

        _logger.LogInformation("Searching for text: \"{Text}\" with options: CaseSensitive={Case}, WholeWord={Whole}",
            searchText, options.CaseSensitive, options.WholeWord);

        var matches = new List<TextMatch>();

        try
        {
            // Save document to temp file for PdfPig reading
            var tempPath = System.IO.Path.GetTempFileName();
            try
            {
                // Save to memory stream first to avoid marking the document as "saved"
                // PdfSharp marks documents as read-only after Save() is called
                using var memoryStream = new System.IO.MemoryStream();
                document.Save(memoryStream, false); // false = don't close the stream
                System.IO.File.WriteAllBytes(tempPath, memoryStream.ToArray());

                using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(tempPath);
                var pagesToSearch = GetPagesToSearch(pdfPigDoc.NumberOfPages, options.PageRange);

                foreach (var pageNum in pagesToSearch)
                {
                    if (pageNum < 1 || pageNum > pdfPigDoc.NumberOfPages)
                        continue;

                    var page = pdfPigDoc.GetPage(pageNum);
                    var pageMatches = FindTextOnPage(page, pageNum, searchText, options);
                    matches.AddRange(pageMatches);

                    if (options.MaxResults > 0 && matches.Count >= options.MaxResults)
                    {
                        matches = matches.Take(options.MaxResults).ToList();
                        break;
                    }
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for text in document");
        }

        _logger.LogInformation("Found {Count} matches for \"{Text}\"", matches.Count, searchText);
        return matches;
    }

    /// <summary>
    /// Find all matches for a regex pattern
    /// </summary>
    public List<TextMatch> FindPattern(PdfSharp.Pdf.PdfDocument document, string regexPattern, SearchOptions options)
    {
        options.UseRegex = true;
        return FindText(document, regexPattern, options);
    }

    /// <summary>
    /// Find all instances of a specific PII type
    /// </summary>
    public List<TextMatch> FindPII(PdfSharp.Pdf.PdfDocument document, PIIType piiType)
    {
        var matcher = new PIIPatternMatcher(_logger as ILogger<PIIPatternMatcher> ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PIIPatternMatcher>.Instance);

        var matches = new List<TextMatch>();

        try
        {
            var tempPath = System.IO.Path.GetTempFileName();
            try
            {
                // Save to memory stream first to avoid marking the document as "saved"
                // PdfSharp marks documents as read-only after Save() is called
                using var memoryStream = new System.IO.MemoryStream();
                document.Save(memoryStream, false); // false = don't close the stream
                System.IO.File.WriteAllBytes(tempPath, memoryStream.ToArray());

                using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(tempPath);

                for (int pageNum = 1; pageNum <= pdfPigDoc.NumberOfPages; pageNum++)
                {
                    var page = pdfPigDoc.GetPage(pageNum);
                    var pageText = page.Text;
                    var pageHeight = page.Height;

                    // Find PII matches in text
                    var piiMatches = matcher.FindPII(pageText, piiType);

                    // Map text matches to bounding boxes
                    // Issue #95: Expand PII matches to word boundaries to prevent context leakage
                    foreach (var piiMatch in piiMatches)
                    {
                        var bounds = FindTextBounds(page, piiMatch.MatchedText, piiMatch.StartIndex, expandToWord: true);
                        if (bounds.HasValue)
                        {
                            matches.Add(new TextMatch
                            {
                                PageNumber = pageNum,
                                BoundingBox = ConvertToAvaloniaRect(bounds.Value, pageHeight),
                                MatchedText = piiMatch.MatchedText,
                                StartIndex = piiMatch.StartIndex,
                                EndIndex = piiMatch.EndIndex,
                                Confidence = piiMatch.Confidence,
                                PIIType = piiType
                            });
                        }
                    }
                }
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for PII type {Type}", piiType);
        }

        _logger.LogInformation("Found {Count} {Type} matches", matches.Count, piiType);
        return matches;
    }

    /// <summary>
    /// Find all PII of any supported type
    /// </summary>
    public List<TextMatch> FindAllPII(PdfSharp.Pdf.PdfDocument document)
    {
        var allMatches = new List<TextMatch>();

        foreach (PIIType piiType in Enum.GetValues<PIIType>())
        {
            if (piiType == PIIType.Custom)
                continue;

            var matches = FindPII(document, piiType);
            allMatches.AddRange(matches);
        }

        // Sort by page then position
        return allMatches
            .OrderBy(m => m.PageNumber)
            .ThenBy(m => m.BoundingBox.Y)
            .ThenBy(m => m.BoundingBox.X)
            .ToList();
    }

    /// <summary>
    /// Find PII in a PDF file (file-based version that doesn't mark PdfSharp documents as saved)
    /// </summary>
    public List<TextMatch> FindPIIInFile(string filePath, PIIType piiType)
    {
        var matcher = new PIIPatternMatcher(_logger as ILogger<PIIPatternMatcher> ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PIIPatternMatcher>.Instance);

        var matches = new List<TextMatch>();

        try
        {
            using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath);

            for (int pageNum = 1; pageNum <= pdfPigDoc.NumberOfPages; pageNum++)
            {
                var page = pdfPigDoc.GetPage(pageNum);
                var pageText = page.Text;
                var pageHeight = page.Height;

                // Find PII matches in text
                var piiMatches = matcher.FindPII(pageText, piiType);

                // Map text matches to bounding boxes
                // Issue #95: Expand PII matches to word boundaries to prevent context leakage
                foreach (var piiMatch in piiMatches)
                {
                    var bounds = FindTextBounds(page, piiMatch.MatchedText, piiMatch.StartIndex, expandToWord: true);
                    if (bounds.HasValue)
                    {
                        matches.Add(new TextMatch
                        {
                            PageNumber = pageNum,
                            BoundingBox = ConvertToAvaloniaRect(bounds.Value, pageHeight),
                            MatchedText = piiMatch.MatchedText,
                            StartIndex = piiMatch.StartIndex,
                            EndIndex = piiMatch.EndIndex,
                            Confidence = piiMatch.Confidence,
                            PIIType = piiType
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for PII type {Type} in file {File}", piiType, filePath);
        }

        _logger.LogInformation("Found {Count} {Type} matches in {File}", matches.Count, piiType, System.IO.Path.GetFileName(filePath));
        return matches;
    }

    /// <summary>
    /// Find text in a PDF file (file-based version that doesn't mark PdfSharp documents as saved)
    /// </summary>
    public List<TextMatch> FindTextInFile(string filePath, string searchText, SearchOptions options)
    {
        if (string.IsNullOrEmpty(searchText))
            return new List<TextMatch>();

        _logger.LogInformation("Searching file {File} for text: \"{Text}\"",
            System.IO.Path.GetFileName(filePath), searchText);

        var matches = new List<TextMatch>();

        try
        {
            using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath);
            var pagesToSearch = GetPagesToSearch(pdfPigDoc.NumberOfPages, options.PageRange);

            foreach (var pageNum in pagesToSearch)
            {
                if (pageNum < 1 || pageNum > pdfPigDoc.NumberOfPages)
                    continue;

                var page = pdfPigDoc.GetPage(pageNum);
                var pageMatches = FindTextOnPage(page, pageNum, searchText, options);
                matches.AddRange(pageMatches);

                if (options.MaxResults > 0 && matches.Count >= options.MaxResults)
                {
                    matches = matches.Take(options.MaxResults).ToList();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for text in file {File}", filePath);
        }

        _logger.LogInformation("Found {Count} matches for \"{Text}\" in {File}",
            matches.Count, searchText, System.IO.Path.GetFileName(filePath));
        return matches;
    }

    private List<TextMatch> FindTextOnPage(Page page, int pageNumber, string searchText, SearchOptions options)
    {
        var matches = new List<TextMatch>();
        var pageText = page.Text;
        var pageHeight = page.Height;

        if (string.IsNullOrEmpty(pageText))
            return matches;

        var comparison = options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // Issue #95: Determine if we should expand substring matches to word boundaries
        var expandToWord = options.ExpandSubstringToWord && !options.WholeWord;

        if (options.UseRegex)
        {
            // Regex search
            var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                var regex = new Regex(searchText, regexOptions);
                var regexMatches = regex.Matches(pageText);

                foreach (Match regexMatch in regexMatches)
                {
                    var bounds = FindTextBounds(page, regexMatch.Value, regexMatch.Index, expandToWord);
                    if (bounds.HasValue)
                    {
                        matches.Add(new TextMatch
                        {
                            PageNumber = pageNumber,
                            BoundingBox = ConvertToAvaloniaRect(bounds.Value, pageHeight),
                            MatchedText = regexMatch.Value,
                            StartIndex = regexMatch.Index,
                            EndIndex = regexMatch.Index + regexMatch.Length,
                            Confidence = 1.0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", searchText);
            }
        }
        else
        {
            // Simple text search
            var searchIn = options.CaseSensitive ? pageText : pageText.ToLower();
            var searchFor = options.CaseSensitive ? searchText : searchText.ToLower();

            int index = 0;
            while ((index = searchIn.IndexOf(searchFor, index, comparison)) != -1)
            {
                var matchedText = pageText.Substring(index, searchText.Length);

                // Check whole word if required
                if (options.WholeWord && !IsWholeWord(pageText, index, searchText.Length))
                {
                    index++;
                    continue;
                }

                var bounds = FindTextBounds(page, matchedText, index, expandToWord);
                if (bounds.HasValue)
                {
                    matches.Add(new TextMatch
                    {
                        PageNumber = pageNumber,
                        BoundingBox = ConvertToAvaloniaRect(bounds.Value, pageHeight),
                        MatchedText = matchedText,
                        StartIndex = index,
                        EndIndex = index + searchText.Length,
                        Confidence = 1.0
                    });
                }

                index++;
            }
        }

        return matches;
    }

    private UglyToad.PdfPig.Core.PdfRectangle? FindTextBounds(Page page, string text, int startIndex, bool expandToWord = true)
    {
        try
        {
            var words = page.GetWords().ToList();
            var letters = page.Letters.ToList();

            // Try to find the text among letters
            if (letters.Count > 0 && startIndex < letters.Count)
            {
                // Find letters that match our text
                var matchingLetters = new List<Letter>();
                var currentText = "";
                var currentIndex = 0;

                foreach (var letter in letters)
                {
                    currentText += letter.Value;

                    if (currentIndex >= startIndex && currentIndex < startIndex + text.Length)
                    {
                        matchingLetters.Add(letter);
                    }

                    currentIndex++;

                    if (currentIndex >= startIndex + text.Length)
                        break;
                }

                if (matchingLetters.Count > 0)
                {
                    // Issue #95: If expandToWord is true, find the containing word
                    // and use its bounding box to prevent context leakage
                    if (expandToWord)
                    {
                        var containingWord = FindContainingWord(words, matchingLetters.First());
                        if (containingWord != null && containingWord.Text.Length > text.Length)
                        {
                            _logger.LogDebug("Expanding substring match '{Match}' to word '{Word}' to prevent context leakage",
                                text, containingWord.Text);
                            return containingWord.BoundingBox;
                        }
                    }

                    var minX = matchingLetters.Min(l => l.GlyphRectangle.Left);
                    var minY = matchingLetters.Min(l => l.GlyphRectangle.Bottom);
                    var maxX = matchingLetters.Max(l => l.GlyphRectangle.Right);
                    var maxY = matchingLetters.Max(l => l.GlyphRectangle.Top);

                    return new UglyToad.PdfPig.Core.PdfRectangle(minX, minY, maxX, maxY);
                }
            }

            // Fallback: try word-based matching
            foreach (var word in words)
            {
                if (word.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    return word.BoundingBox;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Find the word that contains the given letter (issue #95).
    /// </summary>
    private Word? FindContainingWord(List<Word> words, Letter letter)
    {
        foreach (var word in words)
        {
            // Check if the letter's position falls within the word's bounding box
            var letterCenter = new UglyToad.PdfPig.Core.PdfPoint(
                (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2,
                (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2);

            if (word.BoundingBox.Left <= letterCenter.X && letterCenter.X <= word.BoundingBox.Right &&
                word.BoundingBox.Bottom <= letterCenter.Y && letterCenter.Y <= word.BoundingBox.Top)
            {
                return word;
            }
        }
        return null;
    }

    private Rect ConvertToAvaloniaRect(UglyToad.PdfPig.Core.PdfRectangle pdfRect, double pageHeight)
    {
        // PDF uses bottom-left origin, Avalonia uses top-left
        var y = pageHeight - pdfRect.Top;
        return new Rect(pdfRect.Left, y, pdfRect.Width, pdfRect.Height);
    }

    private bool IsWholeWord(string text, int index, int length)
    {
        // Check character before
        if (index > 0 && char.IsLetterOrDigit(text[index - 1]))
            return false;

        // Check character after
        if (index + length < text.Length && char.IsLetterOrDigit(text[index + length]))
            return false;

        return true;
    }

    private IEnumerable<int> GetPagesToSearch(int totalPages, List<int>? pageRange)
    {
        if (pageRange == null || pageRange.Count == 0)
        {
            return Enumerable.Range(1, totalPages);
        }
        return pageRange.Where(p => p >= 1 && p <= totalPages);
    }
}

/// <summary>
/// Represents a text match found during search
/// </summary>
public class TextMatch
{
    public int PageNumber { get; set; }
    public Rect BoundingBox { get; set; }
    public string MatchedText { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public double Confidence { get; set; } = 1.0;
    public PIIType? PIIType { get; set; }
}

/// <summary>
/// Options for text searching
/// </summary>
public class SearchOptions
{
    public bool CaseSensitive { get; set; } = false;
    public bool WholeWord { get; set; } = false;
    public bool UseRegex { get; set; } = false;
    public int MaxResults { get; set; } = 1000;
    public List<int>? PageRange { get; set; }

    /// <summary>
    /// When true, redaction of a substring match will expand to cover the entire
    /// containing word to prevent context leakage (issue #95).
    /// Default is true for security.
    /// </summary>
    public bool ExpandSubstringToWord { get; set; } = true;
}

/// <summary>
/// Types of personally identifiable information
/// </summary>
public enum PIIType
{
    SSN,
    Email,
    Phone,
    CreditCard,
    DateOfBirth,
    Address,
    Custom
}
