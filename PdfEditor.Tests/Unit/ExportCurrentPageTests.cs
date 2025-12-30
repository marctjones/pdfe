using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Verification;
using PdfEditor.Tests.Utilities;
using PdfEditor.ViewModels;
using SkiaSharp;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for Export Current Page functionality (Issue #35).
/// Tests the ExportCurrentPageToImageAsync method that exports PDF pages to PNG/JPEG.
/// </summary>
public class ExportCurrentPageTests : IDisposable
{
    private readonly Mock<ILogger<MainWindowViewModel>> _vmLoggerMock;
    private readonly Mock<ILogger<PdfDocumentService>> _docLoggerMock;
    private readonly Mock<ILogger<PdfRenderService>> _renderLoggerMock;
    private readonly Mock<ILogger<RedactionService>> _redactionLoggerMock;
    private readonly Mock<ILogger<PdfTextExtractionService>> _textLoggerMock;
    private readonly Mock<ILogger<PdfSearchService>> _searchLoggerMock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<string> _tempFiles = new();

    public ExportCurrentPageTests()
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

    private string CreateTestPdf(string content = "Test Document Content")
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"export_test_{Guid.NewGuid()}.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(tempPath, content);
        _tempFiles.Add(tempPath);
        return tempPath;
    }

    private string GetTempOutputPath(string extension)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"export_output_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(tempPath);
        return tempPath;
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_WithLoadedDocument_CreatesPngFile()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test content for export");
        var outputPath = GetTempOutputPath(".png");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        await vm.ExportCurrentPageToImageAsync(outputPath);

        // Assert
        File.Exists(outputPath).Should().BeTrue("PNG file should be created");
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0, "PNG file should have content");
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_WithLoadedDocument_CreatesJpegFile()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Test content for JPEG export");
        var outputPath = GetTempOutputPath(".jpg");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        await vm.ExportCurrentPageToImageAsync(outputPath);

        // Assert
        File.Exists(outputPath).Should().BeTrue("JPEG file should be created");
        var fileInfo = new FileInfo(outputPath);
        fileInfo.Length.Should().BeGreaterThan(0, "JPEG file should have content");
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_PngFile_IsValidImage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Content to render as image");
        var outputPath = GetTempOutputPath(".png");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        await vm.ExportCurrentPageToImageAsync(outputPath);

        // Assert
        using var bitmap = SKBitmap.Decode(outputPath);
        bitmap.Should().NotBeNull("should decode as valid PNG");
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_JpegFile_IsValidImage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Content to render as JPEG");
        var outputPath = GetTempOutputPath(".jpeg");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        await vm.ExportCurrentPageToImageAsync(outputPath);

        // Assert
        using var bitmap = SKBitmap.Decode(outputPath);
        bitmap.Should().NotBeNull("should decode as valid JPEG");
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_WithHigherDpi_CreatesLargerImage()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("DPI test content");
        var lowDpiPath = GetTempOutputPath(".png");
        var highDpiPath = GetTempOutputPath(".png");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        await vm.ExportCurrentPageToImageAsync(lowDpiPath, dpi: 72);
        await vm.ExportCurrentPageToImageAsync(highDpiPath, dpi: 300);

        // Assert
        using var lowDpiBitmap = SKBitmap.Decode(lowDpiPath);
        using var highDpiBitmap = SKBitmap.Decode(highDpiPath);

        highDpiBitmap.Width.Should().BeGreaterThan(lowDpiBitmap.Width,
            "higher DPI should produce larger image");
        highDpiBitmap.Height.Should().BeGreaterThan(lowDpiBitmap.Height,
            "higher DPI should produce larger image");
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_NoDocumentLoaded_DoesNotCreateFile()
    {
        // Arrange
        var vm = CreateViewModel();
        var outputPath = GetTempOutputPath(".png");

        // Act
        await vm.ExportCurrentPageToImageAsync(outputPath);

        // Assert
        File.Exists(outputPath).Should().BeFalse("should not create file when no document loaded");
    }

    [Fact]
    public async Task ExportCurrentPageToImageAsync_DefaultDpi_Is150()
    {
        // Arrange
        var vm = CreateViewModel();
        var pdfPath = CreateTestPdf("Default DPI test");
        var defaultDpiPath = GetTempOutputPath(".png");
        var explicit150DpiPath = GetTempOutputPath(".png");

        await vm.LoadDocumentAsync(pdfPath);

        // Act
        await vm.ExportCurrentPageToImageAsync(defaultDpiPath); // default DPI
        await vm.ExportCurrentPageToImageAsync(explicit150DpiPath, dpi: 150); // explicit 150 DPI

        // Assert
        using var defaultBitmap = SKBitmap.Decode(defaultDpiPath);
        using var explicitBitmap = SKBitmap.Decode(explicit150DpiPath);

        defaultBitmap.Width.Should().Be(explicitBitmap.Width, "default DPI should be 150");
        defaultBitmap.Height.Should().Be(explicitBitmap.Height, "default DPI should be 150");
    }
}
