using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;
using Avalonia;
using System.IO;
using System.Text;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Glyph Removal Verification Tests
///
/// These tests verify that text glyphs are REMOVED from the PDF structure,
/// not just visually covered with black boxes.
///
/// CRITICAL: If any of these tests fail, the redaction feature is broken
/// and represents a security vulnerability.
///
/// Each test includes extensive logging to help diagnose failures.
/// </summary>
public class GlyphRemovalVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public GlyphRemovalVerificationTests(ITestOutputHelper output)
    {
        _output = output;

        // Initialize font resolver for cross-platform support
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new CustomFontResolver();
        }

        // Create logger factory that outputs to test output
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        var logger = _loggerFactory.CreateLogger<RedactionService>();
        _redactionService = new RedactionService(logger, _loggerFactory);

        _output.WriteLine("=== Glyph Removal Verification Test Suite ===");
        _output.WriteLine($"Test started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");
    }

    #region Core Glyph Removal Tests

    /// <summary>
    /// CRITICAL TEST: Verifies that text is actually removed from PDF structure
    /// This is the most important test - if it fails, redaction is broken.
    /// </summary>
    [Fact]
    public void GlyphRemoval_TextMustBeAbsentAfterRedaction()
    {
        _output.WriteLine("=== TEST: GlyphRemoval_TextMustBeAbsentAfterRedaction ===");
        _output.WriteLine("Purpose: Verify text glyphs are REMOVED from PDF structure");
        _output.WriteLine("");

        // Arrange
        var testPdf = CreateTempPath("glyph_removal_test.pdf");
        var targetText = "REMOVE_THIS_TEXT";

        _output.WriteLine($"Step 1: Creating test PDF with text '{targetText}'");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, targetText);
        _tempFiles.Add(testPdf);

        // Verify text exists before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Step 2: Extracted text before redaction:");
        _output.WriteLine($"  '{textBefore.Trim()}'");

        var containsBefore = textBefore.Contains(targetText);
        _output.WriteLine($"Step 3: Text contains '{targetText}': {containsBefore}");
        containsBefore.Should().BeTrue($"'{targetText}' must exist before redaction");

        // Get content stream before for comparison
        var contentBefore = PdfTestHelpers.GetPageContentStream(testPdf);
        _output.WriteLine($"Step 4: Content stream size before: {contentBefore.Length} bytes");

        // Act - Apply redaction
        _output.WriteLine("Step 5: Applying redaction...");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact area covering the text (approximate position)
        var redactionArea = new Rect(90, 90, 200, 30);
        _output.WriteLine($"  Redaction area: X={redactionArea.X}, Y={redactionArea.Y}, " +
                         $"W={redactionArea.Width}, H={redactionArea.Height}");

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("glyph_removal_test_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();
        _output.WriteLine($"  Saved redacted PDF to: {redactedPdf}");

        // Assert - CRITICAL: Text must be ABSENT
        _output.WriteLine("Step 6: Verifying text removal...");
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"  Extracted text after redaction:");
        _output.WriteLine($"  '{textAfter.Trim()}'");

        var containsAfter = textAfter.Contains(targetText);
        _output.WriteLine($"Step 7: Text contains '{targetText}': {containsAfter}");

        // Get content stream after for comparison
        var contentAfter = PdfTestHelpers.GetPageContentStream(redactedPdf);
        _output.WriteLine($"Step 8: Content stream size after: {contentAfter.Length} bytes");
        _output.WriteLine($"  Content stream changed: {contentBefore.Length != contentAfter.Length}");

        // THE CRITICAL ASSERTION
        textAfter.Should().NotContain(targetText,
            $"CRITICAL FAILURE: '{targetText}' must be REMOVED from PDF structure. " +
            "If this test fails, text is only visually covered, not removed. " +
            "This is a SECURITY VULNERABILITY.");

        // Additional verification
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF must remain valid after redaction");

        _output.WriteLine("");
        _output.WriteLine("✅ TEST PASSED: Text glyphs were successfully removed from PDF structure");
        _output.WriteLine("=== END TEST ===");
    }

    /// <summary>
    /// Verifies that non-redacted text is preserved
    /// </summary>
    [Fact]
    public void GlyphRemoval_PreservesNonRedactedText()
    {
        _output.WriteLine("=== TEST: GlyphRemoval_PreservesNonRedactedText ===");
        _output.WriteLine("Purpose: Verify text outside redaction area is preserved");
        _output.WriteLine("");

        // Arrange
        var testPdf = CreateTempPath("preserve_text_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        _output.WriteLine("Step 1: Created PDF with multiple text items:");
        _output.WriteLine("  - CONFIDENTIAL (at 100, 100)");
        _output.WriteLine("  - Public Information (at 100, 200)");
        _output.WriteLine("  - Secret Data (at 100, 300)");
        _output.WriteLine("  - Normal Text (at 100, 400)");

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Step 2: All text before redaction:");
        _output.WriteLine($"  '{textBefore.Trim()}'");

        // Act - Redact only CONFIDENTIAL
        _output.WriteLine("Step 3: Redacting only 'CONFIDENTIAL' area...");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var redactionArea = new Rect(90, 90, 150, 30);
        _output.WriteLine($"  Redaction area: X={redactionArea.X}, Y={redactionArea.Y}, " +
                         $"W={redactionArea.Width}, H={redactionArea.Height}");

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("preserve_text_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Step 4: Text after redaction:");
        _output.WriteLine($"  '{textAfter.Trim()}'");

        _output.WriteLine("Step 5: Verifying results...");

        // Redacted text should be removed
        textAfter.Should().NotContain("CONFIDENTIAL",
            "CONFIDENTIAL should be removed");
        _output.WriteLine("  ✓ 'CONFIDENTIAL' removed");

        // Other text should be preserved
        textAfter.Should().Contain("Public", "Public Information should be preserved");
        _output.WriteLine("  ✓ 'Public Information' preserved");

        textAfter.Should().Contain("Secret", "Secret Data should be preserved");
        _output.WriteLine("  ✓ 'Secret Data' preserved");

        textAfter.Should().Contain("Normal", "Normal Text should be preserved");
        _output.WriteLine("  ✓ 'Normal Text' preserved");

        _output.WriteLine("");
        _output.WriteLine("✅ TEST PASSED: Selective glyph removal working correctly");
        _output.WriteLine("=== END TEST ===");
    }

    /// <summary>
    /// Verifies that content stream is actually modified (not just black box added)
    /// </summary>
    [Fact]
    public void GlyphRemoval_ContentStreamMustBeModified()
    {
        _output.WriteLine("=== TEST: GlyphRemoval_ContentStreamMustBeModified ===");
        _output.WriteLine("Purpose: Verify PDF content stream is modified, not just appended");
        _output.WriteLine("");

        // Arrange
        var testPdf = CreateTempPath("content_stream_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "TEST_CONTENT");
        _tempFiles.Add(testPdf);

        var contentBefore = PdfTestHelpers.GetPageContentStream(testPdf);
        var contentStringBefore = Encoding.UTF8.GetString(contentBefore);

        _output.WriteLine($"Step 1: Content stream before redaction:");
        _output.WriteLine($"  Size: {contentBefore.Length} bytes");
        _output.WriteLine($"  Contains 'TEST_CONTENT': {contentStringBefore.Contains("TEST_CONTENT")}");

        // Act
        _output.WriteLine("Step 2: Applying redaction...");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 150, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("content_stream_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var contentAfter = PdfTestHelpers.GetPageContentStream(redactedPdf);
        var contentStringAfter = Encoding.UTF8.GetString(contentAfter);

        _output.WriteLine($"Step 3: Content stream after redaction:");
        _output.WriteLine($"  Size: {contentAfter.Length} bytes");
        _output.WriteLine($"  Contains 'TEST_CONTENT': {contentStringAfter.Contains("TEST_CONTENT")}");

        // Content stream should be different
        var bytesMatch = contentBefore.SequenceEqual(contentAfter);
        _output.WriteLine($"Step 4: Content streams identical: {bytesMatch}");

        bytesMatch.Should().BeFalse(
            "Content stream must be modified for glyph removal. " +
            "If streams are identical, only a black box was appended without removing text.");

        // The target text should not be in the new content stream
        contentStringAfter.Should().NotContain("TEST_CONTENT",
            "Text should be removed from content stream bytes");

        _output.WriteLine("");
        _output.WriteLine("✅ TEST PASSED: Content stream was properly modified");
        _output.WriteLine("=== END TEST ===");
    }

    #endregion

    #region Multiple Redaction Tests

    /// <summary>
    /// Verifies multiple redactions work correctly
    /// </summary>
    [Fact]
    public void GlyphRemoval_MultipleRedactionsWork()
    {
        _output.WriteLine("=== TEST: GlyphRemoval_MultipleRedactionsWork ===");
        _output.WriteLine("Purpose: Verify multiple redaction areas remove correct glyphs");
        _output.WriteLine("");

        // Arrange
        var testPdf = CreateTempPath("multi_redaction_test.pdf");
        var (pdfPath, contentMap) = TestPdfGenerator.CreateMappedContentPdf(testPdf);
        _tempFiles.Add(testPdf);

        _output.WriteLine("Step 1: Created PDF with mapped content:");
        foreach (var item in contentMap)
        {
            _output.WriteLine($"  {item.Key}: ({item.Value.x}, {item.Value.y})");
        }

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        _output.WriteLine($"Step 2: Text before redaction contains all items: " +
            $"{textBefore.Contains("CONFIDENTIAL") && textBefore.Contains("SECRET") && textBefore.Contains("PUBLIC")}");

        // Act - Redact multiple items
        _output.WriteLine("Step 3: Applying multiple redactions...");
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact CONFIDENTIAL
        var pos1 = contentMap["CONFIDENTIAL"];
        var area1 = new Rect(pos1.x - 5, pos1.y - 5, pos1.width + 10, pos1.height + 10);
        _output.WriteLine($"  Redacting CONFIDENTIAL at: {area1}");
        _redactionService.RedactArea(page, area1, renderDpi: 72);

        // Redact SECRET
        var pos2 = contentMap["SECRET"];
        var area2 = new Rect(pos2.x - 5, pos2.y - 5, pos2.width + 10, pos2.height + 10);
        _output.WriteLine($"  Redacting SECRET at: {area2}");
        _redactionService.RedactArea(page, area2, renderDpi: 72);

        var redactedPdf = CreateTempPath("multi_redaction_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Step 4: Text after redaction:");
        _output.WriteLine($"  '{textAfter.Trim()}'");

        _output.WriteLine("Step 5: Verifying results...");

        textAfter.Should().NotContain("CONFIDENTIAL", "CONFIDENTIAL should be removed");
        _output.WriteLine("  ✓ 'CONFIDENTIAL' removed");

        textAfter.Should().NotContain("SECRET", "SECRET should be removed");
        _output.WriteLine("  ✓ 'SECRET' removed");

        textAfter.Should().Contain("PUBLIC", "PUBLIC should be preserved");
        _output.WriteLine("  ✓ 'PUBLIC' preserved");

        textAfter.Should().Contain("PRIVATE", "PRIVATE should be preserved");
        _output.WriteLine("  ✓ 'PRIVATE' preserved");

        textAfter.Should().Contain("INTERNAL", "INTERNAL should be preserved");
        _output.WriteLine("  ✓ 'INTERNAL' preserved");

        _output.WriteLine("");
        _output.WriteLine("✅ TEST PASSED: Multiple redactions working correctly");
        _output.WriteLine("=== END TEST ===");
    }

    #endregion

    #region Diagnostic Tests

    /// <summary>
    /// Diagnostic test that outputs detailed information about the redaction process
    /// Run this test to debug redaction failures
    /// </summary>
    [Fact]
    public void Diagnostic_DetailedRedactionAnalysis()
    {
        _output.WriteLine("=== DIAGNOSTIC: DetailedRedactionAnalysis ===");
        _output.WriteLine("Purpose: Generate detailed diagnostic output for debugging");
        _output.WriteLine("");

        // Create test PDF
        var testPdf = CreateTempPath("diagnostic_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "DIAGNOSTIC_TEXT");
        _tempFiles.Add(testPdf);

        _output.WriteLine("=== PRE-REDACTION STATE ===");

        // Analyze before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        var wordsBefore = PdfTestHelpers.GetAllUniqueWords(testPdf);
        var contentBefore = PdfTestHelpers.GetPageContentStream(testPdf);
        var positionsBefore = PdfTestHelpers.GetTextWithPositions(testPdf);

        _output.WriteLine($"Text content: '{textBefore.Trim()}'");
        _output.WriteLine($"Unique words: {string.Join(", ", wordsBefore)}");
        _output.WriteLine($"Content stream size: {contentBefore.Length} bytes");
        _output.WriteLine($"Text positions:");
        foreach (var pos in positionsBefore)
        {
            _output.WriteLine($"  '{pos.text}' at ({pos.x:F1}, {pos.y:F1})");
        }

        // Apply redaction
        _output.WriteLine("");
        _output.WriteLine("=== APPLYING REDACTION ===");

        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var redactionArea = new Rect(90, 90, 200, 30);

        _output.WriteLine($"Redaction area: X={redactionArea.X}, Y={redactionArea.Y}, " +
                         $"W={redactionArea.Width}, H={redactionArea.Height}");

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("diagnostic_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Analyze after redaction
        _output.WriteLine("");
        _output.WriteLine("=== POST-REDACTION STATE ===");

        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        var wordsAfter = PdfTestHelpers.GetAllUniqueWords(redactedPdf);
        var contentAfter = PdfTestHelpers.GetPageContentStream(redactedPdf);
        var positionsAfter = PdfTestHelpers.GetTextWithPositions(redactedPdf);

        _output.WriteLine($"Text content: '{textAfter.Trim()}'");
        _output.WriteLine($"Unique words: {string.Join(", ", wordsAfter)}");
        _output.WriteLine($"Content stream size: {contentAfter.Length} bytes");
        _output.WriteLine($"Text positions:");
        foreach (var pos in positionsAfter)
        {
            _output.WriteLine($"  '{pos.text}' at ({pos.x:F1}, {pos.y:F1})");
        }

        // Compare
        _output.WriteLine("");
        _output.WriteLine("=== COMPARISON ===");

        var (removed, added, kept) = PdfTestHelpers.CompareContent(testPdf, redactedPdf);
        _output.WriteLine($"Words removed: {string.Join(", ", removed)}");
        _output.WriteLine($"Words added: {string.Join(", ", added)}");
        _output.WriteLine($"Words kept: {string.Join(", ", kept)}");

        _output.WriteLine("");
        _output.WriteLine($"Content stream changed: {contentBefore.Length != contentAfter.Length}");
        _output.WriteLine($"  Before: {contentBefore.Length} bytes");
        _output.WriteLine($"  After: {contentAfter.Length} bytes");
        _output.WriteLine($"  Difference: {contentAfter.Length - contentBefore.Length} bytes");

        // Verdict
        _output.WriteLine("");
        _output.WriteLine("=== DIAGNOSTIC VERDICT ===");

        var glyphRemoved = !textAfter.Contains("DIAGNOSTIC_TEXT");
        var contentChanged = contentBefore.Length != contentAfter.Length;
        var pdfValid = PdfTestHelpers.IsValidPdf(redactedPdf);

        _output.WriteLine($"Glyph removed from text extraction: {(glyphRemoved ? "✓ YES" : "✗ NO")}");
        _output.WriteLine($"Content stream modified: {(contentChanged ? "✓ YES" : "✗ NO")}");
        _output.WriteLine($"PDF remains valid: {(pdfValid ? "✓ YES" : "✗ NO")}");

        if (glyphRemoved && contentChanged && pdfValid)
        {
            _output.WriteLine("");
            _output.WriteLine("✅ GLYPH REMOVAL IS WORKING CORRECTLY");
        }
        else
        {
            _output.WriteLine("");
            _output.WriteLine("❌ GLYPH REMOVAL HAS ISSUES:");
            if (!glyphRemoved)
                _output.WriteLine("  - Text is still extractable (NOT removed from structure)");
            if (!contentChanged)
                _output.WriteLine("  - Content stream unchanged (only black box added?)");
            if (!pdfValid)
                _output.WriteLine("  - PDF is corrupted");
        }

        // Final assertion
        glyphRemoved.Should().BeTrue("Glyph must be removed");
        contentChanged.Should().BeTrue("Content stream must be modified");
        pdfValid.Should().BeTrue("PDF must remain valid");

        _output.WriteLine("");
        _output.WriteLine("=== END DIAGNOSTIC ===");
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Tests redaction of empty area (no content to remove)
    /// </summary>
    [Fact]
    public void GlyphRemoval_EmptyAreaDoesNotCorruptPdf()
    {
        _output.WriteLine("=== TEST: GlyphRemoval_EmptyAreaDoesNotCorruptPdf ===");

        var testPdf = CreateTempPath("empty_area_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "TEST");
        _tempFiles.Add(testPdf);

        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact area with no content
        _redactionService.RedactArea(page, new Rect(500, 500, 50, 50), renderDpi: 72);

        var redactedPdf = CreateTempPath("empty_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // PDF should remain valid and text should still exist
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();
        var text = PdfTestHelpers.ExtractAllText(redactedPdf);
        text.Should().Contain("TEST", "Text outside redaction area should be preserved");

        _output.WriteLine("✅ TEST PASSED: Empty area redaction doesn't corrupt PDF");
    }

    /// <summary>
    /// Tests that PDF remains valid after multiple sequential redactions
    /// </summary>
    [Fact]
    public void GlyphRemoval_SequentialRedactionsRemainValid()
    {
        _output.WriteLine("=== TEST: GlyphRemoval_SequentialRedactionsRemainValid ===");

        var testPdf = CreateTempPath("sequential_test.pdf");
        TestPdfGenerator.CreateMultiTextPdf(testPdf);
        _tempFiles.Add(testPdf);

        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Apply 4 sequential redactions
        _redactionService.RedactArea(page, new Rect(90, 90, 150, 30), renderDpi: 72);
        _redactionService.RedactArea(page, new Rect(90, 190, 200, 30), renderDpi: 72);
        _redactionService.RedactArea(page, new Rect(90, 290, 150, 30), renderDpi: 72);
        _redactionService.RedactArea(page, new Rect(90, 390, 150, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("sequential_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF must remain valid after multiple sequential redactions");

        _output.WriteLine("✅ TEST PASSED: Sequential redactions maintain PDF validity");
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "GlyphRemovalTests");
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
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #endregion
}

#region Xunit Logger Provider

/// <summary>
/// Logger provider that outputs to xUnit test output
/// </summary>
public class XunitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XunitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// Logger that writes to xUnit test output
/// </summary>
public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            var shortCategory = _categoryName.Split('.').LastOrDefault() ?? _categoryName;
            _output.WriteLine($"[{logLevel}] {shortCategory}: {message}");

            if (exception != null)
            {
                _output.WriteLine($"  Exception: {exception.Message}");
            }
        }
        catch
        {
            // Ignore logging errors
        }
    }
}

#endregion
