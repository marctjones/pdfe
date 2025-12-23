using FluentAssertions;
using PdfEditor.Redaction.Cli.Tests.TestHelpers;
using Xunit;

namespace PdfEditor.Redaction.Cli.Tests.Unit;

/// <summary>
/// Tests for the search command functionality.
/// </summary>
public class SearchCommandTests : IDisposable
{
    private readonly string _tempDir;

    public SearchCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdfer_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region Literal Search

    [Fact]
    public void Search_FindsExactMatch()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSensitiveDataPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "123-45-6789");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Found 1 occurrence");
        result.Stdout.Should().Contain("Page 1");
    }

    [Fact]
    public void Search_ReturnsNoMatchesWhenNotFound()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("search", pdfPath, "NotInDocument");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("No occurrences");
    }

    [Fact]
    public void Search_CaseInsensitive_FindsAllCases()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateCaseSensitivePdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "confidential", "-i");

        result.ExitCode.Should().Be(0);
        // Should find multiple occurrences of different cases
        result.Stdout.Should().Contain("occurrence");
    }

    [Fact]
    public void Search_ShowsContext()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSensitiveDataPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "SSN", "--context");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("...");  // Context markers
    }

    #endregion

    #region Regex Search

    [Fact]
    public void Search_Regex_FindsSsnPatterns()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreatePatternTestPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"\d{3}-\d{2}-\d{4}");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Found 3 occurrence");  // 3 SSN patterns
        result.Stdout.Should().Contain("123-45-6789");
        result.Stdout.Should().Contain("987-65-4321");
    }

    [Fact]
    public void Search_Regex_FindsEmailPatterns()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreatePatternTestPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("occurrence");
    }

    [Fact]
    public void Search_Regex_FindsPhonePatterns()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreatePatternTestPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"\(\d{3}\)\s*\d{3}-\d{4}");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("occurrence");
    }

    [Fact]
    public void Search_Regex_FindsDatePatterns()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreatePatternTestPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"\d{4}-\d{2}-\d{2}");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Found 2 occurrence");  // 2 dates
        result.Stdout.Should().Contain("2024-01-15");
    }

    [Fact]
    public void Search_Regex_CaseInsensitive()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateCaseSensitivePdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", "CONFIDENTIAL", "-i");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("occurrence");
    }

    [Fact]
    public void Search_Regex_WithContext()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreatePatternTestPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"\d{3}-\d{2}-\d{4}", "--context");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("...");
    }

    #endregion

    #region JSON Output

    [Fact]
    public void Search_JsonOutput_ReturnsValidJson()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSensitiveDataPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "SSN", "--json");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("\"file\":");
        result.Stdout.Should().Contain("\"total\":");
        result.Stdout.Should().Contain("\"results\":");
    }

    [Fact]
    public void Search_Regex_JsonOutput_IncludesMatchedText()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreatePatternTestPdf(pdfPath);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"\d{3}-\d{2}-\d{4}", "--json");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("\"MatchedText\":");
        result.Stdout.Should().Contain("\"regex\":");
    }

    #endregion

    #region Multi-Page Search

    [Fact]
    public void Search_MultiPage_FindsOnCorrectPages()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateMultiPagePdf(pdfPath, 3);

        var result = PdferTestRunner.Run("search", pdfPath, "SECRET-2");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Page 2");
        result.Stdout.Should().Contain("Found 1 occurrence");
    }

    [Fact]
    public void Search_MultiPage_Regex_FindsAcrossAllPages()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateMultiPagePdf(pdfPath, 3);

        var result = PdferTestRunner.Run("search", pdfPath, "-r", @"SECRET-\d");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Found 3 occurrence");  // One per page
    }

    #endregion
}
