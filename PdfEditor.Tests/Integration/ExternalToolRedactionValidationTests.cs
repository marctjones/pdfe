using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// External tool validation tests for redaction verification.
///
/// These tests use industry-standard PDF forensic tools to validate that
/// redacted content is TRULY removed from the PDF structure, not just
/// visually hidden.
///
/// Required tools (install via apt-get on Linux):
/// - pdftotext (poppler-utils)
/// - qpdf
/// - strings (built-in)
///
/// Optional tools:
/// - mutool (mupdf-tools)
/// - pdf-parser.py (Didier Stevens)
/// </summary>
public class ExternalToolRedactionValidationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    // Tool availability flags
    private readonly bool _hasPdftotext;
    private readonly bool _hasQpdf;
    private readonly bool _hasStrings;
    private readonly bool _hasMutool;

    public ExternalToolRedactionValidationTests(ITestOutputHelper output)
    {
        _output = output;

        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        var logger = _loggerFactory.CreateLogger<RedactionService>();
        _redactionService = new RedactionService(logger, _loggerFactory);

        // Check tool availability
        _hasPdftotext = IsToolAvailable("pdftotext", "-v");
        _hasQpdf = IsToolAvailable("qpdf", "--version");
        _hasStrings = IsToolAvailable("strings", "--version");
        _hasMutool = IsToolAvailable("mutool", "-v");

        _output.WriteLine("=== External Tool Availability ===");
        _output.WriteLine($"pdftotext: {(_hasPdftotext ? "Available" : "NOT FOUND")}");
        _output.WriteLine($"qpdf: {(_hasQpdf ? "Available" : "NOT FOUND")}");
        _output.WriteLine($"strings: {(_hasStrings ? "Available" : "NOT FOUND")}");
        _output.WriteLine($"mutool: {(_hasMutool ? "Available" : "NOT FOUND")}");
        _output.WriteLine("");
    }

    #region pdftotext Validation Tests

    [Fact]
    public void Pdftotext_ShouldNotFindRedactedText()
    {
        if (!_hasPdftotext)
        {
            _output.WriteLine("SKIP: pdftotext not installed (apt-get install poppler-utils)");
            return;
        }

        _output.WriteLine("=== TEST: pdftotext Validation ===");

        // Arrange
        var testPdf = CreateTempPath("pdftotext_test.pdf");
        var secretText = "PDFTOTEXT_SECRET_12345";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Verify text exists before
        var textBefore = RunPdftotext(testPdf);
        _output.WriteLine($"Before redaction: '{textBefore.Trim()}'");
        textBefore.Should().Contain(secretText, "Text should exist before redaction");

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("pdftotext_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = RunPdftotext(redactedPdf);
        _output.WriteLine($"After redaction: '{textAfter.Trim()}'");

        textAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: pdftotext can still extract redacted text!");
        textAfter.Should().NotContain("PDFTOTEXT_SECRET",
            "CRITICAL FAILURE: Partial text found by pdftotext!");

        _output.WriteLine("PASS: pdftotext cannot extract redacted text");
    }

    [Fact]
    public void Pdftotext_Layout_ShouldNotFindRedactedText()
    {
        if (!_hasPdftotext)
        {
            _output.WriteLine("SKIP: pdftotext not installed");
            return;
        }

        _output.WriteLine("=== TEST: pdftotext Layout Mode Validation ===");

        // Arrange
        var testPdf = CreateTempPath("pdftotext_layout.pdf");
        var secretText = "LAYOUT_MODE_SECRET";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("pdftotext_layout_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Use layout mode which preserves positioning
        var textAfter = RunCommand("pdftotext", $"-layout \"{redactedPdf}\" -");
        textAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: pdftotext layout mode found redacted text!");

        _output.WriteLine("PASS: pdftotext layout mode cannot extract redacted text");
    }

    [Fact]
    public void Pdftotext_RawMode_ShouldNotFindRedactedText()
    {
        if (!_hasPdftotext)
        {
            _output.WriteLine("SKIP: pdftotext not installed");
            return;
        }

        _output.WriteLine("=== TEST: pdftotext Raw Mode Validation ===");

        // Arrange
        var testPdf = CreateTempPath("pdftotext_raw.pdf");
        var secretText = "RAW_MODE_SECRET";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("pdftotext_raw_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Use raw mode which extracts in content stream order
        var textAfter = RunCommand("pdftotext", $"-raw \"{redactedPdf}\" -");
        textAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: pdftotext raw mode found redacted text!");

        _output.WriteLine("PASS: pdftotext raw mode cannot extract redacted text");
    }

    #endregion

    #region qpdf Content Stream Validation Tests

    [Fact]
    public void Qpdf_ContentStreams_ShouldNotContainRedactedText()
    {
        if (!_hasQpdf)
        {
            _output.WriteLine("SKIP: qpdf not installed (apt-get install qpdf)");
            return;
        }

        _output.WriteLine("=== TEST: qpdf Content Stream Validation ===");

        // Arrange
        var testPdf = CreateTempPath("qpdf_test.pdf");
        var secretText = "QPDF_STREAM_SECRET";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Verify text in streams before
        var qdfBefore = CreateTempPath("qpdf_before.qdf");
        _tempFiles.Add(qdfBefore);
        RunCommand("qpdf", $"--qdf --object-streams=disable \"{testPdf}\" \"{qdfBefore}\"");
        var streamsBefore = File.ReadAllText(qdfBefore);
        streamsBefore.Should().Contain(secretText, "Text should be in streams before redaction");

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("qpdf_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Dump to QDF and check streams
        var qdfAfter = CreateTempPath("qpdf_after.qdf");
        _tempFiles.Add(qdfAfter);
        RunCommand("qpdf", $"--qdf --object-streams=disable \"{redactedPdf}\" \"{qdfAfter}\"");

        var streamsAfter = File.ReadAllText(qdfAfter);
        _output.WriteLine($"QDF file size: {streamsAfter.Length} bytes");

        streamsAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: Redacted text found in raw content streams!");
        streamsAfter.Should().NotContain("QPDF_STREAM",
            "CRITICAL FAILURE: Partial redacted text found in streams!");

        _output.WriteLine("PASS: qpdf content stream analysis - no redacted text found");
    }

    [Fact]
    public void Qpdf_Decompressed_ShouldNotContainRedactedText()
    {
        if (!_hasQpdf)
        {
            _output.WriteLine("SKIP: qpdf not installed");
            return;
        }

        _output.WriteLine("=== TEST: qpdf Decompressed Stream Validation ===");

        // Arrange
        var testPdf = CreateTempPath("qpdf_decompress.pdf");
        var secretText = "DECOMPRESS_SECRET_DATA";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("qpdf_decompress_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Decompress and check
        var decompressed = CreateTempPath("qpdf_decompressed.pdf");
        _tempFiles.Add(decompressed);
        RunCommand("qpdf", $"--stream-data=uncompress \"{redactedPdf}\" \"{decompressed}\"");

        var content = File.ReadAllText(decompressed, Encoding.Latin1);
        content.Should().NotContain(secretText,
            "CRITICAL FAILURE: Redacted text in decompressed streams!");

        _output.WriteLine("PASS: qpdf decompressed streams - no redacted text found");
    }

    #endregion

    #region strings Command Validation Tests

    [Fact]
    public void Strings_ShouldNotFindRedactedText()
    {
        if (!_hasStrings)
        {
            _output.WriteLine("SKIP: strings command not available");
            return;
        }

        _output.WriteLine("=== TEST: strings Command Validation ===");

        // Arrange
        var testPdf = CreateTempPath("strings_test.pdf");
        var secretText = "STRINGS_CMD_SECRET";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Verify text exists before using PdfPig (more reliable than strings for PDFs)
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(secretText, "Text should exist in PDF before redaction");

        // Also check strings output (may or may not find it depending on encoding)
        var stringsBefore = RunStrings(testPdf);
        _output.WriteLine($"strings before contains secret: {stringsBefore.Contains(secretText)}");

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("strings_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Text should be gone from both extraction methods
        var stringsAfter = RunStrings(redactedPdf);
        stringsAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: strings command found redacted text!");

        // Double-check with PdfPig
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: PdfPig can still extract redacted text!");

        _output.WriteLine("PASS: strings command cannot find redacted text");
    }

    [Fact]
    public void Strings_MinLength_ShouldNotFindRedactedText()
    {
        if (!_hasStrings)
        {
            _output.WriteLine("SKIP: strings command not available");
            return;
        }

        _output.WriteLine("=== TEST: strings with Min Length Validation ===");

        // Arrange
        var testPdf = CreateTempPath("strings_minlen.pdf");
        var secretText = "MINLEN_SECRET_TEXT_12345";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("strings_minlen_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Use minimum length of 4 (default) and 8
        var strings4 = RunCommand("strings", $"-n 4 \"{redactedPdf}\"");
        var strings8 = RunCommand("strings", $"-n 8 \"{redactedPdf}\"");

        strings4.Should().NotContain(secretText);
        strings8.Should().NotContain(secretText);
        strings4.Should().NotContain("MINLEN_SECRET");
        strings8.Should().NotContain("MINLEN_SECRET");

        _output.WriteLine("PASS: strings with various min lengths - no redacted text");
    }

    #endregion

    #region mutool Validation Tests

    [Fact]
    public void Mutool_TextExtraction_ShouldNotFindRedactedText()
    {
        if (!_hasMutool)
        {
            _output.WriteLine("SKIP: mutool not installed (apt-get install mupdf-tools)");
            return;
        }

        _output.WriteLine("=== TEST: mutool Text Extraction Validation ===");

        // Arrange
        var testPdf = CreateTempPath("mutool_test.pdf");
        var secretText = "MUTOOL_SECRET_DATA";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 250, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("mutool_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Extract text with mutool
        var textAfter = RunCommand("mutool", $"draw -F txt \"{redactedPdf}\"");
        textAfter.Should().NotContain(secretText,
            "CRITICAL FAILURE: mutool can extract redacted text!");

        _output.WriteLine("PASS: mutool text extraction - no redacted text found");
    }

    #endregion

    #region Comprehensive Multi-Tool Validation

    [Fact]
    public void AllTools_ComprehensiveValidation()
    {
        _output.WriteLine("=== TEST: Comprehensive Multi-Tool Validation ===");

        // Skip if no tools available
        if (!_hasPdftotext && !_hasQpdf && !_hasStrings)
        {
            _output.WriteLine("SKIP: No external tools available");
            return;
        }

        // Arrange
        var testPdf = CreateTempPath("comprehensive_test.pdf");
        var secretText = "COMPREHENSIVE_SECRET_99999";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 350, 30), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("comprehensive_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert with all available tools
        var failures = new List<string>();

        if (_hasPdftotext)
        {
            var pdftotext = RunPdftotext(redactedPdf);
            if (pdftotext.Contains(secretText))
                failures.Add("pdftotext");
        }

        if (_hasQpdf)
        {
            var qdf = CreateTempPath("comprehensive.qdf");
            _tempFiles.Add(qdf);
            RunCommand("qpdf", $"--qdf --object-streams=disable \"{redactedPdf}\" \"{qdf}\"");
            var streams = File.ReadAllText(qdf);
            if (streams.Contains(secretText))
                failures.Add("qpdf streams");
        }

        if (_hasStrings)
        {
            var strings = RunStrings(redactedPdf);
            if (strings.Contains(secretText))
                failures.Add("strings");
        }

        if (_hasMutool)
        {
            var mutool = RunCommand("mutool", $"draw -F txt \"{redactedPdf}\"");
            if (mutool.Contains(secretText))
                failures.Add("mutool");
        }

        // Also check PdfPig (always available)
        var pdfpig = PdfTestHelpers.ExtractAllText(redactedPdf);
        if (pdfpig.Contains(secretText))
            failures.Add("PdfPig");

        // Also check raw binary
        var rawBytes = Encoding.ASCII.GetString(File.ReadAllBytes(redactedPdf));
        if (rawBytes.Contains(secretText))
            failures.Add("raw binary");

        // Report results
        _output.WriteLine($"Tools tested: {(_hasPdftotext ? "pdftotext " : "")}{(_hasQpdf ? "qpdf " : "")}{(_hasStrings ? "strings " : "")}{(_hasMutool ? "mutool " : "")}PdfPig raw-binary");

        if (failures.Count > 0)
        {
            _output.WriteLine($"FAILURES: {string.Join(", ", failures)}");
            Assert.Fail($"CRITICAL: Redacted text found by: {string.Join(", ", failures)}");
        }

        _output.WriteLine("PASS: All tools confirm text is properly redacted");
    }

    [Fact]
    public void AllTools_MultipleSecrets_Validation()
    {
        _output.WriteLine("=== TEST: Multiple Secrets Multi-Tool Validation ===");

        if (!_hasPdftotext && !_hasQpdf && !_hasStrings)
        {
            _output.WriteLine("SKIP: No external tools available");
            return;
        }

        // Arrange - Multiple secrets
        var testPdf = CreateTempPath("multi_secrets.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        var secrets = new[] { "SECRET_ALPHA", "SECRET_BETA", "SECRET_GAMMA" };

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString(secrets[0], font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString(secrets[1], font, XBrushes.Black, new XPoint(100, 200));
            gfx.DrawString(secrets[2], font, XBrushes.Black, new XPoint(100, 300));
            gfx.DrawString("PUBLIC_INFO", font, XBrushes.Black, new XPoint(100, 400));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact all secrets
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin - text at Y=100, 200, 300
        _redactionService.RedactArea(pg, new Rect(90, 90, 200, 20), testPdf, renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(90, 190, 200, 20), testPdf, renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(90, 290, 200, 20), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("multi_secrets_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert with all tools
        var allText = "";

        if (_hasPdftotext)
            allText += RunPdftotext(redactedPdf) + " ";

        if (_hasQpdf)
        {
            var qdf = CreateTempPath("multi.qdf");
            _tempFiles.Add(qdf);
            RunCommand("qpdf", $"--qdf --object-streams=disable \"{redactedPdf}\" \"{qdf}\"");
            allText += File.ReadAllText(qdf) + " ";
        }

        if (_hasStrings)
            allText += RunStrings(redactedPdf) + " ";

        allText += PdfTestHelpers.ExtractAllText(redactedPdf) + " ";
        allText += Encoding.ASCII.GetString(File.ReadAllBytes(redactedPdf));

        // Check each secret
        foreach (var secret in secrets)
        {
            allText.Should().NotContain(secret,
                $"CRITICAL: {secret} found in PDF after redaction");
        }

        // Public info should remain
        allText.Should().Contain("PUBLIC_INFO", "Non-redacted text should remain");

        _output.WriteLine("PASS: All secrets removed, public info retained");
    }

    #endregion

    #region Real-World Document Validation

    [Fact]
    public void RealWorld_LegalDocument_ExternalValidation()
    {
        _output.WriteLine("=== TEST: Real-World Legal Document Validation ===");

        if (!_hasPdftotext && !_hasStrings)
        {
            _output.WriteLine("SKIP: Need pdftotext or strings for this test");
            return;
        }

        // Arrange - Create realistic legal document
        var testPdf = CreateTempPath("legal_doc.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var titleFont = new XFont("Arial", 16);
            var bodyFont = new XFont("Arial", 10);

            gfx.DrawString("CONFIDENTIAL SETTLEMENT AGREEMENT", titleFont, XBrushes.Black, new XPoint(72, 72));
            gfx.DrawString("Between PLAINTIFF_NAME and DEFENDANT_CORP", bodyFont, XBrushes.Black, new XPoint(72, 120));
            gfx.DrawString("Settlement Amount: $5,000,000", bodyFont, XBrushes.Black, new XPoint(72, 150));
            gfx.DrawString("Account Number: 1234-5678-9012", bodyFont, XBrushes.Black, new XPoint(72, 180));
            gfx.DrawString("This agreement is binding.", bodyFont, XBrushes.Black, new XPoint(72, 240));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact sensitive information
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin - text at Y=120, 150, 180
        // Redact names
        _redactionService.RedactArea(pg, new Rect(140, 110, 300, 15), testPdf, renderDpi: 72);
        // Redact amount
        _redactionService.RedactArea(pg, new Rect(200, 140, 100, 15), testPdf, renderDpi: 72);
        // Redact account
        _redactionService.RedactArea(pg, new Rect(200, 170, 150, 15), testPdf, renderDpi: 72);

        var redactedPdf = CreateTempPath("legal_doc_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var allExtracted = "";

        if (_hasPdftotext)
            allExtracted += RunPdftotext(redactedPdf) + " ";

        if (_hasStrings)
            allExtracted += RunStrings(redactedPdf) + " ";

        allExtracted += PdfTestHelpers.ExtractAllText(redactedPdf);

        // Sensitive data must not be found
        allExtracted.Should().NotContain("PLAINTIFF_NAME");
        allExtracted.Should().NotContain("DEFENDANT_CORP");
        allExtracted.Should().NotContain("5,000,000");
        allExtracted.Should().NotContain("1234-5678-9012");

        // Non-sensitive should remain
        allExtracted.Should().Contain("SETTLEMENT AGREEMENT");
        allExtracted.Should().Contain("binding");

        _output.WriteLine("PASS: Legal document properly redacted");
    }

    #endregion

    #region Helper Methods

    private bool IsToolAvailable(string tool, string versionArg)
    {
        try
        {
            // First check if the tool exists in PATH
            var whichProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = tool,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            whichProcess.Start();
            var whichOutput = whichProcess.StandardOutput.ReadToEnd();
            whichProcess.WaitForExit(5000);

            if (whichProcess.ExitCode != 0 || string.IsNullOrWhiteSpace(whichOutput))
                return false;

            // Tool exists, try to run it
            var result = RunCommand(tool, versionArg, timeoutMs: 5000, ignoreErrors: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string RunPdftotext(string pdfPath)
    {
        return RunCommand("pdftotext", $"\"{pdfPath}\" -");
    }

    private string RunStrings(string filePath)
    {
        return RunCommand("strings", $"\"{filePath}\"");
    }

    private string RunCommand(string command, string args, int timeoutMs = 30000, bool ignoreErrors = false)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                throw new TimeoutException($"Command timed out: {command} {args}");
            }

            if (process.ExitCode != 0 && !ignoreErrors)
            {
                // Some tools return non-zero even on success
                if (!string.IsNullOrEmpty(output))
                    return output;
            }

            return output;
        }
        catch (Exception) when (ignoreErrors)
        {
            return "";
        }
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExternalToolRedactionTests");
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
            catch { }
        }
    }

    #endregion
}
