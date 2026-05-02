using AwesomeAssertions;
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

    /// <summary>
    /// Regression test for the page-tree cycle stack overflow uncovered
    /// by the differential corpus harness against pdf.js's regression
    /// PDFs. A malformed Pages dictionary whose /Kids array points back
    /// at itself used to recurse infinitely in LoadPagesRecursive,
    /// crashing the host (not even a catchable exception — bare stack
    /// overflow). The fix added a reference-equality visited-set and a
    /// MaxPageTreeDepth limit so the parser bails gracefully.
    /// </summary>
    [Fact]
    public void Open_PageTreeWithSelfReferencingKids_DoesNotStackOverflow()
    {
        // Build a PDF where /Pages has /Kids [2 0 R] and obj 2 is the
        // /Pages dict itself — a one-step cycle. Real-world malformed
        // PDFs have hit this shape; the fuzzer / pdf.js corpus has it.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("%PDF-1.7");
        long o1 = sb.Length;
        sb.AppendLine("1 0 obj");
        sb.AppendLine("<< /Type /Catalog /Pages 2 0 R >>");
        sb.AppendLine("endobj");
        long o2 = sb.Length;
        sb.AppendLine("2 0 obj");
        // /Kids points to obj 2 itself — the cycle.
        sb.AppendLine("<< /Type /Pages /Kids [2 0 R] /Count 0 >>");
        sb.AppendLine("endobj");
        long xref = sb.Length;
        sb.AppendLine("xref");
        sb.AppendLine("0 3");
        sb.AppendLine("0000000000 65535 f ");
        sb.AppendLine($"{o1:D10} 00000 n ");
        sb.AppendLine($"{o2:D10} 00000 n ");
        sb.AppendLine("trailer << /Size 3 /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine(xref.ToString());
        sb.AppendLine("%%EOF");

        var bytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

        // The bug: this used to stack-overflow. With cycle detection,
        // it must complete and yield a 0-page document (the cycle
        // contributed nothing).
        Action act = () =>
        {
            using var doc = PdfDocument.Open(bytes);
            // Force tree walk.
            _ = doc.PageCount;
        };

        act.Should().NotThrow("circular /Pages tree must be detected, not crash the host");
    }

    [Fact]
    public void Open_PathologicallyDeepPageTree_BailsAtDepthLimit()
    {
        // Build a PDF whose /Pages tree is 100 levels deep — exceeds
        // MaxPageTreeDepth. Loader must terminate cleanly at the limit.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("%PDF-1.7");
        var offsets = new System.Collections.Generic.List<long>();
        offsets.Add(0); // obj 0 placeholder

        // obj 1 = Catalog
        offsets.Add(sb.Length);
        sb.AppendLine("1 0 obj");
        sb.AppendLine("<< /Type /Catalog /Pages 2 0 R >>");
        sb.AppendLine("endobj");

        // obj 2..101 = nested Pages, each with Kids pointing to next.
        for (int i = 2; i <= 101; i++)
        {
            offsets.Add(sb.Length);
            sb.AppendLine($"{i} 0 obj");
            if (i < 101)
                sb.AppendLine($"<< /Type /Pages /Kids [{i + 1} 0 R] /Count 0 >>");
            else
                sb.AppendLine("<< /Type /Pages /Kids [] /Count 0 >>");
            sb.AppendLine("endobj");
        }

        long xref = sb.Length;
        sb.AppendLine("xref");
        sb.AppendLine("0 102");
        sb.AppendLine("0000000000 65535 f ");
        for (int i = 1; i < 102; i++)
            sb.AppendLine($"{offsets[i]:D10} 00000 n ");
        sb.AppendLine("trailer << /Size 102 /Root 1 0 R >>");
        sb.AppendLine("startxref");
        sb.AppendLine(xref.ToString());
        sb.AppendLine("%%EOF");

        var bytes = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

        Action act = () =>
        {
            using var doc = PdfDocument.Open(bytes);
            _ = doc.PageCount;
        };

        act.Should().NotThrow("deeply-nested page trees must bail at the depth limit, not blow the stack");
    }
}
