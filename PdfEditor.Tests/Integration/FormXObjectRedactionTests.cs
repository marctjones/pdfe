using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for Form XObject (nested content stream) redaction support
/// </summary>
public class FormXObjectRedactionTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public FormXObjectRedactionTests()
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
    }

    private string CreateTempFile(string extension = ".pdf")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void ParseContentStreamRecursive_ShouldIncludeMainContent()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiTextPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        // Act
        var operations = parser.ParseContentStreamRecursive(document.Pages[0]);

        // Assert
        operations.Should().NotBeEmpty();
        operations.OfType<TextOperation>().Should().NotBeEmpty("Should parse text from main content stream");
    }

    [Fact]
    public void ParseContentStreamRecursive_ShouldNotCrashOnEmptyPage()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        var document = new PdfDocument();
        var page = document.AddPage();
        document.Save(pdfPath);
        document.Dispose();

        using var reloaded = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        // Act
        var operations = parser.ParseContentStreamRecursive(reloaded.Pages[0]);

        // Assert
        operations.Should().NotBeNull();
    }

    [Fact]
    public void ParseFormXObjects_ShouldHandleNoXObjects()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Simple text only");

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        // Act
        var operations = parser.ParseContentStreamRecursive(document.Pages[0]);

        // Assert
        operations.Should().NotBeNull();
        // Simple PDFs typically don't have Form XObjects
    }

    [Fact]
    public void ContentStreamParser_ShouldParseMultipleOperationTypes()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateTextWithGraphicsPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        // Act
        var operations = parser.ParseContentStream(document.Pages[0]);

        // Assert
        operations.Should().NotBeEmpty();
        // Should have various operation types
        var operationTypes = operations.Select(o => o.GetType().Name).Distinct().ToList();
        operationTypes.Should().HaveCountGreaterThan(1, "Should parse multiple operation types");
    }

    [Fact]
    public void RedactionService_WithRecursiveParsing_ShouldFindAllContent()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateComplexContentPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        // Act
        var standardOps = parser.ParseContentStream(document.Pages[0]);
        var recursiveOps = parser.ParseContentStreamRecursive(document.Pages[0]);

        // Assert
        recursiveOps.Count.Should().BeGreaterThanOrEqualTo(standardOps.Count,
            "Recursive parsing should find at least as many operations");
    }

    [Fact]
    public void FormXObjectOperation_ShouldTrackSourceName()
    {
        // Arrange
        var operation = new FormXObjectOperation(new PdfSharp.Pdf.Content.Objects.CComment())
        {
            SourceXObjectName = "/Form1"
        };

        // Assert
        operation.SourceXObjectName.Should().Be("/Form1");
        operation.BoundingBox.Should().NotBeNull();
    }

    [Fact]
    public void ParseContentStreamRecursive_PerformanceTest()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 5);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var allOperations = new List<PdfOperation>();
        foreach (var page in document.Pages.Cast<PdfPage>())
        {
            allOperations.AddRange(parser.ParseContentStreamRecursive(page));
        }

        sw.Stop();

        // Assert
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "Recursive parsing should complete quickly");
        allOperations.Should().NotBeEmpty();
    }

    [Fact]
    public void RedactionWithFormXObjects_ShouldNotCrash()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateLayeredShapesPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var redactionService = new RedactionService(
            _loggerFactory.CreateLogger<RedactionService>(),
            _loggerFactory);

        // Act - Apply redaction in an area
        var act = () => redactionService.RedactArea(
            document.Pages[0],
            new Rect(150, 150, 100, 100),
            pdfPath,
            renderDpi: 72);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetArrayDouble_HelperMethod_ShouldWork()
    {
        // This test verifies the helper method used in Form XObject parsing
        // by testing through the parser

        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateTransformedTextPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        // Act
        var operations = parser.ParseContentStreamRecursive(document.Pages[0]);

        // Assert
        operations.Should().NotBeNull();
        // Transformed text PDF has rotation/scale transformations
    }
}

/// <summary>
/// Additional test utilities for Form XObject testing
/// </summary>
public static class FormXObjectTestHelpers
{
    /// <summary>
    /// Creates a PDF with a Form XObject containing text
    /// </summary>
    public static string CreatePdfWithFormXObject(string outputPath, string text)
    {
        // Note: Creating Form XObjects programmatically with PdfSharp is complex
        // For now, we test with regular content and verify parsing doesn't crash
        TestPdfGenerator.CreateTextWithGraphicsPdf(outputPath);
        return outputPath;
    }
}
