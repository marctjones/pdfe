using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for batch document processing
/// </summary>
public class BatchProcessingTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();
    private readonly ILoggerFactory _loggerFactory;

    public BatchProcessingTests()
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
    public async Task BatchProcess_SingleFile_ShouldSucceed()
    {
        // Arrange
        var inputPath = CreateTempFile();
        TestPdfGenerator.CreateComplexContentPdf(inputPath);

        var outputDir = CreateTempDirectory();

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var rules = new RedactionRuleSet
        {
            PIITypesToRedact = new List<PIIType> { PIIType.SSN }
        };

        var options = new BatchOptions
        {
            OutputDirectory = outputDir,
            CreateAuditLog = false
        };

        // Act
        var result = await processor.ProcessFilesAsync(
            new[] { inputPath },
            rules,
            options);

        // Assert
        result.TotalFiles.Should().Be(1);
        result.SuccessfulFiles.Should().Be(1);
        result.FailedFiles.Should().Be(0);
    }

    [Fact]
    public async Task BatchProcess_MultipleFiles_ShouldProcessAll()
    {
        // Arrange
        var inputDir = CreateTempDirectory();
        var outputDir = CreateTempDirectory();

        var files = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var path = Path.Combine(inputDir, $"doc{i}.pdf");
            TestPdfGenerator.CreateSimpleTextPdf(path, $"Document {i} SSN: 123-45-678{i}");
            files.Add(path);
        }

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var rules = new RedactionRuleSet
        {
            PIITypesToRedact = new List<PIIType> { PIIType.SSN }
        };

        var options = new BatchOptions
        {
            OutputDirectory = outputDir
        };

        // Act
        var result = await processor.ProcessFilesAsync(files, rules, options);

        // Assert
        result.TotalFiles.Should().Be(3);
        result.SuccessfulFiles.Should().Be(3);
        result.FileResults.Should().HaveCount(3);
    }

    [Fact]
    public async Task BatchProcess_WithProgress_ShouldReportProgress()
    {
        // Arrange
        var inputPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Test content");

        var outputDir = CreateTempDirectory();
        var progressReports = new List<BatchProgress>();

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var progress = new Progress<BatchProgress>(p => progressReports.Add(p));

        // Act
        var result = await processor.ProcessFilesAsync(
            new[] { inputPath },
            new RedactionRuleSet(),
            new BatchOptions { OutputDirectory = outputDir },
            progress);

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Last().Status.Should().Be("Finished");
    }

    [Fact]
    public async Task BatchProcess_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var inputDir = CreateTempDirectory();
        var files = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var path = Path.Combine(inputDir, $"doc{i}.pdf");
            TestPdfGenerator.CreateSimpleTextPdf(path, "Test content");
            files.Add(path);
        }

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await processor.ProcessFilesAsync(
                files,
                new RedactionRuleSet(),
                new BatchOptions { OutputDirectory = CreateTempDirectory() },
                cancellationToken: cts.Token);
        });
    }

    [Fact]
    public async Task BatchProcess_ContinueOnError_ShouldProcessRemainingFiles()
    {
        // Arrange
        var inputDir = CreateTempDirectory();
        var outputDir = CreateTempDirectory();

        // Create valid files
        var validPath1 = Path.Combine(inputDir, "valid1.pdf");
        var validPath2 = Path.Combine(inputDir, "valid2.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(validPath1, "Valid 1");
        TestPdfGenerator.CreateSimpleTextPdf(validPath2, "Valid 2");

        // Create an invalid file
        var invalidPath = Path.Combine(inputDir, "invalid.pdf");
        File.WriteAllText(invalidPath, "This is not a valid PDF");

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var options = new BatchOptions
        {
            OutputDirectory = outputDir,
            ContinueOnError = true
        };

        // Act
        var result = await processor.ProcessFilesAsync(
            new[] { validPath1, invalidPath, validPath2 },
            new RedactionRuleSet(),
            options);

        // Assert
        result.SuccessfulFiles.Should().Be(2);
        result.FailedFiles.Should().Be(1);
        result.FileResults.Where(r => !r.Success).Should().HaveCount(1);
    }

    [Fact]
    public async Task BatchProcess_ShouldCreateAuditLog()
    {
        // Arrange
        var inputPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Test SSN: 123-45-6789");

        var outputDir = CreateTempDirectory();

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var options = new BatchOptions
        {
            OutputDirectory = outputDir,
            CreateAuditLog = true
        };

        // Act
        var result = await processor.ProcessFilesAsync(
            new[] { inputPath },
            new RedactionRuleSet { PIITypesToRedact = { PIIType.SSN } },
            options);

        // Assert
        result.AuditLogPath.Should().NotBeNullOrEmpty();
        File.Exists(result.AuditLogPath).Should().BeTrue();
    }

    [Fact]
    public async Task BatchProcess_Directory_ShouldProcessAllPdfs()
    {
        // Arrange
        var inputDir = CreateTempDirectory();
        var outputDir = CreateTempDirectory();

        for (int i = 0; i < 3; i++)
        {
            var path = Path.Combine(inputDir, $"doc{i}.pdf");
            TestPdfGenerator.CreateSimpleTextPdf(path, $"Document {i}");
        }

        // Also create a non-PDF file that should be ignored
        File.WriteAllText(Path.Combine(inputDir, "readme.txt"), "Not a PDF");

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        // Act
        var result = await processor.ProcessDirectoryAsync(
            inputDir,
            new RedactionRuleSet(),
            new BatchOptions { OutputDirectory = outputDir });

        // Assert
        result.TotalFiles.Should().Be(3);
        result.SuccessfulFiles.Should().Be(3);
    }

    [Fact]
    public async Task BatchProcess_OutputFilePattern_ShouldApplyPattern()
    {
        // Arrange
        var inputPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Test content");

        var outputDir = CreateTempDirectory();
        var inputFileName = Path.GetFileNameWithoutExtension(inputPath);

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var options = new BatchOptions
        {
            OutputDirectory = outputDir,
            OutputFilePattern = "{filename}_SECURE{extension}"
        };

        // Act
        var result = await processor.ProcessFilesAsync(
            new[] { inputPath },
            new RedactionRuleSet(),
            options);

        // Assert
        var expectedOutput = Path.Combine(outputDir, $"{inputFileName}_SECURE.pdf");
        result.FileResults[0].OutputPath.Should().Be(expectedOutput);
        File.Exists(expectedOutput).Should().BeTrue();
    }

    [Fact]
    public async Task BatchProcess_OverwriteExisting_ShouldRespectSetting()
    {
        // Arrange
        var inputPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Test content");

        var outputDir = CreateTempDirectory();
        var inputFileName = Path.GetFileNameWithoutExtension(inputPath);
        var outputPath = Path.Combine(outputDir, $"{inputFileName}_redacted.pdf");

        // Create existing output file
        File.WriteAllText(outputPath, "Existing file");

        var processor = new BatchDocumentProcessor(
            _loggerFactory.CreateLogger<BatchDocumentProcessor>(),
            _loggerFactory);

        var options = new BatchOptions
        {
            OutputDirectory = outputDir,
            OverwriteExisting = false
        };

        // Act
        var result = await processor.ProcessFilesAsync(
            new[] { inputPath },
            new RedactionRuleSet(),
            options);

        // Assert
        result.FailedFiles.Should().Be(1);
        result.FileResults[0].Success.Should().BeFalse();
        result.FileResults[0].ErrorMessage.Should().Contain("already exists");
    }
}
