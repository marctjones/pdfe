using Microsoft.Extensions.Logging;
using Pdfe.Core.Document;
using Pdfe.Core.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PdfEditor.Services;

/// <summary>
/// Service for searching text within PDF documents
/// </summary>
public class PdfSearchService
{
    private readonly ILogger<PdfSearchService> _logger;

    /// <summary>
    /// Number of pages per worker thread (configurable).
    /// Larger values = fewer threads, less context switching overhead.
    /// Smaller values = better load balancing but higher overhead.
    /// </summary>
    private const int PagesPerChunk = 8;

    /// <summary>
    /// Maximum degree of parallelism. Use Environment.ProcessorCount
    /// for full utilization, or cap at a reasonable limit to avoid
    /// excessive context switching.
    /// </summary>
    private static readonly int MaxDegreeOfParallelism =
        Math.Min(Environment.ProcessorCount, 4);

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
    /// Progress report emitted as the service walks the document.
    /// Consumed via IProgress&lt;SearchProgress&gt; — typical use is to
    /// drive a status bar / inline spinner / determinate progress
    /// indicator while a long search runs on a large file.
    /// </summary>
    public readonly record struct SearchProgress(
        int PagesScanned, int TotalPages, int MatchesFound);

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
        bool useRegex = false,
        IProgress<SearchProgress>? progress = null)
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
            return Search(document, searchTerm, caseSensitive, wholeWordsOnly, useRegex,
                progress: progress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching PDF: {Message}", ex.Message);
            return matches;
        }
    }

    /// <summary>
    /// Search for text in an already-open PDF document. Use this overload
    /// when the caller already holds a parsed <see cref="PdfDocument"/> —
    /// re-parsing on every keystroke makes typed search unusable on
    /// large files. The optional <paramref name="cancellationToken"/>
    /// lets a debounced caller drop a stale search when a newer
    /// keystroke supersedes it.
    /// </summary>
    /// <summary>
    /// Search using a pre-built <see cref="DocumentTextIndex"/>. Skips
    /// per-page text extraction (the dominant cost) so subsequent
    /// queries on the same document are nearly instant.
    /// </summary>
    public List<SearchMatch> Search(
        DocumentTextIndex index,
        string searchTerm,
        bool caseSensitive = false,
        bool wholeWordsOnly = false,
        bool useRegex = false,
        CancellationToken cancellationToken = default,
        IProgress<SearchProgress>? progress = null)
    {
        var matches = new List<SearchMatch>();
        if (index == null || string.IsNullOrWhiteSpace(searchTerm))
            return matches;

        try
        {
            var totalPages = index.PageCount;
            progress?.Report(new SearchProgress(0, totalPages, 0));

            for (int i = 0; i < totalPages; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!index.IsPageIndexed(i))
                {
                    // Index built incrementally — skip pages not yet cached.
                    // The next search after the build completes covers them.
                    continue;
                }
                var pageText = index.GetPageText(i);
                var pageWords = index.GetPageWords(i);
                if (useRegex)
                    matches.AddRange(SearchWithRegex(pageText, searchTerm, pageWords.ToList(), i, caseSensitive));
                else if (wholeWordsOnly)
                    matches.AddRange(SearchWholeWords(pageWords.ToList(), searchTerm, i, caseSensitive));
                else
                    matches.AddRange(SearchSubstring(pageText, pageWords.ToList(), searchTerm, i, caseSensitive));

                if (progress != null && ((i & 3) == 0 || i == totalPages - 1))
                    progress.Report(new SearchProgress(i + 1, totalPages, matches.Count));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Indexed search cancelled at page progress; returning partial results");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching index: {Message}", ex.Message);
        }
        return matches;
    }

    public List<SearchMatch> Search(
        PdfDocument document,
        string searchTerm,
        bool caseSensitive = false,
        bool wholeWordsOnly = false,
        bool useRegex = false,
        CancellationToken cancellationToken = default,
        IProgress<SearchProgress>? progress = null)
    {
        if (document == null || string.IsNullOrWhiteSpace(searchTerm))
            return new List<SearchMatch>();

        try
        {
            var totalPages = document.PageCount;
            _logger.LogInformation("Searching for '{SearchTerm}' in PDF with {PageCount} pages (parallel)",
                searchTerm, totalPages);

            // Initial 0/N report so the UI can show the spinner+text
            // immediately rather than waiting for page 1 to finish.
            progress?.Report(new SearchProgress(0, totalPages, 0));

            // Parallel search with bounded concurrency and progress tracking
            var allMatches = SearchParallel(
                document, searchTerm, caseSensitive, wholeWordsOnly, useRegex,
                totalPages, progress, cancellationToken);

            _logger.LogInformation("Found {MatchCount} matches for '{SearchTerm}'",
                allMatches.Count, searchTerm);

            return allMatches;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Search cancelled; returning partial results");
            return new List<SearchMatch>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching PDF: {Message}", ex.Message);
            return new List<SearchMatch>();
        }
    }

    /// <summary>
    /// Parallel search implementation using Parallel.For with bounded concurrency.
    /// Pages are processed in chunks; each worker searches its chunk independently.
    /// Results are collected in a thread-safe bag and sorted by (PageNumber, Index)
    /// before returning.
    /// </summary>
    private List<SearchMatch> SearchParallel(
        PdfDocument document,
        string searchTerm,
        bool caseSensitive,
        bool wholeWordsOnly,
        bool useRegex,
        int totalPages,
        IProgress<SearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var matchesBag = new ConcurrentBag<SearchMatch>();
        int pagesDone = 0;
        var pageDoneLock = new object();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        try
        {
            // Split pages into chunks and process in parallel
            var pageChunks = GetPageChunks(totalPages);

            Parallel.ForEach(pageChunks, parallelOptions, chunk =>
            {
                // Process each page in this chunk
                foreach (var pageIndex in chunk)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var page = document.GetPage(pageIndex + 1);
                        var pageMatches = SearchInPage(
                            page, searchTerm, caseSensitive, wholeWordsOnly, useRegex, pageIndex);

                        foreach (var match in pageMatches)
                        {
                            matchesBag.Add(match);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error searching page {PageIndex}", pageIndex);
                    }

                    // Update progress atomically — only every 4 pages to throttle callbacks
                    lock (pageDoneLock)
                    {
                        pagesDone++;
                        if ((pagesDone & 3) == 0 || pagesDone == totalPages)
                        {
                            progress?.Report(new SearchProgress(pagesDone, totalPages, matchesBag.Count));
                        }
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Parallel search cancelled");
        }

        // Sort matches by page number, then by position in page for deterministic ordering
        var sorted = matchesBag
            .OrderBy(m => m.PageIndex)
            .ThenBy(m => m.Y)
            .ThenBy(m => m.X)
            .ToList();

        return sorted;
    }

    /// <summary>
    /// Split pages into chunks for parallel processing.
    /// Each chunk contains up to PagesPerChunk pages.
    /// </summary>
    private List<List<int>> GetPageChunks(int totalPages)
    {
        var chunks = new List<List<int>>();
        for (int i = 0; i < totalPages; i += PagesPerChunk)
        {
            var chunkEnd = Math.Min(i + PagesPerChunk, totalPages);
            var chunk = Enumerable.Range(i, chunkEnd - i).ToList();
            chunks.Add(chunk);
        }
        return chunks;
    }

    /// <summary>
    /// Search for text in a specific page
    /// </summary>
    public List<SearchMatch> SearchInPage(
        PdfPage page,
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
    /// Find words at a specific text position.
    ///
    /// FIX for issue #96: The previous implementation used IndexOf to find word positions,
    /// which was unreliable when the same word appeared multiple times on a page.
    ///
    /// New approach: Build a character index map by iterating through words in order,
    /// tracking cumulative text positions. This ensures correct bounding box mapping
    /// even when duplicate words exist.
    /// </summary>
    private List<Word> FindWordsAtPosition(List<Word> words, string pageText, int startIndex, int length)
    {
        var matchedWords = new List<Word>();

        // Build character index map from words
        // This maps character index in pageText to the corresponding Word
        var wordAtIndex = new Dictionary<int, Word>();
        int charIndex = 0;

        foreach (var word in words)
        {
            // Find next occurrence of this word's text starting from current position
            // This handles duplicates correctly by advancing position after each match
            int wordPos = pageText.IndexOf(word.Text, charIndex, StringComparison.Ordinal);

            if (wordPos != -1)
            {
                // Map each character index to this word
                for (int i = 0; i < word.Text.Length; i++)
                {
                    wordAtIndex[wordPos + i] = word;
                }

                // Advance past this word (+ any whitespace/separator)
                charIndex = wordPos + word.Text.Length;
            }
        }

        // Now find all words that overlap with the search range [startIndex, startIndex+length)
        var matchEndIndex = startIndex + length;

        for (int i = startIndex; i < matchEndIndex && i < pageText.Length; i++)
        {
            if (wordAtIndex.TryGetValue(i, out var word))
            {
                if (!matchedWords.Contains(word))
                {
                    matchedWords.Add(word);
                }
            }
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
