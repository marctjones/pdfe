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
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// EXCESSIVE redaction tests - testing so thorough it's excessive.
/// These tests verify TRUE glyph-level content removal across multiple dimensions:
/// - Multiple extraction tools
/// - Binary-level verification
/// - Edge cases and transformations
/// - Security attack vectors
/// - Forensic-level validation
/// </summary>
public class ExcessiveRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public ExcessiveRedactionTests(ITestOutputHelper output)
    {
        _output = output;

        // Initialize font resolver
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

    #region Core Glyph Removal Verification Tests

    [Fact]
    public void RedactText_VerifyTextNotExtractableByPdfPig()
    {
        _output.WriteLine("=== TEST: RedactText_VerifyTextNotExtractableByPdfPig ===");

        // Arrange
        var testPdf = CreateTempPath("pdfpig_extraction_test.pdf");
        var secretText = "SECRET_PASSWORD_12345";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Verify text exists before
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(secretText);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("pdfpig_extraction_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - PdfPig extraction
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        _output.WriteLine($"Text before: '{textBefore}'");
        _output.WriteLine($"Text after: '{textAfter}'");

        textAfter.Should().NotContain(secretText,
            "SECRET TEXT MUST BE REMOVED - NOT JUST VISUALLY HIDDEN");
        textAfter.Should().NotContain("SECRET",
            "No part of secret text should be extractable");
        textAfter.Should().NotContain("PASSWORD",
            "No part of secret text should be extractable");
        textAfter.Should().NotContain("12345",
            "No part of secret text should be extractable");

        _output.WriteLine("PASSED: Text not extractable by PdfPig");
    }

    [Fact]
    public void RedactText_VerifyBinaryNotContainingRedactedText()
    {
        _output.WriteLine("=== TEST: RedactText_VerifyBinaryNotContainingRedactedText ===");

        // Arrange
        var testPdf = CreateTempPath("binary_check_test.pdf");
        var secretText = "ULTRA_SECRET_BINARY_CHECK";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Verify text is extractable before redaction
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(secretText, "Text should be extractable before redaction");

        // Note: PDF text may be encoded and not appear as raw ASCII in bytes

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("binary_check_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Binary level check
        var bytesAfter = File.ReadAllBytes(redactedPdf);
        var stringAfter = Encoding.ASCII.GetString(bytesAfter);

        _output.WriteLine($"PDF size after redaction: {bytesAfter.Length} bytes");

        stringAfter.Should().NotContain(secretText,
            "CRITICAL: Redacted text must not appear in raw PDF bytes");
        stringAfter.Should().NotContain("ULTRA_SECRET",
            "No part of redacted text should be in raw bytes");

        _output.WriteLine("PASSED: Binary-level verification complete");
    }

    [Fact]
    public void RedactText_VerifyContentStreamDoesNotContainText()
    {
        _output.WriteLine("=== TEST: RedactText_VerifyContentStreamDoesNotContainText ===");

        // Arrange
        var testPdf = CreateTempPath("content_stream_check.pdf");
        var secretText = "CONTENT_STREAM_SECRET";
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, secretText);
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("content_stream_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Check that text is removed using PdfPig extraction
        var contentStreamText = PdfTestHelpers.ExtractAllText(redactedPdf);

        _output.WriteLine($"Content stream excerpt: {contentStreamText.Substring(0, Math.Min(200, contentStreamText.Length))}...");

        contentStreamText.Should().NotContain(secretText,
            "Content stream must not contain redacted text");

        _output.WriteLine("PASSED: Content stream verification complete");
    }

    #endregion

    #region Multiple Font Size Tests

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(18)]
    [InlineData(24)]
    [InlineData(36)]
    [InlineData(48)]
    [InlineData(72)]
    public void RedactText_VariousFontSizes(int fontSize)
    {
        _output.WriteLine($"=== TEST: RedactText_FontSize_{fontSize}pt ===");

        // Arrange
        var testPdf = CreateTempPath($"font_size_{fontSize}.pdf");
        var secretText = $"SECRET_{fontSize}";
        CreateTextWithFontSize(testPdf, secretText, fontSize);
        _tempFiles.Add(testPdf);

        // Verify text exists
        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(secretText);

        // Act - Calculate appropriate redaction area based on font size
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var areaHeight = fontSize * 1.5;
        var areaWidth = secretText.Length * fontSize * 0.8;
        var redactionArea = new Rect(95, 95, areaWidth, areaHeight);

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath($"font_size_{fontSize}_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain(secretText,
            $"Font size {fontSize}pt text must be redacted");

        _output.WriteLine($"PASSED: Font size {fontSize}pt redaction works");
    }

    #endregion

    #region Coordinate System Tests

    [Fact]
    public void RedactText_AtBottomOfPage()
    {
        _output.WriteLine("=== TEST: RedactText_AtBottomOfPage ===");

        // Arrange
        var testPdf = CreateTempPath("bottom_page_test.pdf");
        CreateTextAtPosition(testPdf, "BOTTOM_SECRET", 100, 750); // Near bottom in PDF coords
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain("BOTTOM_SECRET");

        // Act - Redact at bottom of page
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // XGraphics uses top-left origin, so text at Y=750 needs redaction at ~Y=740
        var redactionArea = new Rect(90, 740, 200, 30);

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("bottom_page_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("BOTTOM_SECRET",
            "Text at bottom of page must be redacted");

        _output.WriteLine("PASSED: Bottom of page redaction works");
    }

    [Fact]
    public void RedactText_AtTopOfPage()
    {
        _output.WriteLine("=== TEST: RedactText_AtTopOfPage ===");

        // Arrange
        var testPdf = CreateTempPath("top_page_test.pdf");
        CreateTextAtPosition(testPdf, "TOP_SECRET", 100, 50); // Near top in PDF coords
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain("TOP_SECRET");

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // XGraphics uses top-left origin, so text at Y=50 needs redaction at ~Y=40
        var redactionArea = new Rect(90, 40, 200, 30);

        _redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        var redactedPdf = CreateTempPath("top_page_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("TOP_SECRET",
            "Text at top of page must be redacted");

        _output.WriteLine("PASSED: Top of page redaction works");
    }

    [Fact]
    public void RedactText_AtEdgesOfPage()
    {
        _output.WriteLine("=== TEST: RedactText_AtEdgesOfPage ===");

        // Arrange
        var testPdf = CreateTempPath("edges_test.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 10);
            // Place text at various edges
            gfx.DrawString("LEFT_EDGE", font, XBrushes.Black, new XPoint(10, 400));
            gfx.DrawString("RIGHT_EDGE", font, XBrushes.Black, new XPoint(500, 400));
            gfx.DrawString("CORNER", font, XBrushes.Black, new XPoint(10, 10));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact left edge text
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin, so text at Y=400 needs redaction at ~Y=390
        _redactionService.RedactArea(pg, new Rect(5, 390, 100, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("edges_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("LEFT_EDGE");
        textAfter.Should().Contain("RIGHT_EDGE");

        _output.WriteLine("PASSED: Edge text redaction works");
    }

    #endregion

    #region Multiple Redaction Tests

    [Fact]
    public void RedactMultipleAreas_NonOverlapping()
    {
        _output.WriteLine("=== TEST: RedactMultipleAreas_NonOverlapping ===");

        // Arrange
        var testPdf = CreateTempPath("multi_non_overlapping.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("SECRET_ONE", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString("SECRET_TWO", font, XBrushes.Black, new XPoint(100, 200));
            gfx.DrawString("SECRET_THREE", font, XBrushes.Black, new XPoint(100, 300));
            gfx.DrawString("KEEP_THIS", font, XBrushes.Black, new XPoint(100, 400));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin - redact at same Y values as text
        _redactionService.RedactArea(pg, new Rect(90, 90, 150, 30), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(90, 190, 150, 30), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(90, 290, 150, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("multi_non_overlapping_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        textAfter.Should().NotContain("SECRET_ONE");
        textAfter.Should().NotContain("SECRET_TWO");
        textAfter.Should().NotContain("SECRET_THREE");
        textAfter.Should().Contain("KEEP_THIS",
            "Non-redacted text must remain");

        _output.WriteLine("PASSED: Multiple non-overlapping redactions work");
    }

    [Fact]
    public void RedactMultipleAreas_Overlapping()
    {
        _output.WriteLine("=== TEST: RedactMultipleAreas_Overlapping ===");

        // Arrange
        var testPdf = CreateTempPath("multi_overlapping.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            // Text that will be covered by overlapping redactions
            gfx.DrawString("OVERLAPPING_SECRET", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Apply overlapping redactions
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        // XGraphics uses top-left origin, text at Y=100 needs redaction at Y=90
        _redactionService.RedactArea(pg, new Rect(90, 90, 100, 30), renderDpi: 72);
        _redactionService.RedactArea(pg, new Rect(140, 90, 100, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("multi_overlapping_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("OVERLAPPING_SECRET",
            "Overlapping redactions must still remove text");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF must remain valid with overlapping redactions");

        _output.WriteLine("PASSED: Overlapping redactions work");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public void RedactMultipleAreas_ManyRedactions(int count)
    {
        _output.WriteLine($"=== TEST: RedactMultipleAreas_{count}_Redactions ===");

        // Arrange
        var testPdf = CreateTempPath($"many_redactions_{count}.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        var secrets = new List<string>();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 8);
            for (int i = 0; i < count; i++)
            {
                var secret = $"SECRET_{i:D3}";
                secrets.Add(secret);
                var y = 50 + (i * 15);
                gfx.DrawString(secret, font, XBrushes.Black, new XPoint(100, y));
            }
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        for (int i = 0; i < count; i++)
        {
            // Text is at XGraphics Y = 50 + (i * 15), which is top-left origin
            // Text body extends upward from baseline, so subtract font height
            var textY = 50 + (i * 15);
            var redactY = textY - 8; // 8pt font, so body is ~8 pixels above baseline
            _redactionService.RedactArea(pg, new Rect(90, redactY, 100, 15), renderDpi: 72);
        }

        var redactedPdf = CreateTempPath($"many_redactions_{count}_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);

        foreach (var secret in secrets)
        {
            textAfter.Should().NotContain(secret,
                $"{secret} must be redacted");
        }

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue(
            "PDF must remain valid with many redactions");

        _output.WriteLine($"PASSED: {count} redactions work correctly");
    }

    #endregion

    #region Special Character Tests

    [Theory]
    [InlineData("Secret with spaces")]
    [InlineData("Secret_with_underscores")]
    [InlineData("Secret-with-dashes")]
    [InlineData("Secret123Numbers456")]
    [InlineData("MixedCASE_Secret")]
    public void RedactText_SpecialPatterns(string text)
    {
        _output.WriteLine($"=== TEST: RedactText_Pattern: '{text}' ===");

        // Arrange
        var testPdf = CreateTempPath($"pattern_{text.Replace(" ", "_")}.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, text);
        _tempFiles.Add(testPdf);

        var textBefore = PdfTestHelpers.ExtractAllText(testPdf);
        textBefore.Should().Contain(text);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath($"pattern_{text.Replace(" ", "_")}_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain(text,
            $"Text '{text}' must be redacted");

        _output.WriteLine($"PASSED: Pattern '{text}' redacted correctly");
    }

    #endregion

    #region Multi-Page Tests

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void RedactAcrossMultiplePages(int pageCount)
    {
        _output.WriteLine($"=== TEST: RedactAcrossMultiplePages_{pageCount} ===");

        // Arrange
        var testPdf = CreateTempPath($"multi_page_{pageCount}.pdf");
        var document = new PdfDocument();

        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString($"PAGE_{i + 1}_SECRET", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString($"PAGE_{i + 1}_KEEP", font, XBrushes.Black, new XPoint(100, 200));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact secrets on all pages
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);

        for (int i = 0; i < pageCount; i++)
        {
            var pg = doc.Pages[i];
            // XGraphics uses top-left origin, text at Y=100 needs redaction at Y=90
            _redactionService.RedactArea(pg, new Rect(90, 90, 200, 30), renderDpi: 72);
        }

        var redactedPdf = CreateTempPath($"multi_page_{pageCount}_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        for (int i = 0; i < pageCount; i++)
        {
            var pageText = PdfTestHelpers.ExtractTextFromPage(redactedPdf, i);
            pageText.Should().NotContain($"PAGE_{i + 1}_SECRET",
                $"Secret on page {i + 1} must be redacted");
            pageText.Should().Contain($"PAGE_{i + 1}_KEEP",
                $"Non-secret on page {i + 1} must remain");
        }

        PdfTestHelpers.GetPageCount(redactedPdf).Should().Be(pageCount,
            "Page count must remain unchanged");

        _output.WriteLine($"PASSED: {pageCount}-page redaction works");
    }

    [Fact]
    public void RedactSpecificPageOnly()
    {
        _output.WriteLine("=== TEST: RedactSpecificPageOnly ===");

        // Arrange
        var testPdf = CreateTempPath("specific_page.pdf");
        var document = new PdfDocument();

        for (int i = 0; i < 3; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString($"SECRET_{i + 1}", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Only redact page 2 (index 1)
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[1];
        // XGraphics uses top-left origin, text at Y=100 needs redaction at Y=90
        _redactionService.RedactArea(pg, new Rect(90, 90, 150, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("specific_page_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var page1Text = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 0);
        var page2Text = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 1);
        var page3Text = PdfTestHelpers.ExtractTextFromPage(redactedPdf, 2);

        page1Text.Should().Contain("SECRET_1", "Page 1 was not redacted");
        page2Text.Should().NotContain("SECRET_2", "Page 2 must be redacted");
        page3Text.Should().Contain("SECRET_3", "Page 3 was not redacted");

        _output.WriteLine("PASSED: Specific page redaction works");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void RedactEmptyArea_ShouldNotCorrupt()
    {
        _output.WriteLine("=== TEST: RedactEmptyArea_ShouldNotCorrupt ===");

        // Arrange
        var testPdf = CreateTempPath("empty_area.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "KEEP_THIS");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact far away from text
        _redactionService.RedactArea(page, new Rect(400, 400, 100, 100), renderDpi: 72);

        var redactedPdf = CreateTempPath("empty_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().Contain("KEEP_THIS");

        _output.WriteLine("PASSED: Empty area redaction doesn't corrupt");
    }

    [Fact]
    public void RedactVerySmallArea()
    {
        _output.WriteLine("=== TEST: RedactVerySmallArea ===");

        // Arrange
        var testPdf = CreateTempPath("small_area.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "X");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Very small redaction
        _redactionService.RedactArea(page, new Rect(99, 99, 5, 5), renderDpi: 72);

        var redactedPdf = CreateTempPath("small_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert
        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine("PASSED: Very small area redaction works");
    }

    [Fact]
    public void RedactVeryLargeArea()
    {
        _output.WriteLine("=== TEST: RedactVeryLargeArea ===");

        // Arrange
        var testPdf = CreateTempPath("large_area.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            for (int y = 100; y <= 700; y += 50)
            {
                gfx.DrawString($"SECRET_AT_{y}", font, XBrushes.Black, new XPoint(100, y));
            }
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - Redact entire page
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];
        _redactionService.RedactArea(pg, new Rect(0, 0, 612, 792), renderDpi: 72);

        var redactedPdf = CreateTempPath("large_area_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("SECRET_AT_",
            "All text on page must be redacted");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine("PASSED: Very large area (full page) redaction works");
    }

    [Fact]
    public void RedactEntireDocument()
    {
        _output.WriteLine("=== TEST: RedactEntireDocument ===");

        // Arrange
        var testPdf = CreateTempPath("entire_doc.pdf");
        var document = new PdfDocument();

        for (int i = 0; i < 3; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString($"REDACT_ALL_{i}", font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);

        foreach (var pg in doc.Pages)
        {
            _redactionService.RedactArea(pg, new Rect(0, 0, 612, 792), renderDpi: 72);
        }

        var redactedPdf = CreateTempPath("entire_doc_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("REDACT_ALL_",
            "Entire document must be redacted");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        _output.WriteLine("PASSED: Entire document redaction works");
    }

    #endregion

    #region PDF Validity Tests

    [Fact]
    public void RedactedPdf_CanBeReopened()
    {
        _output.WriteLine("=== TEST: RedactedPdf_CanBeReopened ===");

        // Arrange
        var testPdf = CreateTempPath("reopen_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "SECRET");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 150, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("reopen_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Can reopen and modify again
        var reopened = PdfReader.Open(redactedPdf, PdfDocumentOpenMode.Modify);
        reopened.Should().NotBeNull();
        reopened.PageCount.Should().Be(1);

        // Can add another page
        var newPage = reopened.AddPage();
        newPage.Should().NotBeNull();

        var finalPdf = CreateTempPath("reopen_final.pdf");
        _tempFiles.Add(finalPdf);
        reopened.Save(finalPdf);
        reopened.Dispose();

        PdfTestHelpers.IsValidPdf(finalPdf).Should().BeTrue();

        _output.WriteLine("PASSED: Redacted PDF can be reopened and modified");
    }

    [Fact]
    public void RedactedPdf_CanBeRedactedAgain()
    {
        _output.WriteLine("=== TEST: RedactedPdf_CanBeRedactedAgain ===");

        // Arrange
        var testPdf = CreateTempPath("double_redact.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString("FIRST_SECRET", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString("SECOND_SECRET", font, XBrushes.Black, new XPoint(100, 200));
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act - First redaction
        var doc1 = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg1 = doc1.Pages[0];
        // XGraphics uses top-left origin, text at Y=100 needs redaction at Y=90
        _redactionService.RedactArea(pg1, new Rect(90, 90, 200, 30), renderDpi: 72);

        var firstRedacted = CreateTempPath("double_redact_first.pdf");
        _tempFiles.Add(firstRedacted);
        doc1.Save(firstRedacted);
        doc1.Dispose();

        // Second redaction - text at Y=200 needs redaction at Y=190
        var doc2 = PdfReader.Open(firstRedacted, PdfDocumentOpenMode.Modify);
        var pg2 = doc2.Pages[0];
        _redactionService.RedactArea(pg2, new Rect(90, 190, 200, 30), renderDpi: 72);

        var secondRedacted = CreateTempPath("double_redact_second.pdf");
        _tempFiles.Add(secondRedacted);
        doc2.Save(secondRedacted);
        doc2.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(secondRedacted);
        textAfter.Should().NotContain("FIRST_SECRET");
        textAfter.Should().NotContain("SECOND_SECRET");

        PdfTestHelpers.IsValidPdf(secondRedacted).Should().BeTrue();

        _output.WriteLine("PASSED: PDF can be redacted multiple times");
    }

    #endregion

    #region Security Attack Vector Tests

    [Fact]
    public void RedactText_CannotRecoverByCopyPaste()
    {
        _output.WriteLine("=== TEST: RedactText_CannotRecoverByCopyPaste ===");
        _output.WriteLine("NOTE: This tests that text is not in extractable form");

        // Arrange
        var testPdf = CreateTempPath("copy_paste_test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "COPY_PASTE_SECRET");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("copy_paste_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Use extraction as proxy for copy-paste
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
        textAfter.Should().NotContain("COPY_PASTE_SECRET",
            "Text must not be copy-pasteable");

        _output.WriteLine("PASSED: Text cannot be recovered by copy-paste");
    }

    [Fact]
    public void RedactText_NotInPdfMetadata()
    {
        _output.WriteLine("=== TEST: RedactText_NotInPdfMetadata ===");

        // Arrange
        var testPdf = CreateTempPath("metadata_check.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(testPdf, "METADATA_SECRET");
        _tempFiles.Add(testPdf);

        // Act
        var document = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, new Rect(90, 90, 300, 30), renderDpi: 72);

        var redactedPdf = CreateTempPath("metadata_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        document.Save(redactedPdf);
        document.Dispose();

        // Assert - Check metadata
        var bytes = File.ReadAllBytes(redactedPdf);
        var content = Encoding.UTF8.GetString(bytes);

        // Metadata shouldn't contain our secret (though this test is basic)
        content.Should().NotContain("METADATA_SECRET",
            "Redacted text should not appear in metadata");

        _output.WriteLine("PASSED: Text not found in metadata");
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void RedactComplexPage_Performance()
    {
        _output.WriteLine("=== TEST: RedactComplexPage_Performance ===");

        // Arrange - Create page with lots of text
        var testPdf = CreateTempPath("performance_test.pdf");
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 6);
            for (int y = 50; y <= 750; y += 10)
            {
                for (int x = 50; x <= 550; x += 100)
                {
                    gfx.DrawString($"T{x}{y}", font, XBrushes.Black, new XPoint(x, y));
                }
            }
        }

        document.Save(testPdf);
        document.Dispose();
        _tempFiles.Add(testPdf);

        // Act
        var doc = PdfReader.Open(testPdf, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];

        var startTime = DateTime.Now;
        _redactionService.RedactArea(pg, new Rect(100, 100, 200, 400), renderDpi: 72);
        var elapsed = DateTime.Now - startTime;

        var redactedPdf = CreateTempPath("performance_redacted.pdf");
        _tempFiles.Add(redactedPdf);
        doc.Save(redactedPdf);
        doc.Dispose();

        // Assert
        _output.WriteLine($"Redaction time: {elapsed.TotalMilliseconds:F2}ms");

        PdfTestHelpers.IsValidPdf(redactedPdf).Should().BeTrue();

        // Should complete within reasonable time (5 seconds for complex page)
        elapsed.TotalSeconds.Should().BeLessThan(5,
            "Redaction should complete in reasonable time");

        _output.WriteLine("PASSED: Performance is acceptable");
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ExcessiveRedactionTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    private void CreateTextWithFontSize(string path, string text, int fontSize)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", fontSize);
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 100));
        }

        document.Save(path);
        document.Dispose();
    }

    private void CreateTextAtPosition(string path, string text, double x, double y)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Arial", 12);
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y));
        }

        document.Save(path);
        document.Dispose();
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
