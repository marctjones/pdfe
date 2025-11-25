using Avalonia;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Service for batch redaction operations
/// </summary>
public class BatchRedactService
{
    private readonly ILogger<BatchRedactService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public BatchRedactService(ILogger<BatchRedactService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Redact all matches in a document
    /// </summary>
    public BatchRedactionResult RedactMatches(
        PdfDocument document,
        List<TextMatch> matches,
        RedactionOptions options)
    {
        var result = new BatchRedactionResult();
        var sw = Stopwatch.StartNew();

        try
        {
            result.TotalMatches = matches.Count;

            // Group matches by page
            var matchesByPage = matches
                .GroupBy(m => m.PageNumber)
                .ToDictionary(g => g.Key, g => g.ToList());

            var redactionService = new RedactionService(
                _loggerFactory.CreateLogger<RedactionService>(),
                _loggerFactory);

            foreach (var pageGroup in matchesByPage)
            {
                var pageIndex = pageGroup.Key - 1; // Convert to 0-based
                if (pageIndex < 0 || pageIndex >= document.PageCount)
                {
                    _logger.LogWarning("Invalid page number {Page}, skipping", pageGroup.Key);
                    continue;
                }

                var page = document.Pages[pageIndex];
                var pageMatches = pageGroup.Value;

                foreach (var match in pageMatches)
                {
                    try
                    {
                        // Redact at 72 DPI (PDF native)
                        redactionService.RedactArea(page, match.BoundingBox, 72);
                        result.RedactedCount++;

                        // Track per-page stats
                        if (!result.MatchesPerPage.ContainsKey(pageGroup.Key))
                            result.MatchesPerPage[pageGroup.Key] = 0;
                        result.MatchesPerPage[pageGroup.Key]++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        result.Errors.Add(new RedactionError
                        {
                            PageNumber = pageGroup.Key,
                            MatchedText = match.MatchedText,
                            Error = ex.Message
                        });
                        _logger.LogWarning(ex, "Failed to redact match \"{Text}\" on page {Page}",
                            match.MatchedText, pageGroup.Key);
                    }
                }
            }

            // Sanitize metadata if requested
            if (options.SanitizeMetadata)
            {
                redactionService.SanitizeDocumentMetadata(document);
            }

            result.Success = result.FailedCount == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch redaction failed");
            result.Success = false;
            result.Errors.Add(new RedactionError
            {
                Error = $"Batch redaction failed: {ex.Message}"
            });
        }

        sw.Stop();
        result.Duration = sw.Elapsed;

        _logger.LogInformation(
            "Batch redaction complete. Total: {Total}, Redacted: {Redacted}, Failed: {Failed}, Duration: {Duration}ms",
            result.TotalMatches, result.RedactedCount, result.FailedCount, result.Duration.TotalMilliseconds);

        return result;
    }

    /// <summary>
    /// Search and redact in one operation
    /// </summary>
    public BatchRedactionResult SearchAndRedact(
        PdfDocument document,
        string pattern,
        SearchOptions searchOptions,
        RedactionOptions redactionOptions)
    {
        var searchService = new TextSearchService(
            _loggerFactory.CreateLogger<TextSearchService>());

        // Find matches
        List<TextMatch> matches;
        if (searchOptions.UseRegex)
        {
            matches = searchService.FindPattern(document, pattern, searchOptions);
        }
        else
        {
            matches = searchService.FindText(document, pattern, searchOptions);
        }

        _logger.LogInformation("Found {Count} matches for pattern \"{Pattern}\"", matches.Count, pattern);

        // Redact matches
        return RedactMatches(document, matches, redactionOptions);
    }

    /// <summary>
    /// Redact all PII of specified types
    /// </summary>
    public BatchRedactionResult RedactAllPII(
        PdfDocument document,
        PIIType[] piiTypes,
        RedactionOptions options)
    {
        var searchService = new TextSearchService(
            _loggerFactory.CreateLogger<TextSearchService>());

        var allMatches = new List<TextMatch>();

        foreach (var piiType in piiTypes)
        {
            var matches = searchService.FindPII(document, piiType);
            allMatches.AddRange(matches);
            _logger.LogInformation("Found {Count} {Type} matches", matches.Count, piiType);
        }

        // Remove duplicates (overlapping matches)
        allMatches = RemoveOverlappingMatches(allMatches);

        _logger.LogInformation("Total PII matches to redact: {Count}", allMatches.Count);

        return RedactMatches(document, allMatches, options);
    }

    /// <summary>
    /// Remove overlapping matches, keeping the longer match
    /// </summary>
    private List<TextMatch> RemoveOverlappingMatches(List<TextMatch> matches)
    {
        if (matches.Count <= 1)
            return matches;

        var result = new List<TextMatch>();
        var sortedMatches = matches
            .OrderBy(m => m.PageNumber)
            .ThenBy(m => m.StartIndex)
            .ToList();

        TextMatch? current = null;
        foreach (var match in sortedMatches)
        {
            if (current == null)
            {
                current = match;
                continue;
            }

            // Check if overlapping on same page
            if (match.PageNumber == current.PageNumber &&
                match.StartIndex < current.EndIndex)
            {
                // Keep the longer match
                if (match.MatchedText.Length > current.MatchedText.Length)
                {
                    current = match;
                }
            }
            else
            {
                result.Add(current);
                current = match;
            }
        }

        if (current != null)
            result.Add(current);

        return result;
    }
}

/// <summary>
/// Result of a batch redaction operation
/// </summary>
public class BatchRedactionResult
{
    public bool Success { get; set; }
    public int TotalMatches { get; set; }
    public int RedactedCount { get; set; }
    public int FailedCount { get; set; }
    public List<RedactionError> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public Dictionary<int, int> MatchesPerPage { get; set; } = new();
}

/// <summary>
/// Error during redaction
/// </summary>
public class RedactionError
{
    public int PageNumber { get; set; }
    public string MatchedText { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}
