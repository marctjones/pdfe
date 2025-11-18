using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PdfEditor.Services;

/// <summary>
/// Service for searching text within PDF documents
/// </summary>
public class PdfSearchService
{
    private readonly ILogger<PdfSearchService> _logger;

    public PdfSearchService(ILogger<PdfSearchService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Represents a single search match
    /// </summary>
    public class SearchMatch
    {
        public int PageIndex { get; set; }
        public string MatchedText { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Context { get; set; } = string.Empty; // Surrounding text for preview
    }

    /// <summary>
    /// Search for text in a PDF file
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file</param>
    /// <param name="searchTerm">Text to search for</param>
    /// <param name="caseSensitive">Whether the search should be case sensitive</param>
    /// <param name="wholeWordsOnly">Whether to match whole words only</param>
    /// <param name="useRegex">Whether to treat searchTerm as a regular expression</param>
    /// <returns>List of search matches</returns>
    public List<SearchMatch> Search(
        string pdfPath,
        string searchTerm,
        bool caseSensitive = false,
        bool wholeWordsOnly = false,
        bool useRegex = false)
    {
        var matches = new List<SearchMatch>();

        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            _logger.LogWarning("Search term is empty");
            return matches;
        }

        try
        {
            using var document = PdfDocument.Open(pdfPath);

            _logger.LogInformation("Searching for '{SearchTerm}' in PDF with {PageCount} pages",
                searchTerm, document.NumberOfPages);

            for (int i = 0; i < document.NumberOfPages; i++)
            {
                var page = document.GetPage(i + 1); // PdfPig uses 1-based indexing
                var pageMatches = SearchInPage(page, searchTerm, caseSensitive, wholeWordsOnly, useRegex, i);
                matches.AddRange(pageMatches);
            }

            _logger.LogInformation("Found {MatchCount} matches for '{SearchTerm}'",
                matches.Count, searchTerm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching PDF: {Message}", ex.Message);
        }

        return matches;
    }

    /// <summary>
    /// Search for text in a specific page
    /// </summary>
    public List<SearchMatch> SearchInPage(
        Page page,
        string searchTerm,
        bool caseSensitive = false,
        bool wholeWordsOnly = false,
        bool useRegex = false,
        int pageIndex = 0)
    {
        var matches = new List<SearchMatch>();

        try
        {
            var pageText = page.Text;
            var words = page.GetWords().ToList();

            if (useRegex)
            {
                matches.AddRange(SearchWithRegex(pageText, searchTerm, words, pageIndex, caseSensitive));
            }
            else if (wholeWordsOnly)
            {
                matches.AddRange(SearchWholeWords(words, searchTerm, pageIndex, caseSensitive));
            }
            else
            {
                matches.AddRange(SearchSubstring(pageText, words, searchTerm, pageIndex, caseSensitive));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching page {PageIndex}: {Message}",
                pageIndex, ex.Message);
        }

        return matches;
    }

    /// <summary>
    /// Search using regular expression
    /// </summary>
    private List<SearchMatch> SearchWithRegex(
        string pageText,
        string pattern,
        List<Word> words,
        int pageIndex,
        bool caseSensitive)
    {
        var matches = new List<SearchMatch>();

        try
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(pattern, options);
            var regexMatches = regex.Matches(pageText);

            foreach (Match match in regexMatches)
            {
                // Find word positions for this match
                var matchedWords = FindWordsAtPosition(words, pageText, match.Index, match.Length);

                if (matchedWords.Any())
                {
                    var firstWord = matchedWords.First();
                    var lastWord = matchedWords.Last();

                    matches.Add(new SearchMatch
                    {
                        PageIndex = pageIndex,
                        MatchedText = match.Value,
                        X = firstWord.BoundingBox.Left,
                        Y = firstWord.BoundingBox.Bottom,
                        Width = lastWord.BoundingBox.Right - firstWord.BoundingBox.Left,
                        Height = Math.Max(firstWord.BoundingBox.Height, lastWord.BoundingBox.Height),
                        Context = GetContext(pageText, match.Index, match.Length)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid regex pattern: {Pattern}", pattern);
        }

        return matches;
    }

    /// <summary>
    /// Search for whole words only
    /// </summary>
    private List<SearchMatch> SearchWholeWords(
        List<Word> words,
        string searchTerm,
        int pageIndex,
        bool caseSensitive)
    {
        var matches = new List<SearchMatch>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var word in words)
        {
            if (word.Text.Equals(searchTerm, comparison))
            {
                matches.Add(new SearchMatch
                {
                    PageIndex = pageIndex,
                    MatchedText = word.Text,
                    X = word.BoundingBox.Left,
                    Y = word.BoundingBox.Bottom,
                    Width = word.BoundingBox.Width,
                    Height = word.BoundingBox.Height,
                    Context = $"...{word.Text}..."
                });
            }
        }

        return matches;
    }

    /// <summary>
    /// Search for substring matches
    /// </summary>
    private List<SearchMatch> SearchSubstring(
        string pageText,
        List<Word> words,
        string searchTerm,
        int pageIndex,
        bool caseSensitive)
    {
        var matches = new List<SearchMatch>();
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int index = 0;
        while ((index = pageText.IndexOf(searchTerm, index, comparison)) != -1)
        {
            // Find words that contain this match
            var matchedWords = FindWordsAtPosition(words, pageText, index, searchTerm.Length);

            if (matchedWords.Any())
            {
                var firstWord = matchedWords.First();
                var lastWord = matchedWords.Last();

                matches.Add(new SearchMatch
                {
                    PageIndex = pageIndex,
                    MatchedText = pageText.Substring(index, searchTerm.Length),
                    X = firstWord.BoundingBox.Left,
                    Y = firstWord.BoundingBox.Bottom,
                    Width = lastWord.BoundingBox.Right - firstWord.BoundingBox.Left,
                    Height = Math.Max(firstWord.BoundingBox.Height, lastWord.BoundingBox.Height),
                    Context = GetContext(pageText, index, searchTerm.Length)
                });
            }

            index += searchTerm.Length;
        }

        return matches;
    }

    /// <summary>
    /// Find words at a specific text position
    /// </summary>
    private List<Word> FindWordsAtPosition(List<Word> words, string pageText, int startIndex, int length)
    {
        var matchedWords = new List<Word>();
        int currentPos = 0;

        foreach (var word in words)
        {
            int wordStart = pageText.IndexOf(word.Text, currentPos, StringComparison.Ordinal);

            if (wordStart == -1)
                continue;

            int wordEnd = wordStart + word.Text.Length;

            // Check if this word overlaps with the match
            if (wordStart < startIndex + length && wordEnd > startIndex)
            {
                matchedWords.Add(word);
            }

            currentPos = wordEnd;

            // Stop if we've passed the match
            if (wordStart > startIndex + length)
                break;
        }

        return matchedWords;
    }

    /// <summary>
    /// Get surrounding context for a match
    /// </summary>
    private string GetContext(string pageText, int matchIndex, int matchLength, int contextChars = 30)
    {
        int start = Math.Max(0, matchIndex - contextChars);
        int end = Math.Min(pageText.Length, matchIndex + matchLength + contextChars);

        string prefix = start > 0 ? "..." : "";
        string suffix = end < pageText.Length ? "..." : "";

        return prefix + pageText.Substring(start, end - start) + suffix;
    }
}
