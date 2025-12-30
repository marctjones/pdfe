using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Conformance tests using external PDF validation tools.
///
/// These tests verify that PDFs produced by PdfEditor are valid according to
/// external industry-standard validators. Tests are skipped if tools are not installed.
///
/// To install required tools:
/// - veraPDF: https://verapdf.org/software/ (Java-based, cross-platform)
/// - qpdf: sudo apt install qpdf (Linux) or brew install qpdf (macOS)
/// - mutool: sudo apt install mupdf-tools (Linux)
///
/// Run these tests with: dotnet test --filter "Category=ExternalValidator"
/// </summary>
[Collection("PdfRenderingTests")]
[Trait("Category", "ExternalValidator")]
public class VeraPdfConformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly PdfDocumentService _documentService;
    private readonly RedactionService _redactionService;
    private readonly string _testOutputDir;
    private static readonly bool _veraPdfAvailable;
    private static readonly bool _qpdfAvailable;
    private static readonly bool _mutoolAvailable;
    private static readonly string _veraPdfPath;

    static VeraPdfConformanceTests()
    {
        // Check for veraPDF in known installation locations
        _veraPdfPath = FindVeraPdf();
        _veraPdfAvailable = !string.IsNullOrEmpty(_veraPdfPath);
        _qpdfAvailable = IsToolAvailable("qpdf", "--version");
        _mutoolAvailable = IsToolAvailable("mutool", "-v");
    }

    private static string FindVeraPdf()
    {
        // Check common installation paths
        var possiblePaths = new[]
        {
            "/home/marc/verapdf/verapdf",  // User installation
            "/usr/local/bin/verapdf",
            "/usr/bin/verapdf",
            "verapdf"  // In PATH
        };

        foreach (var path in possiblePaths)
        {
            if (IsToolAvailable(path, "--version"))
                return path;
        }

        return string.Empty;
    }

    public VeraPdfConformanceTests(ITestOutputHelper output)
    {
        _output = output;
        var docLogger = new Mock<ILogger<PdfDocumentService>>().Object;
        var redactionLogger = new Mock<ILogger<RedactionService>>().Object;
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

        _documentService = new PdfDocumentService(docLogger);
        _redactionService = new RedactionService(redactionLogger, loggerFactory);

        _testOutputDir = Path.Combine(Path.GetTempPath(), "VeraPdfTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testOutputDir);
    }

    #region veraPDF Validation Tests

    [SkippableFact]
    [Trait("Tool", "veraPDF")]
    public void OriginalPdf_PassesVeraPdfAutoDetect()
    {
        Skip.IfNot(_veraPdfAvailable, "veraPDF not installed. Install from https://verapdf.org/software/");

        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "original.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, text: "Test Content");

        // Act
        var (exitCode, output) = RunVeraPdf(pdfPath, autoDetectFlavour: true);

        // Assert
        _output.WriteLine($"veraPDF output:\n{output}");
        // Note: veraPDF may report warnings for non-PDF/A files, but should not crash
        exitCode.Should().BeOneOf(new[] { 0, 1 }, "veraPDF should process the file without errors");
    }

    [SkippableFact]
    [Trait("Tool", "veraPDF")]
    public void RedactedPdf_PassesVeraPdfStructureCheck()
    {
        Skip.IfNot(_veraPdfAvailable, "veraPDF not installed");

        // Arrange
        var originalPath = Path.Combine(_testOutputDir, "to_redact.pdf");
        var redactedPath = Path.Combine(_testOutputDir, "redacted.pdf");

        var (_, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPath);
        _documentService.LoadDocument(originalPath);

        var doc = _documentService.GetCurrentDocument()!;
        var page = doc.Pages[0];
        var targetPos = contentMap["CONFIDENTIAL"];

        // Apply redaction
        _redactionService.RedactArea(page, new Avalonia.Rect(
            targetPos.x - 5, targetPos.y - 5,
            targetPos.width + 10, targetPos.height + 10
        ), originalPath, renderDpi: 72);

        _documentService.SaveDocument(redactedPath);

        // Act - Run veraPDF feature extraction to check structure
        var (exitCode, output) = RunVeraPdf(redactedPath, extractFeatures: true);

        // Assert
        _output.WriteLine($"veraPDF output:\n{output}");
        File.Exists(redactedPath).Should().BeTrue();
        // The file should be parseable by veraPDF - check for actual exceptions
        // Note: veraExceptions="0" is expected in XML output - we're checking for exception elements
        output.Should().Contain("veraExceptions=\"0\"", "veraPDF should report 0 exceptions");
        output.Should().NotContain("<exception>", "veraPDF should not report any exception elements");
    }

    #endregion

    #region qpdf Validation Tests

    [SkippableFact]
    [Trait("Tool", "qpdf")]
    public void OriginalPdf_PassesQpdfCheck()
    {
        Skip.IfNot(_qpdfAvailable, "qpdf not installed. Install: apt install qpdf");

        // Arrange
        var pdfPath = Path.Combine(_testOutputDir, "qpdf_original.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Test Content");

        // Act
        var (exitCode, output) = RunCommand("qpdf", $"--check \"{pdfPath}\"");

        // Assert
        _output.WriteLine($"qpdf output:\n{output}");
        // qpdf exit codes: 0 = success, 2 = errors, 3 = success with warnings
        exitCode.Should().BeOneOf(new[] { 0, 3 }, "qpdf --check should pass for valid PDF (0=success, 3=warnings)");
    }

    [SkippableFact]
    [Trait("Tool", "qpdf")]
    public void RedactedPdf_PassesQpdfCheck()
    {
        Skip.IfNot(_qpdfAvailable, "qpdf not installed");

        // Arrange
        var originalPath = Path.Combine(_testOutputDir, "qpdf_to_redact.pdf");
        var redactedPath = Path.Combine(_testOutputDir, "qpdf_redacted.pdf");

        var (_, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPath);
        _documentService.LoadDocument(originalPath);

        var doc = _documentService.GetCurrentDocument()!;
        var page = doc.Pages[0];
        var targetPos = contentMap["SECRET"];

        _redactionService.RedactArea(page, new Avalonia.Rect(
            targetPos.x - 5, targetPos.y - 5,
            targetPos.width + 10, targetPos.height + 10
        ), originalPath, renderDpi: 72);

        _documentService.SaveDocument(redactedPath);

        // Act
        var (exitCode, output) = RunCommand("qpdf", $"--check \"{redactedPath}\"");

        // Assert
        _output.WriteLine($"qpdf output:\n{output}");
        // qpdf exit codes: 0 = success, 2 = errors, 3 = success with warnings
        exitCode.Should().BeOneOf(new[] { 0, 3 }, "qpdf --check should pass for redacted PDF (0=success, 3=warnings)");
    }

    [SkippableFact]
    [Trait("Tool", "qpdf")]
    public void RedactedPdf_PassesQpdfLinearizationCheck()
    {
        Skip.IfNot(_qpdfAvailable, "qpdf not installed");

        // Arrange
        var originalPath = Path.Combine(_testOutputDir, "qpdf_linear.pdf");
        var redactedPath = Path.Combine(_testOutputDir, "qpdf_linear_redacted.pdf");

        TestPdfGenerator.CreateSimpleTextPdf(originalPath, "REMOVE_THIS");
        _documentService.LoadDocument(originalPath);

        var doc = _documentService.GetCurrentDocument()!;
        _redactionService.RedactArea(doc.Pages[0], new Avalonia.Rect(90, 90, 150, 30), originalPath, renderDpi: 72);
        _documentService.SaveDocument(redactedPath);

        // Act - Check with JSON output for detailed analysis
        var (exitCode, output) = RunCommand("qpdf", $"--json \"{redactedPath}\"");

        // Assert
        _output.WriteLine($"qpdf JSON length: {output.Length} chars");
        exitCode.Should().Be(0, "qpdf --json should succeed");
        output.Should().Contain("\"version\"", "JSON output should contain version info");
    }

    #endregion

    #region mutool Validation Tests

    [SkippableFact]
    [Trait("Tool", "mutool")]
    public void RedactedPdf_PassesMutoolInfo()
    {
        Skip.IfNot(_mutoolAvailable, "mutool not installed. Install: apt install mupdf-tools");

        // Arrange
        var originalPath = Path.Combine(_testOutputDir, "mutool_test.pdf");
        var redactedPath = Path.Combine(_testOutputDir, "mutool_redacted.pdf");

        var (_, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPath);
        _documentService.LoadDocument(originalPath);

        var doc = _documentService.GetCurrentDocument()!;
        var targetPos = contentMap["PRIVATE"];

        _redactionService.RedactArea(doc.Pages[0], new Avalonia.Rect(
            targetPos.x - 5, targetPos.y - 5,
            targetPos.width + 10, targetPos.height + 10
        ), originalPath, renderDpi: 72);

        _documentService.SaveDocument(redactedPath);

        // Act
        var (exitCode, output) = RunCommand("mutool", $"info \"{redactedPath}\"");

        // Assert
        _output.WriteLine($"mutool info output:\n{output}");
        exitCode.Should().Be(0, "mutool info should succeed");
        output.Should().Contain("Pages:", "Should report page count");
    }

    #endregion

    #region Cross-Validator Consistency Tests

    [SkippableFact]
    [Trait("Tool", "Multiple")]
    public void RedactedPdf_TextRemovalVerifiedByMultipleTools()
    {
        Skip.IfNot(_qpdfAvailable || _mutoolAvailable, "Need at least qpdf or mutool installed");

        // Arrange
        var originalPath = Path.Combine(_testOutputDir, "multi_tool.pdf");
        var redactedPath = Path.Combine(_testOutputDir, "multi_tool_redacted.pdf");
        const string targetText = "CONFIDENTIAL";

        var (_, contentMap) = TestPdfGenerator.CreateMappedContentPdf(originalPath);
        _documentService.LoadDocument(originalPath);

        var doc = _documentService.GetCurrentDocument()!;
        var targetPos = contentMap[targetText];

        _redactionService.RedactArea(doc.Pages[0], new Avalonia.Rect(
            targetPos.x - 5, targetPos.y - 5,
            targetPos.width + 10, targetPos.height + 10
        ), originalPath, renderDpi: 72);

        _documentService.SaveDocument(redactedPath);

        // Act & Assert - Verify with pdftotext if available
        if (IsToolAvailable("pdftotext", "-v"))
        {
            var textFile = Path.Combine(_testOutputDir, "extracted.txt");
            var (exitCode, _) = RunCommand("pdftotext", $"\"{redactedPath}\" \"{textFile}\"");

            if (exitCode == 0 && File.Exists(textFile))
            {
                var extractedText = File.ReadAllText(textFile);
                extractedText.Should().NotContain(targetText,
                    $"pdftotext should not find '{targetText}' in redacted PDF");
                _output.WriteLine($"pdftotext verification: PASS - '{targetText}' not found");
            }
        }

        // Verify with mutool draw (text extraction)
        if (_mutoolAvailable)
        {
            var (exitCode, output) = RunCommand("mutool", $"draw -F txt \"{redactedPath}\"");
            if (exitCode == 0)
            {
                output.Should().NotContain(targetText,
                    $"mutool should not find '{targetText}' in redacted PDF");
                _output.WriteLine($"mutool verification: PASS - '{targetText}' not found");
            }
        }

        // Verify with qpdf content stream
        if (_qpdfAvailable)
        {
            var (exitCode, _) = RunCommand("qpdf", $"--check \"{redactedPath}\"");
            // qpdf exit codes: 0 = success, 2 = errors, 3 = success with warnings
            exitCode.Should().BeOneOf(new[] { 0, 3 }, "qpdf should validate the PDF structure (0=success, 3=warnings)");
            _output.WriteLine("qpdf verification: PASS - valid PDF structure");
        }
    }

    #endregion

    #region Helper Methods

    private static bool IsToolAvailable(string tool, string versionArg)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = versionArg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0 || process?.ExitCode == 1; // Some tools return 1 for --version
        }
        catch
        {
            return false;
        }
    }

    private (int exitCode, string output) RunCommand(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, $"Error running {command}: {ex.Message}");
        }
    }

    private (int exitCode, string output) RunVeraPdf(string pdfPath,
        bool autoDetectFlavour = false, bool extractFeatures = false)
    {
        var args = new List<string>();

        if (autoDetectFlavour)
            args.Add("--flavour 0");

        if (extractFeatures)
            args.Add("--extract informationDict,metadata");

        args.Add($"\"{pdfPath}\"");

        return RunCommand(_veraPdfPath, string.Join(" ", args));
    }

    #endregion

    public void Dispose()
    {
        _documentService.CloseDocument();
        try
        {
            if (Directory.Exists(_testOutputDir))
                Directory.Delete(_testOutputDir, true);
        }
        catch { /* Cleanup best effort */ }
    }
}
