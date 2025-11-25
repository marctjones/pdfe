using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfEditor.ViewModels;
using System.Reactive.Linq;
using Xunit;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Integration tests for MainWindowViewModel with actual PDF documents
/// Tests the full workflow from document loading to operations
/// </summary>
[Collection("AvaloniaTests")]
public class ViewModelIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<MainWindowViewModel>> _vmLoggerMock;
    private readonly Mock<ILogger<PdfDocumentService>> _docLoggerMock;
    private readonly Mock<ILogger<PdfRenderService>> _renderLoggerMock;
    private readonly Mock<ILogger<RedactionService>> _redactionLoggerMock;
    private readonly Mock<ILogger<PdfTextExtractionService>> _textLoggerMock;
    private readonly Mock<ILogger<PdfSearchService>> _searchLoggerMock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<string> _tempFiles = new();

    public ViewModelIntegrationTests()
    {
        _vmLoggerMock = new Mock<ILogger<MainWindowViewModel>>();
        _docLoggerMock = new Mock<ILogger<PdfDocumentService>>();
        _renderLoggerMock = new Mock<ILogger<PdfRenderService>>();
        _redactionLoggerMock = new Mock<ILogger<RedactionService>>();
        _textLoggerMock = new Mock<ILogger<PdfTextExtractionService>>();
        _searchLoggerMock = new Mock<ILogger<PdfSearchService>>();
        _loggerFactory = NullLoggerFactory.Instance;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch { }
        }
    }

    private MainWindowViewModel CreateViewModel()
    {
        var documentService = new PdfDocumentService(_docLoggerMock.Object);
        var renderService = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService = new PdfSearchService(_searchLoggerMock.Object);

        return new MainWindowViewModel(
            _vmLoggerMock.Object,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService);
    }

    private string CreateTestPdf(string content = "Test Document Content")
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"viewmodel_test_{Guid.NewGuid()}.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(tempPath, content);
        _tempFiles.Add(tempPath);
        return tempPath;
    }

    #region Document Loading Tests

    [AvaloniaFact]
    public async Task LoadDocument_ValidPdf_UpdatesDocumentName()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        // Act
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Assert
        vm.DocumentName.Should().Be(Path.GetFileName(pdfPath));
    }

    [AvaloniaFact]
    public async Task LoadDocument_ValidPdf_UpdatesTotalPages()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        // Act
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Assert
        vm.TotalPages.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task LoadDocument_ValidPdf_SetsCurrentPageToZero()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        // Act
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Assert
        vm.CurrentPageIndex.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task LoadDocument_ValidPdf_DisplayPageNumberIsOne()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        // Act
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Assert
        vm.DisplayPageNumber.Should().Be(1);
    }

    #endregion

    #region Zoom with Document Tests

    [AvaloniaFact]
    public async Task ZoomIn_WithDocumentLoaded_UpdatesStatusText()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        var initialStatus = vm.StatusText;

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe();
        });

        // Assert - Status should include new zoom level
        vm.StatusText.Should().NotBe(initialStatus);
        vm.StatusText.Should().Contain("Zoom:");
    }

    [AvaloniaFact]
    public async Task ZoomLevel_AfterMultipleOperations_StaysWithinBounds()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Act - Zoom in many times
        Dispatcher.UIThread.Invoke(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                vm.ZoomInCommand.Execute().Subscribe();
            }
        });

        // Assert
        vm.ZoomLevel.Should().BeLessOrEqualTo(5.0);

        // Act - Zoom out many times
        Dispatcher.UIThread.Invoke(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                vm.ZoomOutCommand.Execute().Subscribe();
            }
        });

        // Assert
        vm.ZoomLevel.Should().BeGreaterOrEqualTo(0.25);
    }

    #endregion

    #region Mode Tests with Document

    [AvaloniaFact]
    public async Task RedactionMode_WithDocumentLoaded_CanBeToggled()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Assert
        vm.IsRedactionMode.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task TextSelectionMode_WithDocumentLoaded_CanBeToggled()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ToggleTextSelectionModeCommand.Execute().Subscribe();
        });

        // Assert
        vm.IsTextSelectionMode.Should().BeTrue();
    }

    #endregion

    #region Close Document Tests

    [AvaloniaFact]
    public async Task CloseDocument_AfterLoad_ClearsDocumentName()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });
        vm.DocumentName.Should().NotBe("No document open");

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CloseDocumentCommand.Execute().Subscribe();
        });

        // Assert
        vm.DocumentName.Should().Be("No document open");
    }

    [AvaloniaFact]
    public async Task CloseDocument_AfterLoad_ClearsTotalPages()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });
        vm.TotalPages.Should().BeGreaterThan(0);

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CloseDocumentCommand.Execute().Subscribe();
        });

        // Assert
        vm.TotalPages.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task CloseDocument_AfterLoad_ClearsCurrentPageImage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CloseDocumentCommand.Execute().Subscribe();
        });

        // Assert
        vm.CurrentPageImage.Should().BeNull();
    }

    #endregion

    #region Status Text Tests

    [AvaloniaFact]
    public async Task StatusText_AfterDocumentLoad_ShowsPageInfo()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        // Act
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Assert
        vm.StatusText.Should().Contain("Page");
        vm.StatusText.Should().Contain("Zoom");
    }

    [AvaloniaFact]
    public async Task StatusText_AfterZoom_ReflectsNewZoom()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Act
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe();
            vm.ZoomInCommand.Execute().Subscribe();
        });

        // Assert - Status should show zoom greater than 100%
        vm.StatusText.Should().Contain("Zoom:");
    }

    #endregion

    #region Redaction Area with Zoom

    [AvaloniaFact]
    public async Task RedactionArea_AtDifferentZoomLevels_StoresCorrectValues()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test Content");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Act - Set redaction area at 100% zoom
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.CurrentRedactionArea = new Rect(100, 100, 200, 50);
        });

        var area1 = vm.CurrentRedactionArea;

        // Change zoom
        Dispatcher.UIThread.Invoke(() =>
        {
            vm.ZoomInCommand.Execute().Subscribe();
        });

        // The stored redaction area should remain the same (zoom-independent PDF coordinates)
        var area2 = vm.CurrentRedactionArea;

        // Assert
        area1.Should().Be(area2);
    }

    #endregion

    #region Multiple Documents Test

    [AvaloniaFact]
    public async Task LoadDocument_SecondDocument_ReplacesFirst()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath1 = CreateTestPdf("First Document");
        var pdfPath2 = CreateTestPdf("Second Document");

        // Act - Load first document
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath1);
        });

        var firstName = vm.DocumentName;

        // Act - Load second document
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath2);
        });

        // Assert
        vm.DocumentName.Should().NotBe(firstName);
        vm.DocumentName.Should().Be(Path.GetFileName(pdfPath2));
    }

    #endregion

    #region Full Redaction Workflow Tests - End-to-End UI Verification

    /// <summary>
    /// CRITICAL TEST: Full UI workflow for redaction
    /// 1. Generate PDF with known text
    /// 2. Load via ViewModel
    /// 3. Enable redaction mode
    /// 4. Set redaction area
    /// 5. Apply redaction via command
    /// 6. Save document
    /// 7. Verify text was REMOVED (not just hidden)
    /// </summary>
    [AvaloniaFact]
    public async Task FullRedactionWorkflow_ViaViewModel_RemovesTextFromPdfStructure()
    {
        // STEP 1: Generate PDF with known text at known position
        var originalPdf = Path.Combine(Path.GetTempPath(), $"ui_redaction_test_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"ui_redaction_test_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        // Create PDF with "CONFIDENTIAL" text at position (100, 100)
        TestPdfGenerator.CreateSimpleTextPdf(originalPdf, "CONFIDENTIAL");

        // Verify text exists before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(originalPdf);
        textBefore.Should().Contain("CONFIDENTIAL", "text should exist before redaction");

        // STEP 2: Create ViewModel and load document
        var documentService = new PdfDocumentService(_docLoggerMock.Object);
        var renderService = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService = new PdfSearchService(_searchLoggerMock.Object);

        var vm = new MainWindowViewModel(
            _vmLoggerMock.Object,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
        });

        vm.TotalPages.Should().Be(1, "document should be loaded");

        // STEP 3: Enable redaction mode via command
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        vm.IsRedactionMode.Should().BeTrue("redaction mode should be enabled");

        // STEP 4: Set redaction area (covers the text at position ~100, 100)
        // Text is at (100, 100) in PDF points. The RedactionService scales by 72/renderDpi.
        // Default renderDpi is 150, so we need screen coordinates: PDF coords * (150/72)
        // screenX = 100 * 150/72 ≈ 208, screenY = 100 * 150/72 ≈ 208
        var redactionArea = new Rect(180, 180, 300, 60);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.CurrentRedactionArea = redactionArea;
        });

        vm.CurrentRedactionArea.Should().Be(redactionArea, "redaction area should be set");

        // Verify state before applying redaction
        vm.IsRedactionMode.Should().BeTrue("redaction mode must be enabled");
        vm.CurrentRedactionArea.Width.Should().BeGreaterThan(0, "redaction area width must be positive");
        vm.CurrentRedactionArea.Height.Should().BeGreaterThan(0, "redaction area height must be positive");

        // STEP 5: Apply redaction via command (simulates clicking "Apply Redaction" button)
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Execute the command and properly await completion using FirstAsync()
            await vm.ApplyRedactionCommand.Execute().FirstAsync();
        });

        // STEP 6: Save document to new path
        documentService.SaveDocument(redactedPdf);

        // Debug: Check that document was actually modified
        var fileInfo = new FileInfo(redactedPdf);
        fileInfo.Length.Should().BeGreaterThan(0, "saved file should have content");

        // STEP 7: Verify text was REMOVED from PDF structure
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("CONFIDENTIAL",
            "CRITICAL: Text must be REMOVED from PDF structure via UI workflow, not just visually hidden");

        // Verify PDF is still valid
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue("PDF should remain valid after redaction");
    }

    /// <summary>
    /// Test selective redaction: only targeted text is removed, other text preserved
    /// </summary>
    [AvaloniaFact]
    public async Task FullRedactionWorkflow_SelectiveRemoval_PreservesNonTargetedText()
    {
        // Create PDF with multiple text items
        var originalPdf = Path.Combine(Path.GetTempPath(), $"ui_selective_test_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"ui_selective_test_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        // Create PDF with mapped content (multiple text items at known positions)
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPdf);

        // Verify all text exists before
        var textBefore = PdfTestHelpers.ExtractAllText(originalPdf);
        textBefore.Should().Contain("CONFIDENTIAL");
        textBefore.Should().Contain("PUBLIC");
        textBefore.Should().Contain("SECRET");

        // Create ViewModel and load document
        var documentService = new PdfDocumentService(_docLoggerMock.Object);
        var renderService = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService = new PdfSearchService(_searchLoggerMock.Object);

        var vm = new MainWindowViewModel(
            _vmLoggerMock.Object,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
        });

        // Enable redaction mode
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Redact ONLY "CONFIDENTIAL" using its mapped position
        // The contentMap returns PDF point coordinates. Scale to screen coords (150 DPI).
        var confidentialPos = contentMap["CONFIDENTIAL"];
        var dpiScale = 150.0 / 72.0;
        var redactionArea = new Rect(
            (confidentialPos.x - 5) * dpiScale,
            (confidentialPos.y - 5) * dpiScale,
            (confidentialPos.width + 10) * dpiScale,
            (confidentialPos.height + 10) * dpiScale
        );

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.CurrentRedactionArea = redactionArea;
        });

        // Apply redaction
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.ApplyRedactionCommand.Execute().FirstAsync();
        });

        // Save
        documentService.SaveDocument(redactedPdf);

        // Verify selective removal
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("CONFIDENTIAL",
            "targeted text should be REMOVED");
        textAfter.Should().Contain("PUBLIC",
            "non-targeted text should be PRESERVED");
        textAfter.Should().Contain("SECRET",
            "non-targeted text should be PRESERVED");
    }

    /// <summary>
    /// Test multiple sequential redactions via UI
    /// </summary>
    [AvaloniaFact]
    public async Task FullRedactionWorkflow_MultipleRedactions_AllTextRemoved()
    {
        var originalPdf = Path.Combine(Path.GetTempPath(), $"ui_multi_redact_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"ui_multi_redact_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        // Create PDF with mapped content
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPdf);

        // Create ViewModel and load
        var documentService = new PdfDocumentService(_docLoggerMock.Object);
        var renderService = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService = new PdfSearchService(_searchLoggerMock.Object);

        var vm = new MainWindowViewModel(
            _vmLoggerMock.Object,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
        });

        // Enable redaction mode
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Apply multiple redactions sequentially (simulating user drawing multiple boxes)
        var itemsToRedact = new[] { "CONFIDENTIAL", "SECRET" };
        var dpiScale = 150.0 / 72.0;  // Scale PDF points to screen coordinates at 150 DPI

        foreach (var item in itemsToRedact)
        {
            var pos = contentMap[item];
            var redactionArea = new Rect(
                (pos.x - 5) * dpiScale,
                (pos.y - 5) * dpiScale,
                (pos.width + 10) * dpiScale,
                (pos.height + 10) * dpiScale
            );

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.CurrentRedactionArea = redactionArea;
            });

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await vm.ApplyRedactionCommand.Execute().FirstAsync();
            });

            // Simulate user re-enabling redaction mode after each redaction (as the UI does)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!vm.IsRedactionMode)
                    vm.ToggleRedactionModeCommand.Execute().Subscribe();
            });
        }

        // Save
        documentService.SaveDocument(redactedPdf);

        // Verify all targeted items removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("CONFIDENTIAL", "first redacted item should be removed");
        textAfter.Should().NotContain("SECRET", "second redacted item should be removed");
        textAfter.Should().Contain("PUBLIC", "non-redacted item should remain");
        textAfter.Should().Contain("PRIVATE", "non-redacted item should remain");
    }

    /// <summary>
    /// Test that redaction via UI survives save and reload
    /// </summary>
    [AvaloniaFact]
    public async Task FullRedactionWorkflow_SaveAndReload_RedactionIsPermanent()
    {
        var originalPdf = Path.Combine(Path.GetTempPath(), $"ui_permanent_test_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"ui_permanent_test_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        TestPdfGenerator.CreateSimpleTextPdf(originalPdf, "PERMANENT REMOVAL TEST");

        // First session: Apply redaction
        var documentService1 = new PdfDocumentService(_docLoggerMock.Object);
        var renderService1 = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService1 = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService1 = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService1 = new PdfSearchService(_searchLoggerMock.Object);

        var vm1 = new MainWindowViewModel(
            _vmLoggerMock.Object,
            documentService1,
            renderService1,
            redactionService1,
            textExtractionService1,
            searchService1);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm1.LoadDocumentAsync(originalPdf);
            vm1.ToggleRedactionModeCommand.Execute().Subscribe();
            // Scale PDF coords (100, 100) to screen coords at 150 DPI
            var dpiScale = 150.0 / 72.0;
            vm1.CurrentRedactionArea = new Rect(80 * dpiScale, 80 * dpiScale, 300 * dpiScale, 60 * dpiScale);
            await vm1.ApplyRedactionCommand.Execute().FirstAsync();
        });

        documentService1.SaveDocument(redactedPdf);

        // Second session: Reload and verify
        var documentService2 = new PdfDocumentService(new Mock<ILogger<PdfDocumentService>>().Object);
        documentService2.LoadDocument(redactedPdf);

        // Use independent verification (PdfPig via PdfTestHelpers)
        var textAfterReload = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfterReload.Should().NotContain("PERMANENT REMOVAL TEST",
            "text should be permanently removed after save and reload - not recoverable");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();
    }

    /// <summary>
    /// Test redaction with random area selection (simulates user drawing arbitrary box)
    /// </summary>
    [AvaloniaFact]
    public async Task FullRedactionWorkflow_RandomAreaSelection_RemovesIntersectingContent()
    {
        var originalPdf = Path.Combine(Path.GetTempPath(), $"ui_random_area_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"ui_random_area_{Guid.NewGuid()}_redacted.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        // Create PDF with grid content
        TestPdfGenerator.CreateGridContentPdf(originalPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(originalPdf);
        textBefore.Should().Contain("Cell(100,100)", "grid content should exist");

        // Create ViewModel and load
        var documentService = new PdfDocumentService(_docLoggerMock.Object);
        var renderService = new PdfRenderService(_renderLoggerMock.Object);
        var redactionService = new RedactionService(_redactionLoggerMock.Object, _loggerFactory);
        var textExtractionService = new PdfTextExtractionService(_textLoggerMock.Object);
        var searchService = new PdfSearchService(_searchLoggerMock.Object);

        var vm = new MainWindowViewModel(
            _vmLoggerMock.Object,
            documentService,
            renderService,
            redactionService,
            textExtractionService,
            searchService);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
        });

        // Enable redaction mode
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Apply redaction at position that covers Cell(100,100)
        // Cell(100,100) is at PDF point (100, 100). Scale to screen coords at 150 DPI.
        var dpiScale = 150.0 / 72.0;
        var redactionArea = new Rect(90 * dpiScale, 90 * dpiScale, 120 * dpiScale, 30 * dpiScale);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.CurrentRedactionArea = redactionArea;
        });

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.ApplyRedactionCommand.Execute().FirstAsync();
        });

        // Save
        documentService.SaveDocument(redactedPdf);

        // Verify
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        // Cell(100,100) should be removed (it's under the redaction area)
        textAfter.Should().NotContain("Cell(100,100)",
            "content under redaction area should be removed");

        // But other cells should remain
        textAfter.Should().Contain("Cell(100,200)",
            "content outside redaction area should be preserved");
    }

    #endregion
}
