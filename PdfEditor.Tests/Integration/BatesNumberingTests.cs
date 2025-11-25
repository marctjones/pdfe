using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for Bates numbering functionality
/// </summary>
public class BatesNumberingTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();
    private readonly ILoggerFactory _loggerFactory;

    public BatesNumberingTests()
    {
        _loggerFactory = new NullLoggerFactory();
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
        }
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    private string CreateTempFile(string extension = ".pdf")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    [Fact]
    public void ApplyBatesNumbers_SingleDocument_ShouldNumberAllPages()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 5);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        var options = new BatesOptions
        {
            Prefix = "DOE",
            StartNumber = 1,
            NumberOfDigits = 4
        };

        // Act
        service.ApplyBatesNumbers(document, options);
        var outputPath = CreateTempFile();
        document.Save(outputPath);

        // Assert
        // Verify the document was modified
        document.PageCount.Should().Be(5);

        // Reload and check text
        using var reloaded = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        var text = PdfTestHelpers.ExtractAllText(reloaded);

        // Should contain Bates numbers
        text.Should().Contain("DOE0001");
        text.Should().Contain("DOE0005");
    }

    [Fact]
    public void ApplyBatesNumbers_WithPrefixAndSuffix_ShouldFormatCorrectly()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Test Content");

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        var options = new BatesOptions
        {
            Prefix = "SMITH-",
            Suffix = "-CONF",
            StartNumber = 100,
            NumberOfDigits = 6
        };

        // Act
        service.ApplyBatesNumbers(document, options);
        var outputPath = CreateTempFile();
        document.Save(outputPath);

        // Assert
        using var reloaded = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        var text = PdfTestHelpers.ExtractAllText(reloaded);

        text.Should().Contain("SMITH-000100-CONF");
    }

    [Theory]
    [InlineData(BatesPosition.TopLeft)]
    [InlineData(BatesPosition.TopCenter)]
    [InlineData(BatesPosition.TopRight)]
    [InlineData(BatesPosition.BottomLeft)]
    [InlineData(BatesPosition.BottomCenter)]
    [InlineData(BatesPosition.BottomRight)]
    public void ApplyBatesNumbers_DifferentPositions_ShouldWork(BatesPosition position)
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Test Content");

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        var options = new BatesOptions
        {
            Prefix = "TEST",
            Position = position
        };

        // Act & Assert - Should not throw
        var act = () => service.ApplyBatesNumbers(document, options);
        act.Should().NotThrow();
    }

    [Fact]
    public void ApplyBatesNumbersToSet_MultipleDocuments_ShouldBeSequential()
    {
        // Arrange
        var outputDir = CreateTempDirectory();
        var files = new List<string>();

        // Create 3 documents with different page counts
        var path1 = CreateTempFile();
        var path2 = CreateTempFile();
        var path3 = CreateTempFile();

        TestPdfGenerator.CreateMultiPagePdf(path1, 3);  // 3 pages
        TestPdfGenerator.CreateMultiPagePdf(path2, 2);  // 2 pages
        TestPdfGenerator.CreateMultiPagePdf(path3, 4);  // 4 pages

        files.AddRange(new[] { path1, path2, path3 });

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        var options = new BatesOptions
        {
            Prefix = "DOC",
            StartNumber = 1,
            NumberOfDigits = 4,
            OutputDirectory = outputDir
        };

        // Act
        var result = service.ApplyBatesNumbersToSet(files, options);

        // Assert
        result.Documents.Should().HaveCount(3);
        result.FirstBatesNumber.Should().Be("DOC0001");
        result.LastBatesNumber.Should().Be("DOC0009"); // 3+2+4 = 9
        result.NextBatesNumber.Should().Be(10);
        result.TotalPages.Should().Be(9);

        // Verify sequence
        result.Documents[0].FirstBatesNumber.Should().Be("DOC0001");
        result.Documents[0].LastBatesNumber.Should().Be("DOC0003");

        result.Documents[1].FirstBatesNumber.Should().Be("DOC0004");
        result.Documents[1].LastBatesNumber.Should().Be("DOC0005");

        result.Documents[2].FirstBatesNumber.Should().Be("DOC0006");
        result.Documents[2].LastBatesNumber.Should().Be("DOC0009");
    }

    [Fact]
    public void ApplyBatesNumbersToSet_WithStartNumber_ShouldStartFromSpecified()
    {
        // Arrange
        var outputDir = CreateTempDirectory();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 3);

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        var options = new BatesOptions
        {
            Prefix = "CASE-",
            StartNumber = 500,
            NumberOfDigits = 5,
            OutputDirectory = outputDir
        };

        // Act
        var result = service.ApplyBatesNumbersToSet(new[] { pdfPath }, options);

        // Assert
        result.FirstBatesNumber.Should().Be("CASE-00500");
        result.LastBatesNumber.Should().Be("CASE-00502");
        result.NextBatesNumber.Should().Be(503);
    }

    [Fact]
    public void CalculateNextNumber_ShouldReturnCorrectValue()
    {
        // Arrange
        var files = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var path = CreateTempFile();
            TestPdfGenerator.CreateMultiPagePdf(path, i + 1); // 1, 2, 3 pages
            files.Add(path);
        }

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        // Act
        var nextNumber = service.CalculateNextNumber(files, startNumber: 100);

        // Assert
        // 1 + 2 + 3 = 6 pages total, starting at 100
        nextNumber.Should().Be(106);
    }

    [Fact]
    public void BatesOptions_DefaultValues_ShouldBeReasonable()
    {
        // Arrange & Act
        var options = new BatesOptions();

        // Assert
        options.Prefix.Should().BeEmpty();
        options.Suffix.Should().BeEmpty();
        options.StartNumber.Should().Be(1);
        options.NumberOfDigits.Should().Be(6);
        options.Position.Should().Be(BatesPosition.BottomRight);
        options.FontSize.Should().Be(10);
        options.MarginX.Should().Be(36); // 0.5 inch
        options.MarginY.Should().Be(36);
    }

    [Fact]
    public void ApplyBatesNumbersToSet_CreatesOutputFiles()
    {
        // Arrange
        var outputDir = CreateTempDirectory();
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Test");

        var service = new BatesNumberingService(_loggerFactory.CreateLogger<BatesNumberingService>());

        var options = new BatesOptions
        {
            Prefix = "DOC",
            OutputDirectory = outputDir,
            OutputSuffix = "_bates"
        };

        // Act
        var result = service.ApplyBatesNumbersToSet(new[] { pdfPath }, options);

        // Assert
        result.Documents[0].OutputPath.Should().NotBeNullOrEmpty();
        File.Exists(result.Documents[0].OutputPath).Should().BeTrue();
        result.Documents[0].OutputPath.Should().Contain("_bates");
    }
}
