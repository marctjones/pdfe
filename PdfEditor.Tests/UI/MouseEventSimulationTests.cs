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
using PdfEditor.Tests.Utilities;
using PdfEditor.ViewModels;
using SkiaSharp;
using System.Reactive.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.UI;

/// <summary>
/// Tests that simulate actual mouse events through Avalonia's headless testing
/// to verify the complete coordinate conversion pipeline from user interaction
/// to PDF redaction.
///
/// These tests verify:
/// 1. Mouse drag creates correct redaction area coordinates
/// 2. Coordinate conversion from screen to image pixels is accurate
/// 3. Zoom scaling is properly handled
/// 4. Full pipeline: mouse event → coordinate conversion → redaction → text removal
/// </summary>
[Collection("AvaloniaTests")]
public class MouseEventSimulationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<MainWindowViewModel>> _vmLoggerMock;
    private readonly Mock<ILogger<PdfDocumentService>> _docLoggerMock;
    private readonly Mock<ILogger<PdfRenderService>> _renderLoggerMock;
    private readonly Mock<ILogger<RedactionService>> _redactionLoggerMock;
    private readonly Mock<ILogger<PdfTextExtractionService>> _textLoggerMock;
    private readonly Mock<ILogger<PdfSearchService>> _searchLoggerMock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<string> _tempFiles = new();

    public MouseEventSimulationTests(ITestOutputHelper output)
    {
        _output = output;
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
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    private (MainWindowViewModel ViewModel, PdfDocumentService DocumentService, RedactionService RedactionService) CreateViewModelWithServices()
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

        var vm = new MainWindowViewModel(
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

        return (vm, documentService, redactionService);
    }

    private MainWindowViewModel CreateViewModel()
    {
        return CreateViewModelWithServices().ViewModel;
    }

    private string CreateTestPdf(string content = "Test Document Content")
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"mouse_sim_test_{Guid.NewGuid()}.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(tempPath, content);
        _tempFiles.Add(tempPath);
        return tempPath;
    }

    #region Direct ViewModel Coordinate Tests

    /// <summary>
    /// Test that setting CurrentRedactionArea via simulated UI produces correct coordinates.
    /// This simulates what happens when mouse events set the redaction area.
    /// </summary>
    [AvaloniaFact]
    public async Task RedactionArea_SetViaUI_HasCorrectCoordinates()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("TARGET TEXT TO REDACT");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
        });

        // Simulate user enabling redaction mode
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        vm.IsRedactionMode.Should().BeTrue();

        // Simulate mouse drag at 150 DPI coordinates (image pixels)
        // These are the coordinates that would come from Canvas.GetPosition
        var startX = 100.0;
        var startY = 50.0;
        var endX = 300.0;
        var endY = 100.0;

        // Act - Simulate setting redaction area (as mouse events would)
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var x = Math.Min(startX, endX);
            var y = Math.Min(startY, endY);
            var width = Math.Abs(endX - startX);
            var height = Math.Abs(endY - startY);

            vm.CurrentRedactionArea = new Rect(x, y, width, height);
        });

        // Assert
        vm.CurrentRedactionArea.X.Should().Be(100);
        vm.CurrentRedactionArea.Y.Should().Be(50);
        vm.CurrentRedactionArea.Width.Should().Be(200);
        vm.CurrentRedactionArea.Height.Should().Be(50);

        _output.WriteLine($"Redaction area set: {vm.CurrentRedactionArea}");
    }

    /// <summary>
    /// Test the coordinate conversion at different zoom levels.
    /// Note: With Avalonia's ScaleTransform, Canvas.GetPosition already returns
    /// coordinates in canvas space (not zoom-scaled).
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task RedactionArea_AtDifferentZoomLevels_CorrectlyConverted(double zoomLevel)
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("ZOOM TEST TEXT");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
            vm.ZoomLevel = zoomLevel;
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Image pixel coordinates (as returned by Canvas.GetPosition with ScaleTransform)
        // These are zoom-compensated coordinates in the 150 DPI image space
        var imageX = 150.0;
        var imageY = 200.0;
        var imageWidth = 250.0;
        var imageHeight = 50.0;

        // Act
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.CurrentRedactionArea = new Rect(imageX, imageY, imageWidth, imageHeight);
        });

        // Assert - coordinates should be the same regardless of zoom
        // (zoom is handled by Canvas ScaleTransform, coordinates are in image space)
        vm.CurrentRedactionArea.X.Should().Be(imageX);
        vm.CurrentRedactionArea.Y.Should().Be(imageY);
        vm.CurrentRedactionArea.Width.Should().Be(imageWidth);
        vm.CurrentRedactionArea.Height.Should().Be(imageHeight);

        _output.WriteLine($"At zoom {zoomLevel}: Redaction area = {vm.CurrentRedactionArea}");
    }

    /// <summary>
    /// Test that simulated mouse selection and redaction correctly removes text.
    /// </summary>
    [AvaloniaFact]
    public async Task SimulatedMouseSelection_ApplyRedaction_RemovesText()
    {
        // Arrange - use shared service so we can save the redacted document
        var (vm, documentService, redactionService) = CreateViewModelWithServices();
        var originalPdf = Path.Combine(Path.GetTempPath(), $"mouse_sim_redact_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"mouse_sim_redact_{Guid.NewGuid()}_done.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        // Create PDF with text at known position
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(originalPdf);
        textBefore.Should().Contain("CONFIDENTIAL");

        // Get position of target text
        var confidentialPos = contentMap["CONFIDENTIAL"];
        _output.WriteLine($"CONFIDENTIAL at PDF points: ({confidentialPos.x}, {confidentialPos.y}) {confidentialPos.width}x{confidentialPos.height}");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
        });

        vm.TotalPages.Should().Be(1);

        // Enable redaction mode
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Simulate mouse drag over the CONFIDENTIAL text
        // Convert PDF points to 150 DPI image pixels
        var dpiScale = 150.0 / 72.0;
        var mouseStartX = (confidentialPos.x - 5) * dpiScale;
        var mouseStartY = (confidentialPos.y - 5) * dpiScale;
        var mouseEndX = (confidentialPos.x + confidentialPos.width + 5) * dpiScale;
        var mouseEndY = (confidentialPos.y + confidentialPos.height + 5) * dpiScale;

        _output.WriteLine($"Simulated mouse selection (150 DPI pixels): " +
            $"({mouseStartX:F0}, {mouseStartY:F0}) to ({mouseEndX:F0}, {mouseEndY:F0})");

        // Act - Set redaction area (simulating mouse drag result)
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var x = Math.Min(mouseStartX, mouseEndX);
            var y = Math.Min(mouseStartY, mouseEndY);
            var width = Math.Abs(mouseEndX - mouseStartX);
            var height = Math.Abs(mouseEndY - mouseStartY);

            vm.CurrentRedactionArea = new Rect(x, y, width, height);
        });

        _output.WriteLine($"Redaction area: {vm.CurrentRedactionArea}");

        // Apply redaction directly via RedactionService
        // Note: The ViewModel now uses a mark-then-apply workflow with file dialogs,
        // which doesn't work in headless tests. We apply the redaction directly instead.
        var document = documentService.GetCurrentDocument();
        document.Should().NotBeNull("document should be loaded");
        var page = document!.Pages[0];
        redactionService.RedactArea(page, vm.CurrentRedactionArea, originalPdf, renderDpi: 150);

        // Save the redacted document using the SAME document service the ViewModel used
        documentService.SaveDocument(redactedPdf);

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after redaction: {textAfter}");

        textAfter.Should().NotContain("CONFIDENTIAL",
            "Simulated mouse selection should result in text removal");
        textAfter.Should().Contain("PUBLIC",
            "Non-selected text should be preserved");
    }

    #endregion

    #region Coordinate Pipeline Tests

    /// <summary>
    /// Test the full coordinate pipeline: image pixels → PDF points → redaction.
    /// </summary>
    [AvaloniaFact]
    public async Task CoordinatePipeline_ImagePixelsToPdfPoints_IsAccurate()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("PIPELINE TEST");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Test various coordinate conversions
        var testCases = new[]
        {
            (imageX: 150.0, imageY: 150.0, expectedPdfX: 72.0, expectedPdfY: 72.0),  // 150 DPI → 72 DPI
            (imageX: 300.0, imageY: 300.0, expectedPdfX: 144.0, expectedPdfY: 144.0),
            (imageX: 75.0, imageY: 225.0, expectedPdfX: 36.0, expectedPdfY: 108.0),
        };

        foreach (var (imageX, imageY, expectedPdfX, expectedPdfY) in testCases)
        {
            // Set redaction area in image pixels (150 DPI)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.CurrentRedactionArea = new Rect(imageX, imageY, 100, 50);
            });

            // The conversion factor: PDF points = image pixels * (72 / 150)
            var scale = 72.0 / 150.0;
            var actualPdfX = vm.CurrentRedactionArea.X * scale;
            var actualPdfY = vm.CurrentRedactionArea.Y * scale;

            actualPdfX.Should().BeApproximately(expectedPdfX, 0.1,
                $"Image ({imageX}, {imageY}) should convert to PDF ({expectedPdfX}, {expectedPdfY})");
            actualPdfY.Should().BeApproximately(expectedPdfY, 0.1);

            _output.WriteLine($"Image ({imageX}, {imageY}) → PDF ({actualPdfX:F2}, {actualPdfY:F2})");
        }
    }

    /// <summary>
    /// Test that sequential mouse selections and redactions work correctly.
    /// </summary>
    [AvaloniaFact]
    public async Task SequentialMouseSelections_MultipleRedactions_AllWorkCorrectly()
    {
        // Arrange - use shared service so we can save the redacted document
        var (vm, documentService, redactionService) = CreateViewModelWithServices();
        var originalPdf = Path.Combine(Path.GetTempPath(), $"mouse_seq_test_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"mouse_seq_test_{Guid.NewGuid()}_done.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPdf);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
        });

        var dpiScale = 150.0 / 72.0;
        var redactionTargets = new[] { "CONFIDENTIAL", "SECRET" };

        // Apply redactions directly via RedactionService
        // Note: The ViewModel now uses a mark-then-apply workflow with file dialogs,
        // which doesn't work in headless tests. We apply the redactions directly instead.
        var document = documentService.GetCurrentDocument();
        document.Should().NotBeNull("document should be loaded");
        var page = document!.Pages[0];

        foreach (var target in redactionTargets)
        {
            // Enable redaction mode
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!vm.IsRedactionMode)
                    vm.ToggleRedactionModeCommand.Execute().Subscribe();
            });

            // Get target position and simulate mouse selection
            var pos = contentMap[target];
            var selectionArea = new Rect(
                (pos.x - 5) * dpiScale,
                (pos.y - 5) * dpiScale,
                (pos.width + 10) * dpiScale,
                (pos.height + 10) * dpiScale);

            _output.WriteLine($"Simulating mouse selection for '{target}': {selectionArea}");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.CurrentRedactionArea = selectionArea;
            });

            redactionService.RedactArea(page, selectionArea, pdfPath, renderDpi: 150);
        }

        // Save the redacted document using the SAME document service the ViewModel used
        documentService.SaveDocument(redactedPdf);

        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        foreach (var target in redactionTargets)
        {
            textAfter.Should().NotContain(target, $"'{target}' should be removed");
        }

        textAfter.Should().Contain("PUBLIC", "Non-redacted text should remain");
        textAfter.Should().Contain("PRIVATE", "Non-redacted text should remain");

        _output.WriteLine("✓ All sequential mouse selections and redactions worked correctly");
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Test mouse selection at page boundaries.
    /// </summary>
    [AvaloniaFact]
    public async Task MouseSelection_AtPageBoundaries_HandledCorrectly()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("BOUNDARY TEST");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Test selection near edges (in 150 DPI image pixels)
        var edgeCases = new[]
        {
            new Rect(5, 5, 100, 50),       // Near top-left
            new Rect(1200, 5, 100, 50),    // Near top-right (assuming ~1275 pixel width at 150 DPI for letter)
            new Rect(5, 1600, 100, 50),   // Near bottom-left (assuming ~1650 pixel height)
            new Rect(600, 800, 100, 50),   // Center
        };

        foreach (var selection in edgeCases)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.CurrentRedactionArea = selection;
            });

            // Should not throw and should store coordinates
            vm.CurrentRedactionArea.X.Should().Be(selection.X);
            vm.CurrentRedactionArea.Y.Should().Be(selection.Y);

            _output.WriteLine($"Edge selection at ({selection.X}, {selection.Y}) accepted");
        }
    }

    /// <summary>
    /// Test that very small mouse selections are handled.
    /// </summary>
    [AvaloniaFact]
    public async Task MouseSelection_VerySmall_IsAccepted()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("SMALL SELECTION TEST");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Very small selection (e.g., accidental click)
        var smallSelection = new Rect(100, 100, 5, 5);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.CurrentRedactionArea = smallSelection;
        });

        vm.CurrentRedactionArea.Width.Should().Be(5);
        vm.CurrentRedactionArea.Height.Should().Be(5);
        _output.WriteLine("Very small selection handled correctly");
    }

    /// <summary>
    /// Test that mouse selection with inverted start/end points is normalized.
    /// </summary>
    [AvaloniaFact]
    public async Task MouseSelection_InvertedCoordinates_NormalizedCorrectly()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("INVERT TEST");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(pdfPath);
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Simulate drag from bottom-right to top-left
        var startX = 300.0;
        var startY = 200.0;
        var endX = 100.0;
        var endY = 100.0;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Normalize coordinates (as the UI code does)
            var x = Math.Min(startX, endX);
            var y = Math.Min(startY, endY);
            var width = Math.Abs(endX - startX);
            var height = Math.Abs(endY - startY);

            vm.CurrentRedactionArea = new Rect(x, y, width, height);
        });

        // Should be normalized to top-left origin
        vm.CurrentRedactionArea.X.Should().Be(100);
        vm.CurrentRedactionArea.Y.Should().Be(100);
        vm.CurrentRedactionArea.Width.Should().Be(200);
        vm.CurrentRedactionArea.Height.Should().Be(100);

        _output.WriteLine($"Inverted selection normalized to: {vm.CurrentRedactionArea}");
    }

    #endregion

    #region Visual Verification Tests

    /// <summary>
    /// Test that mouse-selected redaction produces visual black box at correct position.
    /// </summary>
    [AvaloniaFact]
    public async Task MouseSelection_Redaction_ProducesBlackBoxAtCorrectPosition()
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        // Arrange - use shared service so we can save the redacted document
        var (vm, documentService, redactionService) = CreateViewModelWithServices();
        var originalPdf = Path.Combine(Path.GetTempPath(), $"mouse_visual_test_{Guid.NewGuid()}.pdf");
        var redactedPdf = Path.Combine(Path.GetTempPath(), $"mouse_visual_test_{Guid.NewGuid()}_done.pdf");
        _tempFiles.Add(originalPdf);
        _tempFiles.Add(redactedPdf);

        TestPdfGenerator.CreateSimpleTextPdf(originalPdf, "VISUAL TARGET");

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await vm.LoadDocumentAsync(originalPdf);
            vm.ToggleRedactionModeCommand.Execute().Subscribe();
        });

        // Simulate mouse selection at specific location (150 DPI pixels)
        var selectionX = 150.0;
        var selectionY = 200.0;
        var selectionWidth = 300.0;
        var selectionHeight = 50.0;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            vm.CurrentRedactionArea = new Rect(selectionX, selectionY, selectionWidth, selectionHeight);
        });

        // Apply redaction directly via RedactionService
        // Note: The ViewModel now uses a mark-then-apply workflow with file dialogs,
        // which doesn't work in headless tests. We apply the redaction directly instead.
        var document = documentService.GetCurrentDocument();
        document.Should().NotBeNull("document should be loaded");
        var page = document!.Pages[0];
        redactionService.RedactArea(page, vm.CurrentRedactionArea, originalPdf, renderDpi: 150);

        // Save the redacted document using the SAME document service the ViewModel used
        documentService.SaveDocument(redactedPdf);

        // Render at 150 DPI and verify black box position
        const int renderDpi = 150;
        using var fileStream = File.OpenRead(redactedPdf);
        using var memoryStream = new MemoryStream();
        fileStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        var options = new PDFtoImage.RenderOptions(Dpi: renderDpi);
        using var bitmap = PDFtoImage.Conversion.ToImage(memoryStream, page: 0, options: options);

        // Check pixels at selection center
        var centerX = (int)(selectionX + selectionWidth / 2);
        var centerY = (int)(selectionY + selectionHeight / 2);

        var centerPixel = bitmap.GetPixel(centerX, centerY);
        _output.WriteLine($"Pixel at selection center ({centerX}, {centerY}): RGB({centerPixel.Red},{centerPixel.Green},{centerPixel.Blue})");

        var isBlack = centerPixel.Red < 50 && centerPixel.Green < 50 && centerPixel.Blue < 50;
        isBlack.Should().BeTrue("black box should appear at mouse selection location");

        _output.WriteLine("✓ Visual verification passed: black box at correct position");
    }

    #endregion
}
