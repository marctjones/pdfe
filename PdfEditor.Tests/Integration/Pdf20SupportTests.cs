using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Avalonia;
using System.IO;
using System.Text;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests to verify PDF 2.0 (ISO 32000-2) support for display and redaction.
///
/// PDF 2.0 uses the same content stream operators as PDF 1.7, so redaction
/// should work without modification. These tests verify that assumption.
/// </summary>
public class Pdf20SupportTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;

    public Pdf20SupportTests(ITestOutputHelper output)
    {
        _output = output;

        // Initialize font resolver
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new CustomFontResolver();
        }

        var loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, loggerFactory);
    }

    #region PDF Version Detection Tests

    /// <summary>
    /// Test that we can detect PDF version from file header
    /// </summary>
    [Fact]
    public void CanDetectPdfVersion()
    {
        _output.WriteLine("=== TEST: CanDetectPdfVersion ===");

        // Create a standard PDF (will be 1.4)
        var pdfPath = CreateTempPath("version_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Test");
        _tempFiles.Add(pdfPath);

        // Read the PDF header to get version
        var version = GetPdfVersion(pdfPath);
        _output.WriteLine($"Detected PDF version: {version}");

        version.Should().NotBeNullOrEmpty();
        version.Should().StartWith("1."); // PdfSharpCore writes 1.x

        _output.WriteLine("✅ TEST PASSED: Can detect PDF version");
    }

    /// <summary>
    /// Test that we can open a manually created PDF 2.0 file
    /// </summary>
    [Fact]
    public void CanOpenPdf20Document()
    {
        _output.WriteLine("=== TEST: CanOpenPdf20Document ===");

        // Create a PDF 2.0 file by modifying the header
        var pdf20Path = CreateTempPath("pdf20_test.pdf");
        CreatePdf20TestFile(pdf20Path, "PDF 2.0 Test Content");
        _tempFiles.Add(pdf20Path);

        var version = GetPdfVersion(pdf20Path);
        _output.WriteLine($"Created PDF with version: {version}");
        version.Should().Be("2.0");

        // Try to open with PdfSharpCore
        _output.WriteLine("Attempting to open with PdfReader...");

        try
        {
            using var document = PdfReader.Open(pdf20Path, PdfDocumentOpenMode.Import);
            document.Should().NotBeNull();
            document.PageCount.Should().BeGreaterThan(0);
            _output.WriteLine($"Successfully opened PDF 2.0 document with {document.PageCount} pages");
            _output.WriteLine("✅ TEST PASSED: Can open PDF 2.0 document");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ Failed to open PDF 2.0: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region PDF 2.0 Text Extraction Tests

    /// <summary>
    /// Test that text can be extracted from PDF 2.0 using PdfPig
    /// </summary>
    [Fact]
    public void CanExtractTextFromPdf20()
    {
        _output.WriteLine("=== TEST: CanExtractTextFromPdf20 ===");

        var pdf20Path = CreateTempPath("pdf20_extract_test.pdf");
        var testText = "PDF20 Extraction Test";
        CreatePdf20TestFile(pdf20Path, testText);
        _tempFiles.Add(pdf20Path);

        _output.WriteLine($"Created PDF 2.0 with text: '{testText}'");

        // Extract text using PdfPig (which supports PDF 2.0)
        var extractedText = PdfTestHelpers.ExtractAllText(pdf20Path);
        _output.WriteLine($"Extracted text: '{extractedText.Trim()}'");

        extractedText.Should().Contain(testText,
            "PdfPig should be able to extract text from PDF 2.0");

        _output.WriteLine("✅ TEST PASSED: Can extract text from PDF 2.0");
    }

    #endregion

    #region PDF 2.0 Redaction Tests

    /// <summary>
    /// CRITICAL: Test that redaction works on PDF 2.0 documents
    /// </summary>
    [Fact]
    public void Redaction_WorksOnPdf20Document()
    {
        _output.WriteLine("=== TEST: Redaction_WorksOnPdf20Document ===");

        // Create PDF 2.0 with known content
        var pdf20Path = CreateTempPath("pdf20_redaction_test.pdf");
        var targetText = "REDACT_THIS_TEXT";
        CreatePdf20TestFile(pdf20Path, targetText);
        _tempFiles.Add(pdf20Path);

        _output.WriteLine($"Created PDF 2.0 with text: '{targetText}'");

        // Verify text exists before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(pdf20Path);
        _output.WriteLine($"Text before redaction: '{textBefore.Trim()}'");
        textBefore.Should().Contain(targetText);

        // Apply redaction
        _output.WriteLine("Applying redaction to PDF 2.0...");
        var document = PdfReader.Open(pdf20Path, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var redactionArea = new Rect(90, 90, 200, 30);
        _output.WriteLine($"Redaction area: {redactionArea}");

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPath = CreateTempPath("pdf20_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        _output.WriteLine($"Saved redacted PDF to: {redactedPath}");

        // Verify text is removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after redaction: '{textAfter.Trim()}'");

        textAfter.Should().NotContain(targetText,
            "Redacted text must be REMOVED from PDF 2.0 structure");

        // Verify output is valid
        PdfTestHelpers.IsValidPdf(redactedPath).Should().BeTrue(
            "Redacted PDF 2.0 must remain valid");

        _output.WriteLine("✅ TEST PASSED: Redaction works on PDF 2.0 documents");
    }

    /// <summary>
    /// Test that redacted PDF 2.0 preserves non-redacted content
    /// </summary>
    [Fact]
    public void Redaction_PreservesNonRedactedContentInPdf20()
    {
        _output.WriteLine("=== TEST: Redaction_PreservesNonRedactedContentInPdf20 ===");

        // Create PDF 2.0 with multiple text items
        var pdf20Path = CreateTempPath("pdf20_preserve_test.pdf");
        CreatePdf20WithMultipleText(pdf20Path);
        _tempFiles.Add(pdf20Path);

        var textBefore = PdfTestHelpers.ExtractAllText(pdf20Path);
        _output.WriteLine($"Text before: '{textBefore.Trim()}'");

        // Redact only the first text item
        var document = PdfReader.Open(pdf20Path, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        _redactionService.RedactArea(page, new Rect(90, 90, 150, 30), renderDpi: 72);

        var redactedPath = CreateTempPath("pdf20_preserve_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after: '{textAfter.Trim()}'");

        // First item should be removed
        textAfter.Should().NotContain("REMOVE_ME",
            "Targeted text should be removed");

        // Other items should be preserved
        textAfter.Should().Contain("KEEP_ME",
            "Non-targeted text should be preserved");

        _output.WriteLine("✅ TEST PASSED: Non-redacted content preserved in PDF 2.0");
    }

    #endregion

    #region PDF Version Preservation Tests

    /// <summary>
    /// Test what version the output PDF has after saving
    /// </summary>
    [Fact]
    public void SavedPdf_ChecksOutputVersion()
    {
        _output.WriteLine("=== TEST: SavedPdf_ChecksOutputVersion ===");

        // Create PDF 2.0
        var pdf20Path = CreateTempPath("version_preserve_test.pdf");
        CreatePdf20TestFile(pdf20Path, "Version Test");
        _tempFiles.Add(pdf20Path);

        var inputVersion = GetPdfVersion(pdf20Path);
        _output.WriteLine($"Input PDF version: {inputVersion}");

        // Open and save
        var document = PdfReader.Open(pdf20Path, PdfDocumentOpenMode.Modify);

        var savedPath = CreateTempPath("version_preserve_output.pdf");
        _tempFiles.Add(savedPath);
        document.Save(savedPath);
        document.Dispose();

        var outputVersion = GetPdfVersion(savedPath);
        _output.WriteLine($"Output PDF version: {outputVersion}");

        // Document current behavior
        if (outputVersion != inputVersion)
        {
            _output.WriteLine($"⚠️ NOTE: PDF version changed from {inputVersion} to {outputVersion}");
            _output.WriteLine("   PdfSharpCore writes PDF 1.4 by default");
            _output.WriteLine("   This is expected behavior - document is still valid");
        }
        else
        {
            _output.WriteLine($"✅ PDF version preserved: {outputVersion}");
        }

        // The important thing is that the file is valid
        PdfTestHelpers.IsValidPdf(savedPath).Should().BeTrue();

        _output.WriteLine("✅ TEST PASSED: Saved PDF is valid (version may differ)");
    }

    #endregion

    #region Content Stream Compatibility Tests

    /// <summary>
    /// Test that content stream parsing works on PDF 2.0
    /// </summary>
    [Fact]
    public void ContentStreamParsing_WorksOnPdf20()
    {
        _output.WriteLine("=== TEST: ContentStreamParsing_WorksOnPdf20 ===");

        var pdf20Path = CreateTempPath("pdf20_content_stream_test.pdf");
        CreatePdf20TestFile(pdf20Path, "Content Stream Test");
        _tempFiles.Add(pdf20Path);

        // Open and check we can access content stream
        var document = PdfReader.Open(pdf20Path, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Get content stream
        var contentStream = PdfTestHelpers.GetPageContentStream(pdf20Path);
        _output.WriteLine($"Content stream size: {contentStream.Length} bytes");

        contentStream.Length.Should().BeGreaterThan(0,
            "PDF 2.0 should have parseable content stream");

        // Check it contains expected PDF operators
        var contentString = Encoding.UTF8.GetString(contentStream);
        _output.WriteLine($"Content preview: {contentString.Substring(0, Math.Min(200, contentString.Length))}...");

        // Should contain text operators
        var hasTextOperators = contentString.Contains("BT") ||
                               contentString.Contains("Tj") ||
                               contentString.Contains("TJ");

        hasTextOperators.Should().BeTrue(
            "Content stream should contain text operators");

        document.Dispose();

        _output.WriteLine("✅ TEST PASSED: Content stream parsing works on PDF 2.0");
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Pdf20Tests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    /// <summary>
    /// Get PDF version from file header
    /// </summary>
    private string GetPdfVersion(string pdfPath)
    {
        using var reader = new StreamReader(pdfPath);
        var firstLine = reader.ReadLine();

        // PDF header format: %PDF-X.Y
        if (firstLine != null && firstLine.StartsWith("%PDF-"))
        {
            return firstLine.Substring(5);
        }

        return "unknown";
    }

    /// <summary>
    /// Create a PDF file with version 2.0 header
    /// </summary>
    private void CreatePdf20TestFile(string outputPath, string text)
    {
        // First create a normal PDF
        var tempPath = outputPath + ".temp";
        TestPdfGenerator.CreateSimpleTextPdf(tempPath, text);

        // Read the file as binary and modify the header to 2.0
        var content = File.ReadAllBytes(tempPath);

        // Replace version in header (first few bytes)
        // PDF header is typically %PDF-1.4 or %PDF-1.6
        var header = Encoding.ASCII.GetString(content, 0, Math.Min(20, content.Length));
        if (header.Contains("%PDF-1."))
        {
            content[5] = (byte)'2'; // Change 1.X to 2.X
            content[7] = (byte)'0'; // Change to 2.0
        }

        File.WriteAllBytes(outputPath, content);
        File.Delete(tempPath);
    }

    /// <summary>
    /// Create a PDF 2.0 with multiple text items for testing selective redaction
    /// </summary>
    private void CreatePdf20WithMultipleText(string outputPath)
    {
        // Create temp PDF with multiple text items
        var tempPath = outputPath + ".temp";

        var document = new PdfDocument();
        var page = document.AddPage();

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        gfx.DrawString("REMOVE_ME", font, XBrushes.Black, new XPoint(100, 100));
        gfx.DrawString("KEEP_ME", font, XBrushes.Black, new XPoint(100, 200));

        document.Save(tempPath);
        document.Dispose();

        // Convert to PDF 2.0 using binary operations
        var content = File.ReadAllBytes(tempPath);
        var header = Encoding.ASCII.GetString(content, 0, Math.Min(20, content.Length));
        if (header.Contains("%PDF-1."))
        {
            content[5] = (byte)'2'; // Change 1.X to 2.X
            content[7] = (byte)'0'; // Change to 2.0
        }
        File.WriteAllBytes(outputPath, content);
        File.Delete(tempPath);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch { }
        }
    }

    #endregion
}
