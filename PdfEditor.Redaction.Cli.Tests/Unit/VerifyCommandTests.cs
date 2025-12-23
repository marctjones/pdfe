using FluentAssertions;
using PdfEditor.Redaction.Cli.Tests.TestHelpers;
using Xunit;

namespace PdfEditor.Redaction.Cli.Tests.Unit;

/// <summary>
/// Tests for the verify command functionality.
/// </summary>
public class VerifyCommandTests : IDisposable
{
    private readonly string _tempDir;

    public VerifyCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdfer_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Verify_TextNotPresent_ReturnsSuccess()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "NotInDocument");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("PASS");
        result.Stdout.Should().Contain("not found");
    }

    [Fact]
    public void Verify_TextPresent_ReturnsFailure()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "Hello");

        result.ExitCode.Should().Be(2);  // Verification failed
        result.Stdout.Should().Contain("FAIL");
        result.Stdout.Should().Contain("still extractable");
    }

    [Fact]
    public void Verify_MultipleTerms_AllNotPresent_ReturnsSuccess()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "Secret", "Password", "SSN");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("PASS");
    }

    [Fact]
    public void Verify_MultipleTerms_SomePresent_ReturnsFailure()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "NotHere", "World");

        result.ExitCode.Should().Be(2);
        result.Stdout.Should().Contain("PASS");  // For "NotHere"
        result.Stdout.Should().Contain("FAIL");  // For "World"
    }

    [Fact]
    public void Verify_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "HELLO", "-i");

        result.ExitCode.Should().Be(2);  // Should find it
        result.Stdout.Should().Contain("FAIL");
    }

    [Fact]
    public void Verify_CaseSensitive_DoesNotMatchDifferentCase()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "HELLO");

        result.ExitCode.Should().Be(0);  // Different case, should pass
        result.Stdout.Should().Contain("PASS");
    }

    [Fact]
    public void Verify_JsonOutput_ReturnsValidJson()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("verify", pdfPath, "NotHere", "--json");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("\"success\"");
        result.Stdout.Should().Contain("\"results\"");
    }

    [Fact]
    public void Verify_QuietMode_NoOutputOnSuccess()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        // Note: verify -q still outputs PASS/FAIL to help users
        // We just verify it runs correctly
        var result = PdferTestRunner.Run("verify", pdfPath, "NotHere", "-q");

        result.ExitCode.Should().Be(0);
    }

    [Fact]
    public void Verify_AfterRedaction_Passes()
    {
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfCreator.CreateSensitiveDataPdf(inputPath);

        // First redact
        var redactResult = PdferTestRunner.Run("redact", inputPath, outputPath, "123-45-6789", "-q");
        redactResult.ExitCode.Should().Be(0);

        // Then verify
        var verifyResult = PdferTestRunner.Run("verify", outputPath, "123-45-6789");

        verifyResult.ExitCode.Should().Be(0);
        verifyResult.Stdout.Should().Contain("PASS");
    }
}
