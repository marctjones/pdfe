using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;
using Avalonia;
using System;
using System.IO;
using System.Linq;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Text Extraction After Redaction Tests (Issue #42)
///
/// Verifies that text extraction returns EMPTY results from redacted areas,
/// confirming TRUE content-level redaction.
///
/// Critical: These tests ensure that after redaction is applied and saved,
/// no text can be extracted from the redacted areas using PdfPig text extraction.
/// </summary>
public class TextExtractionAfterRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public TextExtractionAfterRedactionTests(ITestOutputHelper output)
    {
        _output = output;

        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new CustomFontResolver();
        }

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var redactionLogger = _loggerFactory.CreateLogger<RedactionService>();
        _redactionService = new RedactionService(redactionLogger, _loggerFactory);

        _output.WriteLine("=== Text Extraction After Redaction Test Suite ===");
        _output.WriteLine($"Test started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");
    }

    #region Core Text Extraction Tests

    /// <summary>
    /// CRITICAL: Verify text is NOT extractable from redacted area after applying redaction
    /// Uses PdfPig to get precise text bounds (same approach as GUI tests)
    /// </summary>
    [Fact]
    public void TextExtraction_AfterRedaction_TextNotExtractableFromArea()
    {
        _output.WriteLine("=== TEST: TextExtraction_AfterRedaction_TextNotExtractableFromArea ===");

        // Arrange
        var testPdf = CreateTempPath("extraction_test.pdf");
        var targetText = "CONFIDENTIAL";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, targetText);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(targetText);

        // Get precise text bounds from PdfPig
        Rect redactionArea = GetWordBounds(testPdf, targetText);

        // Apply redaction
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], redactionArea, renderDpi: 150);

        var redactedPath = CreateTempPath("extraction_test_redacted.pdf");
        document.Save(redactedPath);
        document.Dispose();
        _tempFiles.Add(redactedPath);

        // Act & Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        textAfter.Should().NotContain(targetText,
            "redacted text must NOT be extractable from PDF structure");

        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify redaction persists after save and reload
    /// </summary>
    [Fact]
    public void TextExtraction_AfterSaveAndReload_TextStillRemoved()
    {
        _output.WriteLine("=== TEST: TextExtraction_AfterSaveAndReload_TextStillRemoved ===");

        // Arrange
        var testPdf = CreateTempPath("save_reload_test.pdf");
        var targetText = "PERSISTENT";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, targetText);
        _tempFiles.Add(testPdf);

        // Redact and save
        Rect redactionArea = GetWordBounds(testPdf, targetText);
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], redactionArea, renderDpi: 150);

        var redactedPath = CreateTempPath("save_reload_test_redacted.pdf");
        document.Save(redactedPath);
        document.Dispose();
        _tempFiles.Add(redactedPath);

        // Simulate reload
        var textAfterReload = PdfTestHelpers.ExtractAllText(redactedPath);
        textAfterReload.Should().NotContain(targetText,
            "redacted text must stay removed even after save and reload");

        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify completely redacted page returns empty text
    /// </summary>
    [Fact]
    public void TextExtraction_CompletelyRedactedPage_ReturnsEmpty()
    {
        _output.WriteLine("=== TEST: TextExtraction_CompletelyRedactedPage_ReturnsEmpty ===");

        // Arrange
        var testPdf = CreateTempPath("complete_redaction_test.pdf");
        var targetText = "REDACT_ALL";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, targetText);
        _tempFiles.Add(testPdf);

        // Redact entire page content area
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        _redactionService.RedactArea(document.Pages[0], new Rect(0, 0, 1000, 1000), renderDpi: 150);

        var redactedPath = CreateTempPath("complete_redaction_test_redacted.pdf");
        document.Save(redactedPath);
        document.Dispose();
        _tempFiles.Add(redactedPath);

        // Act & Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        textAfter.Trim().Should().BeEmpty("completely redacted page should have no extractable text");

        _output.WriteLine("✓ PASS");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the bounds of a word in image pixels (150 DPI, top-left origin)
    /// </summary>
    private Rect GetWordBounds(string pdfPath, string searchText)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var words = page.GetWords().ToList();

        var targetWord = words.FirstOrDefault(w => w.Text.Contains(searchText));
        if (targetWord == null)
        {
            throw new Exception($"Word '{searchText}' not found in PDF");
        }

        // Convert PdfPig coordinates (bottom-left, 72 DPI) to image pixels (top-left, 150 DPI)
        var pdfBounds = targetWord.BoundingBox;
        var scale = 150.0 / 72.0;
        var imageY = (page.Height - pdfBounds.Top) * scale;

        return new Rect(
            (pdfBounds.Left - 5) * scale,  // Small margin
            imageY - 5,
            (pdfBounds.Right - pdfBounds.Left + 10) * scale,
            (pdfBounds.Top - pdfBounds.Bottom + 10) * scale
        );
    }

    private string CreateTempPath(string filename)
    {
        return Path.Combine(Path.GetTempPath(), $"pdfe_test_{Guid.NewGuid()}_{filename}");
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();

        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #endregion
}
