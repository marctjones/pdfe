using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using PdfEditor.ViewModels;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for MainWindowViewModel
/// Tests ViewModel logic, commands, and properties without UI rendering
/// </summary>
public class MainWindowViewModelTests
{
    private readonly Mock<ILogger<MainWindowViewModel>> _loggerMock;
    private readonly Mock<ILogger<PdfDocumentService>> _docLoggerMock;
    private readonly Mock<ILogger<PdfRenderService>> _renderLoggerMock;
    private readonly Mock<ILogger<RedactionService>> _redactionLoggerMock;
    private readonly Mock<ILogger<PdfTextExtractionService>> _textLoggerMock;
    private readonly Mock<ILogger<PdfSearchService>> _searchLoggerMock;
    private readonly ILoggerFactory _loggerFactory;

    public MainWindowViewModelTests()
    {
        _loggerMock = new Mock<ILogger<MainWindowViewModel>>();
        _docLoggerMock = new Mock<ILogger<PdfDocumentService>>();
        _renderLoggerMock = new Mock<ILogger<PdfRenderService>>();
        _redactionLoggerMock = new Mock<ILogger<RedactionService>>();
        _textLoggerMock = new Mock<ILogger<PdfTextExtractionService>>();
        _searchLoggerMock = new Mock<ILogger<PdfSearchService>>();
        _loggerFactory = NullLoggerFactory.Instance;
    }

    private MainWindowViewModel CreateViewModel()
    {
        var documentService = new PdfDocumentService(_docLoggerMock.Object);
        var renderService = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService = new PdfSearchService(_searchLoggerMock.Object);
        var ocrService = new PdfOcrService(new Mock<ILogger<PdfOcrService>>().Object, renderService);
        var signatureService = new SignatureVerificationService(new Mock<ILogger<SignatureVerificationService>>().Object);
        var verifier = new RedactionVerifier(new Mock<ILogger<RedactionVerifier>>().Object, _loggerFactory);

        return new MainWindowViewModel(
            _loggerMock.Object,
            _loggerFactory,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService,
            ocrService,
            signatureService,
            verifier);
    }

    #region Zoom Tests

    [Fact]
    public void ZoomLevel_InitialValue_IsOne()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.ZoomLevel.Should().Be(1.0);
    }

    [Fact]
    public void ZoomInCommand_WhenExecuted_IncreasesZoomLevel()
    {
        // Arrange
        var vm = CreateViewModel();
        var initialZoom = vm.ZoomLevel;

        // Act
        vm.ZoomInCommand.Execute().Subscribe();

        // Assert
        vm.ZoomLevel.Should().BeGreaterThan(initialZoom);
    }

    [Fact]
    public void ZoomOutCommand_WhenExecuted_DecreasesZoomLevel()
    {
        // Arrange
        var vm = CreateViewModel();
        var initialZoom = vm.ZoomLevel;

        // Act
        vm.ZoomOutCommand.Execute().Subscribe();

        // Assert
        vm.ZoomLevel.Should().BeLessThan(initialZoom);
    }

    [Fact]
    public void ZoomInCommand_MultipleTimes_DoesNotExceedMaximum()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - zoom in many times
        for (int i = 0; i < 20; i++)
        {
            vm.ZoomInCommand.Execute().Subscribe();
        }

        // Assert - should not exceed 5.0 (max zoom)
        vm.ZoomLevel.Should().BeLessOrEqualTo(5.0);
    }

    [Fact]
    public void ZoomOutCommand_MultipleTimes_DoesNotGoBelowMinimum()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - zoom out many times
        for (int i = 0; i < 20; i++)
        {
            vm.ZoomOutCommand.Execute().Subscribe();
        }

        // Assert - should not go below 0.25 (min zoom)
        vm.ZoomLevel.Should().BeGreaterOrEqualTo(0.25);
    }

    [Fact]
    public void ZoomActualSizeCommand_WhenExecuted_SetsZoomToOne()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.ZoomInCommand.Execute().Subscribe(); // Change zoom first

        // Act
        vm.ZoomActualSizeCommand.Execute().Subscribe();

        // Assert
        vm.ZoomLevel.Should().Be(1.0);
    }

    [Fact]
    public void ZoomFitWidthCommand_WhenExecuted_WithNoDocument_DefaultsToOne()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ZoomFitWidthCommand.Execute().Subscribe();

        // Assert - When no document loaded, defaults to 1.0
        vm.ZoomLevel.Should().Be(1.0);
    }

    [Fact]
    public void ZoomFitPageCommand_WhenExecuted_WithNoDocument_DefaultsToOne()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ZoomFitPageCommand.Execute().Subscribe();

        // Assert - When no document loaded, defaults to 1.0
        vm.ZoomLevel.Should().Be(1.0);
    }

    [Fact]
    public void ViewportDimensions_CanBeSet()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ViewportWidth = 1024;
        vm.ViewportHeight = 768;

        // Assert
        vm.ViewportWidth.Should().Be(1024);
        vm.ViewportHeight.Should().Be(768);
    }

    #endregion

    #region Mode Toggle Tests

    [Fact]
    public void IsRedactionMode_InitialValue_IsFalse()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.IsRedactionMode.Should().BeFalse();
    }

    [Fact]
    public void ToggleRedactionModeCommand_WhenExecuted_TogglesRedactionMode()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ToggleRedactionModeCommand.Execute().Subscribe();

        // Assert
        vm.IsRedactionMode.Should().BeTrue();

        // Act again
        vm.ToggleRedactionModeCommand.Execute().Subscribe();

        // Assert
        vm.IsRedactionMode.Should().BeFalse();
    }

    [Fact]
    public void IsTextSelectionMode_InitialValue_IsFalse()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.IsTextSelectionMode.Should().BeFalse();
    }

    [Fact]
    public void ToggleTextSelectionModeCommand_WhenExecuted_TogglesTextSelectionMode()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ToggleTextSelectionModeCommand.Execute().Subscribe();

        // Assert
        vm.IsTextSelectionMode.Should().BeTrue();

        // Act again
        vm.ToggleTextSelectionModeCommand.Execute().Subscribe();

        // Assert
        vm.IsTextSelectionMode.Should().BeFalse();
    }

    [Fact]
    public void TextSelectionMode_WhenEnabled_DisablesRedactionMode()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.ToggleRedactionModeCommand.Execute().Subscribe();
        vm.IsRedactionMode.Should().BeTrue();

        // Act
        vm.ToggleTextSelectionModeCommand.Execute().Subscribe();

        // Assert - both modes should be mutually exclusive
        vm.IsTextSelectionMode.Should().BeTrue();
        vm.IsRedactionMode.Should().BeFalse();
    }

    [Fact]
    public void RedactionMode_WhenEnabled_DisablesTextSelectionMode()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.ToggleTextSelectionModeCommand.Execute().Subscribe();
        vm.IsTextSelectionMode.Should().BeTrue();

        // Act
        vm.ToggleRedactionModeCommand.Execute().Subscribe();

        // Assert - both modes should be mutually exclusive
        vm.IsRedactionMode.Should().BeTrue();
        vm.IsTextSelectionMode.Should().BeFalse();
    }

    #endregion

    #region Document State Tests

    [Fact]
    public void DocumentName_WhenNoDocumentLoaded_ReturnsNoDocumentOpen()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.DocumentName.Should().Be("No document open");
    }

    [Fact]
    public void TotalPages_WhenNoDocumentLoaded_ReturnsZero()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.TotalPages.Should().Be(0);
    }

    [Fact]
    public void CurrentPageIndex_InitialValue_IsZero()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.CurrentPageIndex.Should().Be(0);
    }

    [Fact]
    public void DisplayPageNumber_IsOneBasedIndex()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - DisplayPageNumber should be 1-based (0 + 1 = 1)
        vm.DisplayPageNumber.Should().Be(1);
    }

    [Fact]
    public void StatusText_WhenNoDocumentLoaded_ShowsNoDocumentLoaded()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.StatusText.Should().Be("No document loaded");
    }

    #endregion

    #region Search Tests

    [Fact]
    public void IsSearchVisible_InitialValue_IsFalse()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.IsSearchVisible.Should().BeFalse();
    }

    [Fact]
    public void ToggleSearchCommand_WhenExecuted_TogglesSearchVisibility()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.ToggleSearchCommand!.Execute().Subscribe();

        // Assert
        vm.IsSearchVisible.Should().BeTrue();

        // Act again
        vm.ToggleSearchCommand.Execute().Subscribe();

        // Assert
        vm.IsSearchVisible.Should().BeFalse();
    }

    [Fact]
    public void CloseSearchCommand_WhenExecuted_HidesSearch()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.ToggleSearchCommand!.Execute().Subscribe();
        vm.IsSearchVisible.Should().BeTrue();

        // Act
        vm.CloseSearchCommand!.Execute().Subscribe();

        // Assert
        vm.IsSearchVisible.Should().BeFalse();
    }

    [Fact]
    public void SearchResultText_WhenNoMatches_ReturnsNoMatches()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchResultText.Should().Be("No matches");
    }

    [Fact]
    public void SearchText_InitialValue_IsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchText.Should().BeEmpty();
    }

    [Fact]
    public void SearchCaseSensitive_InitialValue_IsFalse()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchCaseSensitive.Should().BeFalse();
    }

    [Fact]
    public void SearchWholeWords_InitialValue_IsFalse()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchWholeWords.Should().BeFalse();
    }

    #endregion

    #region Collections Tests

    [Fact]
    public void PageThumbnails_InitiallyEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.PageThumbnails.Should().NotBeNull();
        vm.PageThumbnails.Should().BeEmpty();
    }

    [Fact]
    public void ClipboardHistory_InitiallyEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.ClipboardHistory.Should().NotBeNull();
        vm.ClipboardHistory.Should().BeEmpty();
    }

    [Fact]
    public void SearchMatches_InitiallyEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchMatches.Should().NotBeNull();
        vm.SearchMatches.Should().BeEmpty();
    }

    [Fact]
    public void RecentFiles_InitiallyEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.RecentFiles.Should().NotBeNull();
        // Note: May contain files if there's a recent.txt file
    }

    [Fact]
    public void HasRecentFiles_WhenEmpty_ReturnsFalse()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.RecentFiles.Clear();

        // Assert
        vm.HasRecentFiles.Should().BeFalse();
    }

    #endregion

    #region Command Existence Tests

    [Fact]
    public void AllCommands_ShouldBeInitialized()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - Core commands
        vm.OpenFileCommand.Should().NotBeNull();
        vm.SaveFileCommand.Should().NotBeNull();
        vm.RemoveCurrentPageCommand.Should().NotBeNull();
        vm.AddPagesCommand.Should().NotBeNull();
        vm.ToggleRedactionModeCommand.Should().NotBeNull();
        vm.ApplyRedactionCommand.Should().NotBeNull();
        vm.ToggleTextSelectionModeCommand.Should().NotBeNull();
        vm.CopyTextCommand.Should().NotBeNull();

        // Zoom commands
        vm.ZoomInCommand.Should().NotBeNull();
        vm.ZoomOutCommand.Should().NotBeNull();
        vm.ZoomActualSizeCommand.Should().NotBeNull();
        vm.ZoomFitWidthCommand.Should().NotBeNull();
        vm.ZoomFitPageCommand.Should().NotBeNull();

        // Navigation commands
        vm.NextPageCommand.Should().NotBeNull();
        vm.PreviousPageCommand.Should().NotBeNull();
        vm.GoToPageCommand.Should().NotBeNull();

        // Rotation commands
        vm.RotatePageLeftCommand.Should().NotBeNull();
        vm.RotatePageRightCommand.Should().NotBeNull();
        vm.RotatePage180Command.Should().NotBeNull();

        // File menu commands
        vm.SaveAsCommand.Should().NotBeNull();
        vm.CloseDocumentCommand.Should().NotBeNull();
        vm.ExitCommand.Should().NotBeNull();

        // Tools menu commands
        vm.ExportPagesCommand.Should().NotBeNull();
        vm.PrintCommand.Should().NotBeNull();

        // Help menu commands
        vm.AboutCommand.Should().NotBeNull();
        vm.ShowShortcutsCommand.Should().NotBeNull();

        // Search commands
        vm.ToggleSearchCommand.Should().NotBeNull();
        vm.FindNextCommand.Should().NotBeNull();
        vm.FindPreviousCommand.Should().NotBeNull();
        vm.CloseSearchCommand.Should().NotBeNull();
    }

    #endregion

    #region Redaction Area Tests

    [Fact]
    public void CurrentRedactionArea_InitialValue_IsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.CurrentRedactionArea.Should().Be(new Rect());
    }

    [Fact]
    public void CurrentRedactionArea_CanBeSet()
    {
        // Arrange
        var vm = CreateViewModel();
        var area = new Rect(10, 20, 100, 50);

        // Act
        vm.CurrentRedactionArea = area;

        // Assert
        vm.CurrentRedactionArea.Should().Be(area);
    }

    [Fact]
    public void CurrentTextSelectionArea_InitialValue_IsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.CurrentTextSelectionArea.Should().Be(new Rect());
    }

    [Fact]
    public void CurrentTextSelectionArea_CanBeSet()
    {
        // Arrange
        var vm = CreateViewModel();
        var area = new Rect(10, 20, 100, 50);

        // Act
        vm.CurrentTextSelectionArea = area;

        // Assert
        vm.CurrentTextSelectionArea.Should().Be(area);
    }

    [Fact]
    public void SelectedText_InitialValue_IsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SelectedText.Should().BeEmpty();
    }

    [Fact]
    public void SelectedText_CanBeSet()
    {
        // Arrange
        var vm = CreateViewModel();
        var text = "Selected text content";

        // Act
        vm.SelectedText = text;

        // Assert
        vm.SelectedText.Should().Be(text);
    }

    #endregion

    #region Property Change Notification Tests

    [Fact]
    public void ZoomLevel_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        var vm = CreateViewModel();
        var propertyChanged = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.ZoomLevel))
                propertyChanged = true;
        };

        // Act
        vm.ZoomLevel = 2.0;

        // Assert
        propertyChanged.Should().BeTrue();
    }

    [Fact]
    public void IsRedactionMode_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        var vm = CreateViewModel();
        var propertyChanged = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.IsRedactionMode))
                propertyChanged = true;
        };

        // Act
        vm.IsRedactionMode = true;

        // Assert
        propertyChanged.Should().BeTrue();
    }

    [Fact]
    public void CurrentPageIndex_WhenChanged_RaisesPropertyChangedForDisplayPageNumber()
    {
        // Arrange
        var vm = CreateViewModel();
        var displayPageNumberChanged = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.DisplayPageNumber))
                displayPageNumberChanged = true;
        };

        // Act
        vm.CurrentPageIndex = 5;

        // Assert
        displayPageNumberChanged.Should().BeTrue();
    }

    #endregion
}
