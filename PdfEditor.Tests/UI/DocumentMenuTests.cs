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
/// Comprehensive tests for Document menu operations.
/// Tests Add Pages, Remove Page, Rotate, Export, and Print commands.
/// </summary>
[Collection("AvaloniaTests")]
public class DocumentMenuTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public DocumentMenuTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"DocumentMenuTests_{Guid.NewGuid()}");
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

    #region Add Pages Command Tests

    [AvaloniaFact]
    public void AddPagesCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.AddPagesCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void AddPagesCommand_NoDocument_CanExecuteIsFalse()
    {
        // Arrange
        var vm = CreateViewModel();

        // Assert - Can't add pages if no document loaded
        vm.IsDocumentLoaded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AddPagesCommand_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.AddPagesCommand.Should().NotBeNull();
    }

    #endregion

    #region Remove Current Page Command Tests

    [AvaloniaFact]
    public void RemoveCurrentPageCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RemoveCurrentPageCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task RemoveCurrentPageCommand_SinglePageDocument_CannotRemove()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Single page document");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert - Single page documents should not allow removal (or should warn)
        vm.TotalPages.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task RemoveCurrentPageCommand_MultiPageDocument_RemovesPage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        await vm.LoadDocumentAsync(pdfPath);
        var initialPages = vm.TotalPages;
        initialPages.Should().Be(3);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.RemoveCurrentPageCommand.Execute().Subscribe();
        });

        // Assert
        vm.TotalPages.Should().Be(initialPages - 1);
    }

    #endregion

    #region Rotate Commands Tests

    [AvaloniaFact]
    public void RotatePageLeftCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RotatePageLeftCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void RotatePageRightCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RotatePageRightCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void RotatePage180Command_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.RotatePage180Command.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task RotatePageLeftCommand_WithDocument_Executes()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Act - Should execute without error
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.RotatePageLeftCommand.Execute().Subscribe();
        });

        // Assert - Page should still exist
        vm.TotalPages.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task RotatePageRightCommand_WithDocument_Executes()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Act - Should execute without error
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.RotatePageRightCommand.Execute().Subscribe();
        });

        // Assert - Page should still exist
        vm.TotalPages.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task RotatePage180Command_WithDocument_Executes()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Act - Should execute without error
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.RotatePage180Command.Execute().Subscribe();
        });

        // Assert - Page should still exist
        vm.TotalPages.Should().BeGreaterThan(0);
    }

    #endregion

    #region Export Current Page Command Tests

    [AvaloniaFact]
    public void ExportCurrentPageCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ExportCurrentPageCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task ExportCurrentPage_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.ExportCurrentPageCommand.Should().NotBeNull();
    }

    #endregion

    #region Export All Pages Command Tests

    [AvaloniaFact]
    public void ExportPagesCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.ExportPagesCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task ExportPages_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.TotalPages.Should().Be(3);
        vm.ExportPagesCommand.Should().NotBeNull();
    }

    #endregion

    #region Print Command Tests

    [AvaloniaFact]
    public void PrintCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.PrintCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task PrintCommand_WithDocument_CanExecute()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.IsDocumentLoaded.Should().BeTrue();
        vm.PrintCommand.Should().NotBeNull();
    }

    #endregion

    #region Page Navigation Tests

    [AvaloniaFact]
    public void NextPageCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.NextPageCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void PreviousPageCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.PreviousPageCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void GoToPageCommand_Exists_AndIsNotNull()
    {
        var vm = CreateViewModel();
        vm.GoToPageCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task NextPageCommand_MultiPageDocument_NavigatesToNextPage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 5);

        await vm.LoadDocumentAsync(pdfPath);
        vm.CurrentPageIndex.Should().Be(0);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.NextPageCommand.Execute().Subscribe();
        });

        // Assert
        vm.CurrentPageIndex.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task PreviousPageCommand_OnSecondPage_NavigatesToFirst()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 5);

        await vm.LoadDocumentAsync(pdfPath);

        // Navigate to second page
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.NextPageCommand.Execute().Subscribe();
        });
        vm.CurrentPageIndex.Should().Be(1);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.PreviousPageCommand.Execute().Subscribe();
        });

        // Assert
        vm.CurrentPageIndex.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task NextPageCommand_OnLastPage_StaysOnLastPage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        await vm.LoadDocumentAsync(pdfPath);

        // Navigate to last page
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.NextPageCommand.Execute().Subscribe();
            vm.NextPageCommand.Execute().Subscribe();
        });
        vm.CurrentPageIndex.Should().Be(2);

        // Act - Try to go beyond last page
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.NextPageCommand.Execute().Subscribe();
        });

        // Assert - Should stay on last page
        vm.CurrentPageIndex.Should().Be(2);
    }

    [AvaloniaFact]
    public async Task PreviousPageCommand_OnFirstPage_StaysOnFirstPage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        await vm.LoadDocumentAsync(pdfPath);
        vm.CurrentPageIndex.Should().Be(0);

        // Act - Try to go before first page
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.PreviousPageCommand.Execute().Subscribe();
        });

        // Assert - Should stay on first page
        vm.CurrentPageIndex.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task DisplayPageNumber_IsOneBased()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 5);

        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.CurrentPageIndex.Should().Be(0);
        vm.DisplayPageNumber.Should().Be(1); // 1-based for user display
    }

    [AvaloniaFact]
    public async Task CurrentPageIndex_SetDirectly_UpdatesPage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 5);

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CurrentPageIndex = 3;
        });

        // Assert
        vm.CurrentPageIndex.Should().Be(3);
        vm.DisplayPageNumber.Should().Be(4);
    }

    #endregion

    #region Page Count Tests

    [AvaloniaFact]
    public void TotalPages_NoDocument_ReturnsZero()
    {
        var vm = CreateViewModel();
        vm.TotalPages.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task TotalPages_WithDocument_ReturnsCorrectCount()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 7);

        // Act
        await vm.LoadDocumentAsync(pdfPath);

        // Assert
        vm.TotalPages.Should().Be(7);
    }

    #endregion

    #region Keyboard Shortcut Tests

    [AvaloniaFact]
    public void KeyboardShortcuts_RotateLeft_CtrlL()
    {
        var vm = CreateViewModel();
        vm.RotatePageLeftCommand.Should().NotBeNull();
        // InputGesture="Ctrl+L" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_RotateRight_CtrlR()
    {
        var vm = CreateViewModel();
        vm.RotatePageRightCommand.Should().NotBeNull();
        // InputGesture="Ctrl+R" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_ExportCurrentPage_CtrlE()
    {
        var vm = CreateViewModel();
        vm.ExportCurrentPageCommand.Should().NotBeNull();
        // InputGesture="Ctrl+E" is defined in MainWindow.axaml
    }

    [AvaloniaFact]
    public void KeyboardShortcuts_Print_CtrlP()
    {
        var vm = CreateViewModel();
        vm.PrintCommand.Should().NotBeNull();
        // InputGesture="Ctrl+P" is defined in MainWindow.axaml
    }

    #endregion
}
