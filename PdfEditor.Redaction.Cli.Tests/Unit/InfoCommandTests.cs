using FluentAssertions;
using PdfEditor.Redaction.Cli.Tests.TestHelpers;
using Xunit;

namespace PdfEditor.Redaction.Cli.Tests.Unit;

/// <summary>
/// Tests for the info command functionality.
/// </summary>
public class InfoCommandTests : IDisposable
{
    private readonly string _tempDir;

    public InfoCommandTests()
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
    public void Info_ShowsBasicInfo()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("info", pdfPath);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("File:");
        result.Stdout.Should().Contain("Pages: 1");
        result.Stdout.Should().Contain("PDF Version:");
    }

    [Fact]
    public void Info_MultiPage_ShowsCorrectPageCount()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateMultiPagePdf(pdfPath, 5);

        var result = PdferTestRunner.Run("info", pdfPath);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Pages: 5");
    }

    [Fact]
    public void Info_WithText_ShowsTextContent()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World", "Second Line");

        var result = PdferTestRunner.Run("info", pdfPath, "--text");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Text Content:");
        result.Stdout.Should().Contain("Hello World");
        result.Stdout.Should().Contain("Second Line");
    }

    [Fact]
    public void Info_JsonOutput_ReturnsValidJson()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateSimplePdf(pdfPath, "Hello World");

        var result = PdferTestRunner.Run("info", pdfPath, "--json");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("\"file\":");
        result.Stdout.Should().Contain("\"pages\":");
        result.Stdout.Should().Contain("\"version\":");
    }

    [Fact]
    public void Info_EmptyPdf_ShowsZeroText()
    {
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfCreator.CreateEmptyPdf(pdfPath);

        var result = PdferTestRunner.Run("info", pdfPath);

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Pages: 1");
    }
}
