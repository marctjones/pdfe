using FluentAssertions;
using PdfEditor.Redaction.Cli.Tests.TestHelpers;
using Xunit;

namespace PdfEditor.Redaction.Cli.Tests.Unit;

/// <summary>
/// Tests for CLI argument parsing and command routing.
/// </summary>
public class CommandParsingTests : IDisposable
{
    private readonly string _tempDir;

    public CommandParsingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdfer_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region Help and Version

    [Fact]
    public void NoArgs_ShowsUsage()
    {
        var result = PdferTestRunner.Run();

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("pdfer");
        result.Stdout.Should().Contain("USAGE:");
        result.Stdout.Should().Contain("COMMANDS:");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_ShowsUsage(string flag)
    {
        var result = PdferTestRunner.Run(flag);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("USAGE:");
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void Version_ShowsVersion(string flag)
    {
        var result = PdferTestRunner.Run(flag);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().MatchRegex(@"pdfer \d+\.\d+\.\d+");
    }

    [Fact]
    public void UnknownCommand_ReturnsError()
    {
        var result = PdferTestRunner.Run("unknowncommand");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Unknown command");
    }

    #endregion

    #region Redact Command Parsing

    [Fact]
    public void Redact_Help_ShowsRedactHelp()
    {
        var result = PdferTestRunner.Run("redact", "--help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("pdfer redact");
        result.Stdout.Should().Contain("OPTIONS:");
        result.Stdout.Should().Contain("--case-insensitive");
    }

    [Fact]
    public void Redact_NoArgs_ShowsError()
    {
        var result = PdferTestRunner.Run("redact");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Input file is required");
    }

    [Fact]
    public void Redact_NoOutput_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("redact", inputPath);

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Output file is required");
    }

    [Fact]
    public void Redact_NoTerms_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("redact", inputPath, outputPath);

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("At least one search term");
    }

    [Fact]
    public void Redact_FileNotFound_ShowsError()
    {
        var outputPath = Path.Combine(_tempDir, "output.pdf");

        var result = PdferTestRunner.Run("redact", "/nonexistent/file.pdf", outputPath, "term");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("not found");
    }

    [Fact]
    public void Redact_UnknownOption_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("redact", inputPath, outputPath, "--unknown-flag");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Unknown option");
    }

    [Fact]
    public void Redact_TermsFileNotFound_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("redact", inputPath, outputPath, "-f", "/nonexistent/terms.txt");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Terms file not found");
    }

    #endregion

    #region Verify Command Parsing

    [Fact]
    public void Verify_Help_ShowsVerifyHelp()
    {
        var result = PdferTestRunner.Run("verify", "--help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("pdfer verify");
    }

    [Fact]
    public void Verify_NoArgs_ShowsError()
    {
        var result = PdferTestRunner.Run("verify");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("PDF file is required");
    }

    [Fact]
    public void Verify_NoTerms_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("verify", inputPath);

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("At least one search term");
    }

    #endregion

    #region Search Command Parsing

    [Fact]
    public void Search_Help_ShowsSearchHelp()
    {
        var result = PdferTestRunner.Run("search", "--help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("pdfer search");
        result.Stdout.Should().Contain("--regex");
    }

    [Fact]
    public void Search_NoArgs_ShowsError()
    {
        var result = PdferTestRunner.Run("search");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("PDF file is required");
    }

    [Fact]
    public void Search_NoTerm_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("search", inputPath);

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Search term or --regex pattern is required");
    }

    [Fact]
    public void Search_RegexWithoutPattern_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("search", inputPath, "--regex");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("requires a pattern");
    }

    [Fact]
    public void Search_InvalidRegex_ShowsError()
    {
        var inputPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(inputPath, "Test");

        var result = PdferTestRunner.Run("search", inputPath, "-r", "[invalid(regex");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("Invalid regex");
    }

    #endregion

    #region Info Command Parsing

    [Fact]
    public void Info_Help_ShowsInfoHelp()
    {
        var result = PdferTestRunner.Run("info", "--help");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("pdfer info");
    }

    [Fact]
    public void Info_NoArgs_ShowsError()
    {
        var result = PdferTestRunner.Run("info");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("PDF file is required");
    }

    #endregion
}
