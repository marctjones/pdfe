using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Avalonia;
using System.IO;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Integration tests for the complete redaction workflow
/// Tests the entire pipeline: Parse → Filter → Rebuild → Replace
/// </summary>
public class RedactionIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;

    public RedactionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, loggerFactory);
    }

    [Fact]
    public void RedactSimpleText_ShouldRemoveTextFromPdf()
    {
        // Arrange
        _output.WriteLine("Test: RedactSimpleText_ShouldRemoveTextFromPdf");
        _output.WriteLine("Creating test PDF with 'CONFIDENTIAL' text...");
        
        var testPdf = CreateTempPath("simple_text_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "CONFIDENTIAL");
        _tempFiles.Add(testPdf);

        _output.WriteLine($"Test PDF created: {testPdf}");
        
        // Verify text exists before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before redaction: {textBefore}");
        textBefore.Should().Contain("CONFIDENTIAL", "text should exist before redaction");

        // Act
        _output.WriteLine("Opening PDF for redaction...");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact area where text is located
        // Text is at (100, 100), create redaction area around it
        var redactionArea = new Rect(90, 90, 150, 30);
        _output.WriteLine($"Redacting area: X={redactionArea.X}, Y={redactionArea.Y}, W={redactionArea.Width}, H={redactionArea.Height}");
        
        _redactionService.RedactArea(page, redactionArea);
        
        var redactedPdf = CreateTempPath("simple_text_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();
        
        _output.WriteLine($"Redacted PDF saved: {redactedPdf}");

        // Assert
        _output.WriteLine("Extracting text from redacted PDF...");
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after redaction: '{textAfter}'");
        
        // The text should be removed from the PDF structure
        textAfter.Should().NotContain("CONFIDENTIAL", 
            "redacted text should be permanently removed from PDF");
        
        // PDF should still be valid
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "redacted PDF should still be valid");
        
        _output.WriteLine("✓ Test passed: Text successfully redacted");
    }

    [Fact]
    public void RedactMultipleTextBlocks_ShouldOnlyRemoveTargetedText()
    {
        // Arrange
        _output.WriteLine("Test: RedactMultipleTextBlocks_ShouldOnlyRemoveTargetedText");
        
        var testPdf = CreateTempPath("multi_text_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before: {textBefore}");
        
        textBefore.Should().Contain("CONFIDENTIAL");
        textBefore.Should().Contain("Public Information");
        textBefore.Should().Contain("Secret Data");

        // Act - Redact only "CONFIDENTIAL" text
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact first text block only (CONFIDENTIAL at y=100)
        var redactionArea = new Rect(90, 90, 150, 30);
        _output.WriteLine($"Redacting area around 'CONFIDENTIAL': {redactionArea}");
        
        _redactionService.RedactArea(page, redactionArea);
        
        var redactedPdf = CreateTempPath("multi_text_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after: {textAfter}");
        
        textAfter.Should().NotContain("CONFIDENTIAL", 
            "targeted text should be removed");
        textAfter.Should().Contain("Public Information", 
            "non-targeted text should remain");
        textAfter.Should().Contain("Secret Data", 
            "non-targeted text should remain");
        
        _output.WriteLine("✓ Test passed: Only targeted text was redacted");
    }

    [Fact]
    public void RedactArea_WithNoContent_ShouldNotCorruptPdf()
    {
        // Arrange
        _output.WriteLine("Test: RedactArea_WithNoContent_ShouldNotCorruptPdf");
        
        var testPdf = CreateTempPath("empty_area_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "Some Text");
        _tempFiles.Add(testPdf);

        // Act - Redact area with no content
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact area far from any content
        var redactionArea = new Rect(400, 400, 100, 50);
        _output.WriteLine($"Redacting empty area: {redactionArea}");
        
        _redactionService.RedactArea(page, redactionArea);
        
        var redactedPdf = CreateTempPath("empty_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF should remain valid even when redacting empty area");
        
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().Contain("Some Text", 
            "original text should remain when redacting empty area");
        
        _output.WriteLine("✓ Test passed: PDF remains valid when redacting empty area");
    }

    [Fact]
    public void RedactMultipleAreas_ShouldRemoveAllTargetedContent()
    {
        // Arrange
        _output.WriteLine("Test: RedactMultipleAreas_ShouldRemoveAllTargetedContent");
        
        var testPdf = CreateTempPath("multiple_areas_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Redact multiple areas
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact "CONFIDENTIAL" and "Secret Data"
        var area1 = new Rect(90, 90, 150, 30);   // CONFIDENTIAL at y=100
        var area2 = new Rect(90, 290, 150, 30);  // Secret Data at y=300
        
        _output.WriteLine($"Redacting area 1: {area1}");
        _redactionService.RedactArea(page, area1);
        
        _output.WriteLine($"Redacting area 2: {area2}");
        _redactionService.RedactArea(page, area2);
        
        var redactedPdf = CreateTempPath("multiple_areas_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after multiple redactions: {textAfter}");
        
        textAfter.Should().NotContain("CONFIDENTIAL");
        textAfter.Should().NotContain("Secret Data");
        textAfter.Should().Contain("Public Information", 
            "unredacted text should remain");
        
        _output.WriteLine("✓ Test passed: Multiple areas successfully redacted");
    }

    [Fact]
    public void RedactPage_ShouldMaintainPdfStructure()
    {
        // Arrange
        _output.WriteLine("Test: RedactPage_ShouldMaintainPdfStructure");
        
        var testPdf = CreateTempPath("structure_test.pdf");
        TestPdfGenerator.CreateMultiPagePdf(testPdf, pageCount: 3);
        _tempFiles.Add(testPdf);

        var pageCountBefore = PdfTestHelpers.GetPageCount(testPdf);
        _output.WriteLine($"Pages before: {pageCountBefore}");

        // Act - Redact content on page 2
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[1]; // Second page

        var redactionArea = new Rect(90, 190, 200, 30);
        _output.WriteLine("Redacting content on page 2...");
        _redactionService.RedactArea(page, redactionArea);
        
        var redactedPdf = CreateTempPath("structure_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var pageCountAfter = PdfTestHelpers.GetPageCount(redactedPdf);
        _output.WriteLine($"Pages after: {pageCountAfter}");
        
        pageCountAfter.Should().Be(pageCountBefore, 
            "page count should remain unchanged");
        
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF structure should remain valid");
        
        // Check that content on other pages is intact
        var textPage1 = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 0);
        var textPage3 = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 2);
        
        textPage1.Should().Contain("Page 1 Content", 
            "content on page 1 should be intact");
        textPage3.Should().Contain("Page 3 Content", 
            "content on page 3 should be intact");
        
        _output.WriteLine("✓ Test passed: PDF structure maintained after redaction");
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PdfEditorTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    public void Dispose()
    {
        // Cleanup temp files
        foreach (var file in _tempFiles)
        {
            TestPdfGenerator.CleanupTestFile(file);
        }
    }
}
