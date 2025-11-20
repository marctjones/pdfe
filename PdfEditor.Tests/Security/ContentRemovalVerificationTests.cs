using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Avalonia;
using System.IO;
using System.Text;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;

namespace PdfEditor.Tests.Security;

/// <summary>
/// Security verification tests for redaction.
/// These tests verify that content is TRULY REMOVED from PDF structure,
/// not just visually hidden. This is critical for security.
/// </summary>
public class ContentRemovalVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;

    public ContentRemovalVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, loggerFactory);
    }

    #region Core Content Removal Tests

    /// <summary>
    /// CRITICAL TEST: Text extraction using PdfPig must fail for redacted content.
    /// This verifies TRUE glyph-level removal.
    /// </summary>
    [Fact]
    public void RedactedText_ShouldNotBeExtractableWithPdfPig()
    {
        // Arrange
        _output.WriteLine("=== TEST: RedactedText_ShouldNotBeExtractableWithPdfPig ===");

        var testPdf = CreateTempPath("pdfpig_extraction_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "SUPER_SECRET_DATA");
        _tempFiles.Add(testPdf);

        // Verify text exists before
        var textBefore = ExtractWithPdfPig(testPdf);
        _output.WriteLine($"Text before redaction: '{textBefore}'");
        textBefore.Should().Contain("SUPER_SECRET_DATA");

        // Act - Apply redaction
        // Text at Y=100 - use redaction at Y=90 with height 30 to cover it
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var redactionArea = new Rect(90, 90, 250, 30);
        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("pdfpig_extraction_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Extract with PdfPig (independent library)
        var textAfter = ExtractWithPdfPig(redactedPdf);
        _output.WriteLine($"Text after redaction: '{textAfter}'");

        textAfter.Should().NotContain("SUPER_SECRET_DATA",
            "CRITICAL: Text must be REMOVED from PDF structure, not just visually hidden. " +
            "PdfPig extraction should not find the redacted text.");

        _output.WriteLine("=== PASSED: Text not extractable with PdfPig ===");
    }

    /// <summary>
    /// Verify content stream is actually modified (not just black box added)
    /// </summary>
    [Fact]
    public void RedactedPdf_ContentStreamShouldBeModified()
    {
        // Arrange
        _output.WriteLine("=== TEST: RedactedPdf_ContentStreamShouldBeModified ===");

        var testPdf = CreateTempPath("content_stream_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "MODIFY_TEST_DATA");
        _tempFiles.Add(testPdf);

        // Get content stream before
        var contentBefore = PdfTestHelpers.GetPageContentStream(testPdf);
        _output.WriteLine($"Content stream size before: {contentBefore.Length} bytes");

        // Act - Text at Y=100, body from ~88-100
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("content_stream_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var contentAfter = PdfTestHelpers.GetPageContentStream(redactedPdf);
        _output.WriteLine($"Content stream size after: {contentAfter.Length} bytes");

        // Content should be different (modified)
        contentBefore.Should().NotEqual(contentAfter,
            "Content stream must be modified for true glyph removal");

        // The redacted text should not appear in raw content
        var rawContent = Encoding.UTF8.GetString(contentAfter);
        rawContent.Should().NotContain("MODIFY_TEST_DATA",
            "Redacted text should not appear in raw content stream");

        _output.WriteLine("=== PASSED: Content stream modified ===");
    }

    /// <summary>
    /// Binary search of PDF file should not find redacted text
    /// </summary>
    [Fact]
    public void RedactedPdf_BinarySearchShouldNotFindText()
    {
        // Arrange
        _output.WriteLine("=== TEST: RedactedPdf_BinarySearchShouldNotFindText ===");

        var testPdf = CreateTempPath("binary_search_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "BINARY_SEARCH_TARGET");
        _tempFiles.Add(testPdf);

        // Act - Text at Y=100, body from ~88-100
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("binary_search_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Read entire PDF as bytes
        var pdfBytes = File.ReadAllBytes(redactedPdf);
        var pdfContent = Encoding.UTF8.GetString(pdfBytes);

        // Search for the text in multiple encodings
        pdfContent.Should().NotContain("BINARY_SEARCH_TARGET",
            "Redacted text should not appear anywhere in PDF binary");

        // Also check for common PDF string encodings
        var asciiSearch = Encoding.ASCII.GetString(pdfBytes);
        asciiSearch.Should().NotContain("BINARY_SEARCH_TARGET",
            "Redacted text should not appear in ASCII search");

        _output.WriteLine("=== PASSED: Binary search did not find text ===");
    }

    #endregion

    #region Selective Removal Tests

    /// <summary>
    /// Verify only targeted content is removed - no collateral damage
    /// </summary>
    [Fact]
    public void RedactArea_ShouldOnlyRemoveTargetedContent()
    {
        // Arrange
        _output.WriteLine("=== TEST: RedactArea_ShouldOnlyRemoveTargetedContent ===");

        var testPdf = CreateTempPath("selective_removal_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Verify all text exists before
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Text before: {textBefore.Replace("\n", " | ")}");
        textBefore.Should().Contain("CONFIDENTIAL");
        textBefore.Should().Contain("Public Information");
        textBefore.Should().Contain("Secret Data");
        textBefore.Should().Contain("Normal Text");

        // Act - Redact only CONFIDENTIAL (at y=100, body ~88-100)
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("selective_removal_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text after: {textAfter.Replace("\n", " | ")}");

        textAfter.Should().NotContain("CONFIDENTIAL", "Targeted text should be removed");
        textAfter.Should().Contain("Public Information", "Non-targeted text must be preserved");
        textAfter.Should().Contain("Secret Data", "Non-targeted text must be preserved");
        textAfter.Should().Contain("Normal Text", "Non-targeted text must be preserved");

        _output.WriteLine("=== PASSED: Only targeted content removed ===");
    }

    /// <summary>
    /// Verify precise boundary - content just outside redaction area is preserved
    /// </summary>
    [Fact]
    public void RedactArea_ShouldRespectBoundaries()
    {
        // Arrange
        _output.WriteLine("=== TEST: RedactArea_ShouldRespectBoundaries ===");

        var testPdf = CreateTempPath("boundary_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Redact a very specific small area
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // This small area should only catch CONFIDENTIAL at y=100 (body ~88-100)
        _redactionService.RedactArea(page, new Rect(95, 90, 150, 25), renderDpi: 72);

        var redactedPdf = CreateTempPath("boundary_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - All other text should be preserved
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().Contain("Public Information",
            "Text at y=200 should not be affected");
        textAfter.Should().Contain("Secret Data",
            "Text at y=300 should not be affected");

        _output.WriteLine("=== PASSED: Boundaries respected ===");
    }

    #endregion

    #region Multiple Verification Methods

    /// <summary>
    /// Use multiple extraction methods to verify removal
    /// </summary>
    [Fact]
    public void RedactedText_ShouldFailAllExtractionMethods()
    {
        // Arrange
        _output.WriteLine("=== TEST: RedactedText_ShouldFailAllExtractionMethods ===");

        var testPdf = CreateTempPath("multi_method_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "MULTI_METHOD_SECRET");
        _tempFiles.Add(testPdf);

        // Act - Text at Y=100, body from ~88-100
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("multi_method_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert with multiple methods
        _output.WriteLine("Testing extraction methods:");

        // Method 1: PdfPig
        var pdfPigText = ExtractWithPdfPig(redactedPdf);
        _output.WriteLine($"  PdfPig: '{pdfPigText}'");
        pdfPigText.Should().NotContain("MULTI_METHOD_SECRET", "PdfPig should not find text");

        // Method 2: PdfTestHelpers (also uses PdfPig)
        var helperText = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"  PdfTestHelpers: '{helperText}'");
        helperText.Should().NotContain("MULTI_METHOD_SECRET", "Helper should not find text");

        // Method 3: Raw content stream
        var contentStream = PdfTestHelpers.GetPageContentStream(redactedPdf);
        var rawText = Encoding.UTF8.GetString(contentStream);
        rawText.Should().NotContain("MULTI_METHOD_SECRET", "Raw content should not contain text");

        // Method 4: Binary file search
        var fileBytes = File.ReadAllBytes(redactedPdf);
        var fileText = Encoding.UTF8.GetString(fileBytes);
        fileText.Should().NotContain("MULTI_METHOD_SECRET", "Binary search should not find text");

        _output.WriteLine("=== PASSED: All extraction methods failed to find text ===");
    }

    #endregion

    #region PDF Validity Tests

    /// <summary>
    /// Redacted PDF must remain valid and openable
    /// </summary>
    [Fact]
    public void RedactedPdf_ShouldRemainValid()
    {
        // Arrange
        var testPdf = CreateTempPath("validity_test.pdf");
        TestPdfGenerator.CreateComplexContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Multiple redactions
        _redactionService.RedactArea(page, new Rect(50, 260, 500, 100)); // Sensitive data area
        _redactionService.RedactArea(page, new Rect(50, 50, 300, 30));   // Title area

        var redactedPdf = CreateTempPath("validity_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - PDF should be valid
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "Redacted PDF must remain valid");

        // Should be openable by multiple libraries
        Action openWithPdfSharp = () =>
        {
            using var doc = PdfReader.Open(redactedPdf, PdfDocumentOpenMode.ReadOnly);
            doc.PageCount.Should().Be(1);
        };
        openWithPdfSharp.Should().NotThrow("PDF should be openable by PdfSharp");

        Action openWithPdfPig = () =>
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(redactedPdf);
            doc.NumberOfPages.Should().Be(1);
        };
        openWithPdfPig.Should().NotThrow("PDF should be openable by PdfPig");
    }

    /// <summary>
    /// Multiple sequential redactions should all work correctly
    /// </summary>
    [Fact]
    public void MultipleRedactions_ShouldAllRemoveContent()
    {
        // Arrange
        var testPdf = CreateTempPath("multiple_redactions_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        // Act - Apply multiple redactions
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact CONFIDENTIAL at y=100 (body ~88-100)
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), renderDpi: 72);
        // Redact Secret Data at y=300 (body ~288-300)
        _redactionService.RedactArea(page, new Rect(90, 290, 200, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("multiple_redactions_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Both should be removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("CONFIDENTIAL", "First redaction should work");
        textAfter.Should().NotContain("Secret Data", "Second redaction should work");
        textAfter.Should().Contain("Public Information", "Unredacted content preserved");
        textAfter.Should().Contain("Normal Text", "Unredacted content preserved");
    }

    #endregion

    #region Black Box Verification

    /// <summary>
    /// Verify black box is drawn over redacted area
    /// </summary>
    [Fact]
    public void RedactedArea_ShouldHaveBlackBox()
    {
        // Arrange
        var testPdf = CreateTempPath("black_box_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "BLACK_BOX_TEST");
        _tempFiles.Add(testPdf);

        // Act - Text at Y=100, body from ~88-100
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("black_box_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Check for black rectangle in content stream
        var contentStream = PdfTestHelpers.GetPageContentStream(redactedPdf);
        var content = Encoding.UTF8.GetString(contentStream);

        // Black color should be set (0 g for grayscale black)
        var hasBlackColor = content.Contains("0 g") || content.Contains("0 0 0 rg");
        hasBlackColor.Should().BeTrue("Black color should be set for redaction box");

        // Rectangle operation should exist
        content.Should().Contain(" re ", "Rectangle operation should exist for black box");

        // Fill operation should exist
        var hasFill = content.Contains(" f") || content.Contains(" F");
        hasFill.Should().BeTrue("Fill operation should exist for black box");
    }

    #endregion

    #region Helper Methods

    private string ExtractWithPdfPig(string pdfPath)
    {
        var text = new StringBuilder();
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }
        return text.ToString();
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SecurityTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            TestPdfGenerator.CleanupTestFile(file);
        }
    }

    #endregion
}
