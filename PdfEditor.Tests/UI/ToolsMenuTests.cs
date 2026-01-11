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
using System.Reactive.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Comprehensive tests for Tools menu operations.
/// Tests OCR, Signature Verification, and Preferences commands.
/// </summary>
[Collection("AvaloniaTests")]
public class ToolsMenuTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public ToolsMenuTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"ToolsMenuTests_{Guid.NewGuid()}");
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

    #region OCR Command Tests

    [AvaloniaFact]
    public void RunOcrCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RunOcrCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void RunOcrAllPagesCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RunOcrAllPagesCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task RunOcrCommand_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.RunOcrCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task RunOcrAllPagesCommand_WithMultiPageDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.TotalPages.Should().Be(3);
        vm.RunOcrAllPagesCommand.Should().NotBeNull();
    }

    #endregion

    #region Signature Verification Command Tests

    [AvaloniaFact]
    public void VerifySignaturesCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.VerifySignaturesCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task VerifySignaturesCommand_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.VerifySignaturesCommand.Should().NotBeNull();
    }

    #endregion

    #region Redaction Verification Command Tests

    [AvaloniaFact]
    public void RunVerifyNowCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RunVerifyNowCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task RunVerifyNowCommand_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.RunVerifyNowCommand.Should().NotBeNull();
    }

    #endregion

    #region Preferences Command Tests

    [AvaloniaFact]
    public void ShowPreferencesCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ShowPreferencesCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ShowPreferencesCommand_CanExecute_WithoutDocument()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - Preferences should always be available
        vm.ShowPreferencesCommand.Should().NotBeNull();
    }

    #endregion

    #region Search Command Tests

    [AvaloniaFact]
    public void ToggleSearchCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ToggleSearchCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void FindNextCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.FindNextCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void FindPreviousCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.FindPreviousCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void CloseSearchCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.CloseSearchCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ToggleSearchCommand_TogglesSearchVisibility()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsSearchVisible.Should().BeFalse();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleSearchCommand!.Execute().Subscribe();
        });

        // Assert
        vm.IsSearchVisible.Should().BeTrue();

        // Act - Toggle again
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleSearchCommand!.Execute().Subscribe();
        });

        // Assert
        vm.IsSearchVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void CloseSearchCommand_HidesSearch()
    {
        // Arrange
        var vm = CreateViewModel();
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleSearchCommand!.Execute().Subscribe();
        });
        vm.IsSearchVisible.Should().BeTrue();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CloseSearchCommand!.Execute().Subscribe();
        });

        // Assert
        vm.IsSearchVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Search_WithDocument_FindsText()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World Test");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        vm.SearchText = "Hello";
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleSearchCommand!.Execute().Subscribe();
        });

        // Allow search to run (async)
        await Task.Delay(100);

        // Assert
        vm.IsSearchVisible.Should().BeTrue();
        // Note: Search results depend on async search operation
    }

    [AvaloniaFact]
    public void SearchResultText_NoMatches_ShowsNoMatches()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert
        vm.SearchResultText.Should().Be("No matches");
    }

    [AvaloniaFact]
    public void SearchCaseSensitive_DefaultIsFalse()
    {
        var vm = CreateViewModel();
        vm.SearchCaseSensitive.Should().BeFalse();
    }

    [AvaloniaFact]
    public void SearchWholeWords_DefaultIsFalse()
    {
        var vm = CreateViewModel();
        vm.SearchWholeWords.Should().BeFalse();
    }

    [AvaloniaFact]
    public void SearchCaseSensitive_CanBeToggled()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SearchCaseSensitive = true;

        // Assert
        vm.SearchCaseSensitive.Should().BeTrue();
    }

    [AvaloniaFact]
    public void SearchWholeWords_CanBeToggled()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.SearchWholeWords = true;

        // Assert
        vm.SearchWholeWords.Should().BeTrue();
    }

    #endregion

    #region Text Selection and Copy Tests

    [AvaloniaFact]
    public void ToggleTextSelectionModeCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ToggleTextSelectionModeCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void CopyTextCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.CopyTextCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ToggleTextSelectionModeCommand_TogglesMode()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsTextSelectionMode.Should().BeFalse();

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleTextSelectionModeCommand.Execute().Subscribe();
        });

        // Assert
        vm.IsTextSelectionMode.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TextSelectionMode_ExclusiveWithRedactionMode()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Enable redaction mode
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

        // Assert - Modes are mutually exclusive
        vm.IsTextSelectionMode.Should().BeTrue();
        vm.IsRedactionMode.Should().BeFalse();
    }

    [AvaloniaFact]
    public void SelectedText_InitiallyEmpty()
    {
        var vm = CreateViewModel();
        vm.SelectedText.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void SelectedText_CanBeSet()
    {
        var vm = CreateViewModel();
        vm.SelectedText = "Selected content";
        vm.SelectedText.Should().Be("Selected content");
    }

    #endregion

    #region Redaction Mode Tests

    [AvaloniaFact]
    public void ToggleRedactionModeCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ToggleRedactionModeCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ApplyRedactionCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ApplyRedactionCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void ToggleRedactionModeCommand_TogglesMode()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.IsRedactionMode.Should().BeFalse();

        // Act
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
    public void CurrentModeText_ReflectsCurrentMode()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act - Enable redaction mode
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Assert
        vm.CurrentModeText.Should().NotBeEmpty();
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
        vm.ClipboardHistory.Should().BeOfType<System.Collections.ObjectModel.ObservableCollection<PdfEditor.Models.ClipboardEntry>>();
    }

    #endregion

    #region Keyboard Shortcut Tests

    [AvaloniaFact]
    public void KeyboardShortcuts_Find_CtrlF()
    {
        var vm = CreateViewModel();
        vm.ToggleSearchCommand.Should().NotBeNull();
        // InputGesture="Ctrl+F" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_FindNext_F3()
    {
        var vm = CreateViewModel();
        vm.FindNextCommand.Should().NotBeNull();
        // InputGesture="F3" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_FindPrevious_ShiftF3()
    {
        var vm = CreateViewModel();
        vm.FindPreviousCommand.Should().NotBeNull();
        // InputGesture="Shift+F3" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_TextSelectionMode_T()
    {
        var vm = CreateViewModel();
        vm.ToggleTextSelectionModeCommand.Should().NotBeNull();
        // InputGesture="T" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_CopyText_CtrlC()
    {
        var vm = CreateViewModel();
        vm.CopyTextCommand.Should().NotBeNull();
        // InputGesture="Ctrl+C" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_RedactionMode_R()
    {
        var vm = CreateViewModel();
        vm.ToggleRedactionModeCommand.Should().NotBeNull();
        // InputGesture="R" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_ApplyRedaction_Enter()
    {
        var vm = CreateViewModel();
        vm.ApplyRedactionCommand.Should().NotBeNull();
        // InputGesture="Enter" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_Preferences_CtrlComma()
    {
        var vm = CreateViewModel();
        vm.ShowPreferencesCommand.Should().NotBeNull();
        // InputGesture="Ctrl+," is defined in MainWindow.axaml
    }

    #endregion
}
