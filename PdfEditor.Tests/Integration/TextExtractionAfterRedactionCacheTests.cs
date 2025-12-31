using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PdfEditor.Services;
using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests to verify that text extraction returns correct (non-stale) data after redaction.
///
/// Issue: User reported that after redacting text and saving:
/// 1. Selecting text in the redacted area still shows the old text (BUG)
/// 2. After closing and reopening, the text is correctly gone (CORRECT)
///
/// This suggests either:
/// - Text extraction is using a cached/stale file path
/// - The file wasn't flushed to disk properly
/// - OS-level file caching issue
/// </summary>
public class TextExtractionAfterRedactionCacheTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly PdfTextExtractionService _textExtractionService;

    public TextExtractionAfterRedactionCacheTests(ITestOutputHelper output)
    {
        _output = output;
        _textExtractionService = new PdfTextExtractionService(NullLogger<PdfTextExtractionService>.Instance);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Verify that after redacting text and saving to a new file,
    /// text extraction from the new file returns the redacted version (text removed).
    /// </summary>
    [Fact]
    public void AfterRedactionAndSave_TextExtraction_ReturnsRedactedContent()
    {
        // Arrange - Use the birth certificate PDF
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var redactedPath = Path.Combine(Path.GetTempPath(), $"redaction_cache_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(redactedPath);

        // First, verify original contains "TORRINGTON"
        var originalText = _textExtractionService.ExtractAllText(originalPath);
        originalText.Should().Contain("TORRINGTON", "Original PDF should contain TORRINGTON");
        _output.WriteLine($"Original text contains TORRINGTON: TRUE");

        // Act - Redact and save
        var redactor = new PdfEditor.Redaction.TextRedactor();
        var result = redactor.RedactText(originalPath, redactedPath, "TORRINGTON");
        result.Success.Should().BeTrue();
        _output.WriteLine($"Redaction completed, saved to: {redactedPath}");

        // Force file system flush (ensure file is fully written)
        // This addresses potential OS-level caching issues
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(100); // Small delay to ensure file is flushed

        // Assert - Text extraction from the saved file should NOT contain the redacted text
        var redactedText = _textExtractionService.ExtractAllText(redactedPath);
        _output.WriteLine($"Redacted text length: {redactedText.Length}");
        _output.WriteLine($"Redacted text contains TORRINGTON: {redactedText.Contains("TORRINGTON")}");

        redactedText.Should().NotContain("TORRINGTON",
            "Text extraction from saved file should NOT contain redacted text - " +
            "if this fails, the file wasn't saved properly or text extraction is using a cached version");
    }

    /// <summary>
    /// Simulate the GUI workflow: redact, save, then try to extract text from the same area.
    /// This tests that re-reading the file after save returns updated content.
    /// </summary>
    [Fact]
    public void AfterRedactionAndSave_RereadingFile_ShowsUpdatedContent()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var redactedPath = Path.Combine(Path.GetTempPath(), $"reread_cache_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(redactedPath);

        // Define the area where "TORRINGTON" appears (approximate coordinates)
        // These coordinates are in rendered image pixels at 150 DPI
        var torringtonArea = new Rect(200, 100, 150, 30); // Adjust as needed

        // Extract text from original at specific area
        var originalAreaText = _textExtractionService.ExtractTextFromArea(
            originalPath, 0, torringtonArea, 150);
        _output.WriteLine($"Original area text: '{originalAreaText}'");

        // Act - Redact and save
        var redactor = new PdfEditor.Redaction.TextRedactor();
        var result = redactor.RedactText(originalPath, redactedPath, "TORRINGTON");
        result.Success.Should().BeTrue();

        // Simulate what the GUI does: update the file path variable
        var currentFilePath = redactedPath; // GUI would do: _currentFilePath = redactedPath;

        // Now extract from the "current" file (simulating GUI behavior)
        var afterRedactionAreaText = _textExtractionService.ExtractTextFromArea(
            currentFilePath, 0, torringtonArea, 150);
        _output.WriteLine($"After redaction area text: '{afterRedactionAreaText}'");

        // Assert
        afterRedactionAreaText.Should().NotContain("TORRINGTON",
            "After redaction and save, text extraction from same area should not contain redacted text");
    }

    /// <summary>
    /// Test sequential redactions to ensure each redaction is properly reflected in text extraction.
    /// </summary>
    [Fact]
    public void SequentialRedactions_EachReflectedInTextExtraction()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var step1Path = Path.Combine(Path.GetTempPath(), $"seq_step1_{Guid.NewGuid()}.pdf");
        var step2Path = Path.Combine(Path.GetTempPath(), $"seq_step2_{Guid.NewGuid()}.pdf");
        var step3Path = Path.Combine(Path.GetTempPath(), $"seq_step3_{Guid.NewGuid()}.pdf");
        _tempFiles.AddRange(new[] { step1Path, step2Path, step3Path });

        var redactor = new PdfEditor.Redaction.TextRedactor();

        // Verify original has all text
        var originalText = _textExtractionService.ExtractAllText(originalPath);
        originalText.Should().Contain("TORRINGTON");
        originalText.Should().Contain("CERTIFICATE");

        // Act - Step 1: Redact TORRINGTON
        var result1 = redactor.RedactText(originalPath, step1Path, "TORRINGTON");
        result1.Success.Should().BeTrue();

        // Verify step 1
        var step1Text = _textExtractionService.ExtractAllText(step1Path);
        step1Text.Should().NotContain("TORRINGTON", "Step 1 should remove TORRINGTON");
        step1Text.Should().Contain("CERTIFICATE", "Step 1 should NOT remove CERTIFICATE");
        _output.WriteLine("Step 1: TORRINGTON removed, CERTIFICATE preserved");

        // Act - Step 2: Redact CERTIFICATE from step 1 output
        var result2 = redactor.RedactText(step1Path, step2Path, "CERTIFICATE");
        result2.Success.Should().BeTrue();

        // Verify step 2
        var step2Text = _textExtractionService.ExtractAllText(step2Path);
        step2Text.Should().NotContain("TORRINGTON", "Step 2 should still not have TORRINGTON");
        step2Text.Should().NotContain("CERTIFICATE", "Step 2 should remove CERTIFICATE");
        _output.WriteLine("Step 2: Both TORRINGTON and CERTIFICATE removed");
    }

    /// <summary>
    /// Test that opening the same file twice returns consistent results.
    /// This helps detect any file handle or caching issues.
    /// </summary>
    [Fact]
    public void OpeningFileTwice_ReturnsConsistentResults()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var redactedPath = Path.Combine(Path.GetTempPath(), $"consistent_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(redactedPath);

        var redactor = new PdfEditor.Redaction.TextRedactor();
        var result = redactor.RedactText(originalPath, redactedPath, "TORRINGTON");
        result.Success.Should().BeTrue();

        // Act - Read the file twice in succession
        var firstRead = _textExtractionService.ExtractAllText(redactedPath);
        var secondRead = _textExtractionService.ExtractAllText(redactedPath);

        // Assert
        firstRead.Should().Be(secondRead, "Two consecutive reads should return identical text");
        firstRead.Should().NotContain("TORRINGTON", "Redacted text should not appear");
        _output.WriteLine($"Both reads returned {firstRead.Length} characters, no TORRINGTON");
    }

    /// <summary>
    /// Test that simulates the exact UI bug: redact, save as new file,
    /// then immediately try to extract text (before closing).
    /// </summary>
    [Fact]
    public void SimulateUIBug_RedactSaveExtract_WithoutClosing()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var redactedPath = Path.Combine(Path.GetTempPath(), $"ui_bug_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(redactedPath);

        // This simulates what the ViewModel does:
        // 1. _currentFilePath = originalPath
        string simulatedCurrentFilePath = originalPath;

        // Verify we can read from original
        var textBeforeRedaction = _textExtractionService.ExtractAllText(simulatedCurrentFilePath);
        textBeforeRedaction.Should().Contain("TORRINGTON");
        _output.WriteLine($"Before redaction: TORRINGTON present");

        // 2. Apply redaction to in-memory document and save to new file
        var redactor = new PdfEditor.Redaction.TextRedactor();
        var result = redactor.RedactText(originalPath, redactedPath, "TORRINGTON");
        result.Success.Should().BeTrue();

        // 3. Update _currentFilePath = redactedPath (this is what SaveFileAsAsync does)
        simulatedCurrentFilePath = redactedPath;
        _output.WriteLine($"Current file path updated to: {simulatedCurrentFilePath}");

        // 4. User tries to select text immediately (using _currentFilePath)
        // This is where the bug was reported
        var textAfterRedaction = _textExtractionService.ExtractAllText(simulatedCurrentFilePath);
        _output.WriteLine($"After redaction (from new file): TORRINGTON present = {textAfterRedaction.Contains("TORRINGTON")}");

        // Assert - This should pass if the bug is NOT present
        textAfterRedaction.Should().NotContain("TORRINGTON",
            "After redaction and save, extraction from new file should NOT contain TORRINGTON. " +
            "If this fails, the UI bug is present - text extraction is returning stale data.");
    }

    /// <summary>
    /// Test that file is properly closed after redaction so subsequent reads work.
    /// </summary>
    [Fact]
    public void AfterRedaction_FileIsNotLocked()
    {
        // Arrange
        var originalPath = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
        if (!File.Exists(originalPath))
        {
            _output.WriteLine($"Skipping: Birth certificate PDF not found at {originalPath}");
            return;
        }

        var redactedPath = Path.Combine(Path.GetTempPath(), $"file_lock_test_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(redactedPath);

        // Act - Redact
        var redactor = new PdfEditor.Redaction.TextRedactor();
        var result = redactor.RedactText(originalPath, redactedPath, "TORRINGTON");
        result.Success.Should().BeTrue();

        // Assert - Should be able to open and read the file
        Action openFile = () =>
        {
            using var stream = File.OpenRead(redactedPath);
            var buffer = new byte[100];
            stream.Read(buffer, 0, 100);
        };

        openFile.Should().NotThrow("File should not be locked after redaction");

        // Should also be able to delete the file
        Action deleteFile = () => File.Delete(redactedPath);
        deleteFile.Should().NotThrow("File should not be locked");

        // Remove from temp files list since we deleted it
        _tempFiles.Remove(redactedPath);
        _output.WriteLine("File was not locked after redaction");
    }
}
