using FluentAssertions;
using Pdfe.Core.Document;
using Xunit;

namespace Pdfe.Core.Tests.Document;

/// <summary>
/// Tests for PageCollection functionality.
/// </summary>
public class PageCollectionTests
{
    [Fact]
    public void Pages_ReturnsAllPages()
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        // Act
        using var doc = PdfDocument.Open(pdfPath);

        // Assert
        doc.Pages.Count.Should().Be(doc.PageCount);
        foreach (var page in doc.Pages)
        {
            page.Should().NotBeNull();
            page.Width.Should().BeGreaterThan(0);
            page.Height.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Pages_IndexerReturnsCorrectPage()
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        using var doc = PdfDocument.Open(pdfPath);
        Skip.If(doc.PageCount < 1, "PDF needs at least 1 page");

        // Act
        var page = doc.Pages[0];

        // Assert
        page.Should().NotBeNull();
        page.PageNumber.Should().Be(1); // 1-based page number
    }

    [Fact]
    public void Pages_InvalidIndexThrowsException()
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        using var doc = PdfDocument.Open(pdfPath);

        // Act & Assert
        var act1 = () => doc.Pages[-1];
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => doc.Pages[doc.PageCount];
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PageRotation_CanBeSetAndRead()
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.Pages[0];
        var originalRotation = page.Rotation;

        // Act
        page.Rotation = 90;

        // Assert
        page.Rotation.Should().Be(90);

        // Reset
        page.Rotation = originalRotation;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void PageRotation_AcceptsValidValues(int degrees)
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.Pages[0];

        // Act
        page.Rotation = degrees;

        // Assert
        page.Rotation.Should().Be(degrees);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(135)]
    [InlineData(225)]
    [InlineData(315)]
    public void PageRotation_RejectsInvalidValues(int degrees)
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.Pages[0];

        // Act & Assert
        var act = () => page.Rotation = degrees;
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PageRotation_NormalizesNegativeValues()
    {
        // Arrange
        var pdfPath = GetTestPdfPath();
        Skip.If(string.IsNullOrEmpty(pdfPath), "No test PDF available");

        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.Pages[0];

        // Act - set -90 which should normalize to 270
        page.Rotation = -90;

        // Assert
        page.Rotation.Should().Be(270);
    }

    private static string? GetTestPdfPath()
    {
        // Try to find a test PDF
        var possiblePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "test.pdf"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "Resources", "test.pdf"),
            "/home/marc/Projects/pdfe/test-pdfs/sample-pdfs/birth-cert-sample.pdf",
            "/home/marc/Projects/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_A-1b/6.1 File structure/6.1.2 File header/veraPDF test suite 6-1-2-t02-pass-a.pdf"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
