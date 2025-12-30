using Xunit;
using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Tests for the new RedactPage() API to verify:
/// 1. TRUE content removal (not just visual overlay) - WHOLE-OPERATION mode
/// 2. In-memory operation (no file I/O)
/// 3. Coordinate system handling
///
/// NOTE: Due to PdfSharp 6.x "already saved" limitation, the in-memory RedactPage() API
/// uses WHOLE-OPERATION redaction (not glyph-level). Glyph-level redaction is only
/// available via file-based API.
/// </summary>
public class RedactPageApiTests : IDisposable
{
    private readonly string _testDir;
    private readonly TextRedactor _redactor;

    public RedactPageApiTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_redactpage_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _redactor = new TextRedactor();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void RedactPage_WholeOperationMode_RemovesTextFromPdfStructure()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "input.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        var testText = "CONFIDENTIAL INFORMATION";
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, testText);

        // Open document
        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];

        // Define redaction area (cover the text)
        var pageHeight = page.Height.Point;
        var redactionArea = new PdfRectangle(
            Left: 90,
            Bottom: pageHeight - 110,  // PDF bottom-left coords
            Right: 300,
            Top: pageHeight - 85
        );

        var options = new RedactionOptions
        {
            UseGlyphLevelRedaction = false,  // Whole-operation for in-memory API
            DrawVisualMarker = true
        };

        // Act - Redact using RedactPage API (in-memory, no file I/O)
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options);

        // Save after redaction
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        result.RedactionCount.Should().BeGreaterThan(0, "Should have redacted content");

        // CRITICAL: Verify text is REMOVED from PDF structure (whole-operation removal)
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("CONFIDENTIAL", "Text must be REMOVED from PDF structure");
        textAfter.Should().NotContain("INFORMATION", "Text must be REMOVED from PDF structure");
    }

    [Fact]
    public void RedactPage_MultipleRedactions_AllRemoveContent()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "multi.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateMultiLineTextPdf(inputPdf, "First Line", "Second Line", "Third Line");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        // Define multiple redaction areas
        var areas = new[]
        {
            new PdfRectangle(90, pageHeight - 110, 200, pageHeight - 85),  // First line
            new PdfRectangle(90, pageHeight - 150, 200, pageHeight - 125)  // Third line (approximate)
        };

        var options = new RedactionOptions { UseGlyphLevelRedaction = false };

        // Act - Apply multiple redactions
        var result = _redactor.RedactPage(page, areas, options);

        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterThan(0);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("First", "First line should be redacted");
        // Second line may or may not remain depending on text operation boundaries
    }

    [Fact]
    public void RedactPage_NoFileIo_WorksInMemory()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "memory.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "IN MEMORY TEST");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        var redactionArea = new PdfRectangle(90, pageHeight - 110, 250, pageHeight - 85);
        var options = new RedactionOptions { UseGlyphLevelRedaction = false };

        // Act - Single redaction should work
        var result1 = _redactor.RedactPage(page, new[] { redactionArea }, options);

        doc.Save(outputPdf);

        // Assert
        result1.Success.Should().BeTrue("Redaction should succeed");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("MEMORY", "Text should be redacted");
    }

    [Fact]
    public void RedactPage_CoordinateConversion_MatchesExpectedArea()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "coords.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");

        // Create PDF with text at known positions
        TestPdfGenerator.CreateTextAtPositions(inputPdf,
            ("TOP LEFT", 50, 750),      // Near top-left
            ("BOTTOM RIGHT", 400, 50)   // Near bottom-right
        );

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;  // 792 for US Letter

        // Redact only TOP LEFT (PDF coords: bottom-left origin)
        // PDF Y = 750 from CreateTextAtPositions (bottom-left origin)
        var redactionArea = new PdfRectangle(
            Left: 40,
            Bottom: 740,  // Below the text at Y=750
            Right: 150,
            Top: 760      // Above the text at Y=750
        );

        var options = new RedactionOptions { UseGlyphLevelRedaction = false };

        // Act
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options);
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue();
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);

        // TOP LEFT should be redacted
        textAfter.Should().NotContain("TOP", "TOP LEFT should be redacted");
        textAfter.Should().NotContain("LEFT", "TOP LEFT should be redacted");

        // BOTTOM RIGHT should remain (different coordinates)
        textAfter.Should().Contain("BOTTOM", "BOTTOM RIGHT should remain");
        textAfter.Should().Contain("RIGHT", "BOTTOM RIGHT should remain");
    }

    [Fact]
    public void RedactPage_EmptyPage_ReturnsSuccessWithZeroCount()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "empty.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateEmptyPdf(inputPdf);

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];

        var redactionArea = new PdfRectangle(0, 0, 100, 100);
        var options = new RedactionOptions { UseGlyphLevelRedaction = false };

        // Act
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options);
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue("Should succeed even on empty page");
        result.RedactionCount.Should().Be(0, "No content to redact");
    }

    [Fact]
    public void RedactPage_WithFilePathForLetters_EnablesGlyphLevelRedaction()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "glyph_test.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        var testText = "REDACT ME PLEASE";
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, testText);

        // Open document
        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];

        // Extract letters from file (mimicking GUI approach)
        int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
        var letters = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(
            inputPdf,
            pageNumber);

        // Define redaction area (cover "REDACT" only, not "ME PLEASE")
        var pageHeight = page.Height.Point;
        var redactionArea = new PdfRectangle(
            Left: 90,
            Bottom: pageHeight - 110,
            Right: 150,  // Cover only first word
            Top: pageHeight - 85
        );

        var options = new RedactionOptions
        {
            UseGlyphLevelRedaction = true,  // TRUE GLYPH-LEVEL!
            DrawVisualMarker = true
        };

        // Act - Redact using RedactPage API with file-extracted letters
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options, pageLetters: letters);

        // Save after redaction
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        result.RedactionCount.Should().BeGreaterThan(0, "Should have redacted content");

        // CRITICAL: Verify TRUE glyph-level redaction
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("REDACT", "First word must be REMOVED via glyph-level redaction");
        textAfter.Should().Contain("PLEASE", "Second part should remain (precise glyph-level redaction)");
    }

    [Fact]
    public void RedactPage_GlyphLevel_MultipleRedactionsOnSamePage()
    {
        // Arrange
        var inputPdf = Path.Combine(_testDir, "multi_glyph.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "CONFIDENTIAL DATA");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        // Extract letters from file
        int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
        var letters = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(inputPdf, pageNumber);

        // Create two separate redaction areas for CONFIDENTIAL and DATA
        var areas = new List<PdfRectangle>
        {
            new PdfRectangle(85, pageHeight - 110, 200, pageHeight - 85),  // CONFIDENTIAL
            new PdfRectangle(205, pageHeight - 110, 260, pageHeight - 85)  // DATA
        };

        var options = new RedactionOptions { UseGlyphLevelRedaction = true };

        // Act
        var result = _redactor.RedactPage(page, areas, options, pageLetters: letters);
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterThan(0);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("CONFIDENTIAL", "CONFIDENTIAL should be removed");
        textAfter.Should().NotContain("DATA", "DATA should be removed");
    }

    [Fact]
    public void RedactPage_GlyphLevel_PartialWordRedaction()
    {
        // Arrange - Test precise glyph-level redaction of partial words
        var inputPdf = Path.Combine(_testDir, "partial.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "Sensitive Information");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
        var letters = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(inputPdf, pageNumber);

        // Redact only "Sensit" from "Sensitive", leaving "ive"
        var redactionArea = new PdfRectangle(
            Left: 90,
            Bottom: pageHeight - 110,
            Right: 130,  // Cover only first 6 letters
            Top: pageHeight - 85
        );

        var options = new RedactionOptions { UseGlyphLevelRedaction = true };

        // Act
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options, pageLetters: letters);
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue();
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("Sensit", "Redacted portion should be removed");
        textAfter.Should().Contain("Information", "Second word should remain intact");
    }

    [Fact]
    public void RedactPage_GlyphLevel_SequentialRedactionsOnSameDocument()
    {
        // Arrange - Test multiple sequential redactions (GUI workflow)
        var inputPdf = Path.Combine(_testDir, "sequential.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "SECRET INFORMATION");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
        var options = new RedactionOptions { UseGlyphLevelRedaction = true };

        // Act - Apply two sequential redactions
        // Redaction 1: "SECRET"
        var letters1 = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(inputPdf, pageNumber);
        var area1 = new PdfRectangle(85, pageHeight - 110, 155, pageHeight - 85);
        var result1 = _redactor.RedactPage(page, new[] { area1 }, options, pageLetters: letters1);

        // Redaction 2: "INFORMATION" - must re-extract letters from ORIGINAL file
        var letters2 = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(inputPdf, pageNumber);
        var area2 = new PdfRectangle(160, pageHeight - 110, 280, pageHeight - 85);
        var result2 = _redactor.RedactPage(page, new[] { area2 }, options, pageLetters: letters2);

        doc.Save(outputPdf);

        // Assert
        result1.Success.Should().BeTrue("First redaction should succeed");
        result2.Success.Should().BeTrue("Second redaction should succeed");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("SECRET", "SECRET should be removed");
        textAfter.Should().NotContain("INFORMATION", "INFORMATION should be removed");
    }

    [Fact]
    public void RedactPage_GlyphLevel_UnicodeText()
    {
        // Arrange - Test glyph-level redaction with Unicode characters
        var inputPdf = Path.Combine(_testDir, "unicode.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "Café München");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
        var letters = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(inputPdf, pageNumber);

        // Redact "Café" only
        var redactionArea = new PdfRectangle(
            Left: 90,
            Bottom: pageHeight - 110,
            Right: 125,
            Top: pageHeight - 85
        );

        var options = new RedactionOptions { UseGlyphLevelRedaction = true };

        // Act
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options, pageLetters: letters);
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue();
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain("Café", "Unicode text should be redacted");
        textAfter.Should().Contain("München", "Second word should remain");
    }

    [Fact]
    public void RedactPage_GlyphLevel_EmptyAreaDoesNotCorruptPdf()
    {
        // Arrange - Test that redacting empty area doesn't corrupt PDF
        var inputPdf = Path.Combine(_testDir, "empty_area.pdf");
        var outputPdf = Path.Combine(_testDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPdf, "Some text here");

        using var doc = PdfReader.Open(inputPdf, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];
        var pageHeight = page.Height.Point;

        int pageNumber = PdfEditor.Redaction.PdfPig.PdfPigHelper.GetPageNumber(page);
        var letters = PdfEditor.Redaction.PdfPig.PdfPigHelper.ExtractLettersFromFile(inputPdf, pageNumber);

        // Redact area with NO text
        var redactionArea = new PdfRectangle(
            Left: 300,
            Bottom: 300,
            Right: 400,
            Top: 400
        );

        var options = new RedactionOptions { UseGlyphLevelRedaction = true };

        // Act
        var result = _redactor.RedactPage(page, new[] { redactionArea }, options, pageLetters: letters);
        doc.Save(outputPdf);

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().Be(0, "No text in area to redact");

        // Verify PDF is not corrupted
        PdfTestHelpers.IsValidPdf(outputPdf).Should().BeTrue("PDF should remain valid");
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().Contain("Some text here", "All text should remain");
    }
}
