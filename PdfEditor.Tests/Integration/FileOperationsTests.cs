using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Tests.Integration;

public class FileOperationsTests : IDisposable
{
    private readonly PdfDocumentService _documentService;
    private readonly string _testOutputDir;

    public FileOperationsTests()
    {
        var logger = new Mock<ILogger<PdfDocumentService>>().Object;
        _documentService = new PdfDocumentService(logger);

        _testOutputDir = Path.Combine(Path.GetTempPath(), "FileOpsTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDir);
    }

    [Fact]
    public void SaveAs_CreatesNewFile()
    {
        // Arrange
        var sourcePdf = Path.Combine(_testOutputDir, "source.pdf");
        var destinationPdf = Path.Combine(_testOutputDir, "destination.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(sourcePdf, pageCount: 3);
        _documentService.LoadDocument(sourcePdf);

        // Act
        _documentService.SaveDocument(destinationPdf);

        // Assert
        File.Exists(destinationPdf).Should().BeTrue();

        using var doc = PdfReader.Open(destinationPdf, PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(3);
    }

    [Fact]
    public void SaveAs_AfterModification_SavesChanges()
    {
        // Arrange
        var sourcePdf = Path.Combine(_testOutputDir, "source_mod.pdf");
        var destinationPdf = Path.Combine(_testOutputDir, "destination_mod.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(sourcePdf, pageCount: 5);
        _documentService.LoadDocument(sourcePdf);

        // Act - Modify and save
        _documentService.RemovePage(0); // Remove first page
        _documentService.RotatePageRight(0); // Rotate new first page
        _documentService.SaveDocument(destinationPdf);

        // Assert
        using var doc = PdfReader.Open(destinationPdf, PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(4); // One page removed
        doc.Pages[0].Rotate.Should().Be(90); // First page rotated
    }

    [Fact]
    public void CloseDocument_ClearsCurrentDocument()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "close_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 2);
        _documentService.LoadDocument(pdfPath);

        _documentService.IsDocumentLoaded.Should().BeTrue();

        // Act
        _documentService.CloseDocument();

        // Assert
        _documentService.IsDocumentLoaded.Should().BeFalse();
        _documentService.PageCount.Should().Be(0);
    }

    [Fact]
    public void LoadDocument_AfterClose_LoadsSuccessfully()
    {
        // Arrange
        var pdf1 = Path.Combine(_testOutputDir, "doc1.pdf");
        var pdf2 = Path.Combine(_testOutputDir, "doc2.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdf1, pageCount: 2);
        TestPdfGenerator.CreateSimpleTextPdf(pdf2, pageCount: 3);

        // Act
        _documentService.LoadDocument(pdf1);
        _documentService.PageCount.Should().Be(2);

        _documentService.CloseDocument();

        _documentService.LoadDocument(pdf2);

        // Assert
        _documentService.IsDocumentLoaded.Should().BeTrue();
        _documentService.PageCount.Should().Be(3);
    }

    [Fact]
    public void SaveDocument_OverwritesOriginal()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "overwrite.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, pageCount: 5);

        _documentService.LoadDocument(pdfPath);

        // Act - Modify and save to same file
        _documentService.RemovePage(0);
        _documentService.RemovePage(0);
        _documentService.SaveDocument(); // Save to original path

        // Assert - Reload and verify
        _documentService.CloseDocument();
        _documentService.LoadDocument(pdfPath);
        _documentService.PageCount.Should().Be(3); // 5 - 2 = 3
    }

    [Fact]
    public void AddPages_ThenSaveAs_PreservesAllPages()
    {
        // Arrange
        var pdf1 = Path.Combine(_testOutputDir, "add_source.pdf");
        var pdf2 = Path.Combine(_testOutputDir, "add_pages_from.pdf");
        var resultPdf = Path.Combine(_testOutputDir, "add_result.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdf1, pageCount: 2);
        TestPdfGenerator.CreateSimpleTextPdf(pdf2, pageCount: 3);

        _documentService.LoadDocument(pdf1);

        // Act
        _documentService.AddPagesFromPdf(pdf2);
        _documentService.SaveDocument(resultPdf);

        // Assert
        using var doc = PdfReader.Open(resultPdf, PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(5); // 2 + 3
    }

    [Fact]
    public void LoadDocument_InvalidPath_ThrowsException()
    {
        // Act & Assert
        // The service wraps the error in a generic Exception
        var ex = Assert.Throws<Exception>(() =>
            _documentService.LoadDocument("nonexistent.pdf"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void SaveDocument_NoDocumentLoaded_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _documentService.SaveDocument());
    }

    public void Dispose()
    {
        _documentService.CloseDocument();
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, true);
        }
    }
}
