using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.Content.Objects;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Tests for CharacterMatcher - verifies character-to-letter matching logic
/// </summary>
public class CharacterMatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CharacterMatcher _matcher;
    private readonly Mock<ILogger<CharacterMatcher>> _loggerMock;

    public CharacterMatcherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CharMatcherTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _loggerMock = new Mock<ILogger<CharacterMatcher>>();
        _matcher = new CharacterMatcher(_loggerMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public void MatchLettersToOperation_SimpleText_MatchesAllCharacters()
    {
        // Arrange: Create PDF with "HELLO"
        var pdfPath = Path.Combine(_tempDir, "simple.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "HELLO");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        // Create a TextOperation that represents this text
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Act
        var result = _matcher.MatchLettersToOperation(textOp, letters, page.Height);

        // Assert
        result.Should().NotBeNull("letters should match the operation");
        result.Should().HaveCount(5, "HELLO has 5 characters");
        result![0].Value.Should().Be("H");
        result[1].Value.Should().Be("E");
        result[2].Value.Should().Be("L");
        result[3].Value.Should().Be("L");
        result[4].Value.Should().Be("O");
    }

    [Fact]
    public void MatchLettersToOperation_TextWithSpaces_MatchesSpacesToo()
    {
        // Arrange: Create PDF with "A B C"
        var pdfPath = Path.Combine(_tempDir, "spaces.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "A B C");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        // PdfPig DOES report spaces as letters, so we expect 5 letters: A, space, B, space, C
        letters.Should().HaveCount(5, "A B C should have 5 characters including spaces");

        var textOp = new TextOperation(new CComment())
        {
            Text = "A B C",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Act
        var result = _matcher.MatchLettersToOperation(textOp, letters, page.Height);

        // Assert
        result.Should().NotBeNull();
        // All 5 characters should be matched (including spaces)
        result.Should().HaveCount(5, "all characters including spaces should be matched");
        result.Should().ContainKey(0, "A at index 0 should be matched");
        result.Should().ContainKey(1, "space at index 1 should be matched");
        result.Should().ContainKey(2, "B at index 2 should be matched");
        result.Should().ContainKey(3, "space at index 3 should be matched");
        result.Should().ContainKey(4, "C at index 4 should be matched");
    }

    [Fact]
    public void IsLetterInRedactionArea_CenterInside_ReturnsTrue()
    {
        // Arrange: Create PDF and get a letter
        var pdfPath = Path.Combine(_tempDir, "test.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "X");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letter = page.Letters.First();

        // Create redaction area that covers the letter's center
        var centerX = (letter.GlyphRectangle.Left + letter.GlyphRectangle.Right) / 2.0;
        var centerY = (letter.GlyphRectangle.Bottom + letter.GlyphRectangle.Top) / 2.0;

        // Convert to Avalonia coordinates (top-left origin)
        var avaloniaY = page.Height - centerY;

        var area = new Rect(
            centerX - 5,
            avaloniaY - 5,
            10,
            10);

        // Act
        var result = _matcher.IsLetterInRedactionArea(letter, area, page.Height);

        // Assert
        result.Should().BeTrue("letter center is inside redaction area");
    }

    [Fact]
    public void IsLetterInRedactionArea_CenterOutside_ReturnsFalse()
    {
        // Arrange: Create PDF and get a letter
        var pdfPath = Path.Combine(_tempDir, "test2.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "X");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letter = page.Letters.First();

        // Create redaction area far from the letter
        var area = new Rect(500, 500, 10, 10);

        // Act
        var result = _matcher.IsLetterInRedactionArea(letter, area, page.Height);

        // Assert
        result.Should().BeFalse("letter center is outside redaction area");
    }

    [Fact]
    public void MatchLettersToOperation_EmptyText_ReturnsNull()
    {
        // Arrange
        var textOp = new TextOperation(new CComment())
        {
            Text = "",
            BoundingBox = new Rect()
        };
        var letters = new List<Letter>();

        // Act
        var result = _matcher.MatchLettersToOperation(textOp, letters, 792);

        // Assert
        result.Should().BeNull("empty text cannot be matched");
    }

    [Fact]
    public void MatchLettersToOperation_NoMatchingLetters_ReturnsNull()
    {
        // Arrange: Create operation with text in a completely different area
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(500, 500, 50, 10)  // Far from actual content
        };

        // Create a PDF to get some letters
        var pdfPath = Path.Combine(_tempDir, "different.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "WORLD");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        // Actual letters are around x=100, y=692 based on test output
        // Our operation bbox is at x=500, y=500 which is far away

        // Act
        var result = _matcher.MatchLettersToOperation(textOp, letters, page.Height);

        // Assert
        result.Should().BeNull("no letters match the operation's bounding box with tolerance of 5 points");
    }
}
