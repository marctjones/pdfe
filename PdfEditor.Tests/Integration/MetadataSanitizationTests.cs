using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
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
/// Tests for metadata sanitization functionality.
/// Verifies that redacted content is removed from all PDF metadata locations.
/// </summary>
public class MetadataSanitizationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public MetadataSanitizationTests(ITestOutputHelper output)
    {
        _output = output;

        // Initialize font resolver
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }

        _loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, _loggerFactory);
    }

    #region Document Info Sanitization Tests

    [Fact]
    public void SanitizeDocumentMetadata_RemovesRedactedTermsFromTitle()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_RemovesRedactedTermsFromTitle ===");

        // Arrange
        var pdfPath = CreateTempPath("metadata_title_test.pdf");
        var document = new PdfDocument();
        document.Info.Title = "Confidential Report - Secret Project";
        document.Info.Author = "John Smith";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Confidential content here", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act - Sanitize with specific terms
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.SanitizeDocument(document, new[] { "Confidential", "Secret" });

        var sanitizedPath = CreateTempPath("metadata_title_sanitized.pdf");
        document.Save(sanitizedPath);
        _tempFiles.Add(sanitizedPath);
        document.Dispose();

        // Assert
        using var sanitizedDoc = PdfReader.Open(sanitizedPath, PdfDocumentOpenMode.ReadOnly);

        _output.WriteLine($"Original title: 'Confidential Report - Secret Project'");
        _output.WriteLine($"Sanitized title: '{sanitizedDoc.Info.Title}'");

        sanitizedDoc.Info.Title.Should().NotContain("Confidential");
        sanitizedDoc.Info.Title.Should().NotContain("Secret");
        sanitizedDoc.Info.Title.Should().Contain("█"); // Redaction markers
        sanitizedDoc.Info.Author.Should().Be("John Smith"); // Unchanged

        _output.WriteLine("✅ TEST PASSED: Title sanitized correctly");
    }

    [Fact]
    public void SanitizeDocumentMetadata_RemovesRedactedTermsFromAllInfoFields()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_RemovesRedactedTermsFromAllInfoFields ===");

        // Arrange
        var pdfPath = CreateTempPath("metadata_all_fields_test.pdf");
        var document = new PdfDocument();
        document.Info.Title = "Secret Document";
        document.Info.Author = "Secret Agent";
        document.Info.Subject = "Secret Mission";
        document.Info.Keywords = "Secret, Classified, Top Secret";
        document.Info.Creator = "Secret App";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Content", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.SanitizeDocument(document, new[] { "Secret" });

        var sanitizedPath = CreateTempPath("metadata_all_fields_sanitized.pdf");
        document.Save(sanitizedPath);
        _tempFiles.Add(sanitizedPath);
        document.Dispose();

        // Assert
        using var sanitizedDoc = PdfReader.Open(sanitizedPath, PdfDocumentOpenMode.ReadOnly);

        _output.WriteLine($"Title: '{sanitizedDoc.Info.Title}'");
        _output.WriteLine($"Author: '{sanitizedDoc.Info.Author}'");
        _output.WriteLine($"Subject: '{sanitizedDoc.Info.Subject}'");
        _output.WriteLine($"Keywords: '{sanitizedDoc.Info.Keywords}'");
        _output.WriteLine($"Creator: '{sanitizedDoc.Info.Creator}'");

        sanitizedDoc.Info.Title.Should().NotContain("Secret");
        sanitizedDoc.Info.Author.Should().NotContain("Secret");
        sanitizedDoc.Info.Subject.Should().NotContain("Secret");
        sanitizedDoc.Info.Keywords.Should().NotContain("Secret");
        sanitizedDoc.Info.Creator.Should().NotContain("Secret");

        _output.WriteLine("✅ TEST PASSED: All info fields sanitized");
    }

    [Fact]
    public void SanitizeDocumentMetadata_CaseInsensitive()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_CaseInsensitive ===");

        // Arrange
        var pdfPath = CreateTempPath("metadata_case_test.pdf");
        var document = new PdfDocument();
        document.Info.Title = "SECRET report with secret and SECRET data";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Content", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.SanitizeDocument(document, new[] { "secret" });

        var sanitizedPath = CreateTempPath("metadata_case_sanitized.pdf");
        document.Save(sanitizedPath);
        _tempFiles.Add(sanitizedPath);
        document.Dispose();

        // Assert
        using var sanitizedDoc = PdfReader.Open(sanitizedPath, PdfDocumentOpenMode.ReadOnly);

        _output.WriteLine($"Original: 'SECRET report with secret and SECRET data'");
        _output.WriteLine($"Sanitized: '{sanitizedDoc.Info.Title}'");

        sanitizedDoc.Info.Title.ToLower().Should().NotContain("secret");

        _output.WriteLine("✅ TEST PASSED: Case-insensitive sanitization works");
    }

    #endregion

    #region Remove All Metadata Tests

    [Fact]
    public void RemoveAllMetadata_ClearsAllDocumentInfo()
    {
        _output.WriteLine("=== TEST: RemoveAllMetadata_ClearsAllDocumentInfo ===");

        // Arrange
        var pdfPath = CreateTempPath("metadata_remove_all_test.pdf");
        var document = new PdfDocument();
        document.Info.Title = "Important Document";
        document.Info.Author = "Author Name";
        document.Info.Subject = "Subject Text";
        document.Info.Keywords = "keyword1, keyword2";
        document.Info.Creator = "Creator App";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Content", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.RemoveAllMetadata(document);

        var sanitizedPath = CreateTempPath("metadata_remove_all_result.pdf");
        document.Save(sanitizedPath);
        _tempFiles.Add(sanitizedPath);
        document.Dispose();

        // Assert
        using var sanitizedDoc = PdfReader.Open(sanitizedPath, PdfDocumentOpenMode.ReadOnly);

        _output.WriteLine($"Title: '{sanitizedDoc.Info.Title}'");
        _output.WriteLine($"Author: '{sanitizedDoc.Info.Author}'");
        _output.WriteLine($"Subject: '{sanitizedDoc.Info.Subject}'");
        _output.WriteLine($"Keywords: '{sanitizedDoc.Info.Keywords}'");

        sanitizedDoc.Info.Title.Should().BeEmpty();
        sanitizedDoc.Info.Author.Should().BeEmpty();
        sanitizedDoc.Info.Subject.Should().BeEmpty();
        sanitizedDoc.Info.Keywords.Should().BeEmpty();
        sanitizedDoc.Info.Producer.Should().Be("pdfe");

        _output.WriteLine("✅ TEST PASSED: All metadata removed");
    }

    #endregion

    #region Integrated Redaction with Metadata Sanitization

    [Fact]
    public void RedactWithOptions_SanitizesMetadataAfterRedaction()
    {
        _output.WriteLine("=== TEST: RedactWithOptions_SanitizesMetadataAfterRedaction ===");

        // Arrange
        var pdfPath = CreateTempPath("redact_with_sanitize_test.pdf");
        var document = new PdfDocument();
        document.Info.Title = "Report about CONFIDENTIAL data";
        document.Info.Subject = "Contains CONFIDENTIAL information";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("CONFIDENTIAL", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act - Redact the text and sanitize metadata
        var redactArea = new Rect(90, 90, 150, 30);
        var options = new RedactionOptions { SanitizeMetadata = true };

        _redactionService.RedactWithOptions(document, page, new[] { redactArea }, options, renderDpi: 72);

        var redactedPath = CreateTempPath("redact_with_sanitize_result.pdf");
        document.Save(redactedPath);
        _tempFiles.Add(redactedPath);
        document.Dispose();

        // Assert
        // Check text is removed from content
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after redaction: '{textAfter.Trim()}'");
        textAfter.Should().NotContain("CONFIDENTIAL");

        // Check metadata is sanitized
        using var redactedDoc = PdfReader.Open(redactedPath, PdfDocumentOpenMode.ReadOnly);
        _output.WriteLine($"Title after: '{redactedDoc.Info.Title}'");
        _output.WriteLine($"Subject after: '{redactedDoc.Info.Subject}'");

        redactedDoc.Info.Title.Should().NotContain("CONFIDENTIAL");
        redactedDoc.Info.Subject.Should().NotContain("CONFIDENTIAL");

        _output.WriteLine("✅ TEST PASSED: Redaction with metadata sanitization works");
    }

    [Fact]
    public void RedactWithOptions_RemovesAllMetadataWhenRequested()
    {
        _output.WriteLine("=== TEST: RedactWithOptions_RemovesAllMetadataWhenRequested ===");

        // Arrange
        var pdfPath = CreateTempPath("redact_remove_all_metadata_test.pdf");
        var document = new PdfDocument();
        document.Info.Title = "Sensitive Document";
        document.Info.Author = "Sensitive Author";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Some text", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act - Redact with RemoveAllMetadata option
        var redactArea = new Rect(90, 90, 100, 30);
        var options = new RedactionOptions { RemoveAllMetadata = true };

        _redactionService.RedactWithOptions(document, page, new[] { redactArea }, options, renderDpi: 72);

        var redactedPath = CreateTempPath("redact_remove_all_metadata_result.pdf");
        document.Save(redactedPath);
        _tempFiles.Add(redactedPath);
        document.Dispose();

        // Assert
        using var redactedDoc = PdfReader.Open(redactedPath, PdfDocumentOpenMode.ReadOnly);

        redactedDoc.Info.Title.Should().BeEmpty();
        redactedDoc.Info.Author.Should().BeEmpty();

        _output.WriteLine("✅ TEST PASSED: All metadata removed with redaction");
    }

    [Fact]
    public void RedactedTerms_TrackedCorrectly()
    {
        _output.WriteLine("=== TEST: RedactedTerms_TrackedCorrectly ===");

        // Arrange
        var pdfPath = CreateTempPath("tracked_terms_test.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("SECRET_TEXT", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString("OTHER_TEXT", font, XBrushes.Black, new XPoint(100, 200));
        }

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act - Clear and redact
        _redactionService.ClearRedactedTerms();
        _redactionService.RedactArea(page, new Rect(90, 90, 150, 30), renderDpi: 72);

        // Assert
        _output.WriteLine($"Redacted terms count: {_redactionService.RedactedTerms.Count}");
        foreach (var term in _redactionService.RedactedTerms)
        {
            _output.WriteLine($"  - '{term}'");
        }

        _redactionService.RedactedTerms.Should().NotBeEmpty();

        document.Dispose();
        _output.WriteLine("✅ TEST PASSED: Redacted terms tracked correctly");
    }

    #endregion

    #region Outline/Bookmark Sanitization Tests

    [Fact]
    public void SanitizeDocumentMetadata_SanitizesOutlines()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_SanitizesOutlines ===");

        // Arrange
        var pdfPath = CreateTempPath("metadata_outline_test.pdf");
        var document = new PdfDocument();

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Content", font, XBrushes.Black, new XPoint(100, 100));
        }

        // Add outline with sensitive text
        var outline = document.Outlines.Add("Secret Chapter", page, true);
        outline.Outlines.Add("Secret Section", page, true);

        document.Save(pdfPath);
        _tempFiles.Add(pdfPath);

        // Act
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.SanitizeDocument(document, new[] { "Secret" });

        var sanitizedPath = CreateTempPath("metadata_outline_sanitized.pdf");
        document.Save(sanitizedPath);
        _tempFiles.Add(sanitizedPath);
        document.Dispose();

        // Assert
        using var sanitizedDoc = PdfReader.Open(sanitizedPath, PdfDocumentOpenMode.ReadOnly);

        if (sanitizedDoc.Outlines.Count > 0)
        {
            var firstOutline = sanitizedDoc.Outlines[0];
            _output.WriteLine($"First outline: '{firstOutline.Title}'");
            firstOutline.Title.Should().NotContain("Secret");
        }

        _output.WriteLine("✅ TEST PASSED: Outlines sanitized");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void SanitizeDocumentMetadata_HandlesEmptyTermsList()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_HandlesEmptyTermsList ===");

        // Arrange
        var document = new PdfDocument();
        document.Info.Title = "Test Document";

        var page = document.AddPage();

        // Act - Should not throw
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        var act = () => sanitizer.SanitizeDocument(document, new string[] { });

        // Assert
        act.Should().NotThrow();
        document.Info.Title.Should().Be("Test Document"); // Unchanged

        document.Dispose();
        _output.WriteLine("✅ TEST PASSED: Empty terms list handled");
    }

    [Fact]
    public void SanitizeDocumentMetadata_HandlesNullAndEmptyFields()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_HandlesNullAndEmptyFields ===");

        // Arrange
        var document = new PdfDocument();
        // Leave all info fields as default (empty/null)

        var page = document.AddPage();

        // Act - Should not throw
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        var act = () => sanitizer.SanitizeDocument(document, new[] { "test" });

        // Assert
        act.Should().NotThrow();

        document.Dispose();
        _output.WriteLine("✅ TEST PASSED: Null/empty fields handled");
    }

    [Fact]
    public void SanitizeDocumentMetadata_PreservesReplacementLength()
    {
        _output.WriteLine("=== TEST: SanitizeDocumentMetadata_PreservesReplacementLength ===");

        // Arrange
        var document = new PdfDocument();
        document.Info.Title = "The SECRET is here";

        var page = document.AddPage();

        // Act
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.SanitizeDocument(document, new[] { "SECRET" });

        // Assert
        _output.WriteLine($"Original: 'The SECRET is here' (length: {18})");
        _output.WriteLine($"Sanitized: '{document.Info.Title}' (length: {document.Info.Title.Length})");

        // The replacement should have same length as original term
        document.Info.Title.Length.Should().Be(18);
        document.Info.Title.Should().Contain("██████"); // 6 chars

        document.Dispose();
        _output.WriteLine("✅ TEST PASSED: Replacement length preserved");
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MetadataSanitizationTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
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
