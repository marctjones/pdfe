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
/// Tests for CharacterLevelTextFilter - verifies character-level filtering logic
/// </summary>
public class CharacterLevelTextFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CharacterLevelTextFilter _filter;
    private readonly CharacterMatcher _matcher;
    private readonly Mock<ILogger<CharacterLevelTextFilter>> _filterLoggerMock;
    private readonly Mock<ILogger<CharacterMatcher>> _matcherLoggerMock;

    public CharacterLevelTextFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CharFilterTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _matcherLoggerMock = new Mock<ILogger<CharacterMatcher>>();
        _matcher = new CharacterMatcher(_matcherLoggerMock.Object);

        _filterLoggerMock = new Mock<ILogger<CharacterLevelTextFilter>>();
        _filter = new CharacterLevelTextFilter(_matcher, _filterLoggerMock.Object);
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
    public void FilterTextOperation_NoCharactersInArea_ReturnsOriginalOperation()
    {
        // Arrange: Create PDF with "HELLO" at specific location
        var pdfPath = Path.Combine(_tempDir, "test1.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "HELLO");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        // Create text operation
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Create redaction area far from text (will not intersect)
        var redactionArea = new Rect(500, 500, 50, 20);

        // Act
        var result = _filter.FilterTextOperation(textOp, letters, redactionArea, page.Height);

        // Assert
        result.Should().NotBeNull();
        result.FallbackToOperationLevel.Should().BeFalse("character matching should succeed");
        result.Operations.Should().HaveCount(1, "should return original operation");
        result.Operations[0].Should().BeSameAs(textOp, "should be the exact same operation");
        result.RemovedText.Should().BeEmpty("nothing was removed");
    }

    [Fact]
    public void FilterTextOperation_AllCharactersInArea_ReturnsEmptyOperations()
    {
        // Arrange: Create PDF with "HELLO"
        var pdfPath = Path.Combine(_tempDir, "test2.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "HELLO");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Create redaction area that covers all letters
        var redactionArea = new Rect(
            letters.Min(l => l.GlyphRectangle.Left) - 10,
            page.Height - letters.Max(l => l.GlyphRectangle.Top) - 10,
            letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left) + 20,
            letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom) + 20);

        // Act
        var result = _filter.FilterTextOperation(textOp, letters, redactionArea, page.Height);

        // Assert
        result.Should().NotBeNull();
        result.FallbackToOperationLevel.Should().BeFalse();
        result.Operations.Should().BeEmpty("all characters removed, no operations to keep");
        result.RemovedText.Should().Be("HELLO", "all text was removed");
    }

    [Fact]
    public void FilterTextOperation_MiddleCharactersInArea_ReturnsTwoPartialOperations()
    {
        // Arrange: Create PDF with "FIRSTMIDDLELAST"
        var pdfPath = Path.Combine(_tempDir, "test3.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "FIRSTMIDDLELAST");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        var textOp = new TextOperation(new CComment())
        {
            Text = "FIRSTMIDDLELAST",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Create redaction area that covers "MIDDLE" (indices 5-10)
        // Find the letters corresponding to "MIDDLE"
        var middleLetters = letters.Skip(5).Take(6).ToList();
        var redactionArea = new Rect(
            middleLetters.Min(l => l.GlyphRectangle.Left) - 2,
            page.Height - middleLetters.Max(l => l.GlyphRectangle.Top) - 2,
            middleLetters.Max(l => l.GlyphRectangle.Right) - middleLetters.Min(l => l.GlyphRectangle.Left) + 4,
            middleLetters.Max(l => l.GlyphRectangle.Top) - middleLetters.Min(l => l.GlyphRectangle.Bottom) + 4);

        // Act
        var result = _filter.FilterTextOperation(textOp, letters, redactionArea, page.Height);

        // Assert
        result.Should().NotBeNull();
        result.FallbackToOperationLevel.Should().BeFalse();
        result.Operations.Should().HaveCount(2, "should split into 'FIRST' and 'LAST'");

        var firstOp = result.Operations[0] as PartialTextOperation;
        var lastOp = result.Operations[1] as PartialTextOperation;

        firstOp.Should().NotBeNull();
        firstOp!.DisplayText.Should().Be("FIRST");

        lastOp.Should().NotBeNull();
        lastOp!.DisplayText.Should().Be("LAST");

        result.RemovedText.Should().Be("MIDDLE", "middle text was removed");
    }

    [Fact]
    public void FilterTextOperation_FirstWordInArea_ReturnsSecondWordOperation()
    {
        // Arrange: Create PDF with "HELLO WORLD"
        var pdfPath = Path.Combine(_tempDir, "test4.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "HELLO WORLD");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO WORLD",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Create redaction area covering first 5 letters (HELLO)
        // Note: PdfPig may or may not report the space as a letter
        var firstWordLetters = letters.Take(5).ToList();
        var redactionArea = new Rect(
            firstWordLetters.Min(l => l.GlyphRectangle.Left) - 2,
            page.Height - firstWordLetters.Max(l => l.GlyphRectangle.Top) - 2,
            firstWordLetters.Max(l => l.GlyphRectangle.Right) - firstWordLetters.Min(l => l.GlyphRectangle.Left) + 4,
            firstWordLetters.Max(l => l.GlyphRectangle.Top) - firstWordLetters.Min(l => l.GlyphRectangle.Bottom) + 4);

        // Act
        var result = _filter.FilterTextOperation(textOp, letters, redactionArea, page.Height);

        // Assert
        result.Should().NotBeNull();
        result.FallbackToOperationLevel.Should().BeFalse();
        result.Operations.Should().HaveCountGreaterThanOrEqualTo(1, "should keep at least WORLD");

        // Find operation containing WORLD
        var worldOp = result.Operations.OfType<PartialTextOperation>()
            .FirstOrDefault(op => op.DisplayText.Contains("WORLD"));

        worldOp.Should().NotBeNull("WORLD should be kept");
        result.RemovedText.Should().Contain("HELLO", "HELLO should be removed");
    }

    [Fact]
    public void FilterTextOperation_MatchingFails_UsesFallback()
    {
        // Arrange: Create operation with text far from actual content
        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLO",
            BoundingBox = new Rect(500, 500, 50, 10)  // Far from any actual content
        };

        // Create a PDF to get some letters
        var pdfPath = Path.Combine(_tempDir, "test5.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "WORLD");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        var redactionArea = new Rect(100, 100, 50, 20);

        // Act
        var result = _filter.FilterTextOperation(textOp, letters, redactionArea, page.Height);

        // Assert
        result.Should().NotBeNull();
        result.FallbackToOperationLevel.Should().BeTrue("character matching should fail");
        result.Operations.Should().HaveCount(1, "should return original operation for fallback");
        result.Operations[0].Should().BeSameAs(textOp);
        result.RemovedText.Should().BeEmpty("fallback doesn't determine removed text");
    }

    [Fact]
    public void FilterTextOperation_LastWordInArea_ReturnsFirstWordOperation()
    {
        // Arrange: Create PDF with "HELLOWORLD" (no space to avoid whitespace trimming issues)
        var pdfPath = Path.Combine(_tempDir, "test6.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "HELLOWORLD");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(1);
        var letters = page.Letters.ToList();

        var textOp = new TextOperation(new CComment())
        {
            Text = "HELLOWORLD",
            BoundingBox = new Rect(
                letters.Min(l => l.GlyphRectangle.Left),
                page.Height - letters.Max(l => l.GlyphRectangle.Top),
                letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left),
                letters.Max(l => l.GlyphRectangle.Top) - letters.Min(l => l.GlyphRectangle.Bottom))
        };

        // Create redaction area covering last 5 letters (WORLD)
        var lastWordLetters = letters.Skip(5).Take(5).ToList();
        var redactionArea = new Rect(
            lastWordLetters.Min(l => l.GlyphRectangle.Left) - 2,
            page.Height - lastWordLetters.Max(l => l.GlyphRectangle.Top) - 2,
            lastWordLetters.Max(l => l.GlyphRectangle.Right) - lastWordLetters.Min(l => l.GlyphRectangle.Left) + 4,
            lastWordLetters.Max(l => l.GlyphRectangle.Top) - lastWordLetters.Min(l => l.GlyphRectangle.Bottom) + 4);

        // Act
        var result = _filter.FilterTextOperation(textOp, letters, redactionArea, page.Height);

        // Assert
        result.Should().NotBeNull();
        result.FallbackToOperationLevel.Should().BeFalse();
        result.Operations.Should().HaveCount(1, "should keep HELLO");

        var helloOp = result.Operations[0] as PartialTextOperation;
        helloOp.Should().NotBeNull();
        helloOp!.DisplayText.Should().Be("HELLO", "HELLO should be kept");
        result.RemovedText.Should().Be("WORLD", "WORLD should be removed");
    }
}
