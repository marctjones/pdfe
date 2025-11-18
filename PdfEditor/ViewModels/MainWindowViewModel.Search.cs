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

        // TODO: Scroll to the match position and highlight it
        // This would require adding scroll position control and highlight rendering

        _logger.LogInformation("Navigated to match on page {PageIndex}: '{Text}'",
            match.PageIndex + 1, match.MatchedText);
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
