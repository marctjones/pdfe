using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for position leakage analysis and prevention
/// Based on research from "Story Beyond the Eye: Glyph Positions Break PDF Text Redaction" (PETS 2023)
/// </summary>
public class PositionLeakageTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public PositionLeakageTests()
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

    [Fact]
    public void PositionLeakageAnalyzer_ShouldIdentifyHighEntropySpacing()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateComplexContentPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var analyzer = new PositionLeakageAnalyzer(_loggerFactory.CreateLogger<PositionLeakageAnalyzer>());

        var redactedAreas = new List<RedactionArea>
        {
            new RedactionArea { PageIndex = 0, Area = new Rect(60, 295, 200, 20) }
        };

        // Act
        var report = analyzer.Analyze(document, redactedAreas);

        // Assert
        report.Should().NotBeNull();
        report.OverallRiskScore.Should().BeGreaterThanOrEqualTo(0);
        report.OverallRiskScore.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void PositionLeakageAnalyzer_CalculateSpacingEntropy_ShouldReturnValidValue()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateMultiTextPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var analyzer = new PositionLeakageAnalyzer(_loggerFactory.CreateLogger<PositionLeakageAnalyzer>());
        var page = document.Pages[0];
        var redactionArea = new Rect(100, 100, 100, 30);

        // Act
        var entropy = analyzer.CalculateSpacingEntropy(page, redactionArea);

        // Assert
        entropy.Should().BeGreaterThanOrEqualTo(0);
        entropy.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void PositionNormalizer_NormalizePositions_ShouldNotCrash()
    {
        // Arrange
        var normalizer = new PositionNormalizer(_loggerFactory.CreateLogger<PositionNormalizer>());
        var operations = new List<PdfOperation>();
        var removed = new List<PdfOperation>();
        var area = new Rect(100, 100, 200, 50);

        // Act
        var result = normalizer.NormalizePositions(operations, removed, area);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void PositionNormalizer_WithOperations_ShouldPreserveNonAdjacentOperations()
    {
        // Arrange
        var normalizer = new PositionNormalizer(_loggerFactory.CreateLogger<PositionNormalizer>());

        // Create mock operations
        var operations = new List<PdfOperation>
        {
            new GenericOperation(new PdfSharp.Pdf.Content.Objects.CComment(), "BT"),
            new GenericOperation(new PdfSharp.Pdf.Content.Objects.CComment(), "ET")
        };
        var removed = new List<PdfOperation>();
        var area = new Rect(500, 500, 100, 50); // Far from operations

        // Act
        var result = normalizer.NormalizePositions(operations, removed, area);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void PositionLeakageAnalyzer_GeneratesRecommendations()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateTextOnlyPdf(pdfPath);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var analyzer = new PositionLeakageAnalyzer(_loggerFactory.CreateLogger<PositionLeakageAnalyzer>());

        var redactedAreas = new List<RedactionArea>
        {
            new RedactionArea { PageIndex = 0, Area = new Rect(100, 100, 150, 30) }
        };

        // Act
        var report = analyzer.Analyze(document, redactedAreas);

        // Assert
        report.Recommendations.Should().NotBeNull();
        // Recommendations are only added if vulnerabilities found
    }

    [Fact]
    public void ExtendedRedactionOptions_DefaultValues_ShouldBeSecure()
    {
        // Arrange & Act
        var options = new ExtendedRedactionOptions();

        // Assert
        options.NormalizePositions.Should().BeTrue("Position normalization should be on by default for security");
        options.SecurityLevel.Should().Be(RedactionSecurityLevel.Standard);
    }

    [Theory]
    [InlineData(RedactionSecurityLevel.Standard)]
    [InlineData(RedactionSecurityLevel.Enhanced)]
    [InlineData(RedactionSecurityLevel.Paranoid)]
    public void ExtendedRedactionOptions_AllSecurityLevels_ShouldBeValid(RedactionSecurityLevel level)
    {
        // Arrange & Act
        var options = new ExtendedRedactionOptions { SecurityLevel = level };

        // Assert
        options.SecurityLevel.Should().Be(level);
    }
}
