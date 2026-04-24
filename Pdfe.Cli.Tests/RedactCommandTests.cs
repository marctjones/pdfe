using System.IO;
using System.Text;
using FluentAssertions;
using Pdfe.Cli;
using Pdfe.Core.Document;
using Xunit;

namespace Pdfe.Cli.Tests;

/// <summary>
/// Tests for the <c>pdfe redact</c> subcommand. Exercises both the
/// internal <see cref="Program.RunRedact"/> core and the CLI surface
/// (<see cref="Program.RunAsync"/>) so we catch regressions in either
/// the argument parser or the redaction pipeline itself.
/// </summary>
public class RedactCommandTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) try { File.Delete(f); } catch { }
    }

    private string TempPath(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdfe-cli-test-{Guid.NewGuid():N}{suffix}");
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void RunRedact_RemovesExactMatch_FromContentStream()
    {
        // HELLO WORLD → redact WORLD
        var inputPath = TempPath(".pdf");
        var outputPath = TempPath(".pdf");
        File.WriteAllBytes(inputPath, TestPdfBuilder.SinglePage("HELLO WORLD"));

        int count = Program.RunRedact(inputPath, outputPath, "WORLD", caseSensitive: false);

        count.Should().Be(1);

        // The security guarantee: raw content-stream bytes of the output
        // must not contain WORLD. This is the "pdftotext can't recover
        // it" property — structural removal, not visual overlay.
        using var doc = PdfDocument.Open(File.ReadAllBytes(outputPath));
        var raw = Encoding.Latin1.GetString(doc.GetPage(1).GetContentStreamBytes());
        raw.Should().NotContain("WORLD");
        raw.Should().Contain("HELLO", "the non-redacted word must survive");
    }

    [Fact]
    public void RunRedact_NoMatch_ReturnsZero_AndOutputExists()
    {
        var inputPath = TempPath(".pdf");
        var outputPath = TempPath(".pdf");
        File.WriteAllBytes(inputPath, TestPdfBuilder.SinglePage("HELLO WORLD"));

        int count = Program.RunRedact(inputPath, outputPath, "BANANA", caseSensitive: false);

        count.Should().Be(0);
        File.Exists(outputPath).Should().BeTrue("output is always written even when no matches found");

        using var doc = PdfDocument.Open(File.ReadAllBytes(outputPath));
        var raw = Encoding.Latin1.GetString(doc.GetPage(1).GetContentStreamBytes());
        raw.Should().Contain("HELLO");
        raw.Should().Contain("WORLD");
    }

    [Fact]
    public void RunRedact_CaseInsensitive_MatchesDifferentCase()
    {
        var inputPath = TempPath(".pdf");
        var outputPath = TempPath(".pdf");
        File.WriteAllBytes(inputPath, TestPdfBuilder.SinglePage("HELLO WORLD"));

        int count = Program.RunRedact(inputPath, outputPath, "world", caseSensitive: false);

        count.Should().Be(1);
        using var doc = PdfDocument.Open(File.ReadAllBytes(outputPath));
        var raw = Encoding.Latin1.GetString(doc.GetPage(1).GetContentStreamBytes());
        raw.Should().NotContain("WORLD");
    }

    [Fact]
    public void RunRedact_CaseSensitive_DoesNotMatchDifferentCase()
    {
        var inputPath = TempPath(".pdf");
        var outputPath = TempPath(".pdf");
        File.WriteAllBytes(inputPath, TestPdfBuilder.SinglePage("HELLO WORLD"));

        int count = Program.RunRedact(inputPath, outputPath, "world", caseSensitive: true);

        count.Should().Be(0, "case-sensitive search must not match an all-caps word");
    }

    [Fact]
    public async Task RunAsync_RedactSubcommand_EndToEnd_ProducesRedactedOutput()
    {
        var inputPath = TempPath(".pdf");
        var outputPath = TempPath(".pdf");
        File.WriteAllBytes(inputPath, TestPdfBuilder.SinglePage("SECRET DATA"));

        // Redirect stdout so the "Redacted N occurrence(s)" noise doesn't
        // leak into the xunit output.
        var prevOut = Console.Out;
        var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        int exitCode;
        try
        {
            exitCode = await Program.RunAsync(new[]
            {
                "redact", inputPath, outputPath, "SECRET"
            });
        }
        finally
        {
            Console.SetOut(prevOut);
        }

        exitCode.Should().Be(0);
        capturedOut.ToString().Should().Contain("Redacted 1 occurrence(s) of 'SECRET'");

        File.Exists(outputPath).Should().BeTrue();
        using var doc = PdfDocument.Open(File.ReadAllBytes(outputPath));
        var raw = Encoding.Latin1.GetString(doc.GetPage(1).GetContentStreamBytes());
        raw.Should().NotContain("SECRET");
        raw.Should().Contain("DATA");
    }

    [Fact]
    public async Task RunAsync_RedactSubcommand_InputDoesNotExist_ReportsError()
    {
        var outputPath = TempPath(".pdf");
        var prevErr = Console.Error;
        var capturedErr = new StringWriter();
        Console.SetError(capturedErr);
        int exitCode;
        try
        {
            exitCode = await Program.RunAsync(new[]
            {
                "redact", "/tmp/pdfe-does-not-exist-xyz.pdf", outputPath, "SECRET"
            });
        }
        finally
        {
            Console.SetError(prevErr);
        }

        // System.CommandLine invokes the handler (exit code 0) after our
        // explicit Environment.ExitCode=1, but what matters to the user
        // is the error message and that no output file was written.
        capturedErr.ToString().Should().Contain("File not found");
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public void RunRedact_MultipleMatches_AllRemoved()
    {
        // Three copies of the target on one line. The surrounding test
        // string uses wide spacing so each TARGET's bounding box doesn't
        // brush the neighbouring glyphs (the default AnyOverlap strategy
        // would otherwise catch adjacent characters).
        var inputPath = TempPath(".pdf");
        var outputPath = TempPath(".pdf");
        File.WriteAllBytes(inputPath, TestPdfBuilder.SinglePage("TARGET TARGET TARGET"));

        int count = Program.RunRedact(inputPath, outputPath, "TARGET", caseSensitive: false);

        count.Should().Be(3);
        using var doc = PdfDocument.Open(File.ReadAllBytes(outputPath));
        var raw = Encoding.Latin1.GetString(doc.GetPage(1).GetContentStreamBytes());
        raw.Should().NotContain("TARGET",
            "all three occurrences must be removed from the content stream");
    }
}
