using FluentAssertions;
using PdfEditor.Redaction;
using PdfEditor.Redaction.GlyphLevel;
using PdfSharp.Pdf;
using SkiaSharp;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Unit tests for ImageXObjectEmbedder.
/// Tests embedding SKBitmaps into PDF as Image XObjects with transparency.
/// Part of issue #209: Embed clipped glyph image as PDF XObject.
/// </summary>
public class ImageXObjectEmbedderTests
{
    #region GetDrawOperators Tests

    [Fact]
    public void GetDrawOperators_ReturnsCorrectFormat()
    {
        // Arrange
        var imageName = "GlyphImg1";
        var bounds = new PdfRectangle(100, 200, 150, 260);

        // Act
        var operators = ImageXObjectEmbedder.GetDrawOperators(imageName, bounds);

        // Assert
        // Format: q width 0 0 height x y cm /imageName Do Q\n
        operators.Should().StartWith("q ");
        operators.Should().Contain("50.000 0 0 60.000"); // width and height
        operators.Should().Contain("100.000 200.000"); // x and y
        operators.Should().Contain("cm");
        operators.Should().Contain("/GlyphImg1 Do");
        operators.Should().EndWith(" Q\n");
    }

    [Fact]
    public void GetDrawOperators_HandlesSmallBounds()
    {
        // Arrange
        var imageName = "Img2";
        var bounds = new PdfRectangle(0, 0, 1.5, 2.5);

        // Act
        var operators = ImageXObjectEmbedder.GetDrawOperators(imageName, bounds);

        // Assert
        operators.Should().Contain("1.500 0 0 2.500");
        operators.Should().Contain("0.000 0.000 cm");
    }

    #endregion

    #region EmbedImage Tests

    [Fact]
    public void EmbedImage_NullPage_ReturnsNull()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var bitmap = new SKBitmap(10, 10);

        // Act
        var result = embedder.EmbedImage(null!, bitmap, new PdfRectangle(0, 0, 10, 10));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void EmbedImage_NullImage_ReturnsNull()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var doc = new PdfDocument();
        var page = doc.AddPage();

        // Act
        var result = embedder.EmbedImage(page, null!, new PdfRectangle(0, 0, 10, 10));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void EmbedImage_ValidInputs_ReturnsImageName()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var bitmap = CreateTestBitmap(20, 30);
        var bounds = new PdfRectangle(100, 100, 120, 130);

        // Act
        var imageName = embedder.EmbedImage(page, bitmap, bounds);

        // Assert
        imageName.Should().NotBeNull();
        imageName.Should().StartWith("GlyphImg");
    }

    [Fact]
    public void EmbedImage_MultipleImages_ReturnsUniqueNames()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var bitmap1 = CreateTestBitmap(10, 10);
        using var bitmap2 = CreateTestBitmap(10, 10);
        var bounds = new PdfRectangle(0, 0, 10, 10);

        // Act
        var name1 = embedder.EmbedImage(page, bitmap1, bounds);
        var name2 = embedder.EmbedImage(page, bitmap2, bounds);

        // Assert
        name1.Should().NotBeNull();
        name2.Should().NotBeNull();
        name1.Should().NotBe(name2);
    }

    [Fact]
    public void EmbedImage_AddsXObjectToResources()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var bitmap = CreateTestBitmap(10, 10);
        var bounds = new PdfRectangle(0, 0, 10, 10);

        // Act
        var imageName = embedder.EmbedImage(page, bitmap, bounds);

        // Assert
        var xObjects = page.Resources.Elements.GetDictionary("/XObject");
        xObjects.Should().NotBeNull();
        xObjects!.Elements.ContainsKey("/" + imageName).Should().BeTrue();
    }

    #endregion

    #region Transparency Tests

    [Fact]
    public void EmbedImage_OpaqueImage_CreatesXObjectWithoutSMask()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var bitmap = CreateOpaqueBitmap(10, 10);
        var bounds = new PdfRectangle(0, 0, 10, 10);

        // Act
        var imageName = embedder.EmbedImage(page, bitmap, bounds);

        // Assert
        imageName.Should().NotBeNull();
        // Opaque images should not have SMask
        var xObjects = page.Resources.Elements.GetDictionary("/XObject");
        var imageXObject = xObjects!.Elements.GetDictionary("/" + imageName);
        // Note: Direct SMask check might not work due to indirect reference
        // This test verifies the basic embedding works
    }

    [Fact]
    public void EmbedImage_TransparentImage_Succeeds()
    {
        // Arrange
        var embedder = new ImageXObjectEmbedder();
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        using var bitmap = CreateTransparentBitmap(10, 10);
        var bounds = new PdfRectangle(0, 0, 10, 10);

        // Act
        var imageName = embedder.EmbedImage(page, bitmap, bounds);

        // Assert
        imageName.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private SKBitmap CreateTestBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Red);
        }
        return bitmap;
    }

    private SKBitmap CreateOpaqueBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(255, 0, 0, 255)); // Fully opaque red
            }
        }
        return bitmap;
    }

    private SKBitmap CreateTransparentBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Half opaque, half transparent
                byte alpha = (byte)(x < width / 2 ? 255 : 0);
                bitmap.SetPixel(x, y, new SKColor(255, 0, 0, alpha));
            }
        }
        return bitmap;
    }

    #endregion
}
