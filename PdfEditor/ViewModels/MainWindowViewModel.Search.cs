using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace PdfEditor.ViewModels;

/// <summary>
/// Search-related functionality for MainWindowViewModel
/// </summary>
public partial class MainWindowViewModel
{
    private readonly PdfSearchService? _searchService;
    private string _searchText = string.Empty;
    private bool _searchCaseSensitive = false;
    private bool _searchWholeWords = false;
    private int _currentSearchMatchIndex = -1;
    private ObservableCollection<PdfSearchService.SearchMatch> _searchMatches = new();
    private bool _isSearchVisible = false;

    // Debounce + cancellation for incremental ("search-as-you-type")
    // queries. Pre-fix every keystroke kicked off a fresh search that
    // re-opened and re-parsed the PDF; on a 455-page book each one took
    // ~30 s, so by the time the user finished typing they had a queue of
    // overlapping searches and the foreground felt unresponsive.
    private CancellationTokenSource? _searchCts;
    // Pause-after-typing window before kicking a search. 300 ms felt
    // sluggish; 150 ms is short enough to feel "live" but still cancels
    // intermediate keystrokes when typing a multi-letter word at speed.
    private const int SearchDebounceMs = 150;

    private bool _isSearching;
    private string _searchProgressText = string.Empty;

    /// <summary>True while a search is in flight. Drives the inline spinner.</summary>
    public bool IsSearching
    {
        get => _isSearching;
        private set => this.RaiseAndSetIfChanged(ref _isSearching, value);
    }

    /// <summary>
    /// "Searching page 47 of 455 — 12 matches so far" while a search is
    /// running, empty otherwise. Drives the inline progress text in the
    /// search bar.
    /// </summary>
    public string SearchProgressText
    {
        get => _searchProgressText;
        private set => this.RaiseAndSetIfChanged(ref _searchProgressText, value);
    }

    // Search Properties
    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            ScheduleSearchDebounced();
        }
    }

    public bool SearchCaseSensitive
    {
        get => _searchCaseSensitive;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchCaseSensitive, value);
            ScheduleSearchDebounced();
        }
    }

    public bool SearchWholeWords
    {
        get => _searchWholeWords;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchWholeWords, value);
            ScheduleSearchDebounced();
        }
    }

    /// <summary>
    /// Cancel any pending/in-flight search, then schedule a new one
    /// after a short debounce delay. If the user keeps typing, the
    /// delay timer resets so we only actually run once they pause.
    /// </summary>
    private void ScheduleSearchDebounced()
    {
        _searchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            ClearSearch();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;
        OperationStatus = "Searching…";

        // Capture the search inputs at scheduling time so a later
        // keystroke doesn't change them out from under us.
        var query = _searchText;
        var caseSensitive = _searchCaseSensitive;
        var wholeWords = _searchWholeWords;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SearchDebounceMs, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                PerformSearch(query, caseSensitive, wholeWords, token);
            }
            catch (OperationCanceledException) { /* superseded */ }
        });
    }

    public ObservableCollection<PdfSearchService.SearchMatch> SearchMatches
    {
        get => _searchMatches;
        set => this.RaiseAndSetIfChanged(ref _searchMatches, value);
    }

    public int CurrentSearchMatchIndex
    {
        get => _currentSearchMatchIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentSearchMatchIndex, value);
            this.RaisePropertyChanged(nameof(SearchResultText));
        }
    }

    public string SearchResultText
    {
        get
        {
            if (SearchMatches.Count == 0)
                return "No matches";

            if (CurrentSearchMatchIndex >= 0 && CurrentSearchMatchIndex < SearchMatches.Count)
                return $"{CurrentSearchMatchIndex + 1} of {SearchMatches.Count}";

            return $"{SearchMatches.Count} matches";
        }
    }

    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSearchVisible, value);
            // Right-sidebar panel selection depends on this flag.
            this.RaisePropertyChanged(nameof(ShowSearchResultsPanel));
            this.RaisePropertyChanged(nameof(ShowPendingRedactionsPanel));
            this.RaisePropertyChanged(nameof(ShowClipboardHistoryPanel));
        }
    }

    /// <summary>
    /// Sidebar mode selectors. The right sidebar shows exactly one panel
    /// at a time; computing the booleans here keeps the XAML readable
    /// (no MultiBinding gymnastics) and keeps invariants in one place.
    /// </summary>
    public bool ShowSearchResultsPanel => IsSearchVisible;
    public bool ShowPendingRedactionsPanel => IsRedactionMode && !IsSearchVisible;
    public bool ShowClipboardHistoryPanel => !IsRedactionMode && !IsSearchVisible;

    // Search Commands
    public ReactiveCommand<Unit, Unit>? ToggleSearchCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? FindNextCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? FindPreviousCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? CloseSearchCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? FindCommand { get; private set; }
    public ReactiveCommand<PdfSearchService.SearchMatch, Unit>? JumpToSearchMatchCommand { get; private set; }

    /// <summary>
    /// Initialize search commands (call from main constructor)
    /// </summary>
    private void InitializeSearchCommands()
    {
        ToggleSearchCommand = ReactiveCommand.Create(ToggleSearch);
        FindNextCommand = ReactiveCommand.Create(FindNext);
        FindPreviousCommand = ReactiveCommand.Create(FindPrevious);
        CloseSearchCommand = ReactiveCommand.Create(CloseSearch);
        // Manual "Find" trigger — same code path as type-and-pause but
        // bypasses the debounce. Bound to the Find button and to the
        // Enter key in the search box (handled in MainWindow.axaml.cs).
        FindCommand = ReactiveCommand.Create(FindNow);
        // Click on a row in the search-results sidebar.
        JumpToSearchMatchCommand =
            ReactiveCommand.Create<PdfSearchService.SearchMatch>(JumpToSearchMatch);
    }

    /// <summary>
    /// Run a search immediately (no debounce). Cancels any pending
    /// debounced search first so we don't double-search.
    /// </summary>
    public void FindNow()
    {
        _searchCts?.Cancel();
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            ClearSearch();
            return;
        }
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var token = cts.Token;
        OperationStatus = "Searching…";
        var query = _searchText;
        var caseSensitive = _searchCaseSensitive;
        var wholeWords = _searchWholeWords;
        Task.Run(() => PerformSearch(query, caseSensitive, wholeWords, token));
    }

    /// <summary>
    /// Toggle search bar visibility
    /// </summary>
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;

        if (IsSearchVisible)
        {
            _logger.LogInformation("Search activated");
        }
        else
        {
            CloseSearch();
        }
    }

    /// <summary>
    /// Close search and clear results
    /// </summary>
    private void CloseSearch()
    {
        IsSearchVisible = false;
        ClearSearch();
        _logger.LogInformation("Search closed");
    }

    /// <summary>
    /// Perform a search and publish matches to the UI. Reuses the
    /// already-open <see cref="PdfCoreDocument"/> so we don't pay the
    /// ~30 s parse cost per keystroke. The token lets a debounce-
    /// superseding query abort us mid-walk.
    /// </summary>
    private void PerformSearch(string query, bool caseSensitive, bool wholeWords,
        CancellationToken token)
    {
        if (_searchService == null) return;

        var doc = PdfCoreDocument;
        // Fall back to file-path-based search only when the in-memory
        // document isn't available (e.g. legacy code paths in tests).
        try
        {
            _logger.LogInformation(
                "Searching for '{Query}' (CaseSensitive={CaseSensitive}, WholeWords={WholeWords})",
                query, caseSensitive, wholeWords);

            // Show the spinner + progress text immediately. Both run on
            // the UI thread to avoid cross-thread RaisePropertyChanged.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSearching = true;
                SearchProgressText = "Searching…";
            });

            // Progress reports come on the search worker thread; marshal
            // each one to the UI thread to update the bound property.
            // The service throttles the callback rate so we don't flood
            // the dispatcher.
            var progress = new Progress<PdfSearchService.SearchProgress>(p =>
            {
                if (token.IsCancellationRequested) return;
                SearchProgressText = p.PagesScanned == 0
                    ? $"Searching… 0 of {p.TotalPages} pages"
                    : $"Searching… page {p.PagesScanned} of {p.TotalPages} — " +
                      $"{p.MatchesFound} match{(p.MatchesFound == 1 ? "" : "es")} so far";
            });

            System.Collections.Generic.List<PdfSearchService.SearchMatch> matches;
            // Prefer the pre-built text index when available — searches
            // become near-instant because per-page extraction has already
            // happened. Fall back to live extraction while the index is
            // still being built or for unindexed docs.
            var idx = TextIndex;
            if (idx != null && idx.IsReady)
            {
                matches = _searchService.Search(idx, query, caseSensitive, wholeWords,
                    useRegex: false, cancellationToken: token, progress: progress);
            }
            else if (doc != null)
            {
                matches = _searchService.Search(doc, query, caseSensitive, wholeWords,
                    useRegex: false, cancellationToken: token, progress: progress);
            }
            else if (!string.IsNullOrEmpty(_currentFilePath))
            {
                matches = _searchService.Search(_currentFilePath, query,
                    caseSensitive, wholeWords, useRegex: false, progress: progress);
            }
            else return;

            if (token.IsCancellationRequested) return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;

                SearchMatches.Clear();
                foreach (var match in matches)
                    SearchMatches.Add(match);

                if (SearchMatches.Count > 0)
                {
                    CurrentSearchMatchIndex = 0;
                    NavigateToSearchMatch(SearchMatches[0]);
                }
                else
                {
                    CurrentSearchMatchIndex = -1;
                }

                this.RaisePropertyChanged(nameof(SearchResultText));
                IsSearching = false;
                SearchProgressText = string.Empty;
                OperationStatus = string.Empty;
            });

            _logger.LogInformation("Found {MatchCount} matches", matches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search: {Message}", ex.Message);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                IsSearching = false;
                SearchProgressText = string.Empty;
                OperationStatus = string.Empty;
            });
        }
    }

    /// <summary>
    /// Navigate to next search match
    /// </summary>
    private void FindNext()
    {
        if (SearchMatches.Count == 0)
            return;

        CurrentSearchMatchIndex = (CurrentSearchMatchIndex + 1) % SearchMatches.Count;
        NavigateToSearchMatch(SearchMatches[CurrentSearchMatchIndex]);

        _logger.LogDebug("Navigated to next match: {Index} of {Total}",
            CurrentSearchMatchIndex + 1, SearchMatches.Count);
    }

    /// <summary>
    /// Navigate to previous search match
    /// </summary>
    private void FindPrevious()
    {
        if (SearchMatches.Count == 0)
            return;

        CurrentSearchMatchIndex = CurrentSearchMatchIndex <= 0
            ? SearchMatches.Count - 1
            : CurrentSearchMatchIndex - 1;

        NavigateToSearchMatch(SearchMatches[CurrentSearchMatchIndex]);

        _logger.LogDebug("Navigated to previous match: {Index} of {Total}",
            CurrentSearchMatchIndex + 1, SearchMatches.Count);
    }

    /// <summary>
    /// Public entry-point used by the search-results sidebar. Jumps the
    /// viewer to the page containing <paramref name="match"/> and selects
    /// it so the prev/next buttons resume from there.
    /// </summary>
    public void JumpToSearchMatch(PdfSearchService.SearchMatch match)
    {
        if (match == null) return;
        var index = SearchMatches.IndexOf(match);
        if (index < 0) return;
        CurrentSearchMatchIndex = index;
        NavigateToSearchMatch(match);
    }

    /// <summary>
    /// Navigate to a specific search match
    /// </summary>
    private void NavigateToSearchMatch(PdfSearchService.SearchMatch match)
    {
        // Navigate to the page containing the match
        if (match.PageIndex != CurrentPageIndex)
        {
            CurrentPageIndex = match.PageIndex;
            Task.Run(async () => await RenderCurrentPageAsync());
        }

        // Update search highlights for the current page
        UpdateSearchHighlights();

        _logger.LogInformation("Navigated to match on page {PageIndex}: '{Text}'",
            match.PageIndex + 1, match.MatchedText);
    }

    /// <summary>
    /// Update search highlight rectangles for the current page.
    /// Converts PDF coordinates to screen coordinates.
    /// </summary>
    public void UpdateSearchHighlights()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CurrentPageSearchHighlights.Clear();

            if (SearchMatches.Count == 0 || _documentService == null)
                return;

            // Get matches for current page
            var pageMatches = SearchMatches.Where(m => m.PageIndex == CurrentPageIndex).ToList();

            if (pageMatches.Count == 0)
                return;

            // Get page height for coordinate conversion (PDF uses bottom-left origin)
            var pageHeight = _documentService.GetPageHeight(CurrentPageIndex);

            // Convert each match to screen coordinates.
            // SearchMatch is in PDF points (bottom-left origin). Flip Y to
            // Avalonia (top-left), then scale to bitmap DIPs.
            //
            // The DPI here MUST match what PdfViewerControl actually renders
            // at — otherwise highlights drift relative to the page text.
            // Pre-fix this was hardcoded to 150 (the old PdfRenderService
            // default) but the viewer now uses 120, which made highlights
            // appear ~25% too far right and 25% too big — often clipped
            // off the page entirely, so search "looked broken".
            const double viewerRenderDpi = 120.0;
            var dpiScale = viewerRenderDpi / 72.0;

            foreach (var match in pageMatches)
            {
                // Convert Y from PDF (bottom-left) to Avalonia (top-left)
                var avaloniaY = pageHeight - match.Y - match.Height;

                // Scale to screen coordinates (150 DPI render)
                var screenRect = new Rect(
                    match.X * dpiScale,
                    avaloniaY * dpiScale,
                    match.Width * dpiScale,
                    match.Height * dpiScale
                );

                CurrentPageSearchHighlights.Add(screenRect);
            }

            _logger.LogDebug("Updated {Count} search highlights for page {Page}",
                CurrentPageSearchHighlights.Count, CurrentPageIndex + 1);
        });
    }

    /// <summary>
    /// Clear search results
    /// </summary>
    private void ClearSearch()
    {
        SearchMatches.Clear();
        CurrentSearchMatchIndex = -1;
        this.RaisePropertyChanged(nameof(SearchResultText));
    }
}
