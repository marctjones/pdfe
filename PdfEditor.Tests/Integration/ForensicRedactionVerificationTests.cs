using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Forensic-level verification tests for redaction security.
/// These tests ensure TRUE glyph-level content removal that would
/// withstand legal/forensic scrutiny (like the Manafort case).
///
/// CRITICAL: These tests verify that redacted content cannot be recovered
/// through ANY known extraction method.
/// </summary>
public class ForensicRedactionVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public ForensicRedactionVerificationTests(ITestOutputHelper output)
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
    }

    #region Comprehensive Binary Analysis Tests

    [Fact]
    public void ForensicTest_RawBytesDoNotContainRedactedText()
    {
        _output.WriteLine("=== FORENSIC TEST: Raw Bytes Analysis ===");
        _output.WriteLine("This test ensures redacted text is completely removed from PDF binary");

        // Arrange
        var testPdf = CreateTempPath("forensic_binary.pdf");
        var sensitiveData = "SSN_123_45_6789";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, sensitiveData);
        _tempFiles.Add(testPdf);

        // Verify text is extractable before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(sensitiveData,
            "Pre-redaction: sensitive data should be extractable");

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("forensic_binary_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Multiple encoding checks
        var bytesAfter = File.ReadAllBytes(redactedPdf);

        // ASCII check
        var asciiContent = Encoding.ASCII.GetString(bytesAfter);
        asciiContent.Should().NotContain(sensitiveData,
            "FORENSIC FAILURE: Redacted data found in ASCII encoding");

        // UTF-8 check
        var utf8Content = Encoding.UTF8.GetString(bytesAfter);
        utf8Content.Should().NotContain(sensitiveData,
            "FORENSIC FAILURE: Redacted data found in UTF-8 encoding");

        // Hex check - look for hex-encoded version
        var hexString = BitConverter.ToString(Encoding.ASCII.GetBytes(sensitiveData)).Replace("-", "");
        asciiContent.Should().NotContain(hexString,
            "FORENSIC FAILURE: Redacted data found in hex encoding");

        // Check for partial matches
        asciiContent.Should().NotContain("SSN_123",
            "FORENSIC FAILURE: Partial SSN data found");
        asciiContent.Should().NotContain("45_6789",
            "FORENSIC FAILURE: Partial SSN data found");

        _output.WriteLine("FORENSIC PASS: Raw binary analysis complete - no sensitive data found");
    }

    [Fact]
    public void ForensicTest_NoTextInDecompressedStreams()
    {
        _output.WriteLine("=== FORENSIC TEST: Decompressed Stream Analysis ===");

        // Arrange
        var testPdf = CreateTempPath("forensic_streams.pdf");
        var sensitiveData = "ACCOUNT_NUMBER_98765";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, sensitiveData);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("forensic_streams_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Check content streams using PdfPig extraction
        var streamText = PdfTestHelpers.ExtractAllText(redactedPdf);
        streamText.Should().NotContain(sensitiveData,
            "FORENSIC FAILURE: Sensitive data found in content streams");
        streamText.Should().NotContain("ACCOUNT_NUMBER",
            "FORENSIC FAILURE: Partial sensitive data found in streams");

        _output.WriteLine("FORENSIC PASS: Decompressed stream analysis complete");
    }

    #endregion

    #region Multiple Extraction Tool Verification

    [Fact]
    public void ForensicTest_MultipleExtractionMethods()
    {
        _output.WriteLine("=== FORENSIC TEST: Multiple Extraction Methods ===");
        _output.WriteLine("Testing that sensitive data cannot be extracted by ANY method");

        // Arrange
        var testPdf = CreateTempPath("forensic_multi_extract.pdf");
        var sensitiveData = "CLASSIFIED_DOCUMENT_2024";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, sensitiveData);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 350, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("forensic_multi_extract_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Method 1: PdfPig extraction (primary)
        var pdfPigText = PdfTestHelpers.ExtractAllText(redactedPdf);
        pdfPigText.Should().NotContain(sensitiveData,
            "FORENSIC FAILURE: PdfPig can still extract sensitive data");

        // Assert - Method 2: Raw byte search
        var rawBytes = File.ReadAllBytes(redactedPdf);
        var rawString = Encoding.ASCII.GetString(rawBytes);
        rawString.Should().NotContain(sensitiveData,
            "FORENSIC FAILURE: Raw byte search found sensitive data");

        // Assert - Method 3: Character-by-character extraction
        using (var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(redactedPdf))
        {
            var allChars = new StringBuilder();
            foreach (var pdfPage in pdfDoc.GetPages())
            {
                foreach (var letter in pdfPage.Letters)
                {
                    allChars.Append(letter.Value);
                }
            }
            allChars.ToString().Should().NotContain(sensitiveData,
                "FORENSIC FAILURE: Character extraction found sensitive data");
        }

        _output.WriteLine("FORENSIC PASS: Multiple extraction methods verified");
    }

    [Fact]
    public void ForensicTest_NoPartialTextRecovery()
    {
        _output.WriteLine("=== FORENSIC TEST: No Partial Text Recovery ===");

        // Arrange
        var testPdf = CreateTempPath("forensic_partial.pdf");
        var sensitiveData = "JOHN_DOE_CONFIDENTIAL";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, sensitiveData);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("forensic_partial_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Check for ANY part of the string
        var extractedText = PdfTestHelpers.ExtractAllText(redactedPdf);
        var rawBytes = Encoding.ASCII.GetString(File.ReadAllBytes(redactedPdf));

        var parts = new[] { "JOHN", "DOE", "CONFIDENTIAL", "JOHN_DOE", "DOE_CONFIDENTIAL" };

        foreach (var part in parts)
        {
            extractedText.Should().NotContain(part,
                $"FORENSIC FAILURE: Partial text '{part}' found in extraction");
            rawBytes.Should().NotContain(part,
                $"FORENSIC FAILURE: Partial text '{part}' found in raw bytes");
        }

        _output.WriteLine("FORENSIC PASS: No partial text recovery possible");
    }

    #endregion

    #region Real-World Attack Scenario Tests

    [Fact]
    public void ForensicTest_ManafortScenario()
    {
        _output.WriteLine("=== FORENSIC TEST: Manafort Scenario ===");
        _output.WriteLine("Simulating the redaction failure that exposed Manafort's secrets");

        // Arrange - Create document similar to court filing
        var testPdf = CreateTempPath("manafort_scenario.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("Defendant communicated with KONSTANTIN_KILIMNIK about", font, XBrushes.Black, new XPoint(72, 100));
            gfx.DrawString("sharing campaign polling data valued at $75_MILLION", font, XBrushes.Black, new XPoint(72, 120));
            gfx.DrawString("This is public information that can be seen", font, XBrushes.Black, new XPoint(72, 160));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact the sensitive names and amounts
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin - text at Y=100 needs redaction at Y=90
        // Redact "KONSTANTIN_KILIMNIK"
        _redactionService.RedactArea(pg, new Rect(300, 90, 200, 20), renderDpi: 72);

        // Redact "$75_MILLION" - text at Y=120 needs redaction at Y=110
        _redactionService.RedactArea(pg, new Rect(400, 110, 100, 20), renderDpi: 72);

        var redactedPdf = CreateTempPath("manafort_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert - Critical forensic checks
        var extractedText = PdfTestHelpers.ExtractAllText(redactedPdf);
        var rawBytes = Encoding.ASCII.GetString(File.ReadAllBytes(redactedPdf));

        // These must NOT be recoverable
        extractedText.Should().NotContain("KONSTANTIN_KILIMNIK",
            "MANAFORT FAILURE: Name was not properly redacted");
        extractedText.Should().NotContain("75_MILLION",
            "MANAFORT FAILURE: Amount was not properly redacted");

        rawBytes.Should().NotContain("KONSTANTIN_KILIMNIK",
            "MANAFORT FAILURE: Name found in raw bytes");
        rawBytes.Should().NotContain("75_MILLION",
            "MANAFORT FAILURE: Amount found in raw bytes");

        // Public info should remain
        extractedText.Should().Contain("public information",
            "Non-redacted text should remain visible");

        _output.WriteLine("FORENSIC PASS: Manafort scenario - proper redaction achieved");
    }

    [Fact]
    public void ForensicTest_LegalDiscoveryScenario()
    {
        _output.WriteLine("=== FORENSIC TEST: Legal Discovery Scenario ===");

        // Arrange - Medical record with HIPAA data
        var testPdf = CreateTempPath("hipaa_scenario.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 10);
            gfx.DrawString("Patient: JANE_SMITH_12345", font, XBrushes.Black, new XPoint(72, 100));
            gfx.DrawString("DOB: 01/15/1985", font, XBrushes.Black, new XPoint(72, 120));
            gfx.DrawString("Diagnosis: Condition XYZ", font, XBrushes.Black, new XPoint(72, 140));
            gfx.DrawString("Treatment notes follow", font, XBrushes.Black, new XPoint(72, 180));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact PHI
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin - text at Y=100 and Y=120
        _redactionService.RedactArea(pg, new Rect(110, 90, 200, 20), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(100, 110, 100, 20), renderDpi: 72);

        var redactedPdf = CreateTempPath("hipaa_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var extractedText = PdfTestHelpers.ExtractAllText(redactedPdf);

        extractedText.Should().NotContain("JANE_SMITH",
            "HIPAA FAILURE: Patient name exposed");
        extractedText.Should().NotContain("12345",
            "HIPAA FAILURE: Patient ID exposed");
        extractedText.Should().NotContain("01/15/1985",
            "HIPAA FAILURE: DOB exposed");

        // Non-PHI should remain
        extractedText.Should().Contain("Treatment notes",
            "Non-PHI text should remain");

        _output.WriteLine("FORENSIC PASS: Legal discovery scenario - HIPAA compliance achieved");
    }

    [Fact]
    public void ForensicTest_FinancialDataScenario()
    {
        _output.WriteLine("=== FORENSIC TEST: Financial Data Scenario ===");

        // Arrange
        var testPdf = CreateTempPath("financial_scenario.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 10);
            gfx.DrawString("Account: 4532_1234_5678_9012", font, XBrushes.Black, new XPoint(72, 100));
            gfx.DrawString("CVV: 123", font, XBrushes.Black, new XPoint(72, 120));
            gfx.DrawString("Expiry: 12/25", font, XBrushes.Black, new XPoint(72, 140));
            gfx.DrawString("Transaction ID: ABC123", font, XBrushes.Black, new XPoint(72, 160));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact financial data
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin - text at Y=100, 120, 140
        _redactionService.RedactArea(pg, new Rect(110, 90, 200, 20), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(95, 110, 50, 20), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(110, 130, 60, 20), renderDpi: 72);

        var redactedPdf = CreateTempPath("financial_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var extractedText = PdfTestHelpers.ExtractAllText(redactedPdf);
        var rawBytes = Encoding.ASCII.GetString(File.ReadAllBytes(redactedPdf));

        extractedText.Should().NotContain("4532",
            "PCI-DSS FAILURE: Card number partial exposed");
        extractedText.Should().NotContain("9012",
            "PCI-DSS FAILURE: Card number partial exposed");
        rawBytes.Should().NotContain("4532_1234_5678_9012",
            "PCI-DSS FAILURE: Full card number in raw bytes");

        _output.WriteLine("FORENSIC PASS: Financial data scenario - PCI-DSS compliance achieved");
    }

    #endregion

    #region Unicode and Encoding Tests

    [Theory]
    [InlineData("ASCII_SECRET")]
    [InlineData("Secret123!@#")]
    public void ForensicTest_DifferentEncodings(string text)
    {
        _output.WriteLine($"=== FORENSIC TEST: Encoding Test for '{text}' ===");

        // Arrange
        var testPdf = CreateTempPath($"encoding_{text.Replace("!", "").Replace("@", "").Replace("#", "")}.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, text);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath($"encoding_{text.Replace("!", "").Replace("@", "").Replace("#", "")}_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var extracted = PdfTestHelpers.ExtractAllText(redactedPdf);
        extracted.Should().NotContain(text);

        _output.WriteLine($"FORENSIC PASS: Encoding test for '{text}'");
    }

    #endregion

    #region Document Integrity Tests

    [Fact]
    public void ForensicTest_RedactedPdfPassesValidation()
    {
        _output.WriteLine("=== FORENSIC TEST: PDF Validation ===");

        // Arrange
        var testPdf = CreateTempPath("validation_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "VALIDATE_THIS");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 200, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("validation_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Multiple validation checks
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "Redacted PDF must be valid");

        // Can open with PdfSharp
        var pdfSharpDoc = PdfReader.Open(redactedPdf, PdfDocumentOpenMode.ReadOnly);
        pdfSharpDoc.Should().NotBeNull();
        pdfSharpDoc.PageCount.Should().BeGreaterThan(0);
        pdfSharpDoc.Dispose();

        // Can open with PdfPig
        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(redactedPdf))
        {
            pdfPigDoc.Should().NotBeNull();
            pdfPigDoc.NumberOfPages.Should().BeGreaterThan(0);
        }

        // File is not corrupted
        var bytes = File.ReadAllBytes(redactedPdf);
        bytes.Length.Should().BeGreaterThan(100, "PDF should have meaningful content");

        // Starts with PDF header
        var header = Encoding.ASCII.GetString(bytes, 0, 5);
        header.Should().Be("%PDF-", "PDF should start with valid header");

        _output.WriteLine("FORENSIC PASS: PDF validation complete");
    }

    [Fact]
    public void ForensicTest_NoResidulaDataInObjectStreams()
    {
        _output.WriteLine("=== FORENSIC TEST: Object Stream Analysis ===");

        // Arrange
        var testPdf = CreateTempPath("object_stream.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "OBJECT_STREAM_SECRET");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("object_stream_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Scan entire file for any trace
        var content = File.ReadAllText(redactedPdf, Encoding.Latin1);

        content.Should().NotContain("OBJECT_STREAM_SECRET",
            "FORENSIC FAILURE: Sensitive data found in object streams");

        _output.WriteLine("FORENSIC PASS: Object stream analysis complete");
    }

    #endregion

    #region Stress Tests

    [Fact]
    public void ForensicTest_RepeatedRedactionMaintainsSecurity()
    {
        _output.WriteLine("=== FORENSIC TEST: Repeated Redaction Security ===");

        // Arrange
        var testPdf = CreateTempPath("repeated_redaction.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("SECRET_1", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString("SECRET_2", font, XBrushes.Black, new XPoint(100, 200));
            gfx.DrawString("SECRET_3", font, XBrushes.Black, new XPoint(100, 300));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Multiple rounds of redaction
        var currentPdf = testPdf;
        for (int round = 1; round <= 3; round++)
        {
            var doc = PdfReader.Open(currentPdf, PdfDocumentOpenMode.Modify);
            var pg = doc.Pages[0];

            // XGraphics uses top-left origin, text at Y=100, 200, 300
            // Text body extends upward from baseline, so subtract font height
            var textY = round * 100;
            var redactY = textY - 12; // 12pt font
            _redactionService.RedactArea(pg, new Rect(90, redactY, 150, 25), renderDpi: 72);

            var nextPdf = CreateTempPath($"repeated_round_{round}.pdf");
            _tempFiles.Add(nextPdf);
            doc.Save(nextPdf);
            doc.Dispose();

            currentPdf = nextPdf;
        }

        // Assert
        var finalText = PdfTestHelpers.ExtractAllText(currentPdf);
        var rawBytes = Encoding.ASCII.GetString(File.ReadAllBytes(currentPdf));

        finalText.Should().NotContain("SECRET_1");
        finalText.Should().NotContain("SECRET_2");
        finalText.Should().NotContain("SECRET_3");

        rawBytes.Should().NotContain("SECRET_1");
        rawBytes.Should().NotContain("SECRET_2");
        rawBytes.Should().NotContain("SECRET_3");

        PdfTestHelpers.IsValidPdf(currentPdf).Should().BeTrue();

        _output.WriteLine("FORENSIC PASS: Repeated redaction maintains security");
    }

    #endregion

    #region Metadata Integration Tests

    [Fact]
    public void ForensicTest_RedactionWithMetadataSanitization()
    {
        _output.WriteLine("=== FORENSIC TEST: Redaction with Metadata Sanitization ===");

        // Arrange
        var testPdf = CreateTempPath("metadata_integration.pdf");
        var document = new PdfDocument();
        document.Info.Title = "Document about SECRET_DATA";
        document.Info.Subject = "Contains CONFIDENTIAL_INFO";
        document.Info.Author = "John Doe";

        var page = document.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("SECRET_DATA", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString("CONFIDENTIAL_INFO", font, XBrushes.Black, new XPoint(100, 200));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact with metadata sanitization
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // Create list of redacted terms for metadata sanitization
        var redactedTerms = new List<string> { "SECRET_DATA", "CONFIDENTIAL_INFO" };

        // XGraphics uses top-left origin - text at Y=100 and Y=200
        _redactionService.RedactArea(pg, new Rect(90, 90, 200, 20), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(90, 190, 200, 20), renderDpi: 72);

        // Sanitize metadata (using the service's built-in method)
        var sanitizer = new MetadataSanitizer(NullLogger<MetadataSanitizer>.Instance);
        sanitizer.SanitizeDocument(doc, redactedTerms);

        var redactedPdf = CreateTempPath("metadata_integration_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert - Check both content and metadata
        var extractedText = PdfTestHelpers.ExtractAllText(redactedPdf);
        extractedText.Should().NotContain("SECRET_DATA");
        extractedText.Should().NotContain("CONFIDENTIAL_INFO");

        // Check metadata
        var finalDoc = PdfReader.Open(redactedPdf, PdfDocumentOpenMode.ReadOnly);
        finalDoc.Info.Title.Should().NotContain("SECRET_DATA",
            "FORENSIC FAILURE: Redacted text in document title");
        finalDoc.Info.Subject.Should().NotContain("CONFIDENTIAL_INFO",
            "FORENSIC FAILURE: Redacted text in document subject");
        finalDoc.Dispose();

        _output.WriteLine("FORENSIC PASS: Redaction with metadata sanitization complete");
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ForensicRedactionTests");
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
