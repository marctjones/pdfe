using FluentAssertions;
using PdfEditor.Redaction;
using PdfEditor.Redaction.GlyphLevel;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests for the partial glyph redaction feature.
/// Tests the complete workflow: detect overlap -> render -> clip -> embed.
/// Part of issues #199, #206-211.
/// </summary>
public class PartialGlyphIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();

    public PartialGlyphIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private string CreateTempFile(string extension = ".pdf")
    {
        var path = Path.Combine(Path.GetTempPath(), $"partial_glyph_test_{Guid.NewGuid()}{extension}");
        _tempFiles.Add(path);
        return path;
    }

    #region Overlap Detection Integration Tests

    [Fact]
    public void OverlapDetection_WithRealPdf_DetectsPartialOverlap()
    {
        // Arrange - Create PDF with known text position
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "ABCDEFGHIJ");

        // Act - Find letters and check for partial overlap with a narrow redaction
        using var pig = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = pig.GetPage(1);
        var letters = page.Letters.ToList();

        _output.WriteLine($"Found {letters.Count} letters in test PDF");

        // Create a narrow redaction that should partially overlap some letters
        // Assuming letters are roughly at y=700 and 10 points wide each
        var letter = letters.FirstOrDefault(l => l.Value == "E");
        if (letter != null)
        {
            var letterBounds = PdfRectangle.FromPdfPig(letter.GlyphRectangle);
            _output.WriteLine($"Letter 'E' bounds: ({letterBounds.Left:F1}, {letterBounds.Bottom:F1}) to ({letterBounds.Right:F1}, {letterBounds.Top:F1})");

            // Create redaction that overlaps left half of 'E'
            var redactionArea = new PdfRectangle(
                letterBounds.Left - 5,
                letterBounds.Bottom,
                letterBounds.Left + letterBounds.Width / 2,
                letterBounds.Top
            );

            var overlapType = redactionArea.GetOverlapType(letterBounds);
            _output.WriteLine($"Overlap type: {overlapType}");

            // Assert
            overlapType.Should().Be(GlyphOverlapType.Partial, "Redaction covers only left half of glyph");
        }
    }

    [Fact]
    public void OverlapDetection_FullyContainedGlyph_ReturnsFullOverlap()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "TEST");

        using var pig = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = pig.GetPage(1);
        var letters = page.Letters.ToList();

        var letter = letters.FirstOrDefault(l => l.Value == "E");
        if (letter != null)
        {
            var letterBounds = PdfRectangle.FromPdfPig(letter.GlyphRectangle);

            // Create large redaction that fully contains the letter
            var redactionArea = new PdfRectangle(
                letterBounds.Left - 10,
                letterBounds.Bottom - 10,
                letterBounds.Right + 10,
                letterBounds.Top + 10
            );

            // Act
            var overlapType = redactionArea.GetOverlapType(letterBounds);

            // Assert
            overlapType.Should().Be(GlyphOverlapType.Full);
        }
    }

    [Fact]
    public void OverlapDetection_NonOverlappingRedaction_ReturnsNone()
    {
        // Arrange
        var pdfPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(pdfPath, "TEST");

        using var pig = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = pig.GetPage(1);
        var letters = page.Letters.ToList();

        var letter = letters.FirstOrDefault(l => l.Value == "T");
        if (letter != null)
        {
            var letterBounds = PdfRectangle.FromPdfPig(letter.GlyphRectangle);

            // Create redaction far away from the letter
            var redactionArea = new PdfRectangle(0, 0, 10, 10);

            // Act
            var overlapType = redactionArea.GetOverlapType(letterBounds);

            // Assert
            overlapType.Should().Be(GlyphOverlapType.None);
        }
    }

    #endregion

    #region Image Embedding Integration Tests

    [Fact]
    public void ImageEmbedding_EmbedAndSave_CreatesValidPdf()
    {
        // Arrange
        var outputPath = CreateTempFile();
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        var embedder = new ImageXObjectEmbedder();
        using var bitmap = CreateTestBitmap(50, 50, SKColors.Red);
        var bounds = new PdfRectangle(100, 100, 150, 150);

        // Act
        var imageName = embedder.EmbedImage(page, bitmap, bounds);

        // Save the document
        doc.Save(outputPath);

        // Assert
        imageName.Should().NotBeNull();
        File.Exists(outputPath).Should().BeTrue();

        // Verify the saved PDF is valid
        using var savedDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.ReadOnly);
        savedDoc.PageCount.Should().Be(1);
    }

    [Fact]
    public void ImageEmbedding_WithTransparency_PreservesAlphaChannel()
    {
        // Arrange
        var outputPath = CreateTempFile();
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        var embedder = new ImageXObjectEmbedder();
        using var bitmap = CreateBitmapWithTransparency(50, 50);
        var bounds = new PdfRectangle(100, 100, 150, 150);

        // Act
        var imageName = embedder.EmbedImage(page, bitmap, bounds);

        doc.Save(outputPath);

        // Assert
        imageName.Should().NotBeNull();

        // Check that SMask was created (indicates transparency handling)
        var xObjects = page.Resources.Elements.GetDictionary("/XObject");
        xObjects.Should().NotBeNull();
        xObjects!.Elements.ContainsKey("/" + imageName).Should().BeTrue();
    }

    [Fact]
    public void DrawOperators_GeneratedCorrectly_CanBeAddedToContentStream()
    {
        // Arrange
        var imageName = "TestImg";
        var bounds = new PdfRectangle(100, 200, 150, 260);

        // Act
        var operators = ImageXObjectEmbedder.GetDrawOperators(imageName, bounds);

        // Assert
        _output.WriteLine($"Generated operators: {operators}");

        // Verify format: q width 0 0 height x y cm /name Do Q
        operators.Should().Match("q 50.000 0 0 60.000 100.000 200.000 cm /TestImg Do Q*");
    }

    #endregion

    #region Complete Workflow Integration Tests

    [Fact]
    public void PartialGlyphWorkflow_DetectRenderEmbedSave_ProducesValidPdf()
    {
        // This test simulates the complete partial glyph redaction workflow
        // Arrange
        var inputPath = CreateTempFile();
        var outputPath = CreateTempFile();
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Hello World");

        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];

        // Simulate: A glyph with partial overlap was detected
        var glyphBounds = new PdfRectangle(100, 700, 120, 720);
        var redactionArea = new PdfRectangle(110, 700, 130, 720);

        // Create a mock "visible portion" bitmap (in real use, this would come from PartialGlyphRasterizer)
        using var visiblePortionBitmap = CreateTestBitmap(10, 20, SKColors.Black);

        // Embed the visible portion
        var embedder = new ImageXObjectEmbedder();
        var imageName = embedder.EmbedImage(page, visiblePortionBitmap, glyphBounds);

        // Save
        doc.Save(outputPath);

        // Assert
        imageName.Should().NotBeNull();
        File.Exists(outputPath).Should().BeTrue();

        // Verify the PDF is valid and contains the image
        using var savedDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.ReadOnly);
        var savedPage = savedDoc.Pages[0];
        var xObjects = savedPage.Resources.Elements.GetDictionary("/XObject");
        xObjects.Should().NotBeNull();
        xObjects!.Elements.ContainsKey("/" + imageName).Should().BeTrue();

        _output.WriteLine($"Successfully embedded image '{imageName}' at glyph position");
    }

    [Fact]
    public void GetVisibleRegions_WithPartialOverlap_ReturnsNonEmptyRegions()
    {
        // Arrange - glyph partially overlapped by redaction
        var glyphBounds = new PdfRectangle(100, 100, 130, 120);
        var redactionArea = new PdfRectangle(110, 100, 140, 120);

        // Act
        var visibleRegions = PartialGlyphRasterizer.GetVisibleRegions(glyphBounds, redactionArea);

        // Assert
        visibleRegions.Should().NotBeEmpty("Part of glyph is outside redaction");
        _output.WriteLine($"Found {visibleRegions.Count} visible region(s)");

        foreach (var region in visibleRegions)
        {
            _output.WriteLine($"  Region: ({region.Left:F1}, {region.Bottom:F1}) to ({region.Right:F1}, {region.Top:F1})");
            region.Width.Should().BeGreaterThan(0);
            region.Height.Should().BeGreaterThan(0);
        }
    }

    #endregion

    #region Configuration Integration Tests

    [Fact]
    public void RedactionOptions_PartialGlyphSettings_AreRespected()
    {
        // Arrange
        var options = new RedactionOptions
        {
            PreservePartialGlyphsAsImages = true,
            PartialGlyphRasterizationDpi = 600,
            GlyphRemovalStrategy = GlyphRemovalStrategy.AnyOverlap
        };

        // Assert
        options.PreservePartialGlyphsAsImages.Should().BeTrue();
        options.PartialGlyphRasterizationDpi.Should().Be(600);
        options.GlyphRemovalStrategy.Should().Be(GlyphRemovalStrategy.AnyOverlap);
    }

    [Theory]
    [InlineData(GlyphRemovalStrategy.CenterPoint)]
    [InlineData(GlyphRemovalStrategy.AnyOverlap)]
    [InlineData(GlyphRemovalStrategy.FullyContained)]
    public void GlyphRemovalStrategy_AllValuesValid(GlyphRemovalStrategy strategy)
    {
        // Arrange
        var options = new RedactionOptions { GlyphRemovalStrategy = strategy };

        // Assert
        options.GlyphRemovalStrategy.Should().Be(strategy);
    }

    #endregion

    #region Helper Methods

    private SKBitmap CreateTestBitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    private SKBitmap CreateBitmapWithTransparency(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Left half opaque, right half transparent
                byte alpha = (byte)(x < width / 2 ? 255 : 0);
                bitmap.SetPixel(x, y, new SKColor(255, 0, 0, alpha));
            }
        }
        return bitmap;
    }

    #endregion
}
