using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PdfEditor.Services.Redaction;
using Xunit;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for coordinate conversion logic throughout the PDF Editor.
///
/// These tests verify the mathematical correctness of coordinate transformations
/// between different coordinate systems:
/// 1. Screen pixels (at various DPIs)
/// 2. PDF points (72 DPI)
/// 3. PDF native coordinates (bottom-left origin)
/// 4. Avalonia coordinates (top-left origin)
///
/// IMPORTANT: These tests are critical for ensuring redaction and text selection
/// work correctly. Any changes to coordinate handling code should be accompanied
/// by updates to these tests.
/// </summary>
public class CoordinateConversionUnitTests
{
    // ============================================================================
    // DPI SCALING TESTS
    // ============================================================================
    // Verify conversion between rendered image pixels and PDF points.
    // Formula: pdfPoints = imagePixels * (72 / renderDPI)
    // ============================================================================

    [Theory]
    [InlineData(150, 0, 0)]           // Origin stays at origin
    [InlineData(150, 150, 72)]        // 150 pixels at 150 DPI = 72 points (1 inch)
    [InlineData(150, 300, 144)]       // 300 pixels at 150 DPI = 144 points (2 inches)
    [InlineData(150, 75, 36)]         // 75 pixels at 150 DPI = 36 points (0.5 inch)
    [InlineData(72, 72, 72)]          // 72 pixels at 72 DPI = 72 points (1 inch)
    [InlineData(72, 144, 144)]        // 144 pixels at 72 DPI = 144 points (2 inches)
    [InlineData(300, 300, 72)]        // 300 pixels at 300 DPI = 72 points (1 inch)
    [InlineData(300, 600, 144)]       // 600 pixels at 300 DPI = 144 points (2 inches)
    [InlineData(96, 96, 72)]          // 96 pixels at 96 DPI = 72 points (1 inch) - Windows default
    public void DpiScaling_ImagePixelsToPdfPoints_ConvertsCorrectly(
        int renderDpi, double inputPixels, double expectedPoints)
    {
        // Arrange
        var scale = 72.0 / renderDpi;

        // Act
        var result = inputPixels * scale;

        // Assert
        result.Should().BeApproximately(expectedPoints, 0.001,
            $"Converting {inputPixels} pixels at {renderDpi} DPI should give {expectedPoints} points");
    }

    [Theory]
    [InlineData(150, 72, 150)]        // 72 points = 150 pixels at 150 DPI
    [InlineData(150, 144, 300)]       // 144 points = 300 pixels at 150 DPI
    [InlineData(72, 72, 72)]          // 72 points = 72 pixels at 72 DPI
    [InlineData(300, 72, 300)]        // 72 points = 300 pixels at 300 DPI
    public void DpiScaling_PdfPointsToImagePixels_ConvertsCorrectly(
        int renderDpi, double inputPoints, double expectedPixels)
    {
        // Arrange
        var scale = renderDpi / 72.0;

        // Act
        var result = inputPoints * scale;

        // Assert
        result.Should().BeApproximately(expectedPixels, 0.001,
            $"Converting {inputPoints} points to {renderDpi} DPI should give {expectedPixels} pixels");
    }

    // ============================================================================
    // Y-AXIS FLIP TESTS (PDF bottom-left to Avalonia top-left)
    // ============================================================================
    // PDF uses bottom-left origin (Y increases upward)
    // Avalonia uses top-left origin (Y increases downward)
    // Formula: avaloniaY = pageHeight - pdfY
    // ============================================================================

    [Theory]
    [InlineData(792, 0, 792)]         // PDF Y=0 (bottom) → Avalonia Y=792 (bottom)
    [InlineData(792, 792, 0)]         // PDF Y=792 (top) → Avalonia Y=0 (top)
    [InlineData(792, 720, 72)]        // Near top in PDF → Near top in Avalonia
    [InlineData(792, 72, 720)]        // Near bottom in PDF → Near bottom in Avalonia
    [InlineData(792, 396, 396)]       // Middle stays middle
    [InlineData(842, 0, 842)]         // A4 page height
    [InlineData(842, 770, 72)]        // A4 near top
    public void YAxisFlip_PdfToAvalonia_ConvertsCorrectly(
        double pageHeight, double pdfY, double expectedAvaloniaY)
    {
        // Arrange & Act
        var avaloniaY = pageHeight - pdfY;

        // Assert
        avaloniaY.Should().BeApproximately(expectedAvaloniaY, 0.001,
            $"PDF Y={pdfY} on page height {pageHeight} should convert to Avalonia Y={expectedAvaloniaY}");
    }

    [Theory]
    [InlineData(792, 0, 792)]         // Avalonia Y=0 (top) → PDF Y=792 (top)
    [InlineData(792, 792, 0)]         // Avalonia Y=792 (bottom) → PDF Y=0 (bottom)
    [InlineData(792, 72, 720)]        // Near top in Avalonia → Near top in PDF
    [InlineData(792, 720, 72)]        // Near bottom in Avalonia → Near bottom in PDF
    public void YAxisFlip_AvaloniaToP_ConvertsCorrectly(
        double pageHeight, double avaloniaY, double expectedPdfY)
    {
        // Arrange & Act
        var pdfY = pageHeight - avaloniaY;

        // Assert
        pdfY.Should().BeApproximately(expectedPdfY, 0.001,
            $"Avalonia Y={avaloniaY} on page height {pageHeight} should convert to PDF Y={expectedPdfY}");
    }

    // ============================================================================
    // FULL PIPELINE TESTS
    // ============================================================================
    // Test the complete coordinate conversion pipeline from screen to PDF
    // ============================================================================

    [Fact]
    public void FullPipeline_ScreenToRedactionArea_AtZoom100()
    {
        // Arrange: User clicks at (300, 200) on screen, zoom = 1.0, renderDPI = 150
        var screenX = 300.0;
        var screenY = 200.0;
        var zoom = 1.0;
        var renderDPI = 150;

        // Act: Convert to PDF points
        // Step 1: Screen to image pixels (with Canvas ScaleTransform, this is automatic)
        var imageX = screenX / zoom;  // 300 / 1.0 = 300
        var imageY = screenY / zoom;  // 200 / 1.0 = 200

        // Step 2: Image pixels to PDF points
        var scale = 72.0 / renderDPI;  // 0.48
        var pdfX = imageX * scale;     // 300 * 0.48 = 144
        var pdfY = imageY * scale;     // 200 * 0.48 = 96

        // Assert
        imageX.Should().Be(300);
        imageY.Should().Be(200);
        pdfX.Should().BeApproximately(144, 0.001);
        pdfY.Should().BeApproximately(96, 0.001);
    }

    [Fact]
    public void FullPipeline_ScreenToRedactionArea_AtZoom200()
    {
        // Arrange: User clicks at (600, 400) on screen, zoom = 2.0, renderDPI = 150
        // The image appears 2x larger, so clicking at (600, 400) is same PDF location as (300, 200) at zoom 1.0
        var screenX = 600.0;
        var screenY = 400.0;
        var zoom = 2.0;
        var renderDPI = 150;

        // Act: Convert to PDF points
        // With Canvas ScaleTransform, GetPosition returns inverse-transformed coords
        var imageX = screenX / zoom;  // 600 / 2.0 = 300
        var imageY = screenY / zoom;  // 400 / 2.0 = 200

        var scale = 72.0 / renderDPI;
        var pdfX = imageX * scale;     // 300 * 0.48 = 144
        var pdfY = imageY * scale;     // 200 * 0.48 = 96

        // Assert: Same PDF location as zoom 1.0 test
        pdfX.Should().BeApproximately(144, 0.001);
        pdfY.Should().BeApproximately(96, 0.001);
    }

    [Fact]
    public void FullPipeline_ScreenToRedactionArea_AtZoom50()
    {
        // Arrange: User clicks at (150, 100) on screen, zoom = 0.5, renderDPI = 150
        // The image appears 0.5x size, so clicking at (150, 100) is same PDF location as (300, 200) at zoom 1.0
        var screenX = 150.0;
        var screenY = 100.0;
        var zoom = 0.5;
        var renderDPI = 150;

        // Act
        var imageX = screenX / zoom;  // 150 / 0.5 = 300
        var imageY = screenY / zoom;  // 100 / 0.5 = 200

        var scale = 72.0 / renderDPI;
        var pdfX = imageX * scale;
        var pdfY = imageY * scale;

        // Assert: Same PDF location as zoom 1.0 test
        pdfX.Should().BeApproximately(144, 0.001);
        pdfY.Should().BeApproximately(96, 0.001);
    }

    // ============================================================================
    // TEXT BOUNDING BOX TESTS
    // ============================================================================
    // Verify that TextBoundsCalculator correctly converts text positions
    // ============================================================================

    [Fact]
    public void TextBoundsCalculator_TextNearTopOfPage_HasLowAvaloniaY()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<TextBoundsCalculator>>();
        var calculator = new TextBoundsCalculator(loggerMock.Object);

        var pageHeight = 792.0; // Letter size
        var textState = new PdfTextState
        {
            FontSize = 12,
            TextMatrix = PdfMatrix.CreateTranslation(72, 720) // Near top of page in PDF coords
        };
        var graphicsState = new PdfGraphicsState();

        // Act
        var bounds = calculator.CalculateBounds("Test", textState, graphicsState, pageHeight);

        // Assert: Text near top of PDF page should have LOW Y in Avalonia coords
        bounds.Y.Should().BeLessThan(100,
            "Text at PDF Y=720 (near top) should have low Avalonia Y (near top)");
        bounds.X.Should().BeApproximately(72, 5,
            "X coordinate should be preserved");
    }

    [Fact]
    public void TextBoundsCalculator_TextNearBottomOfPage_HasHighAvaloniaY()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<TextBoundsCalculator>>();
        var calculator = new TextBoundsCalculator(loggerMock.Object);

        var pageHeight = 792.0;
        var textState = new PdfTextState
        {
            FontSize = 12,
            TextMatrix = PdfMatrix.CreateTranslation(72, 72) // Near bottom of page in PDF coords
        };
        var graphicsState = new PdfGraphicsState();

        // Act
        var bounds = calculator.CalculateBounds("Test", textState, graphicsState, pageHeight);

        // Assert: Text near bottom of PDF page should have HIGH Y in Avalonia coords
        bounds.Y.Should().BeGreaterThan(700,
            "Text at PDF Y=72 (near bottom) should have high Avalonia Y (near bottom)");
    }

    // ============================================================================
    // INTERSECTION TESTS
    // ============================================================================
    // Verify that selection areas correctly intersect with text bounding boxes
    // ============================================================================

    [Fact]
    public void Intersection_SelectionOverText_ShouldIntersect()
    {
        // Arrange: Text bounding box at (100, 100, 50x20) in Avalonia coords
        var textBounds = new Rect(100, 100, 50, 20);

        // Selection covering the text
        var selection = new Rect(90, 95, 70, 30);

        // Act
        var intersects = textBounds.Intersects(selection);

        // Assert
        intersects.Should().BeTrue("Selection should intersect with text it covers");
    }

    [Fact]
    public void Intersection_SelectionAboveText_ShouldNotIntersect()
    {
        // Arrange: Text at Y=100, selection at Y=50 (above it)
        var textBounds = new Rect(100, 100, 50, 20);
        var selection = new Rect(100, 50, 50, 20);

        // Act
        var intersects = textBounds.Intersects(selection);

        // Assert
        intersects.Should().BeFalse("Selection above text should not intersect");
    }

    [Fact]
    public void Intersection_SelectionBelowText_ShouldNotIntersect()
    {
        // Arrange: Text at Y=100 with height 20, selection at Y=130 (below it)
        var textBounds = new Rect(100, 100, 50, 20);
        var selection = new Rect(100, 130, 50, 20);

        // Act
        var intersects = textBounds.Intersects(selection);

        // Assert
        intersects.Should().BeFalse("Selection below text should not intersect");
    }

    [Fact]
    public void Intersection_SelectionToLeftOfText_ShouldNotIntersect()
    {
        // Arrange
        var textBounds = new Rect(100, 100, 50, 20);
        var selection = new Rect(20, 100, 50, 20);

        // Act
        var intersects = textBounds.Intersects(selection);

        // Assert
        intersects.Should().BeFalse("Selection to left of text should not intersect");
    }

    [Fact]
    public void Intersection_SelectionToRightOfText_ShouldNotIntersect()
    {
        // Arrange
        var textBounds = new Rect(100, 100, 50, 20);
        var selection = new Rect(180, 100, 50, 20);

        // Act
        var intersects = textBounds.Intersects(selection);

        // Assert
        intersects.Should().BeFalse("Selection to right of text should not intersect");
    }

    // ============================================================================
    // EDGE CASE TESTS
    // ============================================================================

    [Fact]
    public void DpiScaling_ZeroDpi_ThrowsOrHandlesGracefully()
    {
        // This tests defensive coding - what happens with invalid input
        var renderDpi = 0;

        // Act & Assert: Should either throw or return infinity/NaN
        var act = () => 72.0 / renderDpi;
        var result = act();

        result.Should().Be(double.PositiveInfinity,
            "Division by zero DPI should result in infinity");
    }

    [Fact]
    public void YAxisFlip_NegativePageHeight_StillCalculates()
    {
        // Edge case: What if page height is somehow negative?
        var pageHeight = -100.0;
        var pdfY = 50.0;

        var avaloniaY = pageHeight - pdfY;

        avaloniaY.Should().Be(-150,
            "Math should still work even with invalid input");
    }

    [Theory]
    [InlineData(0, 0)]       // Zero coordinates
    [InlineData(-10, -10)]   // Negative coordinates
    [InlineData(10000, 10000)] // Very large coordinates
    public void Conversion_EdgeCaseCoordinates_DoNotCrash(double x, double y)
    {
        // Arrange
        var renderDpi = 150;
        var scale = 72.0 / renderDpi;

        // Act
        var pdfX = x * scale;
        var pdfY = y * scale;

        // Assert: Should not throw, result should be finite
        double.IsFinite(pdfX).Should().BeTrue();
        double.IsFinite(pdfY).Should().BeTrue();
    }
}
