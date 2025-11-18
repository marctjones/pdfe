using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Tests.Unit;

public class PageRotationTests : IDisposable
{
    private readonly PdfDocumentService _documentService;
    private readonly string _testOutputDir;
    private readonly string _testPdfPath;

    public PageRotationTests()
    {
        var logger = new Mock<ILogger<PdfDocumentService>>().Object;
        _documentService = new PdfDocumentService(logger);

        _testOutputDir = Path.Combine(Path.GetTempPath(), "PageRotationTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDir);

        // Create a test PDF
        _testPdfPath = Path.Combine(_testOutputDir, "rotation_test.pdf");
        TestPdfGenerator.CreateSimplePdf(_testPdfPath, pageCount: 3);
    }

    [Fact]
    public void RotatePageRight_RotatesBy90Degrees()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);
        var savePath = Path.Combine(_testOutputDir, "rotated_right.pdf");

        // Act
        _documentService.RotatePageRight(0);
        _documentService.SaveDocument(savePath);

        // Assert
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.Pages[0].Rotate.Should().Be(90);
    }

    [Fact]
    public void RotatePageLeft_RotatesBy270Degrees()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);
        var savePath = Path.Combine(_testOutputDir, "rotated_left.pdf");

        // Act
        _documentService.RotatePageLeft(0);
        _documentService.SaveDocument(savePath);

        // Assert
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.Pages[0].Rotate.Should().Be(270);
    }

    [Fact]
    public void RotatePage180_RotatesBy180Degrees()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);
        var savePath = Path.Combine(_testOutputDir, "rotated_180.pdf");

        // Act
        _documentService.RotatePage180(0);
        _documentService.SaveDocument(savePath);

        // Assert
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.Pages[0].Rotate.Should().Be(180);
    }

    [Fact]
    public void RotatePage_MultipleTimes_AccumulatesRotation()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);
        var savePath = Path.Combine(_testOutputDir, "rotated_multiple.pdf");

        // Act - Rotate right 4 times (should complete 360° and return to 0)
        _documentService.RotatePageRight(0);
        _documentService.RotatePageRight(0);
        _documentService.RotatePageRight(0);
        _documentService.RotatePageRight(0);
        _documentService.SaveDocument(savePath);

        // Assert
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.Pages[0].Rotate.Should().Be(0); // 360° = 0°
    }

    [Fact]
    public void RotatePage_SpecificPage_OnlyRotatesThatPage()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);
        var savePath = Path.Combine(_testOutputDir, "rotated_specific.pdf");

        // Act - Only rotate page 1 (index 0)
        _documentService.RotatePageRight(0);
        _documentService.SaveDocument(savePath);

        // Assert
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.ReadOnly);
        doc.Pages[0].Rotate.Should().Be(90);
        doc.Pages[1].Rotate.Should().Be(0); // Unchanged
        doc.Pages[2].Rotate.Should().Be(0); // Unchanged
    }

    [Fact]
    public void RotatePage_InvalidPageIndex_ThrowsException()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _documentService.RotatePageRight(99));
    }

    [Fact]
    public void RotatePage_NoDocumentLoaded_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            _documentService.RotatePageRight(0));
    }

    [Fact]
    public void RotatePage_InvalidDegrees_ThrowsException()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _documentService.RotatePage(0, 45)); // Only 0, 90, 180, 270 allowed
    }

    [Fact]
    public void RotatePage_PersistedAfterReload()
    {
        // Arrange
        _documentService.LoadDocument(_testPdfPath);
        var savePath = Path.Combine(_testOutputDir, "rotated_persisted.pdf");

        // Act
        _documentService.RotatePageRight(0);
        _documentService.SaveDocument(savePath);
        _documentService.CloseDocument();

        // Reload and check
        _documentService.LoadDocument(savePath);
        var doc = _documentService.GetCurrentDocument();

        // Assert
        doc.Should().NotBeNull();
        doc!.Pages[0].Rotate.Should().Be(90);
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
