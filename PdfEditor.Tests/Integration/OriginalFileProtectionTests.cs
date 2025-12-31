using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.ViewModels;
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
/// Original File Protection Tests (Issue #43)
///
/// Verifies that original files CANNOT be overwritten during redaction workflow,
/// preventing data loss from accidental overwrites.
///
/// CRITICAL: These tests verify safety guarantees that protect user data.
/// </summary>
public class OriginalFileProtectionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly PdfDocumentService _documentService;
    private readonly ILoggerFactory _loggerFactory;

    public OriginalFileProtectionTests(ITestOutputHelper output)
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

        var documentLogger = _loggerFactory.CreateLogger<PdfDocumentService>();
        _documentService = new PdfDocumentService(documentLogger);

        _output.WriteLine("=== Original File Protection Test Suite ===");
        _output.WriteLine($"Test started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine("");
    }

    #region Core Protection Tests

    /// <summary>
    /// CRITICAL: Verify original file is NEVER modified when redaction is applied
    /// Tests the low-level service layer to ensure file integrity
    /// </summary>
    [Fact]
    public void OriginalFile_AfterRedactionAndSaveToDifferentPath_RemainsUnchanged()
    {
        _output.WriteLine("\n=== TEST: OriginalFile_AfterRedactionAndSaveToDifferentPath_RemainsUnchanged ===");

        // Arrange: Create original file with known content
        var originalPath = CreateTempPath("important_original.pdf");
        var targetText = "CONFIDENTIAL_DATA";
        TestPdfGenerator.CreateSimpleTextPdf(originalPath, targetText);
        _tempFiles.Add(originalPath);

        var originalTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var originalSize = new FileInfo(originalPath).Length;
        var originalContent = PdfTestHelpers.ExtractAllText(originalPath);
        originalContent.Should().Contain(targetText, "original file should have the text");

        _output.WriteLine($"Original file created: {originalPath}");
        _output.WriteLine($"Original timestamp: {originalTimestamp}");
        _output.WriteLine($"Original size: {originalSize} bytes");
        _output.WriteLine($"Original content contains: '{targetText}'");

        // Small delay to ensure timestamps would differ if file was modified
        System.Threading.Thread.Sleep(100);

        // Act: Load file, apply redaction, save to DIFFERENT path
        _documentService.LoadDocument(originalPath);
        var document = _documentService.GetCurrentDocument();
        document.Should().NotBeNull();

        // Find text and redact it
        var redactionArea = GetWordBounds(originalPath, targetText);
        _redactionService.RedactArea(document!.Pages[0], redactionArea, originalPath, renderDpi: 150);

        // Save to DIFFERENT path
        var redactedPath = CreateTempPath("important_original_REDACTED.pdf");
        _documentService.SaveDocument(redactedPath);
        _tempFiles.Add(redactedPath);

        _output.WriteLine($"Redacted file saved to: {redactedPath}");

        _documentService.CloseDocument();

        // Assert: Original file is completely untouched
        File.Exists(originalPath).Should().BeTrue("original file must still exist");

        var newTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var newSize = new FileInfo(originalPath).Length;
        var newContent = PdfTestHelpers.ExtractAllText(originalPath);

        newTimestamp.Should().Be(originalTimestamp, "original file timestamp must NOT change");
        newSize.Should().Be(originalSize, "original file size must NOT change");
        newContent.Should().Contain(targetText, "original file content must be preserved");

        _output.WriteLine($"✓ Original file unchanged - timestamp: {newTimestamp}, size: {newSize} bytes");

        // Verify redacted file has changes
        var redactedContent = PdfTestHelpers.ExtractAllText(redactedPath);
        redactedContent.Should().NotContain(targetText, "redacted file should have text removed");

        _output.WriteLine($"✓ Redacted file has text removed");
        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify two files exist after redaction (original + redacted)
    /// </summary>
    [Fact]
    public void AfterRedaction_TwoFilesExist_OriginalAndRedacted()
    {
        _output.WriteLine("\n=== TEST: AfterRedaction_TwoFilesExist_OriginalAndRedacted ===");

        // Arrange
        var originalPath = CreateTempPath("document.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(originalPath, "SECRET_INFO");
        _tempFiles.Add(originalPath);

        _documentService.LoadDocument(originalPath);
        var document = _documentService.GetCurrentDocument();

        var redactionArea = GetWordBounds(originalPath, "SECRET");
        _redactionService.RedactArea(document!.Pages[0], redactionArea, originalPath, renderDpi: 150);

        var redactedPath = CreateTempPath("document_REDACTED.pdf");
        _documentService.SaveDocument(redactedPath);
        _tempFiles.Add(redactedPath);

        _documentService.CloseDocument();

        // Assert: Both files exist
        File.Exists(originalPath).Should().BeTrue("original file must exist");
        File.Exists(redactedPath).Should().BeTrue("redacted file must exist");

        // Files are different
        var originalContent = PdfTestHelpers.ExtractAllText(originalPath);
        var redactedContent = PdfTestHelpers.ExtractAllText(redactedPath);

        originalContent.Should().Contain("SECRET", "original still has text");
        redactedContent.Should().NotContain("SECRET", "redacted has text removed");

        _output.WriteLine($"✓ Original file: {originalPath} (contains 'SECRET')");
        _output.WriteLine($"✓ Redacted file: {redactedPath} (text removed)");
        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify that PdfDocumentService.SaveDocument() without path doesn't allow
    /// overwriting original when using the redaction workflow pattern
    /// </summary>
    [Fact]
    public void SaveDocument_WithoutPath_AfterRedaction_DoesNotOverwriteOriginal()
    {
        _output.WriteLine("\n=== TEST: SaveDocument_WithoutPath_AfterRedaction_DoesNotOverwriteOriginal ===");

        // Arrange: Create original file
        var originalPath = CreateTempPath("protected.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(originalPath, "PROTECTED_TEXT");
        _tempFiles.Add(originalPath);

        var originalTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var originalContent = PdfTestHelpers.ExtractAllText(originalPath);

        // Act: Load, redact, save to different path, then try to save without path
        _documentService.LoadDocument(originalPath);
        var document = _documentService.GetCurrentDocument();

        var redactionArea = GetWordBounds(originalPath, "PROTECTED");
        _redactionService.RedactArea(document!.Pages[0], redactionArea, originalPath, renderDpi: 150);

        // Save to different path first
        var redactedPath = CreateTempPath("protected_REDACTED.pdf");
        _documentService.SaveDocument(redactedPath);
        _tempFiles.Add(redactedPath);

        // Now calling SaveDocument() without path should save to redactedPath, NOT originalPath
        _documentService.SaveDocument();

        _documentService.CloseDocument();

        System.Threading.Thread.Sleep(100);

        // Assert: Original file is still unchanged
        var newTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var newContent = PdfTestHelpers.ExtractAllText(originalPath);

        newTimestamp.Should().Be(originalTimestamp, "original file must not be modified");
        newContent.Should().Be(originalContent, "original content must be preserved");

        _output.WriteLine("✓ Original file protected from SaveDocument() without path");
        _output.WriteLine("✓ PASS");
    }

    #endregion

    #region DocumentStateManager Integration Tests

    /// <summary>
    /// Verify DocumentStateManager correctly identifies original vs redacted files
    /// </summary>
    [Fact]
    public void DocumentStateManager_CorrectlyIdentifiesOriginalFile()
    {
        _output.WriteLine("\n=== TEST: DocumentStateManager_CorrectlyIdentifiesOriginalFile ===");

        var stateManager = new DocumentStateManager();

        // Load original file
        var originalPath = CreateTempPath("data.pdf");
        stateManager.SetDocument(originalPath);

        // Assert: Identified as original
        stateManager.IsOriginalFile.Should().BeTrue("should identify as original file");
        stateManager.IsRedactedVersion.Should().BeFalse("should not identify as redacted");
        stateManager.FileType.Should().Be("Original");

        _output.WriteLine($"✓ Original file correctly identified: {stateManager.FileType}");

        // Simulate save to different path
        var redactedPath = CreateTempPath("data_REDACTED.pdf");
        stateManager.UpdateCurrentPath(redactedPath);

        // Assert: No longer original
        stateManager.IsOriginalFile.Should().BeFalse("should no longer be original after save as");
        stateManager.IsRedactedVersion.Should().BeTrue("should identify as redacted version");

        _output.WriteLine($"✓ After Save As: {stateManager.FileType}");
        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify DocumentStateManager tracks unsaved changes correctly
    /// </summary>
    [Fact]
    public void DocumentStateManager_TracksUnsavedChanges()
    {
        _output.WriteLine("\n=== TEST: DocumentStateManager_TracksUnsavedChanges ===");

        var stateManager = new DocumentStateManager();
        var originalPath = CreateTempPath("report.pdf");
        stateManager.SetDocument(originalPath);

        // Initially no changes
        stateManager.HasUnsavedChanges.Should().BeFalse("no changes initially");
        stateManager.FileType.Should().Be("Original");

        _output.WriteLine("✓ Initial state: No unsaved changes");

        // Add pending redaction
        stateManager.PendingRedactionsCount = 1;

        // Assert: Has unsaved changes
        stateManager.HasUnsavedChanges.Should().BeTrue("should have unsaved changes");
        stateManager.FileType.Should().Be("Original (unsaved changes)");

        _output.WriteLine($"✓ After adding redaction: {stateManager.FileType}");

        // Clear changes
        stateManager.PendingRedactionsCount = 0;
        stateManager.HasUnsavedChanges.Should().BeFalse("changes cleared");

        _output.WriteLine("✓ After clearing changes: No unsaved changes");
        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify DocumentStateManager suggests correct save button text
    /// </summary>
    [Fact]
    public void DocumentStateManager_SuggestsCorrectSaveButtonText()
    {
        _output.WriteLine("\n=== TEST: DocumentStateManager_SuggestsCorrectSaveButtonText ===");

        var stateManager = new DocumentStateManager();
        var originalPath = CreateTempPath("contract.pdf");
        stateManager.SetDocument(originalPath);

        // No changes: Just "Save"
        stateManager.GetSaveButtonText().Should().Be("Save");
        _output.WriteLine($"✓ No changes: '{stateManager.GetSaveButtonText()}'");

        // Original with changes: "Save Redacted Version"
        stateManager.PendingRedactionsCount = 1;
        stateManager.GetSaveButtonText().Should().Be("Save Redacted Version");
        _output.WriteLine($"✓ Original with changes: '{stateManager.GetSaveButtonText()}'");

        // After save as: Just "Save"
        stateManager.UpdateCurrentPath(CreateTempPath("contract_REDACTED.pdf"));
        stateManager.GetSaveButtonText().Should().Be("Save");
        _output.WriteLine($"✓ Redacted version: '{stateManager.GetSaveButtonText()}'");

        _output.WriteLine("✓ PASS");
    }

    #endregion

    #region Multi-Redaction Protection Tests

    /// <summary>
    /// Verify original file protection with multiple redactions applied
    /// </summary>
    [Fact]
    public void OriginalFile_MultipleRedactions_RemainsUnchanged()
    {
        _output.WriteLine("\n=== TEST: OriginalFile_MultipleRedactions_RemainsUnchanged ===");

        // Arrange: Create file with multiple text areas
        var originalPath = CreateTempPath("multi_redact.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(originalPath, new[]
        {
            "Line 1: REDACT_THIS",
            "Line 2: ALSO_REDACT",
            "Line 3: AND_THIS_TOO"
        });
        _tempFiles.Add(originalPath);

        var originalTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var originalContent = PdfTestHelpers.ExtractAllText(originalPath);

        System.Threading.Thread.Sleep(100);

        // Act: Apply multiple redactions
        _documentService.LoadDocument(originalPath);
        var document = _documentService.GetCurrentDocument();

        // Redact all three areas
        var area1 = GetWordBounds(originalPath, "REDACT_THIS");
        var area2 = GetWordBounds(originalPath, "ALSO_REDACT");
        var area3 = GetWordBounds(originalPath, "AND_THIS_TOO");

        _redactionService.RedactArea(document!.Pages[0], area1, originalPath, renderDpi: 150);
        _redactionService.RedactArea(document.Pages[0], area2, originalPath, renderDpi: 150);
        _redactionService.RedactArea(document.Pages[0], area3, originalPath, renderDpi: 150);

        var redactedPath = CreateTempPath("multi_redact_REDACTED.pdf");
        _documentService.SaveDocument(redactedPath);
        _tempFiles.Add(redactedPath);

        _documentService.CloseDocument();

        // Assert: Original file unchanged
        var newTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var newContent = PdfTestHelpers.ExtractAllText(originalPath);

        newTimestamp.Should().Be(originalTimestamp, "timestamp unchanged despite multiple redactions");
        newContent.Should().Be(originalContent, "content unchanged");

        // Verify all redactions applied to redacted file
        var redactedContent = PdfTestHelpers.ExtractAllText(redactedPath);
        redactedContent.Should().NotContain("REDACT_THIS");
        redactedContent.Should().NotContain("ALSO_REDACT");
        redactedContent.Should().NotContain("AND_THIS_TOO");

        _output.WriteLine("✓ Original unchanged after 3 redactions");
        _output.WriteLine("✓ All redactions applied to new file");
        _output.WriteLine("✓ PASS");
    }

    /// <summary>
    /// Verify original file protection when reloading and applying more redactions
    /// </summary>
    [Fact]
    public void OriginalFile_SequentialSaves_AlwaysProtected()
    {
        _output.WriteLine("\n=== TEST: OriginalFile_SequentialSaves_AlwaysProtected ===");

        // Arrange: Create file with two separate text blocks
        var originalPath = CreateTempPath("sequential.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(originalPath, "FIRST_SECRET");
        _tempFiles.Add(originalPath);

        var originalTimestamp = File.GetLastWriteTimeUtc(originalPath);

        System.Threading.Thread.Sleep(100);

        // Act: First redaction - part of the text
        _documentService.LoadDocument(originalPath);
        var document = _documentService.GetCurrentDocument();

        var area1 = GetWordBounds(originalPath, "FIRST");
        _redactionService.RedactArea(document!.Pages[0], area1, originalPath, renderDpi: 150);

        var firstSavePath = CreateTempPath("sequential_v1.pdf");
        _documentService.SaveDocument(firstSavePath);
        _tempFiles.Add(firstSavePath);

        _output.WriteLine($"First save: {firstSavePath}");

        // Close and reload for second redaction
        _documentService.CloseDocument();

        // Create a second PDF for sequential saves test
        var secondOriginal = CreateTempPath("sequential2.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(secondOriginal, "ANOTHER_TEXT");
        _tempFiles.Add(secondOriginal);

        var secondOriginalTimestamp = File.GetLastWriteTimeUtc(secondOriginal);

        System.Threading.Thread.Sleep(100);

        // Load second file, redact, save
        _documentService.LoadDocument(secondOriginal);
        var document2 = _documentService.GetCurrentDocument();

        var area2 = GetWordBounds(secondOriginal, "ANOTHER");
        _redactionService.RedactArea(document2!.Pages[0], area2, secondOriginal, renderDpi: 150);

        var secondSavePath = CreateTempPath("sequential2_v1.pdf");
        _documentService.SaveDocument(secondSavePath);
        _tempFiles.Add(secondSavePath);

        _documentService.CloseDocument();

        // Assert: Both originals still unchanged
        var newTimestamp = File.GetLastWriteTimeUtc(originalPath);
        var originalContent = PdfTestHelpers.ExtractAllText(originalPath);

        newTimestamp.Should().Be(originalTimestamp, "first original never touched");
        originalContent.Should().Contain("FIRST_SECRET", "first original preserved");

        var newTimestamp2 = File.GetLastWriteTimeUtc(secondOriginal);
        var originalContent2 = PdfTestHelpers.ExtractAllText(secondOriginal);

        newTimestamp2.Should().Be(secondOriginalTimestamp, "second original never touched");
        originalContent2.Should().Contain("ANOTHER_TEXT", "second original preserved");

        // Verify saved files have redactions
        var v1Content = PdfTestHelpers.ExtractAllText(firstSavePath);
        v1Content.Should().NotContain("FIRST", "first save should have redaction");

        var v2Content = PdfTestHelpers.ExtractAllText(secondSavePath);
        v2Content.Should().NotContain("ANOTHER", "second save should have redaction");

        _output.WriteLine("✓ Both originals protected across sequential operations");
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
        return Path.Combine(Path.GetTempPath(), $"pdfe_protection_test_{Guid.NewGuid()}_{filename}");
    }

    public void Dispose()
    {
        _documentService?.CloseDocument();
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
