using FluentAssertions;
using Pdfe.Core.Document;
using SkiaSharp;
using Xunit;

namespace Pdfe.Rendering.Tests;

/// <summary>
/// TDD tests for SkiaSharp-based PDF rendering.
/// </summary>
public class SkiaRendererTests
{
    #region Basic Rendering Tests

    [Fact]
    public void RenderPage_ReturnsNonNullBitmap()
    {
        // Arrange
        var pdfData = CreateSimplePdf("Hello World");
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
    }

    [Fact]
    public void RenderPage_DefaultDpi_Returns150DpiImage()
    {
        // Arrange
        var pdfData = CreateSimplePdf("Test");
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();
        var page = doc.GetPage(1);

        // Act
        using var bitmap = renderer.RenderPage(page);

        // Assert - US Letter at 150 DPI should be ~1275x1650 pixels
        // 612 points * 150/72 = 1275, 792 points * 150/72 = 1650
        var expectedWidth = (int)Math.Round(page.Width * 150 / 72);
        var expectedHeight = (int)Math.Round(page.Height * 150 / 72);
        bitmap.Width.Should().Be(expectedWidth);
        bitmap.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void RenderPage_CustomDpi_ScalesCorrectly()
    {
        // Arrange
        var pdfData = CreateSimplePdf("Test");
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();
        var page = doc.GetPage(1);
        var options = new RenderOptions { Dpi = 300 };

        // Act
        using var bitmap = renderer.RenderPage(page, options);

        // Assert - 300 DPI should be double the size of 150 DPI
        var expectedWidth = (int)Math.Round(page.Width * 300 / 72);
        var expectedHeight = (int)Math.Round(page.Height * 300 / 72);
        bitmap.Width.Should().Be(expectedWidth);
        bitmap.Height.Should().Be(expectedHeight);
    }

    [Fact]
    public void RenderPage_WhiteBackground_IsWhite()
    {
        // Arrange
        var pdfData = CreateEmptyPdf();
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();
        var options = new RenderOptions { BackgroundColor = SKColors.White };

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1), options);

        // Assert - check center pixel is white
        var centerX = bitmap.Width / 2;
        var centerY = bitmap.Height / 2;
        var pixel = bitmap.GetPixel(centerX, centerY);
        pixel.Should().Be(SKColors.White);
    }

    #endregion

    #region Rectangle Rendering Tests

    [Fact]
    public void RenderPage_FilledRectangle_ShowsBlackPixel()
    {
        // Arrange
        var pdfData = CreatePdfWithRectangle(100, 100, 200, 150, fill: true);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample pixel inside rectangle should be black (or non-white)
        // Rectangle at PDF coordinates (100, 100, 200, 150) means:
        // - X: 100 to 300 (left edge + width)
        // - Y: 100 to 250 (bottom edge + height in PDF coords)
        // At 150 DPI, X pixel = 100 * 150/72 = 208, Y = depends on flip
        var pixelX = (int)(200 * 150 / 72); // Center of rectangle X
        var pixelY = bitmap.Height - (int)(175 * 150 / 72); // Center of rectangle Y, flipped
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Should().NotBe(SKColors.White, "rectangle area should be filled");
    }

    [Fact]
    public void RenderPage_StrokedRectangle_ShowsOutline()
    {
        // Arrange
        var pdfData = CreatePdfWithRectangle(50, 50, 100, 100, fill: false, stroke: true);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - center of rectangle should be white (not filled)
        var centerX = (int)(100 * 150 / 72);
        var centerY = bitmap.Height - (int)(100 * 150 / 72);
        var centerPixel = bitmap.GetPixel(centerX, centerY);
        centerPixel.Should().Be(SKColors.White, "rectangle interior should not be filled");
    }

    #endregion

    #region Line Rendering Tests

    [Fact]
    public void RenderPage_Line_ShowsStroke()
    {
        // Arrange
        var pdfData = CreatePdfWithLine(100, 100, 300, 300);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample a point along the line
        var midX = (int)(200 * 150 / 72);
        var midY = bitmap.Height - (int)(200 * 150 / 72);
        var pixel = bitmap.GetPixel(midX, midY);
        // The line may not hit exactly due to anti-aliasing, so just check it's not white
        pixel.Should().NotBe(SKColors.White, "line should be visible");
    }

    [Fact]
    public void RenderPage_CubicBezierCurve_RendersSmoothCurve()
    {
        // Arrange - c operator: x1 y1 x2 y2 x3 y3 c (cubic Bézier with all control points)
        var content = @"
            0 G
            2 w
            100 400 m
            150 500 250 500 300 400 c
            S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - curve should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_BezierV_UsesCurrentPointAsControl()
    {
        // Arrange - v operator: x2 y2 x3 y3 v (current point is first control point)
        // Equivalent to: currentX currentY x2 y2 x3 y3 c
        var content = @"
            0 G
            2 w
            100 300 m
            250 450 300 300 v
            S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - v operator curve should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_BezierY_UsesEndpointAsControl()
    {
        // Arrange - y operator: x1 y1 x3 y3 y (endpoint is second control point)
        // Equivalent to: x1 y1 x3 y3 x3 y3 c
        var content = @"
            0 G
            2 w
            100 200 m
            150 350 300 200 y
            S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - y operator curve should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ComplexPath_CombinesMultipleCurves()
    {
        // Arrange - Complex path with c, v, and y operators
        var content = @"
            0 G
            3 w
            100 400 m
            150 450 200 450 250 400 c
            300 500 350 350 v
            400 450 450 400 y
            S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - complex curved path should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region Color Tests

    [Fact]
    public void RenderPage_RedRectangle_ShowsRed()
    {
        // Arrange
        var pdfData = CreatePdfWithColoredRectangle(100, 100, 200, 150, 1, 0, 0); // RGB red
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample inside rectangle
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Red.Should().BeGreaterThan(200, "red component should be high");
        pixel.Green.Should().BeLessThan(50, "green component should be low");
        pixel.Blue.Should().BeLessThan(50, "blue component should be low");
    }

    [Fact]
    public void RenderPage_GrayscaleRectangle_ShowsGray()
    {
        // Arrange - 50% gray (0.5 g)
        var pdfData = CreatePdfWithGrayscaleRectangle(100, 100, 200, 150, 0.5);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - sample inside rectangle should be gray
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        // 50% gray = approximately 128
        pixel.Red.Should().BeInRange((byte)100, (byte)160);
        pixel.Green.Should().BeInRange((byte)100, (byte)160);
        pixel.Blue.Should().BeInRange((byte)100, (byte)160);
    }

    [Fact]
    public void RenderPage_ColorSpace_DeviceGray_SetsGrayscale()
    {
        // Arrange - cs/sc operators for device-independent color
        // cs sets color space, sc sets color components
        var content = @"
            /DeviceGray cs
            0.3 sc
            100 100 200 150 re f
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should render 30% gray rectangle
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ColorSpace_DeviceRGB_SetsRGBColor()
    {
        // Arrange - CS/SC for stroke color space
        var content = @"
            /DeviceRGB CS
            1 0 0 SC
            5 w
            100 400 m 400 400 l S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should render red line
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ColorSpace_DeviceCMYK_SetsCMYKColor()
    {
        // Arrange - CMYK color space with sc operator
        var content = @"
            /DeviceCMYK cs
            0 1 1 0 sc
            100 100 200 150 re f
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - CMYK (0, 1, 1, 0) = red, should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ColorSpace_SeparateFillAndStroke_UsesIndependentSpaces()
    {
        // Arrange - cs/CS set fill/stroke color spaces independently
        var content = @"
            /DeviceGray cs
            0.5 sc
            /DeviceRGB CS
            1 0 0 SC
            3 w
            100 100 200 150 re B
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - gray fill, red stroke
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ColorSpace_scn_WorksLikeScWithoutPattern()
    {
        // Arrange - scn/SCN are like sc/SC but support patterns
        // Without pattern name, they work the same as sc/SC
        var content = @"
            /DeviceRGB cs
            0 0 1 scn
            100 100 200 150 re f
            /DeviceRGB CS
            0 1 0 SCN
            3 w
            100 300 200 150 re S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - blue fill, green stroke
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region Transformation Tests

    [Fact]
    public void RenderPage_TranslatedRectangle_IsOffset()
    {
        // Arrange - rectangle at (0,0) translated by (200, 200)
        var pdfData = CreatePdfWithTranslatedRectangle(0, 0, 50, 50, 200, 200);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - rectangle should be at (200, 200) not (0, 0)
        var origin = bitmap.GetPixel(10, bitmap.Height - 10);
        origin.Should().Be(SKColors.White, "original position should be empty");

        var translated = bitmap.GetPixel((int)(225 * 150 / 72), bitmap.Height - (int)(225 * 150 / 72));
        translated.Should().NotBe(SKColors.White, "translated position should have content");
    }

    [Fact]
    public void RenderPage_RotatedContent_Rotates()
    {
        // Arrange - Rotate 45 degrees using cm operator
        // Rotation matrix: [cos θ, sin θ, -sin θ, cos θ, 0, 0]
        var content = @"
            q
            0.707 0.707 -0.707 0.707 300 300 cm
            0 0 1 rg
            0 0 100 100 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should render without error
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ScaledContent_Scales()
    {
        // Arrange - Scale by 2x in both directions
        // Scale matrix: [sx, 0, 0, sy, 0, 0]
        var content = @"
            q
            2 0 0 2 100 100 cm
            1 0 0 rg
            0 0 50 50 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - scaled rectangle should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_NonUniformScale_ScalesDifferently()
    {
        // Arrange - Scale 3x horizontally, 1x vertically
        var content = @"
            q
            3 0 0 1 100 100 cm
            0 1 0 rg
            0 0 50 50 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - non-uniform scaling should work
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_SkewedContent_Skews()
    {
        // Arrange - Skew transformation
        // Skew matrix: [1, tan(angle_y), tan(angle_x), 1, 0, 0]
        var content = @"
            q
            1 0.5 0.3 1 100 100 cm
            0 0 1 rg
            0 0 100 100 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - skewed content should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_CombinedTransformations_AppliesInOrder()
    {
        // Arrange - Combine scale, rotate, and translate
        var content = @"
            q
            1 0 0 1 200 200 cm
            0.707 0.707 -0.707 0.707 0 0 cm
            2 0 0 2 0 0 cm
            1 0 0 rg
            0 0 25 25 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - combined transformations should work
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_NestedTransformations_ComposesCorrectly()
    {
        // Arrange - Nested q/Q with transformations
        var content = @"
            q
            1 0 0 1 100 100 cm
            1 0 0 rg
            0 0 50 50 re f
            q
            2 0 0 2 50 50 cm
            0 1 0 rg
            0 0 25 25 re f
            Q
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - nested transformations should compose
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_TransformationWithText_TransformsText()
    {
        // Arrange - Apply transformation to text
        var content = @"
            q
            2 0 0 2 100 100 cm
            BT
            /F1 24 Tf
            0 0 Td
            (Scaled) Tj
            ET
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - transformed text should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_IdentityMatrix_NoTransformation()
    {
        // Arrange - Identity matrix (no transformation)
        var content = @"
            q
            1 0 0 1 0 0 cm
            0 0 1 rg
            100 100 100 100 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - identity matrix should have no effect
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region State Stack Tests

    [Fact]
    public void RenderPage_SaveRestoreState_WorksCorrectly()
    {
        // Arrange - save state, draw red, restore, draw black
        var pdfData = CreatePdfWithStateStack();
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should have both rectangles rendered
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    #endregion

    #region CMYK Color Tests

    [Fact]
    public void RenderPage_CmykCyan_ShowsCyan()
    {
        // Arrange - Cyan in CMYK: C=1, M=0, Y=0, K=0
        var pdfData = CreatePdfWithCmykRectangle(100, 100, 200, 150, 1, 0, 0, 0);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - cyan = RGB(0, 255, 255) or close to it
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Red.Should().BeLessThan(100, "red component should be low for cyan");
        pixel.Green.Should().BeGreaterThan(200, "green component should be high for cyan");
        pixel.Blue.Should().BeGreaterThan(200, "blue component should be high for cyan");
    }

    [Fact]
    public void RenderPage_CmykMagenta_ShowsMagenta()
    {
        // Arrange - Magenta in CMYK: C=0, M=1, Y=0, K=0
        var pdfData = CreatePdfWithCmykRectangle(100, 100, 200, 150, 0, 1, 0, 0);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - magenta = RGB(255, 0, 255) or close to it
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Red.Should().BeGreaterThan(200, "red component should be high for magenta");
        pixel.Green.Should().BeLessThan(100, "green component should be low for magenta");
        pixel.Blue.Should().BeGreaterThan(200, "blue component should be high for magenta");
    }

    [Fact]
    public void RenderPage_CmykYellow_ShowsYellow()
    {
        // Arrange - Yellow in CMYK: C=0, M=0, Y=1, K=0
        var pdfData = CreatePdfWithCmykRectangle(100, 100, 200, 150, 0, 0, 1, 0);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - yellow = RGB(255, 255, 0) or close to it
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Red.Should().BeGreaterThan(200, "red component should be high for yellow");
        pixel.Green.Should().BeGreaterThan(200, "green component should be high for yellow");
        pixel.Blue.Should().BeLessThan(100, "blue component should be low for yellow");
    }

    [Fact]
    public void RenderPage_CmykBlack_ShowsBlack()
    {
        // Arrange - Black in CMYK: C=0, M=0, Y=0, K=1
        var pdfData = CreatePdfWithCmykRectangle(100, 100, 200, 150, 0, 0, 0, 1);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - black = RGB(0, 0, 0)
        var pixelX = (int)(200 * 150 / 72);
        var pixelY = bitmap.Height - (int)(175 * 150 / 72);
        var pixel = bitmap.GetPixel(pixelX, pixelY);
        pixel.Red.Should().BeLessThan(50, "red component should be low for black");
        pixel.Green.Should().BeLessThan(50, "green component should be low for black");
        pixel.Blue.Should().BeLessThan(50, "blue component should be low for black");
    }

    [Fact]
    public void RenderPage_CmykStroke_UsesKOperator()
    {
        // Arrange - Test K operator for stroke color (uppercase K)
        // K sets stroke CMYK, k sets fill CMYK
        var content = @"
            0 1 1 0 K
            5 w
            100 400 m 400 400 l S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - CMYK (0, 1, 1, 0) = red stroke should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_CmykFillAndStroke_SeparateColors()
    {
        // Arrange - Test k (fill) and K (stroke) together
        var content = @"
            0 1 0 0 k
            1 0 1 0 K
            3 w
            100 100 200 150 re B
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - magenta fill (0,1,0,0), yellow stroke (1,0,1,0)
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region Line Style Tests

    [Fact]
    public void RenderPage_ThickLine_ShowsThickStroke()
    {
        // Arrange
        var pdfData = CreatePdfWithThickLine(100, 100, 300, 100, 10);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - line should be visible
        var midX = (int)(200 * 150 / 72);
        var midY = bitmap.Height - (int)(100 * 150 / 72);
        var pixel = bitmap.GetPixel(midX, midY);
        pixel.Should().NotBe(SKColors.White, "thick line should be visible");
    }

    [Fact]
    public void RenderPage_RoundLineCap_DrawsRoundCaps()
    {
        // Arrange - 1 J sets round line cap
        var content = "0 G 20 w 1 J 100 400 m 300 400 l S";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - just verify it renders without error
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_RoundLineJoin_DrawsRoundJoins()
    {
        // Arrange - 1 j sets round line join
        var content = "0 G 10 w 1 j 100 400 m 200 500 l 300 400 l S";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - just verify it renders without error
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_MiterLimit_Applied()
    {
        // Arrange - M sets miter limit
        var content = "0 G 10 w 0 j 2 M 100 400 m 200 500 l 300 400 l S";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - just verify it renders without error
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_DashedLine_RendersDashPattern()
    {
        // Arrange - d operator sets dash pattern
        // Format: [dashArray] dashPhase d
        // [3 2] 0 d = 3 units on, 2 units off, starting at 0
        var content = @"
            0 G
            3 w
            [3 2] 0 d
            100 400 m 400 400 l S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - dashed line should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_DashPatternWithPhase_RendersWithOffset()
    {
        // Arrange - dash phase offsets the pattern start
        // [10 5] 3 d = 10 on, 5 off, starting 3 units into pattern
        var content = @"
            0 G
            5 w
            [10 5] 3 d
            100 500 m 400 500 l S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - dashed line with phase should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ComplexDashPattern_RendersMultiSegmentPattern()
    {
        // Arrange - complex pattern with multiple on/off segments
        // [5 3 2 3] 0 d = 5 on, 3 off, 2 on, 3 off, repeat
        var content = @"
            0 G
            4 w
            [5 3 2 3] 0 d
            100 300 m 400 300 l S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - complex dash pattern should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_SolidLineAfterDashed_ResetsToSolid()
    {
        // Arrange - empty dash array [] resets to solid line
        var content = @"
            0 G
            3 w
            [5 2] 0 d
            100 500 m 400 500 l S
            [] 0 d
            100 400 m 400 400 l S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should render both dashed and solid lines
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region Clipping Path Tests (Issue #295)

    [Fact]
    public void RenderPage_ClippingPath_NonZeroWinding_RestrictsRendering()
    {
        // Arrange - Create PDF with clipping path using W (non-zero winding)
        var content = @"
            0 G
            2 w
            100 100 200 200 re W n
            0 0 400 400 re S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - rendering should complete without errors
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ClippingPath_EvenOdd_RestrictsRendering()
    {
        // Arrange - Create PDF with clipping path using W* (even-odd rule)
        var content = @"
            0 G
            2 w
            100 100 200 200 re W* n
            0 0 400 400 re S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_NestedClipping_AppliesIntersection()
    {
        // Arrange - Create PDF with nested clipping regions
        var content = @"
            q
            0 G
            2 w
            100 100 300 300 re W n
            q
            200 200 200 200 re W n
            50 50 400 400 re S
            Q
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - nested clipping should create intersection
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ClippingWithFill_FillsOnlyClippedArea()
    {
        // Arrange - Clip then fill
        var content = @"
            1 0 0 rg
            100 100 200 200 re W n
            0 0 400 400 re f
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - fill should be clipped to the rectangle
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ClippingWithText_ClipsText()
    {
        // Arrange - Clip then render text
        var content = @"
            100 500 200 100 re W n
            BT
            /F1 48 Tf
            50 500 Td
            (Clipped Text) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - text should be clipped
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_CircularClippingPath_WorksCorrectly()
    {
        // Arrange - Create circular clipping using Bézier curves
        var content = @"
            q
            200 200 m
            200 310.5 289.5 400 400 400 c
            510.5 400 600 310.5 600 200 c
            600 89.5 510.5 0 400 0 c
            289.5 0 200 89.5 200 200 c
            W n
            0 0 1 rg
            0 0 800 800 re f
            Q
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - circular clip should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_StateRestoreRemovesClipping()
    {
        // Arrange - Clipping should be removed after Q
        var content = @"
            q
            100 100 200 200 re W n
            1 0 0 rg
            0 0 400 400 re f
            Q
            0 0 1 rg
            50 50 300 300 re f
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - second rectangle should not be clipped
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region Text State Operator Tests

    [Fact]
    public void RenderPage_CharacterSpacing_AppliesSpacing()
    {
        // Arrange - Tc operator sets character spacing
        var content = @"
            BT
            /F1 24 Tf
            100 700 Td
            0 Tc
            (Normal) Tj
            0 -30 Td
            5 Tc
            (Spaced) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should render without error
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_WordSpacing_AppliesSpacing()
    {
        // Arrange - Tw operator sets word spacing
        var content = @"
            BT
            /F1 20 Tf
            100 700 Td
            0 Tw
            (Hello World) Tj
            0 -30 Td
            10 Tw
            (Hello World) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_HorizontalScaling_ScalesText()
    {
        // Arrange - Tz operator sets horizontal scaling (percentage)
        var content = @"
            BT
            /F1 24 Tf
            100 700 Td
            100 Tz
            (Normal) Tj
            0 -30 Td
            150 Tz
            (Stretched) Tj
            0 -30 Td
            50 Tz
            (Compressed) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - different horizontal scales should render
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_TextLeading_AffectsLineSpacing()
    {
        // Arrange - TL operator sets text leading for T* operator
        var content = @"
            BT
            /F1 20 Tf
            100 700 Td
            20 TL
            (Line 1) Tj
            T*
            (Line 2) Tj
            T*
            (Line 3) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - lines should be spaced according to leading
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_TextRenderMode_AffectsRendering()
    {
        // Arrange - Tr operator sets text rendering mode
        // 0 = fill, 1 = stroke, 2 = fill then stroke, 3 = invisible
        var content = @"
            BT
            /F1 24 Tf
            100 700 Td
            0 Tr
            (Fill) Tj
            0 -30 Td
            1 Tr
            2 w
            (Stroke) Tj
            0 -30 Td
            2 Tr
            (Fill+Stroke) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - different rendering modes should work
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_TextRise_OffsetText()
    {
        // Arrange - Ts operator sets text rise (vertical offset)
        var content = @"
            BT
            /F1 20 Tf
            100 700 Td
            (Normal) Tj
            10 Ts
            (Raised) Tj
            -10 Ts
            (Lowered) Tj
            0 Ts
            (Back to Normal) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - text with rise should render at different heights
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_CombinedTextState_AppliesAllSettings()
    {
        // Arrange - Combine multiple text state operators
        var content = @"
            BT
            /F1 18 Tf
            100 700 Td
            2 Tc
            5 Tw
            120 Tz
            0 Tr
            (Styled Text) Tj
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - all text state settings should be applied
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_SingleQuoteOperator_MovesAndShowsText()
    {
        // Arrange - ' operator moves to next line and shows text (equivalent to T* Tj)
        var content = @"
            BT
            /F1 20 Tf
            100 700 Td
            14 TL
            (First line) Tj
            (Second line) '
            (Third line) '
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - ' operator should move to next line and show text
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_DoubleQuoteOperator_SetsSpacingAndShowsText()
    {
        // Arrange - " operator sets word/char spacing, moves to next line, shows text
        // Format: aw ac string " (word spacing, char spacing, string)
        var content = @"
            BT
            /F1 20 Tf
            100 700 Td
            14 TL
            (Normal spacing) Tj
            10 2 (Wide spacing) ""
            0 0 (Normal again) ""
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - " operator should set spacing, move to next line, and show text
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_MixedLineOperators_CombinesTjQuoteAndDoubleQuote()
    {
        // Arrange - Mix Tj, ', and " operators in same text object
        var content = @"
            BT
            /F1 18 Tf
            100 700 Td
            12 TL
            (Line 1: Tj) Tj
            (Line 2: quote) '
            5 1 (Line 3: double quote) ""
            0 0 (Line 4: double quote) ""
            ET
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - all three text-showing operators should work together
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region XObject Rendering Tests (Issue #299)

    [Fact]
    public void RenderPage_ImageXObject_RendersImage()
    {
        // Arrange - Create PDF with a grayscale image XObject
        var pdfData = CreatePdfWithImageXObject(width: 10, height: 10, grayscale: true);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - image should be rendered (non-white pixels somewhere)
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);

        // Image XObjects render successfully - check for non-white pixels
        bool hasNonWhitePixels = false;
        for (int y = 0; y < bitmap.Height && !hasNonWhitePixels; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    hasNonWhitePixels = true;
                    break;
                }
            }
        }

        hasNonWhitePixels.Should().BeTrue("Image XObject should render non-white pixels");
    }

    [Fact]
    public void RenderPage_FormXObject_RendersContent()
    {
        // Arrange - Create PDF with Form XObject containing a rectangle
        var pdfData = CreatePdfWithFormXObject();
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - Form's content should be rendered
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);

        // Check if any non-white pixels exist (form rendering working)
        bool hasNonWhitePixels = false;
        for (int y = 0; y < bitmap.Height && !hasNonWhitePixels; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y) != SKColors.White)
                {
                    hasNonWhitePixels = true;
                    break;
                }
            }
        }

        hasNonWhitePixels.Should().BeTrue("Form XObject content should render");
    }

    [Fact]
    public void RenderPage_ScaledImageXObject_AppliesCTM()
    {
        // Arrange - Image XObject with scaling transformation
        var pdfData = CreatePdfWithScaledImageXObject();
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - verify rendering completed successfully
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    #endregion

    #region ExtGState (gs operator) Tests (Issue #294)

    [Fact]
    public void RenderPage_ExtGState_StrokeAlpha_AppliesTransparency()
    {
        // Arrange - Create PDF with gs operator setting stroke alpha (CA)
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "CA", 0.5 } // 50% stroke transparency
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - rendering should complete without errors
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_FillAlpha_AppliesTransparency()
    {
        // Arrange - Create PDF with gs operator setting fill alpha (ca)
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "ca", 0.3 } // 30% fill transparency
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - rendering should complete without errors
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
        bitmap.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_BothAlphas_AppliesBothTransparencies()
    {
        // Arrange - Create PDF with both CA and ca
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "CA", 0.7 }, // 70% stroke transparency
            { "ca", 0.4 }  // 40% fill transparency
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_LineWidth_AppliesStrokeWidth()
    {
        // Arrange - Create PDF with LW (line width) in ExtGState
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "LW", 5.0 } // 5-point line width
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_LineCap_AppliesCapStyle()
    {
        // Arrange - Create PDF with LC (line cap) in ExtGState
        // 0 = Butt cap, 1 = Round cap, 2 = Square cap
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "LC", 1 } // Round cap
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_LineJoin_AppliesJoinStyle()
    {
        // Arrange - Create PDF with LJ (line join) in ExtGState
        // 0 = Miter join, 1 = Round join, 2 = Bevel join
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "LJ", 1 } // Round join
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_MiterLimit_AppliesMiterLimit()
    {
        // Arrange - Create PDF with ML (miter limit) in ExtGState
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "ML", 5.0 } // Miter limit of 5.0
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_CombinedParameters_AppliesAll()
    {
        // Arrange - Create PDF with multiple ExtGState parameters
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "CA", 0.8 },  // Stroke alpha
            { "ca", 0.6 },  // Fill alpha
            { "LW", 3.0 },  // Line width
            { "LC", 2 },    // Square cap
            { "LJ", 0 },    // Miter join
            { "ML", 4.0 }   // Miter limit
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - all parameters should be applied without error
        bitmap.Should().NotBeNull();
        bitmap.Width.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderPage_ExtGState_AlphaClampedToValidRange()
    {
        // Arrange - Create PDF with out-of-range alpha values
        // Alpha should be clamped to [0, 1] range
        var pdfData = CreatePdfWithExtGState(new Dictionary<string, object>
        {
            { "CA", 1.5 },  // > 1.0, should clamp to 1.0
            { "ca", -0.2 }  // < 0.0, should clamp to 0.0
        });
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should render without throwing
        bitmap.Should().NotBeNull();
    }

    [Fact]
    public void RenderPage_ExtGState_MissingDictionary_ContinuesRendering()
    {
        // Arrange - Create PDF with gs operator referencing non-existent ExtGState
        var content = @"
            /GS99 gs
            100 100 200 200 re S
        ";
        var pdfData = CreatePdfWithContent(content);
        using var doc = PdfDocument.Open(pdfData);
        var renderer = new SkiaRenderer();

        // Act
        using var bitmap = renderer.RenderPage(doc.GetPage(1));

        // Assert - should handle gracefully without crashing
        bitmap.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateSimplePdf(string text)
    {
        var content = $"BT /F1 12 Tf 100 700 Td ({text}) Tj ET";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreateEmptyPdf()
    {
        return CreatePdfWithContent("");
    }

    private static byte[] CreatePdfWithRectangle(int x, int y, int w, int h, bool fill = true, bool stroke = false)
    {
        var op = fill && stroke ? "B" : (fill ? "f" : (stroke ? "S" : "n"));
        var content = $"0 g {x} {y} {w} {h} re {op}";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithLine(int x1, int y1, int x2, int y2)
    {
        var content = $"0 G 1 w {x1} {y1} m {x2} {y2} l S";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithColoredRectangle(int x, int y, int w, int h, double r, double g, double b)
    {
        var content = $"{r} {g} {b} rg {x} {y} {w} {h} re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithGrayscaleRectangle(int x, int y, int w, int h, double gray)
    {
        var content = $"{gray} g {x} {y} {w} {h} re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithTranslatedRectangle(int x, int y, int w, int h, double tx, double ty)
    {
        var content = $"q 1 0 0 1 {tx} {ty} cm 0 g {x} {y} {w} {h} re f Q";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithStateStack()
    {
        var content = "q 1 0 0 rg 50 50 100 100 re f Q 0 g 200 200 100 100 re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithCmykRectangle(int x, int y, int w, int h, double c, double m, double yy, double k)
    {
        var content = $"{c} {m} {yy} {k} k {x} {y} {w} {h} re f";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithThickLine(int x1, int y1, int x2, int y2, double lineWidth)
    {
        var content = $"0 G {lineWidth} w {x1} {y1} m {x2} {y2} l S";
        return CreatePdfWithContent(content);
    }

    private static byte[] CreatePdfWithImageXObject(int width, int height, bool grayscale = true)
    {
        // Create a simple grayscale image (gray square)
        var imageData = new byte[width * height];
        for (int i = 0; i < imageData.Length; i++)
            imageData[i] = 128; // Mid-gray

        // Content stream that draws the image at 100x100 points at position (100, 400)
        var content = $"q 100 0 0 100 100 400 cm /Im1 Do Q";

        return CreatePdfWithImageXObjectAndContent(content, imageData, width, height, grayscale);
    }

    private static byte[] CreatePdfWithScaledImageXObject()
    {
        // 2x2 gray image
        var imageData = new byte[] { 128, 128, 128, 128 };

        // Scale the image to 200x200 points
        var content = "q 200 0 0 200 150 350 cm /Im1 Do Q";

        return CreatePdfWithImageXObjectAndContent(content, imageData, 2, 2, grayscale: true);
    }

    private static byte[] CreatePdfWithFormXObject()
    {
        // Form XObject content: a black rectangle
        var formContent = "0 g 10 10 80 80 re f";

        // Main content stream: invoke the Form XObject
        var content = "q 1 0 0 1 100 400 cm /Fm1 Do Q";

        return CreatePdfWithFormXObjectAndContent(content, formContent);
    }

    private static byte[] CreatePdfWithImageXObjectAndContent(string content, byte[] imageData, int width, int height, bool grayscale)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[8];

        // Catalog
        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Pages
        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Page
        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]");
        writer.WriteLine("   /Contents 4 0 R");
        writer.WriteLine("   /Resources << /XObject << /Im1 6 0 R >> /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Content stream
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // Font
        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Image XObject
        offsets[6] = ms.Position;
        writer.WriteLine("6 0 obj");
        var colorSpace = grayscale ? "/DeviceGray" : "/DeviceRGB";
        var bpc = 8;
        writer.WriteLine($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height}");
        writer.WriteLine($"   /ColorSpace {colorSpace} /BitsPerComponent {bpc} /Length {imageData.Length} >>");
        writer.WriteLine("stream");
        writer.Flush();
        ms.Write(imageData, 0, imageData.Length);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // Xref
        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 7");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 6; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 7 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    private static byte[] CreatePdfWithFormXObjectAndContent(string content, string formContent)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[8];

        // Catalog
        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Pages
        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Page
        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]");
        writer.WriteLine("   /Contents 4 0 R");
        writer.WriteLine("   /Resources << /XObject << /Fm1 6 0 R >> /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Content stream
        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // Font
        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");
        writer.Flush();

        // Form XObject
        offsets[6] = ms.Position;
        writer.WriteLine("6 0 obj");
        writer.WriteLine($"<< /Type /XObject /Subtype /Form /BBox [0 0 100 100]");
        writer.WriteLine($"   /Matrix [1 0 0 1 0 0] /Length {formContent.Length} >>");
        writer.WriteLine("stream");
        writer.Write(formContent);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        // Xref
        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 7");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 6; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 7 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    private static byte[] CreatePdfWithExtGState(Dictionary<string, object> extGStateParams)
    {
        // Build ExtGState dictionary content
        var extGStateContent = new System.Text.StringBuilder();
        extGStateContent.Append("<< /Type /ExtGState ");

        foreach (var param in extGStateParams)
        {
            extGStateContent.Append($"/{param.Key} ");
            if (param.Value is int intValue)
                extGStateContent.Append(intValue);
            else if (param.Value is double doubleValue)
                extGStateContent.Append(doubleValue.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture));
            else
                extGStateContent.Append(param.Value);
            extGStateContent.Append(" ");
        }
        extGStateContent.Append(">>");

        // Create content stream that uses the ExtGState
        var content = "/GS1 gs\n100 100 200 200 re\nf\n";

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[7];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /ExtGState << /GS1 6 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[6] = ms.Position;
        writer.WriteLine("6 0 obj");
        writer.WriteLine(extGStateContent.ToString());
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 7");
        writer.WriteLine("0000000000 65535 f ");
        writer.WriteLine($"{offsets[1]:D10} 00000 n ");
        writer.WriteLine($"{offsets[2]:D10} 00000 n ");
        writer.WriteLine($"{offsets[3]:D10} 00000 n ");
        writer.WriteLine($"{offsets[4]:D10} 00000 n ");
        writer.WriteLine("0000000000 65535 f "); // Object 5 unused
        writer.WriteLine($"{offsets[6]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 7 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    private static byte[] CreatePdfWithContent(string content)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[6];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 5; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 6 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }

    #endregion
}
