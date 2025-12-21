using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using PdfEditor.ViewModels;
using PdfEditor.Views;
using System.Collections.Generic;
using Xunit;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Headless UI tests for PdfEditor
/// Tests user interactions, keyboard shortcuts, and View-ViewModel integration
/// </summary>
[Collection("AvaloniaTests")]
public class HeadlessUITests
{
    private readonly Mock<ILogger<MainWindowViewModel>> _vmLoggerMock;
    private readonly Mock<ILogger<PdfDocumentService>> _docLoggerMock;
    private readonly Mock<ILogger<PdfRenderService>> _renderLoggerMock;
    private readonly Mock<ILogger<RedactionService>> _redactionLoggerMock;
    private readonly Mock<ILogger<PdfTextExtractionService>> _textLoggerMock;
    private readonly Mock<ILogger<PdfSearchService>> _searchLoggerMock;
    private readonly ILoggerFactory _loggerFactory;

    public HeadlessUITests()
    {
        _vmLoggerMock = new Mock<ILogger<MainWindowViewModel>>();
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
        var filenameSuggestionService = new FilenameSuggestionService();

        return new MainWindowViewModel(
            _vmLoggerMock.Object,
            _loggerFactory,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService,
            ocrService,
            signatureService,
            verifier,
            filenameSuggestionService);
    }

    #region ViewModel Property Tests with UI Context

    [AvaloniaFact]
    public void ViewModel_ZoomLevel_CanBeChangedFromUI()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Simulate UI changing zoom
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomLevel = 2.5;
        });

        // Assert
        vm.ZoomLevel.Should().Be(2.5);
    }

    [AvaloniaFact]
    public void ViewModel_RedactionMode_TogglesCorrectly()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Toggle redaction mode
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Assert
        vm.IsRedactionMode.Should().BeTrue();

        // Act - Toggle again
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Assert
        vm.IsRedactionMode.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ViewModel_SearchVisibility_TogglesCorrectly()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleSearchCommand!.Execute().Subscribe();
        });

        // Assert
        vm.IsSearchVisible.Should().BeTrue();
    }

    #endregion

    #region Zoom Command Tests

    [AvaloniaFact]
    public void ZoomIn_FromUIThread_IncreasesZoom()
    {
        // Arrange
        var vm = CreateViewModel();
        var initialZoom = vm.ZoomLevel;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe();
        });

        // Assert
        vm.ZoomLevel.Should().BeGreaterThan(initialZoom);
    }

    [AvaloniaFact]
    public void ZoomOut_FromUIThread_DecreasesZoom()
    {
        // Arrange
        var vm = CreateViewModel();
        var initialZoom = vm.ZoomLevel;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomOutCommand.Execute().Subscribe();
        });

        // Assert
        vm.ZoomLevel.Should().BeLessThan(initialZoom);
    }

    [AvaloniaFact]
    public void ZoomActualSize_FromUIThread_SetsZoomToOne()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.ZoomLevel = 2.5;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomActualSizeCommand.Execute().Subscribe();
        });

        // Assert
        vm.ZoomLevel.Should().Be(1.0);
    }

    #endregion

    #region Mode Switching Tests

    [AvaloniaFact]
    public void TextSelectionMode_DisablesRedactionMode()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Enable redaction mode first
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });
        vm.IsRedactionMode.Should().BeTrue();

        // Act - Enable text selection mode
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleTextSelectionModeCommand.Execute().Subscribe();
        });

        // Assert - Modes should be mutually exclusive
        vm.IsTextSelectionMode.Should().BeTrue();
        vm.IsRedactionMode.Should().BeFalse();
    }

    [AvaloniaFact]
    public void RedactionMode_DisablesTextSelectionMode()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Enable text selection mode first
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleTextSelectionModeCommand.Execute().Subscribe();
        });
        vm.IsTextSelectionMode.Should().BeTrue();

        // Act - Enable redaction mode
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Assert - Modes should be mutually exclusive
        vm.IsRedactionMode.Should().BeTrue();
        vm.IsTextSelectionMode.Should().BeFalse();
    }

    #endregion

    #region Redaction Area Tests

    [AvaloniaFact]
    public void CurrentRedactionArea_CanBeSetFromUI()
    {
        // Arrange
        var vm = CreateViewModel();
        var expectedArea = new Rect(100, 150, 200, 50);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CurrentRedactionArea = expectedArea;
        });

        // Assert
        vm.CurrentRedactionArea.Should().Be(expectedArea);
    }

    [AvaloniaFact]
    public void CurrentTextSelectionArea_CanBeSetFromUI()
    {
        // Arrange
        var vm = CreateViewModel();
        var expectedArea = new Rect(50, 75, 300, 25);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CurrentTextSelectionArea = expectedArea;
        });

        // Assert
        vm.CurrentTextSelectionArea.Should().Be(expectedArea);
    }

    #endregion

    #region Search Tests

    [AvaloniaFact]
    public void Search_CloseCommand_HidesSearchPanel()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Open search
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleSearchCommand!.Execute().Subscribe();
        });
        vm.IsSearchVisible.Should().BeTrue();

        // Act - Close search
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CloseSearchCommand!.Execute().Subscribe();
        });

        // Assert
        vm.IsSearchVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void SearchResultText_WithNoMatches_ShowsNoMatches()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchResultText.Should().Be("No matches");
    }

    #endregion

    #region Property Change Notification Tests

    [AvaloniaFact]
    public void PropertyChanged_ZoomLevel_RaisesEvent()
    {
        // Arrange
        var vm = CreateViewModel();
        string? changedProperty = null;
        vm.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomLevel = 2.0;
        });

        // Assert
        changedProperty.Should().Be(nameof(vm.ZoomLevel));
    }

    [AvaloniaFact]
    public void PropertyChanged_IsRedactionMode_RaisesEvent()
    {
        // Arrange
        var vm = CreateViewModel();
        var changedProperties = new List<string>();
        vm.PropertyChanged += (s, e) => changedProperties.Add(e.PropertyName ?? "");

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.IsRedactionMode = true;
        });

        // Assert - IsRedactionMode should be raised (along with CurrentModeText)
        changedProperties.Should().Contain(nameof(vm.IsRedactionMode));
        changedProperties.Should().Contain(nameof(vm.CurrentModeText), "mode indicator should update");
    }

    [AvaloniaFact]
    public void PropertyChanged_SearchText_RaisesEvent()
    {
        // Arrange
        var vm = CreateViewModel();
        string? changedProperty = null;
        vm.PropertyChanged += (s, e) => changedProperty = e.PropertyName;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.SearchText = "test";
        });

        // Assert
        changedProperty.Should().Be(nameof(vm.SearchText));
    }

    #endregion

    #region Command Execution Tests

    [AvaloniaFact]
    public void Commands_CanExecuteOnUIThread()
    {
        // Arrange
        var vm = CreateViewModel();
        bool executed = false;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe(_ => executed = true);
        });

        // Assert
        executed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void MultipleCommands_CanExecuteSequentially()
    {
        // Arrange
        var vm = CreateViewModel();
        var zoomHistory = new System.Collections.Generic.List<double>();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            zoomHistory.Add(vm.ZoomLevel);

            vm.ZoomInCommand.Execute().Subscribe();
            zoomHistory.Add(vm.ZoomLevel);

            vm.ZoomInCommand.Execute().Subscribe();
            zoomHistory.Add(vm.ZoomLevel);

            vm.ZoomOutCommand.Execute().Subscribe();
            zoomHistory.Add(vm.ZoomLevel);
        });

        // Assert
        zoomHistory.Should().HaveCount(4);
        zoomHistory[1].Should().BeGreaterThan(zoomHistory[0]); // First zoom in
        zoomHistory[2].Should().BeGreaterThan(zoomHistory[1]); // Second zoom in
        zoomHistory[3].Should().BeLessThan(zoomHistory[2]);    // Zoom out
    }

    #endregion

    #region Document State Tests

    [AvaloniaFact]
    public void NoDocumentLoaded_StatusText_ShowsCorrectMessage()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.StatusText.Should().Be("No document loaded");
    }

    [AvaloniaFact]
    public void NoDocumentLoaded_DocumentName_ShowsCorrectMessage()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.DocumentName.Should().Be("No document open");
    }

    [AvaloniaFact]
    public void NoDocumentLoaded_TotalPages_IsZero()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.TotalPages.Should().Be(0);
    }

    #endregion

    #region Collection Tests

    [AvaloniaFact]
    public void PageThumbnails_InitializedAsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.PageThumbnails.Should().NotBeNull();
        vm.PageThumbnails.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ClipboardHistory_InitializedAsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.ClipboardHistory.Should().NotBeNull();
        vm.ClipboardHistory.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void SearchMatches_InitializedAsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchMatches.Should().NotBeNull();
        vm.SearchMatches.Should().BeEmpty();
    }

    #endregion
}
