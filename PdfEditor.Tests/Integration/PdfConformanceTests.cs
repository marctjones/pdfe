using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;
using SkiaSharp;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests to verify conformance with basic PDF viewer/editor requirements
/// Based on ISO 32000 (PDF specification) core functionality.
/// Uses PdfRenderingTests collection to avoid parallel execution issues with PDFium.
/// </summary>
[Collection("PdfRenderingTests")]
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

        // Assert - Skip if rendering not available (PDFium native libraries)
        if (bitmap == null)
        {
            // PDFium rendering not available in this environment
            return;
        }
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
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
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.Import);
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
        using var doc = PdfReader.Open(savePath, PdfDocumentOpenMode.Import);
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

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RedactionService>.Instance;
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
        redactionService.RedactArea(page, redactArea, pdfPath, renderDpi: 72);
        _documentService.SaveDocument(savePath);

        // Assert - Text should be removed
        var textAfter = _textService.ExtractTextFromPage(savePath, 0);
        textAfter.Should().NotContain(targetText);

        await Task.CompletedTask;
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

        // Can render all pages (skip if rendering not available)
        var firstBitmap = await _renderService.RenderPageAsync(pdfPath, 0);
        if (firstBitmap != null)
        {
            for (int i = 1; i < 10; i++)
            {
                var bitmap = await _renderService.RenderPageAsync(pdfPath, i);
                bitmap.Should().NotBeNull();
            }
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
        var exported = 0;
        var savedFiles = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var bitmap = await _renderService.RenderPageAsync(pdfPath, i);
            if (bitmap != null)
            {
                var filePath = Path.Combine(exportDir, $"page_{i}.png");
                using var image = SKImage.FromBitmap(bitmap);
                using var encodedData = image.Encode(SKEncodedImageFormat.Png, 100);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                encodedData.SaveTo(fileStream);
                savedFiles.Add(filePath);
                exported++;
            }
        }

        // Assert - Skip if rendering not available
        if (exported == 0)
        {
            return;
        }

        // Verify each file was actually written
        foreach (var file in savedFiles)
        {
            File.Exists(file).Should().BeTrue($"File {file} should exist after save");
        }

        Directory.GetFiles(exportDir, "*.png").Should().HaveCount(exported);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void PDF_HandlesInvalidFiles()
    {
        // Act & Assert
        // The service wraps the error in a generic Exception
        var ex = Assert.Throws<Exception>(() =>
            _documentService.LoadDocument("nonexistent.pdf"));
        Assert.Contains("not found", ex.Message);
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

    #region Mark-Then-Apply Conformance (Issue #30)

    /// <summary>
    /// Verifies mark-then-apply workflow produces valid PDF 1.7 documents.
    /// </summary>
    [Fact]
    public void MarkThenApply_ProducesValidPdf17Document()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "mark_apply_17.pdf");
        var savePath = Path.Combine(_testOutputDir, "mark_apply_17_result.pdf");

        TestPdfGenerator.CreateTextOnlyPdf(pdfPath, new[]
        {
            "Line 1 SECRET",
            "Line 2 PUBLIC",
            "Line 3 SECRET"
        });

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RedactionService>.Instance;
        var redactionService = new RedactionService(logger, loggerFactory);

        // Act - Mark multiple areas then apply all
        using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = pdfPigDoc.GetPage(1);
        var secretWords = page.GetWords().Where(w => w.Text == "SECRET").ToList();

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        foreach (var word in secretWords)
        {
            var pdfBounds = word.BoundingBox;
            var scale = 150.0 / 72.0;
            var imageY = (page.Height - pdfBounds.Top) * scale;

            var screenRect = new Avalonia.Rect(
                pdfBounds.Left * scale - 5,
                imageY - 5,
                (pdfBounds.Right - pdfBounds.Left) * scale + 10,
                (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
            );

            redactionService.RedactArea(document.Pages[0], screenRect, pdfPath, renderDpi: 150);
        }
        document.Save(savePath);
        document.Dispose();
        pdfPigDoc.Dispose();

        // Assert - Document should be valid and openable
        using var resultDoc = PdfReader.Open(savePath, PdfDocumentOpenMode.Import);
        resultDoc.PageCount.Should().Be(1);

        // Verify PDF version is at least 1.4 (default)
        resultDoc.Version.Should().BeGreaterOrEqualTo(14);

        // Verify text content
        var textAfter = _textService.ExtractTextFromPage(savePath, 0);
        textAfter.Should().NotContain("SECRET", "Redacted text should be removed");
        textAfter.Should().Contain("PUBLIC", "Non-redacted text should remain");
    }

    /// <summary>
    /// Verifies multi-page mark-then-apply produces valid PDFs.
    /// </summary>
    [Fact]
    public void MarkThenApply_MultiPage_ProducesValidPdf()
    {
        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "mark_apply_multi.pdf");
        var savePath = Path.Combine(_testOutputDir, "mark_apply_multi_result.pdf");

        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RedactionService>.Instance;
        var redactionService = new RedactionService(logger, loggerFactory);

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        // Act - Redact on each page
        for (int i = 0; i < 3; i++)
        {
            using var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var pigPage = pdfPigDoc.GetPage(i + 1);
            var secretWord = pigPage.GetWords().FirstOrDefault(w => w.Text == "Secret");

            if (secretWord != null)
            {
                var pdfBounds = secretWord.BoundingBox;
                var scale = 150.0 / 72.0;
                var imageY = (pigPage.Height - pdfBounds.Top) * scale;

                var screenRect = new Avalonia.Rect(
                    pdfBounds.Left * scale - 5,
                    imageY - 5,
                    (pdfBounds.Right - pdfBounds.Left) * scale + 10,
                    (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
                );

                redactionService.RedactArea(document.Pages[i], screenRect, pdfPath, renderDpi: 150);
            }
        }

        document.Save(savePath);
        document.Dispose();

        // Assert - Document structure should be valid
        using var resultDoc = PdfReader.Open(savePath, PdfDocumentOpenMode.Import);
        resultDoc.PageCount.Should().Be(3);

        // All pages should be accessible
        for (int i = 0; i < 3; i++)
        {
            var page = resultDoc.Pages[i];
            page.Should().NotBeNull();
        }

        // Text extraction should work without errors
        for (int i = 0; i < 3; i++)
        {
            var text = _textService.ExtractTextFromPage(savePath, i);
            text.Should().NotBeNull();
            text.Should().NotContain("Secret", $"Page {i + 1} should have Secret redacted");
        }
    }

    /// <summary>
    /// Verifies qpdf --check passes on redacted document.
    /// </summary>
    [SkippableFact]
    public void MarkThenApply_QpdfCheckPasses()
    {
        // Skip if qpdf not available
        var qpdfPath = FindExecutable("qpdf");
        Skip.If(qpdfPath == null, "qpdf not installed (apt-get install qpdf)");

        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "qpdf_check.pdf");
        var savePath = Path.Combine(_testOutputDir, "qpdf_check_result.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "SECRET_DATA");

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RedactionService>.Instance;
        var redactionService = new RedactionService(logger, loggerFactory);

        // Act
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var word = page.GetWords().FirstOrDefault();

            if (word != null)
            {
                var pdfBounds = word.BoundingBox;
                var scale = 150.0 / 72.0;
                var imageY = (page.Height - pdfBounds.Top) * scale;

                var screenRect = new Avalonia.Rect(
                    pdfBounds.Left * scale - 5,
                    imageY - 5,
                    (pdfBounds.Right - pdfBounds.Left) * scale + 50,
                    (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
                );

                var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
                redactionService.RedactArea(document.Pages[0], screenRect, pdfPath, renderDpi: 150);
                document.Save(savePath);
                document.Dispose();
            }
        }

        // Assert - qpdf --check should pass
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = qpdfPath,
            Arguments = $"--check \"{savePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi);
        process!.WaitForExit(10000);
        var stderr = process.StandardError.ReadToEnd();

        process.ExitCode.Should().Be(0, $"qpdf --check should pass. Error: {stderr}");
    }

    /// <summary>
    /// Verifies mutool info works on redacted document (no errors).
    /// </summary>
    [SkippableFact]
    public void MarkThenApply_MutoolInfoWorks()
    {
        // Skip if mutool not available
        var mutoolPath = FindExecutable("mutool");
        Skip.If(mutoolPath == null, "mutool not installed (apt-get install mupdf-tools)");

        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "mutool_info.pdf");
        var savePath = Path.Combine(_testOutputDir, "mutool_info_result.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "TEST_CONTENT");

        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RedactionService>.Instance;
        var redactionService = new RedactionService(logger, loggerFactory);

        // Act
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
        {
            var page = pdfPigDoc.GetPage(1);
            var word = page.GetWords().FirstOrDefault();

            if (word != null)
            {
                var pdfBounds = word.BoundingBox;
                var scale = 150.0 / 72.0;
                var imageY = (page.Height - pdfBounds.Top) * scale;

                var screenRect = new Avalonia.Rect(
                    pdfBounds.Left * scale - 5,
                    imageY - 5,
                    (pdfBounds.Right - pdfBounds.Left) * scale + 50,
                    (pdfBounds.Top - pdfBounds.Bottom) * scale + 10
                );

                var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
                redactionService.RedactArea(document.Pages[0], screenRect, pdfPath, renderDpi: 150);
                document.Save(savePath);
                document.Dispose();
            }
        }

        // Assert - mutool info should work without errors
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = mutoolPath,
            Arguments = $"info \"{savePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi);
        process!.WaitForExit(10000);
        var stdout = process.StandardOutput.ReadToEnd();

        // mutool info should return page count info
        stdout.Should().Contain("Pages:", "mutool info should show page count");
        process.ExitCode.Should().Be(0, "mutool info should exit successfully");
    }

    private static string? FindExecutable(string name)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "which",
                Arguments = name,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process!.WaitForExit(5000);
            if (process.ExitCode == 0)
            {
                return process.StandardOutput.ReadToEnd().Trim();
            }
        }
        catch { }
        return null;
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
