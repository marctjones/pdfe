using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
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

    // Search Properties
    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            // Auto-search as user types (with debounce in production)
            if (!string.IsNullOrWhiteSpace(value))
            {
                Task.Run(() => PerformSearch());
            }
            else
            {
                ClearSearch();
            }
        }
    }

    public bool SearchCaseSensitive
    {
        get => _searchCaseSensitive;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchCaseSensitive, value);
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                Task.Run(() => PerformSearch());
            }
        }
    }

    public bool SearchWholeWords
    {
        get => _searchWholeWords;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchWholeWords, value);
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                Task.Run(() => PerformSearch());
            }
        }
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
        set => this.RaiseAndSetIfChanged(ref _isSearchVisible, value);
    }

    // Search Commands
    public ReactiveCommand<Unit, Unit>? ToggleSearchCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? FindNextCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? FindPreviousCommand { get; private set; }
    public ReactiveCommand<Unit, Unit>? CloseSearchCommand { get; private set; }

    /// <summary>
    /// Initialize search commands (call from main constructor)
    /// </summary>
    private void InitializeSearchCommands()
    {
        ToggleSearchCommand = ReactiveCommand.Create(ToggleSearch);
        FindNextCommand = ReactiveCommand.Create(FindNext);
        FindPreviousCommand = ReactiveCommand.Create(FindPrevious);
        CloseSearchCommand = ReactiveCommand.Create(CloseSearch);
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
    /// Perform search with current settings
    /// </summary>
    private void PerformSearch()
    {
        if (_searchService == null || string.IsNullOrEmpty(_currentFilePath))
            return;

        try
        {
            _logger.LogInformation("Searching for '{SearchText}' (CaseSensitive={CaseSensitive}, WholeWords={WholeWords})",
                SearchText, SearchCaseSensitive, SearchWholeWords);

            var matches = _searchService.Search(
                _currentFilePath,
                SearchText,
                SearchCaseSensitive,
                SearchWholeWords,
                useRegex: false);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                SearchMatches.Clear();
                foreach (var match in matches)
                {
                    SearchMatches.Add(match);
                }

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
            });

            _logger.LogInformation("Found {MatchCount} matches", matches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing search: {Message}", ex.Message);
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

            // Convert each match to screen coordinates
            // The SearchMatch has PDF coordinates (bottom-left origin)
            // We need to convert to Avalonia coordinates (top-left origin)
            // Then scale by render DPI (150 DPI rendered / 72 DPI PDF = 2.083x)
            var dpiScale = 150.0 / 72.0;

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
