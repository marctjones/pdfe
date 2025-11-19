using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using Xunit;

namespace PdfEditor.Tests.Unit;

public class PdfSearchServiceTests : IDisposable
{
    private readonly PdfSearchService _searchService;
    private readonly string _testPdfPath;
    private readonly string _testOutputDir;

    public PdfSearchServiceTests()
    {
        var logger = new Mock<ILogger<PdfSearchService>>().Object;
        _searchService = new PdfSearchService(logger);

        _testOutputDir = Path.Combine(Path.GetTempPath(), "PdfSearchTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputDir);

        // Create a test PDF with known text content
        _testPdfPath = Path.Combine(_testOutputDir, "search_test.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(_testPdfPath, new[]
        {
            "Hello World! This is page one.",
            "The quick brown fox jumps over the lazy dog.",
            "Testing search functionality with multiple words."
        });
    }

    [Fact]
    public void Search_FindsSimpleText_ReturnsMatches()
    {
        // Act
        var results = _searchService.Search(_testPdfPath, "Hello");

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(m => m.MatchedText.Contains("Hello"));
        results.First().PageIndex.Should().Be(0);
    }

    [Fact]
    public void Search_CaseSensitive_RespectsCase()
    {
        // Act - Case insensitive (default)
        var resultsInsensitive = _searchService.Search(_testPdfPath, "hello", caseSensitive: false);

        // Act - Case sensitive
        var resultsSensitive = _searchService.Search(_testPdfPath, "hello", caseSensitive: true);

        // Assert
        resultsInsensitive.Should().NotBeEmpty();
        resultsSensitive.Should().BeEmpty(); // "hello" lowercase not in document
    }

    [Fact]
    public void Search_WholeWordsOnly_FindsCompleteWords()
    {
        // Act - Whole words
        var wholeWordResults = _searchService.Search(_testPdfPath, "fox", wholeWordsOnly: true);

        // Act - Partial match
        var partialResults = _searchService.Search(_testPdfPath, "ox", wholeWordsOnly: true);

        // Assert
        wholeWordResults.Should().NotBeEmpty();
        partialResults.Should().BeEmpty(); // "ox" is not a complete word
    }

    [Fact]
    public void Search_MultipleMatches_ReturnsAllOccurrences()
    {
        // Create PDF with repeated word
        var repeatedPdfPath = Path.Combine(_testOutputDir, "repeated.pdf");
        TestPdfGenerator.CreateTextOnlyPdf(repeatedPdfPath, new[]
        {
            "The word test appears multiple times. Test is important. Testing test again."
        });

        // Act
        var results = _searchService.Search(repeatedPdfPath, "test", caseSensitive: false);

        // Assert
        results.Should().HaveCountGreaterOrEqualTo(3); // "test", "Test", "Testing", "test"
    }

    [Fact]
    public void Search_NonExistentText_ReturnsEmpty()
    {
        // Act
        var results = _searchService.Search(_testPdfPath, "XYZ123NotInDocument");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_MultiplePages_FindsMatchesAcrossPages()
    {
        // Act
        var results = _searchService.Search(_testPdfPath, "the", caseSensitive: false);

        // Assert
        results.Should().NotBeEmpty();
        var pageIndices = results.Select(r => r.PageIndex).Distinct().ToList();
        pageIndices.Should().Contain(1); // "The quick brown fox..." on page 2 (index 1)
    }

    [Fact]
    public void Search_EmptySearchTerm_ReturnsEmpty()
    {
        // Act
        var results = _searchService.Search(_testPdfPath, "");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_InvalidPdfPath_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<FileNotFoundException>(() =>
            _searchService.Search("nonexistent.pdf", "test"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            Directory.Delete(_testOutputDir, true);
        }
    }
}
