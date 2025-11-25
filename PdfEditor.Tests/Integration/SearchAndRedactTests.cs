using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for Search & Redact functionality
/// </summary>
public class SearchAndRedactTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public SearchAndRedactTests()
    {
        _loggerFactory = new NullLoggerFactory();
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private string CreateTempFile(string extension = ".pdf")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    #region TextSearchService Tests

    [Fact]
    public void FindText_SimpleTerm_ShouldReturnMatches()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiTextPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var service = new TextSearchService(_loggerFactory.CreateLogger<TextSearchService>());

        // Act
        var matches = service.FindText(document, "CONFIDENTIAL", new SearchOptions());

        // Assert
        matches.Should().NotBeEmpty("PDF contains 'CONFIDENTIAL' text");
        matches.All(m => m.MatchedText.Contains("CONFIDENTIAL", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public void FindText_CaseInsensitive_ShouldMatchAllCases()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "TEST test Test");

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var service = new TextSearchService(_loggerFactory.CreateLogger<TextSearchService>());

        // Act
        var matches = service.FindText(document, "test", new SearchOptions { CaseSensitive = false });

        // Assert
        matches.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void FindText_NotFound_ShouldReturnEmpty()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "Hello World");

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var service = new TextSearchService(_loggerFactory.CreateLogger<TextSearchService>());

        // Act
        var matches = service.FindText(document, "NOTFOUND12345", new SearchOptions());

        // Assert
        matches.Should().BeEmpty();
    }

    [Fact]
    public void FindPattern_Regex_ShouldMatchPattern()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateComplexContentPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var service = new TextSearchService(_loggerFactory.CreateLogger<TextSearchService>());

        // Act - Search for SSN pattern
        var matches = service.FindPattern(document, @"\d{3}-\d{2}-\d{4}", new SearchOptions());

        // Assert - Complex content PDF has "123-45-6789"
        matches.Should().NotBeEmpty("PDF should contain SSN pattern");
    }

    #endregion

    #region PIIPatternMatcher Tests

    [Theory]
    [InlineData("123-45-6789", true)]
    [InlineData("123 45 6789", true)]
    [InlineData("12-345-6789", false)]
    [InlineData("000-00-0000", false)] // Invalid SSN
    public void PIIPatternMatcher_SSN_ShouldValidateCorrectly(string input, bool shouldMatch)
    {
        // Arrange
        var matcher = new PIIPatternMatcher(_loggerFactory.CreateLogger<PIIPatternMatcher>());
        var text = $"SSN: {input}";

        // Act
        var matches = matcher.FindPII(text, PIIType.SSN);

        // Assert
        if (shouldMatch)
            matches.Should().NotBeEmpty($"'{input}' should be recognized as valid SSN");
        else
            matches.Should().BeEmpty($"'{input}' should NOT be recognized as valid SSN");
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("user.name+tag@example.co.uk", true)]
    [InlineData("invalid@", false)]
    [InlineData("@example.com", false)]
    public void PIIPatternMatcher_Email_ShouldValidateCorrectly(string input, bool shouldMatch)
    {
        // Arrange
        var matcher = new PIIPatternMatcher(_loggerFactory.CreateLogger<PIIPatternMatcher>());
        var text = $"Contact: {input}";

        // Act
        var matches = matcher.FindPII(text, PIIType.Email);

        // Assert
        if (shouldMatch)
            matches.Should().NotBeEmpty($"'{input}' should be recognized as valid email");
        else
            matches.Should().BeEmpty($"'{input}' should NOT be recognized as valid email");
    }

    [Fact]
    public void PIIPatternMatcher_CreditCard_ShouldValidateLuhn()
    {
        // Arrange
        var matcher = new PIIPatternMatcher(_loggerFactory.CreateLogger<PIIPatternMatcher>());

        // Valid Visa test number (passes Luhn)
        var validCC = "4111-1111-1111-1111";
        var invalidCC = "4111-1111-1111-1112"; // Fails Luhn

        // Act
        var validMatch = new TextMatch { MatchedText = validCC };
        var invalidMatch = new TextMatch { MatchedText = invalidCC };

        // Assert
        matcher.ValidateMatch(validMatch, PIIType.CreditCard).Should().BeTrue();
        matcher.ValidateMatch(invalidMatch, PIIType.CreditCard).Should().BeFalse();
    }

    [Fact]
    public void PIIPatternMatcher_FindAllPII_ShouldDetectMultipleTypes()
    {
        // Arrange
        var matcher = new PIIPatternMatcher(_loggerFactory.CreateLogger<PIIPatternMatcher>());
        var text = "Contact John at john@email.com or (555) 123-4567. SSN: 123-45-6789";

        // Act
        var matches = matcher.FindAllPII(text);

        // Assert
        matches.Should().HaveCountGreaterThan(1);
        matches.Select(m => m.PIIType).Should().Contain(PIIType.Email);
    }

    #endregion

    #region BatchRedactService Tests

    [Fact]
    public void BatchRedactService_RedactMatches_ShouldRedactAllMatches()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiTextPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var matches = new List<TextMatch>
        {
            new TextMatch
            {
                PageNumber = 1,
                BoundingBox = new Rect(100, 100, 100, 20),
                MatchedText = "CONFIDENTIAL"
            }
        };

        var service = new BatchRedactService(
            _loggerFactory.CreateLogger<BatchRedactService>(),
            _loggerFactory);

        // Act
        var result = service.RedactMatches(document, matches, new RedactionOptions());

        // Assert
        result.TotalMatches.Should().Be(1);
        result.RedactedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BatchRedactService_SearchAndRedact_ShouldFindAndRedact()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateComplexContentPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var service = new BatchRedactService(
            _loggerFactory.CreateLogger<BatchRedactService>(),
            _loggerFactory);

        // Act
        var result = service.SearchAndRedact(
            document,
            "SECRET",
            new SearchOptions(),
            new RedactionOptions());

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void BatchRedactService_RedactAllPII_ShouldFindAndRedactPII()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateComplexContentPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var service = new BatchRedactService(
            _loggerFactory.CreateLogger<BatchRedactService>(),
            _loggerFactory);

        // Act
        var result = service.RedactAllPII(
            document,
            new[] { PIIType.SSN, PIIType.Email },
            new RedactionOptions());

        // Assert
        result.Should().NotBeNull();
        // ComplexContentPdf has SSN pattern "123-45-6789"
    }

    #endregion

    #region Performance Tests

    [Fact]
    [Trait("Category", "Performance")]
    public void SearchAndRedact_ShouldCompleteInReasonableTime()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiPagePdf(pdfPath, 10);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

        var service = new BatchRedactService(
            _loggerFactory.CreateLogger<BatchRedactService>(),
            _loggerFactory);

        var sw = Stopwatch.StartNew();

        // Act
        var result = service.RedactAllPII(
            document,
            new[] { PIIType.SSN, PIIType.Email, PIIType.Phone },
            new RedactionOptions());

        sw.Stop();

        // Assert
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "Search and redact on 10-page document should complete quickly");
    }

    #endregion
}
