using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests to verify conformance with basic PDF viewer/editor requirements
/// Based on ISO 32000 (PDF specification) core functionality
/// </summary>
public class PdfConformanceTests : IDisposable
{
    private readonly PdfDocumentService _documentService;
    private readonly PdfRenderService _renderService;
    private readonly PdfTextExtractionService _textService;
    private readonly PdfSearchService _searchService;
    private readonly string _testOutputDir;

    public PdfConformanceTests()
    {
        var docLogger = new Mock<ILogger<PdfDocumentService>>().Object;
        var renderLogger = new Mock<ILogger<PdfRenderService>>().Object;
        var textLogger = new Mock<ILogger<PdfTextExtractionService>>().Object;
        var searchLogger = new Mock<ILogger<PdfSearchService>>().Object;

        _documentService = new PdfDocumentService(docLogger);
        _renderService = new PdfRenderService(renderLogger);
        _textService = new PdfTextExtractionService(textLogger);
        _searchService = new PdfSearchService(searchLogger);

        _testOutputDir = Path.Combine(Path.GetTempPath(), "ConformanceTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDir);
    }

    #region Basic PDF Operations (ISO 32000 Core)

    [Fact]
    public void PDF_CanOpenAndReadBasicDocument()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "basic.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 1);

        // Act
        _documentService.LoadDocument(pdfPath);

        // Assert
        _documentService.IsDocumentLoaded.Should().BeTrue();
        _documentService.PageCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PDF_CanRenderPages()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "render.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 2);

        // Act
        var bitmap = await _renderService.RenderPageAsync(pdfPath, 0);

        // Assert
        bitmap.Should().NotBeNull();
        bitmap!.PixelSize.Width.Should().BeGreaterThan(0);
        bitmap.PixelSize.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PDF_CanExtractText()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "text.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(pdfPath, new[] { "Test content" });

        // Act
        var text = _textService.ExtractTextFromPage(pdfPath, 0);

        // Assert
        text.Should().NotBeNullOrEmpty();
        text.Should().Contain("Test");
    }

    [Fact]
    public void PDF_CanSearchText()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "search.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(pdfPath, new[] { "Searchable content" });

        // Act
        var results = _searchService.Search(pdfPath, "Searchable");

        // Assert
        results.Should().NotBeEmpty();
    }

    [Fact]
    public void PDF_CanModifyPageCount()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "modify.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 3);

        _documentService.LoadDocument(pdfPath);
        var originalCount = _documentService.PageCount;

        // Act - Remove a page
        _documentService.RemovePage(0);

        // Assert
        _documentService.PageCount.Should().Be(originalCount - 1);
    }

    [Fact]
    public void PDF_CanAddPages()
    {
        // Arrange
        var pdf1 = Path.Combine(_testOutputDir, "add1.pdf");
        var pdf2 = Path.Combine(_testOutputDir, "add2.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdf1, pageCount: 2);
        TestPdfGenerator.CreateSimpleTextPdf(pdf2, pageCount: 3);

        _documentService.LoadDocument(pdf1);
        var originalCount = _documentService.PageCount;

        // Act
        _documentService.AddPagesFromPdf(pdf2);

        // Assert
        _documentService.PageCount.Should().Be(originalCount + 3);
    }

    [Fact]
    public void PDF_CanSaveModifications()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "save_mod.pdf");
        var savePath = Path.Combine(_testOutputDir, "save_mod_result.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 5);
        _documentService.LoadDocument(pdfPath);

        // Act
        _documentService.RemovePage(0);
        _documentService.SaveDocument(savePath);

        // Assert - Verify by reopening
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(4);
    }

    #endregion

    #region Page Manipulation (ISO 32000 Section 7.7.3)

    [Fact]
    public void PDF_CanRotatePages()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "rotate.pdf");
        var savePath = Path.Combine(_testOutputDir, "rotate_result.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 1);
        _documentService.LoadDocument(pdfPath);

        // Act
        _documentService.RotatePageRight(0);
        _documentService.SaveDocument(savePath);

        // Assert
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.Pages[0].Rotate.Should().Be(90);
    }

    [Fact]
    public void PDF_RotationPersistsAcrossSaveLoad()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "rotate_persist.pdf");
        var savePath = Path.Combine(_testOutputDir, "rotate_persist_result.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 1);
        _documentService.LoadDocument(pdfPath);

        // Act
        _documentService.RotatePageRight(0);
        _documentService.SaveDocument(savePath);
        _documentService.CloseDocument();

        // Reload
        _documentService.LoadDocument(savePath);
        var doc = _documentService.GetCurrentDocument();

        // Assert
        doc.Should().NotBeNull();
        doc!.Pages[0].Rotate.Should().Be(90);
    }

    #endregion

    #region Content Modification (Redaction)

    [Fact]
    public async Task PDF_CanRedactContent()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "redact.pdf");
        var savePath = Path.Combine(_testOutputDir, "redact_result.pdf");

        var contentMap = TestPdfGenerator.CreateMappedContentPdf(pdfPath);
        _documentService.LoadDocument(pdfPath);

        var logger = new Mock<ILogger<RedactionService>>().Object;
        var loggerFactory = new Mock<ILoggerFactory>().Object;
        var redactionService = new RedactionService(logger, loggerFactory);

        var doc = _documentService.GetCurrentDocument();
        var page = doc!.Pages[0];

        // Get a text location
        var targetEntry = contentMap.contentMap.First();
        var targetText = targetEntry.Key;  // The actual text string
        var targetPos = targetEntry.Value;  // The position tuple
        var redactArea = new Avalonia.Rect(
            targetPos.x,
            targetPos.y,
            targetPos.width,
            targetPos.height
        );

        // Act
        redactionService.RedactArea(page, redactArea);
        _documentService.SaveDocument(savePath);

        // Assert - Text should be removed
        var textAfter = _textService.ExtractTextFromPage(savePath, 0);
        textAfter.Should().NotContain(targetText);
    }

    #endregion

    #region Multi-Page Operations

    [Fact]
    public async Task PDF_CanHandleMultiplePages()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "multi.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 10);

        // Act & Assert - Can load
        _documentService.LoadDocument(pdfPath);
        _documentService.PageCount.Should().Be(10);

        // Can render all pages
        for (int i = 0; i < 10; i++)
        {
            var bitmap = await _renderService.RenderPageAsync(pdfPath, i);
            bitmap.Should().NotBeNull();
        }

        // Can modify any page
        _documentService.RotatePageRight(5);
        var doc = _documentService.GetCurrentDocument();
        doc!.Pages[5].Rotate.Should().Be(90);
    }

    #endregion

    #region Export Capabilities

    [Fact]
    public async Task PDF_CanExportPagesToImages()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "export.pdf");
        var exportDir = Path.Combine(_testOutputDir, "export_imgs");
        Directory.CreateDirectory(exportDir);

        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 3);

        // Act
        for (int i = 0; i < 3; i++)
        {
            var bitmap = await _renderService.RenderPageAsync(pdfPath, i);
            bitmap!.Save(Path.Combine(exportDir, $"page_{i}.png"));
        }

        // Assert
        Directory.GetFiles(exportDir, "*.png").Should().HaveCount(3);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void PDF_HandlesInvalidFiles()
    {
        // Act & Assert
        Assert.Throws<FileNotFoundException>(() =>
            _documentService.LoadDocument("nonexistent.pdf"));
    }

    [Fact]
    public void PDF_HandlesInvalidPageIndex()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "invalid_idx.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 2);
        _documentService.LoadDocument(pdfPath);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _documentService.RemovePage(99));
    }

    #endregion

    public void Dispose()
    {
        _documentService.CloseDocument();
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, true);
        }
    }
}
