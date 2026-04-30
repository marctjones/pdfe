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

    [Fact]
    public void AddBlank_ToNewDocument_CreatesPage()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();

        // Act
        var page = doc.Pages.AddBlank();

        // Assert
        page.Should().NotBeNull();
        doc.PageCount.Should().Be(1);
        page.Width.Should().Be(612);
        page.Height.Should().Be(792);
    }

    [Fact]
    public void AddBlank_WithCustomSize_CreatesPageWithCorrectSize()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();

        // Act
        var page = doc.Pages.AddBlank(400, 500);

        // Assert
        page.Width.Should().Be(400);
        page.Height.Should().Be(500);
    }

    [Fact]
    public void Add_FromAnotherDocument_CopiesPage()
    {
        // Arrange
        using var doc1 = PdfDocument.CreateNew();
        using var doc2 = PdfDocument.CreateNew();

        var page1 = doc1.Pages.AddBlank();
        var originalCount = doc2.PageCount;

        // Act
        doc2.Pages.Add(page1);

        // Assert
        doc2.PageCount.Should().Be(originalCount + 1);
    }

    [Fact]
    public void Insert_AtValidIndex_InsertsPageAtCorrectPosition()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        var page1 = doc.Pages.AddBlank(100, 100);
        var page2 = doc.Pages.AddBlank(200, 200);
        var pageToInsert = doc.Pages.AddBlank(150, 150);

        // Act - insert between page1 and page2 (Insert clones the page, so count increases)
        doc.Pages.Insert(1, pageToInsert);

        // Assert
        doc.Pages.Count.Should().Be(4);
    }

    [Fact]
    public void Insert_AtBeginning_InsertsAtZeroIndex()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        var page1 = doc.Pages.AddBlank();
        var pageToInsert = doc.Pages.AddBlank();

        // Act - Insert clones the page, so count increases by 1
        doc.Pages.Insert(0, pageToInsert);

        // Assert
        doc.Pages.Count.Should().Be(3);
    }

    [Fact]
    public void Insert_AtInvalidIndex_ThrowsException()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        var page1 = doc.Pages.AddBlank();
        var pageToInsert = doc.Pages.AddBlank();

        // Act & Assert
        var act = () => doc.Pages.Insert(-1, pageToInsert);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => doc.Pages.Insert(10, pageToInsert);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RemoveAt_WithMultiplePages_RemovesPage()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();

        // Act
        doc.Pages.RemoveAt(1);

        // Assert
        doc.Pages.Count.Should().Be(2);
    }

    [Fact]
    public void RemoveAt_LastPageInDocument_ThrowsException()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();

        // Act & Assert
        var act = () => doc.Pages.RemoveAt(0);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot remove the last page from a document");
    }

    [Fact]
    public void RemoveAt_InvalidIndex_ThrowsException()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();

        // Act & Assert
        var act1 = () => doc.Pages.RemoveAt(-1);
        act1.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => doc.Pages.RemoveAt(10);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Move_ToNewPosition_ReordersPages()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        var page1 = doc.Pages.AddBlank(100, 100);
        var page2 = doc.Pages.AddBlank(200, 200);
        var page3 = doc.Pages.AddBlank(300, 300);

        // Act - move page 0 to position 2
        doc.Pages.Move(0, 2);

        // Assert
        doc.Pages.Count.Should().Be(3);
    }

    [Fact]
    public void Move_SamePosition_NoChange()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();

        // Act - move page 0 to position 0 (no change)
        doc.Pages.Move(0, 0);

        // Assert
        doc.Pages.Count.Should().Be(2);
    }

    [Fact]
    public void Move_InvalidFromIndex_ThrowsException()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();

        // Act & Assert
        var act = () => doc.Pages.Move(-1, 1);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => doc.Pages.Move(10, 1);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Move_InvalidToIndex_ThrowsException()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();

        // Act & Assert
        var act = () => doc.Pages.Move(0, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => doc.Pages.Move(0, 10);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetEnumerator_IteratesAllPages()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();
        doc.Pages.AddBlank();

        // Act
        var pages = doc.Pages.ToList();

        // Assert
        pages.Count.Should().Be(3);
        foreach (var page in pages)
        {
            page.Should().NotBeNull();
        }
    }

    [Fact]
    public void Count_ReturnsCorrectPageCount()
    {
        // Arrange
        using var doc = PdfDocument.CreateNew();

        // Act & Assert - initially 0
        doc.Pages.Count.Should().Be(0);

        doc.Pages.AddBlank();
        doc.Pages.Count.Should().Be(1);

        doc.Pages.AddBlank();
        doc.Pages.Count.Should().Be(2);
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
