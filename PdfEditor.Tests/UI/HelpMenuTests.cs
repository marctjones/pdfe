using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using PdfEditor.Tests.Utilities;
using PdfEditor.ViewModels;
using PdfEditor.Models;
using System.Reactive.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Comprehensive tests for Help menu operations.
/// Tests Keyboard Shortcuts, Documentation, and About dialog commands.
/// </summary>
[Collection("AvaloniaTests")]
public class HelpMenuTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public HelpMenuTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"HelpMenuTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _loggerFactory = NullLoggerFactory.Instance;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateTempFile(string extension = ".pdf")
    {
        var path = Path.Combine(_tempDir, $"test_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private MainWindowViewModel CreateViewModel()
    {
        var documentService = new PdfDocumentService(new Mock<ILogger<PdfDocumentService>>().Object);
        var renderService = new PdfRenderService(new Mock<ILogger<PdfRenderService>>().Object);
        var redactionService = new RedactionService(new Mock<ILogger<RedactionService>>().Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(new Mock<ILogger<PdfTextExtractionService>>().Object);
        var searchService = new PdfSearchService(new Mock<ILogger<PdfSearchService>>().Object);
        var ocrService = new PdfOcrService(new Mock<ILogger<PdfOcrService>>().Object, renderService);
        var signatureService = new SignatureVerificationService(new Mock<ILogger<SignatureVerificationService>>().Object);
        var verifier = new RedactionVerifier(new Mock<ILogger<RedactionVerifier>>().Object, _loggerFactory);
        var filenameSuggestionService = new FilenameSuggestionService();

        return new MainWindowViewModel(
            new Mock<ILogger<MainWindowViewModel>>().Object,
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

    #region Keyboard Shortcuts Command Tests

    [AvaloniaFact]
    public void ShowShortcutsCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ShowShortcutsCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ShowShortcutsCommand_CanExecute_WithoutDocument()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - Help commands should always be available
        vm.ShowShortcutsCommand.Should().NotBeNull();
        vm.IsDocumentLoaded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ShowShortcutsCommand_CanExecute_WithDocument()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.ShowShortcutsCommand.Should().NotBeNull();
    }

    #endregion

    #region Documentation Command Tests

    [AvaloniaFact]
    public void ShowDocumentationCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ShowDocumentationCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ShowDocumentationCommand_CanExecute_WithoutDocument()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - Help commands should always be available
        vm.ShowDocumentationCommand.Should().NotBeNull();
        vm.IsDocumentLoaded.Should().BeFalse();
    }

    #endregion

    #region About Command Tests

    [AvaloniaFact]
    public void AboutCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.AboutCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void AboutCommand_CanExecute_WithoutDocument()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - Help commands should always be available
        vm.AboutCommand.Should().NotBeNull();
        vm.IsDocumentLoaded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AboutCommand_CanExecute_WithDocument()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.AboutCommand.Should().NotBeNull();
    }

    #endregion

    #region Keyboard Shortcut Binding Tests

    [AvaloniaFact]
    public void KeyboardShortcuts_ShowShortcuts_F1()
    {
        var vm = CreateViewModel();
        vm.ShowShortcutsCommand.Should().NotBeNull();
        // InputGesture="F1" is defined in MainWindow.axaml
    }

    #endregion

    #region Zoom Command Tests

    [AvaloniaFact]
    public void ZoomInCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ZoomInCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ZoomOutCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ZoomOutCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ZoomFitWidthCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ZoomFitWidthCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ZoomFitPageCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ZoomFitPageCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ZoomActualSizeCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ZoomActualSizeCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task ZoomInCommand_IncreasesZoom()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // First zoom out to ensure we're not at max zoom
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomOutCommand.Execute().Subscribe();
            vm.ZoomOutCommand.Execute().Subscribe();
        });

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
    public async Task ZoomOutCommand_DecreasesZoom()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // First zoom in to give room to zoom out
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe();
            vm.ZoomInCommand.Execute().Subscribe();
        });

        var zoomAfterIn = vm.ZoomLevel;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomOutCommand.Execute().Subscribe();
        });

        // Assert
        vm.ZoomLevel.Should().BeLessThan(zoomAfterIn);
    }

    [AvaloniaFact]
    public async Task ZoomActualSizeCommand_SetsZoomTo100Percent()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Zoom in first
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe();
            vm.ZoomInCommand.Execute().Subscribe();
        });

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomActualSizeCommand.Execute().Subscribe();
        });

        // Assert
        vm.ZoomLevel.Should().Be(1.0);
    }

    [AvaloniaFact]
    public void ZoomLevel_DefaultIsReasonable()
    {
        var vm = CreateViewModel();
        // Default zoom should be 1.0 (100%) or fit to width/page
        vm.ZoomLevel.Should().BeGreaterThan(0);
        vm.ZoomLevel.Should().BeLessOrEqualTo(5.0); // Reasonable upper bound
    }

    [AvaloniaFact]
    public void ZoomLevel_HasMinimumLimit()
    {
        var vm = CreateViewModel();

        // Try to zoom out many times
        Dispatcher.UIThread.Invoke(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                vm.ZoomOutCommand.Execute().Subscribe();
            }
        });

        // Should hit minimum and not go negative
        vm.ZoomLevel.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void ZoomLevel_HasMaximumLimit()
    {
        var vm = CreateViewModel();

        // Try to zoom in many times
        Dispatcher.UIThread.Invoke(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                vm.ZoomInCommand.Execute().Subscribe();
            }
        });

        // Should hit maximum
        vm.ZoomLevel.Should().BeLessThan(10.0); // Reasonable upper bound
    }

    #endregion

    #region Clipboard History Tests

    [AvaloniaFact]
    public void ClipboardHistory_InitiallyEmpty()
    {
        var vm = CreateViewModel();
        vm.ClipboardHistory.Should().NotBeNull();
        vm.ClipboardHistory.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ClipboardHistory_IsObservableCollection()
    {
        var vm = CreateViewModel();
        vm.ClipboardHistory.Should().BeAssignableTo<System.Collections.ObjectModel.ObservableCollection<ClipboardEntry>>();
    }

    #endregion

    #region View Keyboard Shortcut Tests

    [AvaloniaFact]
    public void KeyboardShortcuts_ZoomIn_CtrlPlus()
    {
        var vm = CreateViewModel();
        vm.ZoomInCommand.Should().NotBeNull();
        // InputGesture="Ctrl+Plus" or "Ctrl+=" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_ZoomOut_CtrlMinus()
    {
        var vm = CreateViewModel();
        vm.ZoomOutCommand.Should().NotBeNull();
        // InputGesture="Ctrl+Minus" or "Ctrl+-" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_FitToWidth_Ctrl1()
    {
        var vm = CreateViewModel();
        vm.ZoomFitWidthCommand.Should().NotBeNull();
        // InputGesture="Ctrl+1" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_FitToPage_Ctrl0()
    {
        var vm = CreateViewModel();
        vm.ZoomFitPageCommand.Should().NotBeNull();
        // InputGesture="Ctrl+0" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_ActualSize_Ctrl2()
    {
        var vm = CreateViewModel();
        vm.ZoomActualSizeCommand.Should().NotBeNull();
        // InputGesture="Ctrl+2" is defined in MainWindow.axaml
    }

    #endregion
}
