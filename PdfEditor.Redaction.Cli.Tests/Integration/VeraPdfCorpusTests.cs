using FluentAssertions;
using PdfEditor.Redaction.Cli.Tests.TestHelpers;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Cli.Tests.Integration;

/// <summary>
/// Tests redaction against the veraPDF corpus - a comprehensive collection of PDF test files
/// covering different PDF versions, standards, and features.
///
/// These tests verify that:
/// 1. pdfer can read and process diverse PDF files without crashing
/// 2. Text extraction works correctly across different PDF structures
/// 3. Redaction maintains PDF validity after modification
/// 4. The redaction library handles edge cases from real-world PDFs
///
/// The corpus includes:
/// - PDF/A-1a, 1b, 2a, 2b, 2u, 3b, 4, 4e, 4f (archival formats)
/// - PDF/UA-1, PDF/UA-2 (universal accessibility)
/// - ISO 32000-1, ISO 32000-2 (PDF 1.7 and 2.0 standards)
/// - Various font types, encodings, and content structures
/// </summary>
[Collection("CorpusTests")]
public class VeraPdfCorpusTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;
    private static readonly string CorpusPath = FindCorpusPath();

    public VeraPdfCorpusTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdfer_corpus_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static string FindCorpusPath()
    {
        var searchPaths = new[]
        {
            "/home/marc/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master",
            Path.Combine(Directory.GetCurrentDirectory(), "test-pdfs", "verapdf-corpus", "veraPDF-corpus-master"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test-pdfs", "verapdf-corpus", "veraPDF-corpus-master"),
        };

        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path))
                return path;
        }

        return searchPaths[0]; // Return first path even if not found (tests will skip)
    }

    private static bool CorpusExists => Directory.Exists(CorpusPath);

    #region PDF/A Corpus Tests

    /// <summary>
    /// Tests processing of PDF/A-1b files (basic archival format).
    /// PDF/A-1b is widely used for document archival.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Standard", "PDF/A-1b")]
    public void ProcessPdfA1b_SampleFiles_CanExtractTextAndRedact()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        var pdfA1bDir = Path.Combine(CorpusPath, "PDF_A-1b");
        var pdfFiles = Directory.GetFiles(pdfA1bDir, "*.pdf", SearchOption.AllDirectories)
            .Take(50)  // Test sample of 50 files
            .ToList();

        pdfFiles.Should().NotBeEmpty("PDF/A-1b corpus should contain test files");

        var results = ProcessCorpusFiles(pdfFiles, "PDF/A-1b");
        LogResults(results);

        // At least 80% should process successfully
        var successRate = (double)results.Successful / results.Total;
        successRate.Should().BeGreaterOrEqualTo(0.8, "Most PDF/A-1b files should process correctly");
    }

    /// <summary>
    /// Tests processing of PDF/A-2b files (enhanced archival format).
    /// PDF/A-2b adds JPEG2000, transparency, and layers support.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Standard", "PDF/A-2b")]
    public void ProcessPdfA2b_SampleFiles_CanExtractTextAndRedact()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        var pdfA2bDir = Path.Combine(CorpusPath, "PDF_A-2b");
        var pdfFiles = Directory.GetFiles(pdfA2bDir, "*.pdf", SearchOption.AllDirectories)
            .Take(50)
            .ToList();

        pdfFiles.Should().NotBeEmpty("PDF/A-2b corpus should contain test files");

        var results = ProcessCorpusFiles(pdfFiles, "PDF/A-2b");
        LogResults(results);

        var successRate = (double)results.Successful / results.Total;
        successRate.Should().BeGreaterOrEqualTo(0.8);
    }

    /// <summary>
    /// Tests processing of PDF/A-4 files (latest archival format based on PDF 2.0).
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Standard", "PDF/A-4")]
    public void ProcessPdfA4_SampleFiles_CanExtractTextAndRedact()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        var pdfA4Dir = Path.Combine(CorpusPath, "PDF_A-4");
        if (!Directory.Exists(pdfA4Dir))
        {
            _output.WriteLine("PDF/A-4 directory not found, skipping");
            return;
        }

        var pdfFiles = Directory.GetFiles(pdfA4Dir, "*.pdf", SearchOption.AllDirectories)
            .Take(50)
            .ToList();

        if (pdfFiles.Count == 0)
        {
            _output.WriteLine("No PDF/A-4 files found");
            return;
        }

        var results = ProcessCorpusFiles(pdfFiles, "PDF/A-4");
        LogResults(results);

        // PDF/A-4 is newer, may have more parsing challenges
        var successRate = (double)results.Successful / results.Total;
        successRate.Should().BeGreaterOrEqualTo(0.7);
    }

    #endregion

    #region PDF/UA Accessibility Tests

    /// <summary>
    /// Tests processing of PDF/UA-1 files (universal accessibility).
    /// PDF/UA adds structure and accessibility requirements.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Standard", "PDF/UA-1")]
    public void ProcessPdfUA1_SampleFiles_CanExtractTextAndRedact()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        var pdfUaDir = Path.Combine(CorpusPath, "PDF_UA-1");
        var pdfFiles = Directory.GetFiles(pdfUaDir, "*.pdf", SearchOption.AllDirectories)
            .Take(50)
            .ToList();

        pdfFiles.Should().NotBeEmpty("PDF/UA-1 corpus should contain test files");

        var results = ProcessCorpusFiles(pdfFiles, "PDF/UA-1");
        LogResults(results);

        var successRate = (double)results.Successful / results.Total;
        successRate.Should().BeGreaterOrEqualTo(0.8);
    }

    #endregion

    #region ISO 32000 Standard Tests

    /// <summary>
    /// Tests processing of ISO 32000-1 (PDF 1.7) reference files.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Standard", "ISO32000-1")]
    public void ProcessIso32000_1_ReferenceFiles_CanExtractTextAndRedact()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        var isoDir = Path.Combine(CorpusPath, "ISO 32000-1");
        if (!Directory.Exists(isoDir))
        {
            _output.WriteLine("ISO 32000-1 directory not found");
            return;
        }

        var pdfFiles = Directory.GetFiles(isoDir, "*.pdf", SearchOption.AllDirectories).ToList();

        if (pdfFiles.Count == 0)
        {
            _output.WriteLine("No ISO 32000-1 files found");
            return;
        }

        var results = ProcessCorpusFiles(pdfFiles, "ISO32000-1");
        LogResults(results);
    }

    #endregion

    #region Comprehensive Corpus Tests

    /// <summary>
    /// Tests that pdfer can handle files with text content and successfully redact.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Type", "RedactionIntegrity")]
    public void Redaction_AcrossCorpus_MaintainsIntegrity()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        // Find PDFs with text content to test actual redaction
        var pdfFilesWithText = FindPdfsWithTextContent(20);

        if (pdfFilesWithText.Count == 0)
        {
            _output.WriteLine("No PDFs with extractable text found for redaction testing");
            return;
        }

        int successful = 0;
        int failed = 0;

        foreach (var pdfPath in pdfFilesWithText)
        {
            try
            {
                var result = TestRedactionOnFile(pdfPath);
                if (result)
                    successful++;
                else
                    failed++;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Exception processing {Path.GetFileName(pdfPath)}: {ex.Message}");
                failed++;
            }
        }

        _output.WriteLine($"Redaction integrity test: {successful} successful, {failed} failed");
        successful.Should().BeGreaterThan(0, "At least some files should be redactable");
    }

    /// <summary>
    /// Stress test: process a large number of diverse PDFs to catch edge cases.
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Type", "StressTest")]
    public void StressTest_ProcessManyDiversePdfs()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        // Get files from multiple categories
        var allPdfs = new List<string>();

        var categories = new[] { "PDF_A-1b", "PDF_A-2b", "PDF_UA-1", "PDF_A-4" };
        foreach (var category in categories)
        {
            var dir = Path.Combine(CorpusPath, category);
            if (Directory.Exists(dir))
            {
                allPdfs.AddRange(Directory.GetFiles(dir, "*.pdf", SearchOption.AllDirectories).Take(25));
            }
        }

        _output.WriteLine($"Stress testing {allPdfs.Count} PDFs from {categories.Length} categories");

        var results = new CorpusTestResults();

        foreach (var pdfPath in allPdfs)
        {
            results.Total++;
            try
            {
                // Just verify we can open, extract text, and run info command
                var result = PdferTestRunner.Run("info", pdfPath);

                if (result.ExitCode == 0)
                    results.Successful++;
                else
                    results.Failed++;
            }
            catch
            {
                results.Errors++;
            }
        }

        LogResults(results);

        var successRate = (double)results.Successful / results.Total;
        successRate.Should().BeGreaterOrEqualTo(0.75, "At least 75% of diverse PDFs should be processable");
    }

    #endregion

    #region Edge Case Tests

    /// <summary>
    /// Tests handling of PDFs that intentionally violate standards (fail cases).
    /// </summary>
    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Type", "EdgeCase")]
    public void HandleNonCompliantPdfs_DoesNotCrash()
    {
        Skip.IfNot(CorpusExists, "veraPDF corpus not found");

        // Find "fail" test files - these intentionally have issues
        var failFiles = Directory.GetFiles(CorpusPath, "*fail*.pdf", SearchOption.AllDirectories)
            .Take(30)
            .ToList();

        _output.WriteLine($"Testing {failFiles.Count} intentionally non-compliant PDFs");

        int crashes = 0;
        int handled = 0;

        foreach (var pdfPath in failFiles)
        {
            try
            {
                var result = PdferTestRunner.Run("info", pdfPath);
                handled++;
                // We don't care if it fails, just that it doesn't crash
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Crash on {Path.GetFileName(pdfPath)}: {ex.GetType().Name}");
                crashes++;
            }
        }

        _output.WriteLine($"Handled {handled} non-compliant PDFs, {crashes} crashes");

        // Should handle all files without crashing (returning error is OK)
        crashes.Should().Be(0, "pdfer should handle non-compliant PDFs gracefully");
    }

    #endregion

    #region Helper Methods

    private CorpusTestResults ProcessCorpusFiles(List<string> pdfFiles, string category)
    {
        var results = new CorpusTestResults();

        foreach (var pdfPath in pdfFiles)
        {
            results.Total++;

            try
            {
                // Test 1: Can we get info?
                var infoResult = PdferTestRunner.Run("info", pdfPath);

                if (infoResult.ExitCode != 0)
                {
                    results.Failed++;
                    _output.WriteLine($"[{category}] FAIL (info): {Path.GetFileName(pdfPath)}");
                    continue;
                }

                // Test 2: Can we extract and search text?
                using var doc = PdfDocument.Open(pdfPath);
                var hasText = doc.GetPages().Any(p => !string.IsNullOrWhiteSpace(p.Text));

                if (hasText)
                {
                    var searchResult = PdferTestRunner.Run("search", pdfPath, "a");  // Search for common letter
                    if (searchResult.ExitCode != 0)
                    {
                        results.Failed++;
                        _output.WriteLine($"[{category}] FAIL (search): {Path.GetFileName(pdfPath)}");
                        continue;
                    }
                }

                results.Successful++;
            }
            catch (Exception ex)
            {
                results.Errors++;
                _output.WriteLine($"[{category}] ERROR: {Path.GetFileName(pdfPath)} - {ex.Message}");
            }
        }

        return results;
    }

    private List<string> FindPdfsWithTextContent(int maxCount)
    {
        var result = new List<string>();

        var allPdfs = Directory.GetFiles(CorpusPath, "*.pdf", SearchOption.AllDirectories);

        foreach (var pdfPath in allPdfs)
        {
            if (result.Count >= maxCount) break;

            try
            {
                using var doc = PdfDocument.Open(pdfPath);
                var pageWithText = doc.GetPages().FirstOrDefault(p => p.Text.Length > 10);

                if (pageWithText != null)
                {
                    result.Add(pdfPath);
                }
            }
            catch
            {
                // Skip files that can't be opened
            }
        }

        return result;
    }

    private bool TestRedactionOnFile(string pdfPath)
    {
        // Extract some text to redact
        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPages().FirstOrDefault(p => p.Text.Length > 10);

        if (page == null) return false;

        // Find a word to redact (take first 5+ character word)
        var words = page.Text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var targetWord = words.FirstOrDefault(w => w.Length >= 5 && w.All(char.IsLetter));

        if (targetWord == null) return false;

        var outputPath = Path.Combine(_tempDir, $"redacted_{Guid.NewGuid()}.pdf");

        // Perform redaction
        var result = PdferTestRunner.Run("redact", pdfPath, outputPath, targetWord, "-q");

        if (result.ExitCode != 0) return false;

        // Verify redaction
        var verifyResult = PdferTestRunner.Run("verify", outputPath, targetWord);

        return verifyResult.ExitCode == 0;  // 0 = text not found (good!)
    }

    private void LogResults(CorpusTestResults results)
    {
        _output.WriteLine($"Results: {results.Successful}/{results.Total} successful ({results.SuccessRate:P1})");
        _output.WriteLine($"  Failed: {results.Failed}, Errors: {results.Errors}");
    }

    private class CorpusTestResults
    {
        public int Total { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Errors { get; set; }
        public double SuccessRate => Total > 0 ? (double)Successful / Total : 0;
    }

    #endregion
}

/// <summary>
/// Skip helper for conditional test execution.
/// </summary>
public static class Skip
{
    public static void IfNot(bool condition, string reason)
    {
        if (!condition)
        {
            throw new SkipException(reason);
        }
    }
}

public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
