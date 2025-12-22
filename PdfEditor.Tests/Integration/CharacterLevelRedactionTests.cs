using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for character-level redaction functionality.
/// These tests verify that redaction works at the character level,
/// preventing over-redaction where entire multi-word operations are
/// removed when only a small portion is selected.
///
/// CRITICAL: These tests protect against regression of the character-level
/// redaction fix (commit 531caff). If these tests fail, the application
/// has regressed to operation-level redaction which causes massive over-redaction.
/// </summary>
public class CharacterLevelRedactionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    public CharacterLevelRedactionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CharRedactionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var redactionLogger = _loggerFactory.CreateLogger<RedactionService>();

        _redactionService = new RedactionService(redactionLogger, _loggerFactory);
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Helper to get character positions from a PDF using PdfPig.
    /// Returns positions in PDF coordinates (bottom-left origin).
    /// </summary>
    private List<(string text, double left, double bottom, double right, double top)>
        GetCharacterPositions(string pdfPath, int pageIndex = 0)
    {
        var result = new List<(string, double, double, double, double)>();

        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageIndex + 1);

        foreach (var letter in page.Letters)
        {
            result.Add((
                letter.Value,
                letter.GlyphRectangle.Left,
                letter.GlyphRectangle.Bottom,
                letter.GlyphRectangle.Right,
                letter.GlyphRectangle.Top
            ));
        }

        return result;
    }

    /// <summary>
    /// Converts PDF character bounds to redaction area in top-left coordinates (Avalonia/image space).
    /// </summary>
    private Rect PdfCoordsToRedactionArea(
        double pdfLeft, double pdfBottom, double pdfRight, double pdfTop,
        double pageHeight)
    {
        // Redaction area is in PDF points but with top-left origin (Avalonia convention)
        var avaloniaTop = pageHeight - pdfTop;
        var avaloniaBottom = pageHeight - pdfBottom;

        return new Rect(
            pdfLeft,
            avaloniaTop,
            pdfRight - pdfLeft,
            avaloniaBottom - avaloniaTop
        );
    }

    [Fact]
    public void RedactArea_SelectFirstWordOfThree_OnlyRemovesFirstWord()
    {
        // Arrange: Create PDF with "FIRST MIDDLE LAST" on one line
        var pdfPath = Path.Combine(_tempDir, "three_words.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "FIRST MIDDLE LAST");

        var chars = GetCharacterPositions(pdfPath);
        chars.Should().NotBeEmpty("PDF should contain characters");

        // Find character positions for "FIRST" (chars 0-4: F I R S T)
        var firstWord = chars.Take(5).ToList();
        firstWord.Should().HaveCount(5, "FIRST has 5 letters");

        var firstLeft = firstWord.Min(c => c.left);
        var firstRight = firstWord.Max(c => c.right);
        var firstBottom = firstWord.Min(c => c.bottom);
        var firstTop = firstWord.Max(c => c.top);

        // Get page height for coordinate conversion
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        // Create redaction area covering only "FIRST"
        var redactionArea = PdfCoordsToRedactionArea(
            firstLeft, firstBottom, firstRight, firstTop, pageHeight);

        var outputPath = Path.Combine(_tempDir, "three_words_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Redact the first word
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: "FIRST" should be removed, "MIDDLE" and "LAST" should remain
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().NotContain("FIRST",
            "The first word should be removed by character-level redaction");
        textAfter.Should().Contain("MIDDLE",
            "The second word should NOT be removed - this is the key character-level test");
        textAfter.Should().Contain("LAST",
            "The third word should NOT be removed - this is the key character-level test");
    }

    [Fact]
    public void RedactArea_SelectMiddleWordOfThree_OnlyRemovesMiddleWord()
    {
        // Arrange: Create PDF with "ALPHA BETA GAMMA"
        var pdfPath = Path.Combine(_tempDir, "greek_letters.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "ALPHA BETA GAMMA");

        var chars = GetCharacterPositions(pdfPath);

        // Find "BETA" by position - skip ALPHA (5 chars) and space, take next 4 chars
        // ALPHA has 5 chars, space may or may not be reported, BETA has 4 chars
        var allText = string.Join("", chars.Select(c => c.text));
        var betaStartIndex = allText.IndexOf("BETA");
        betaStartIndex.Should().BeGreaterThanOrEqualTo(0, "BETA should be found in the text");

        var betaChars = chars.Skip(betaStartIndex).Take(4).ToList();
        betaChars.Should().HaveCount(4, "BETA has 4 letters");

        var betaLeft = betaChars.Min(c => c.left);
        var betaRight = betaChars.Max(c => c.right);
        var betaBottom = betaChars.Min(c => c.bottom);
        var betaTop = betaChars.Max(c => c.top);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        var redactionArea = PdfCoordsToRedactionArea(
            betaLeft, betaBottom, betaRight, betaTop, pageHeight);

        var outputPath = Path.Combine(_tempDir, "greek_letters_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Redact only "BETA"
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: Only "BETA" removed, "ALPHA" and "GAMMA" remain
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().Contain("ALPHA", "First word should remain");
        textAfter.Should().NotContain("BETA", "Middle word should be removed");
        textAfter.Should().Contain("GAMMA", "Last word should remain");
    }

    [Fact]
    public void RedactArea_SelectPartialWord_OnlyRemovesIfCharacterCenterSelected()
    {
        // Arrange: Create PDF with "CONFIDENTIAL"
        var pdfPath = Path.Combine(_tempDir, "confidential.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "CONFIDENTIAL");

        var chars = GetCharacterPositions(pdfPath);

        // Select only "FIDE" from "CONFIDENTIAL" (chars 3-6: con[FIDE]ntial)
        var fideChars = chars.Skip(3).Take(4).ToList();
        fideChars.Should().HaveCount(4, "FIDE has 4 letters");

        var fideLeft = fideChars.Min(c => c.left);
        var fideRight = fideChars.Max(c => c.right);
        var fideBottom = fideChars.Min(c => c.bottom);
        var fideTop = fideChars.Max(c => c.top);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        var redactionArea = PdfCoordsToRedactionArea(
            fideLeft, fideBottom, fideRight, fideTop, pageHeight);

        var outputPath = Path.Combine(_tempDir, "confidential_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Redact with selection covering "FIDE"
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: Character-level check means if ANY character center is in area, remove whole operation
        // Since "CONFIDENTIAL" is likely one operation, and "FIDE" centers are selected, entire word removed
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().NotContain("CONFIDENTIAL",
            "Word should be removed because character centers of FIDE are inside redaction area");
        textAfter.Should().NotContain("FIDE",
            "Selected portion should definitely be removed");
    }

    [Fact]
    public void RedactArea_SelectBetweenWords_DoesNotRemoveAdjacentWords()
    {
        // Arrange: Create PDF with "WORD1    WORD2" (large space between)
        var pdfPath = Path.Combine(_tempDir, "spaced_words.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "WORD1                    WORD2");

        var chars = GetCharacterPositions(pdfPath);

        // Find the space between words
        var word1Chars = chars.Take(5).ToList();
        var word2Chars = chars.Skip(5).ToList();

        var word1Right = word1Chars.Max(c => c.right);
        var word2Left = word2Chars.Min(c => c.left);

        // Create redaction area in the space between (no character centers)
        var spaceLeft = word1Right + 2;
        var spaceRight = word2Left - 2;
        var spaceBottom = word1Chars.Min(c => c.bottom);
        var spaceTop = word1Chars.Max(c => c.top);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        var redactionArea = PdfCoordsToRedactionArea(
            spaceLeft, spaceBottom, spaceRight, spaceTop, pageHeight);

        var outputPath = Path.Combine(_tempDir, "spaced_words_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Redact in the space between words
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: No character centers in redaction area, so nothing should be removed
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().Contain("WORD1",
            "First word should remain - no character centers in redaction area");
        textAfter.Should().Contain("WORD2",
            "Second word should remain - no character centers in redaction area");
    }

    [Fact]
    public void RedactArea_SelectTwoAdjacentWords_RemovesBothWords()
    {
        // Arrange: Create PDF with "ONE TWO THREE"
        var pdfPath = Path.Combine(_tempDir, "numbers.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "ONE TWO THREE");

        var chars = GetCharacterPositions(pdfPath);

        // Select "TWO THREE" - find where TWO starts
        var allText = string.Join("", chars.Select(c => c.text));
        var twoStartIndex = allText.IndexOf("TWO");
        twoStartIndex.Should().BeGreaterThanOrEqualTo(0, "TWO should be found");

        var selectedChars = chars.Skip(twoStartIndex).ToList();

        var selLeft = selectedChars.Min(c => c.left);
        var selRight = selectedChars.Max(c => c.right);
        var selBottom = selectedChars.Min(c => c.bottom);
        var selTop = selectedChars.Max(c => c.top);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        var redactionArea = PdfCoordsToRedactionArea(
            selLeft, selBottom, selRight, selTop, pageHeight);

        var outputPath = Path.Combine(_tempDir, "numbers_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Redact "TWO THREE"
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: "ONE" remains, "TWO" and "THREE" removed
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().Contain("ONE", "First word not selected, should remain");
        textAfter.Should().NotContain("TWO", "Second word selected, should be removed");
        textAfter.Should().NotContain("THREE", "Third word selected, should be removed");
    }

    [Fact]
    public void RedactArea_RegressionTest_WideOperationDoesNotCauseOverRedaction()
    {
        // This is the CRITICAL regression test for the original bug:
        // A single PDF text operation spanning "FIRST    MIDDLE    LAST" over 1200+ points
        // User selects just "FIRST", old code removed entire operation

        // Arrange: Create PDF with widely-spaced words
        var pdfPath = Path.Combine(_tempDir, "wide_operation.pdf");
        var longText = "FIRST" + new string(' ', 50) + "MIDDLE" + new string(' ', 50) + "LAST";
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, longText);

        var chars = GetCharacterPositions(pdfPath);

        // Select only "FIRST"
        var firstChars = chars.Take(5).ToList();

        var firstLeft = firstChars.Min(c => c.left);
        var firstRight = firstChars.Max(c => c.right);
        var firstBottom = firstChars.Min(c => c.bottom);
        var firstTop = firstChars.Max(c => c.top);

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        var redactionArea = PdfCoordsToRedactionArea(
            firstLeft, firstBottom, firstRight, firstTop, pageHeight);

        var outputPath = Path.Combine(_tempDir, "wide_operation_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Redact only "FIRST"
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, redactionArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: This is the KEY test - "MIDDLE" and "LAST" must survive
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().NotContain("FIRST",
            "Selected word should be removed");
        textAfter.Should().Contain("MIDDLE",
            "CRITICAL: Unselected word should NOT be removed - this tests character-level filtering");
        textAfter.Should().Contain("LAST",
            "CRITICAL: Unselected word should NOT be removed - this tests character-level filtering");
    }

    [Fact]
    public void RedactArea_MultipleRedactionsOnSameLine_EachRemovesOnlySelected()
    {
        // Arrange: Create PDF with "AAA BBB CCC DDD"
        var pdfPath = Path.Combine(_tempDir, "four_words.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "AAA BBB CCC DDD");

        var chars = GetCharacterPositions(pdfPath);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var pageHeight = doc.GetPage(1).Height;

        // Find positions of "AAA" and "CCC" by text content
        var allText = string.Join("", chars.Select(c => c.text));
        var aaaStartIndex = allText.IndexOf("AAA");
        var cccStartIndex = allText.IndexOf("CCC");

        aaaStartIndex.Should().Be(0, "AAA should be at start");
        cccStartIndex.Should().BeGreaterThan(0, "CCC should be found after AAA and BBB");

        // Redact "AAA" (first 3 chars)
        var aaaChars = chars.Skip(aaaStartIndex).Take(3).ToList();
        var aaaArea = PdfCoordsToRedactionArea(
            aaaChars.Min(c => c.left), aaaChars.Min(c => c.bottom),
            aaaChars.Max(c => c.right), aaaChars.Max(c => c.top),
            pageHeight);

        // Redact "CCC"
        var cccChars = chars.Skip(cccStartIndex).Take(3).ToList();
        var cccArea = PdfCoordsToRedactionArea(
            cccChars.Min(c => c.left), cccChars.Min(c => c.bottom),
            cccChars.Max(c => c.right), cccChars.Max(c => c.top),
            pageHeight);

        var outputPath = Path.Combine(_tempDir, "four_words_redacted.pdf");
        File.Copy(pdfPath, outputPath, true);

        // Act: Apply both redactions
        using (var pdfDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify))
        {
            var page = pdfDoc.Pages[0];
            _redactionService.RedactArea(page, aaaArea, renderDpi: 72);
            _redactionService.RedactArea(page, cccArea, renderDpi: 72);
            pdfDoc.Save(outputPath);
        }

        // Assert: Only "BBB" and "DDD" remain
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);

        textAfter.Should().NotContain("AAA", "First redaction should remove AAA");
        textAfter.Should().Contain("BBB", "BBB should survive both redactions");
        textAfter.Should().NotContain("CCC", "Second redaction should remove CCC");
        textAfter.Should().Contain("DDD", "DDD should survive both redactions");
    }
}
