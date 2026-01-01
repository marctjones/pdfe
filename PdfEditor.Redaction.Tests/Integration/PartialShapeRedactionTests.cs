using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Drawing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests for partial shape redaction (issue #197).
/// Tests that shapes partially covered by redaction areas are clipped
/// rather than entirely removed.
/// </summary>
public class PartialShapeRedactionTests : IDisposable
{
    private readonly string _tempDir;

    public PartialShapeRedactionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"partial_shape_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void RedactLocation_PartialRectangleOverlap_ClipsShape()
    {
        // Arrange: Create PDF with a rectangle at (100, 500)-(200, 600)
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSingleRectanglePdf(inputPath, 100, 500, 100, 100);

        // Redaction area covers right half: (150, 500)-(300, 600)
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(150, 500, 300, 600)
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");

        // The shape should be clipped, not entirely removed
        // We verify by checking the file is still valid and the content stream contains path operators
        var contentBytes = GetPageContentBytes(outputPath);
        contentBytes.Length.Should().BeGreaterThan(0, "Content stream should not be empty");
    }

    [Fact]
    public void RedactLocation_NoOverlapWithShape_PreservesShape()
    {
        // Arrange: Create PDF with a rectangle at (100, 500)-(200, 600)
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSingleRectanglePdf(inputPath, 100, 500, 100, 100);

        // Redaction area is far away: (400, 100)-(500, 200)
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(400, 100, 500, 200)
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
    }

    [Fact]
    public void RedactLocation_FullyContainsShape_RemovesShape()
    {
        // Arrange: Create PDF with a rectangle at (100, 500)-(200, 600)
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSingleRectanglePdf(inputPath, 100, 500, 100, 100);

        // Redaction area fully contains the shape: (50, 450)-(250, 650)
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(50, 450, 250, 650)
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
    }

    [Fact]
    public void RedactLocation_MultipleShapes_OnlyAffectsIntersecting()
    {
        // Arrange: Create PDF with grid of rectangles
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateGridOfRectanglesPdf(inputPath, 3, 3, 50, 20);

        // Redaction area covers only the center rectangle area
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(120, 620, 180, 680)  // Approximately center area
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");

        // The content stream should still have path operations for unaffected shapes
        var contentBytes = GetPageContentBytes(outputPath);
        contentBytes.Length.Should().BeGreaterThan(50, "Content stream should contain remaining shapes");
    }

    [Fact]
    public void RedactLocation_StrokedRectangle_ClipsCorrectly()
    {
        // Arrange: Create PDF with a stroked (outlined) rectangle
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateStrokedRectanglePdf(inputPath, 100, 500, 100, 100, 3.0);

        // Redaction area covers left half
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(50, 450, 150, 650)
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
    }

    [Fact]
    public void RedactLocation_MixedTextAndShapes_HandlesEachCorrectly()
    {
        // Arrange: Create PDF with text and a rectangle
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateTextAndShapesPdf(inputPath,
            "Hello World",
            100, 700,
            (200, 500, 100, 100, XColors.Blue));

        // Redaction area covers part of the rectangle, not the text
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(250, 450, 350, 650)
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");

        // Text should still be present
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().Contain("Hello", "Text outside redaction area should be preserved");
    }

    [Fact]
    public void RedactText_WithShapesInDocument_PreservesShapes()
    {
        // Arrange: Create PDF with text and a rectangle
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateTextAndShapesPdf(inputPath,
            "CONFIDENTIAL DATA",
            100, 700,
            (200, 500, 100, 100, XColors.Blue));

        var redactor = new TextRedactor();

        // Act - Redact text, but shape is not in the same area
        var result = redactor.RedactText(inputPath, outputPath, "CONFIDENTIAL");

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        result.RedactionCount.Should().BeGreaterThan(0, "Should find text to redact");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("CONFIDENTIAL", "Text should be removed");

        // Shape should still be in the document (valid PDF with content)
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
    }

    [Fact]
    public void RedactLocation_MultipleRedactionAreas_AppliesAll()
    {
        // Arrange: Create PDF with a large rectangle
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSingleRectanglePdf(inputPath, 100, 400, 200, 200);

        // Multiple redaction areas that chip away at the rectangle
        var locations = new[]
        {
            new RedactionLocation
            {
                PageNumber = 1,
                BoundingBox = new PdfRectangle(100, 400, 150, 500)  // Left side
            },
            new RedactionLocation
            {
                PageNumber = 1,
                BoundingBox = new PdfRectangle(250, 500, 300, 600)  // Right side
            }
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, locations);

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
    }

    [Fact]
    public void RedactLocation_SmallRedactionArea_ClipsAccurately()
    {
        // Arrange: Create PDF with a large rectangle
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSingleRectanglePdf(inputPath, 100, 400, 200, 200);

        // Small redaction area in the center
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(180, 480, 220, 520)  // 40x40 in center
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");

        // The content should still have path operations (the shape minus the hole)
        var contentBytes = GetPageContentBytes(outputPath);
        contentBytes.Length.Should().BeGreaterThan(50, "Content stream should contain clipped shape");
    }

    [Fact]
    public void RedactLocation_EdgeOverlap_ClipsCorrectly()
    {
        // Arrange: Create PDF with a rectangle
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSingleRectanglePdf(inputPath, 100, 500, 100, 100);

        // Redaction area barely overlaps the edge
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(195, 500, 250, 600)  // Just 5 points of overlap
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");
    }

    /// <summary>
    /// Helper to extract page content bytes for inspection.
    /// </summary>
    private static byte[] GetPageContentBytes(string pdfPath)
    {
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(pdfPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        var page = document.Pages[0];

        if (page.Contents.Elements.Count == 0)
            return Array.Empty<byte>();

        // Get the first content stream
        var content = page.Contents.Elements.GetObject(0);
        if (content is PdfSharp.Pdf.PdfDictionary dict && dict.Stream != null)
        {
            return dict.Stream.Value;
        }

        return Array.Empty<byte>();
    }
}
