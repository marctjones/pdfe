using System;
using Xunit;
using FluentAssertions;
using PdfEditor.Services.Redaction;
using Xunit.Abstractions;
using Avalonia;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for TextBoundsCalculator - text bounding box calculation
/// </summary>
public class TextBoundsCalculatorTests
{
    private readonly ITestOutputHelper _output;
    private readonly TextBoundsCalculator _calculator;

    public TextBoundsCalculatorTests(ITestOutputHelper output)
    {
        _output = output;
        var logger = NullLogger<TextBoundsCalculator>.Instance;
        _calculator = new TextBoundsCalculator(logger);
    }

    [Fact]
    public void CalculateBounds_WithSimpleText_ShouldReturnBounds()
    {
        // Arrange
        var text = "Hello";
        var textState = new PdfTextState { FontSize = 12 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0; // Standard letter size
        _output.WriteLine("Test: Calculate bounds for simple text 'Hello' at font size 12");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        bounds.Should().NotBe(default(Rect), "bounds should be calculated");
        bounds.Width.Should().BeGreaterThan(0, "text should have width");
        bounds.Height.Should().BeGreaterThan(0, "text should have height");
        bounds.Height.Should().BeApproximately(12, 1, "height should be approximately font size");

        _output.WriteLine($"Bounds: X={bounds.X}, Y={bounds.Y}, W={bounds.Width}, H={bounds.Height}");
    }

    [Fact]
    public void CalculateBounds_WithEmptyText_ShouldReturnEmptyRect()
    {
        // Arrange
        var textState = new PdfTextState { FontSize = 12 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds for empty text");

        // Act
        var bounds = _calculator.CalculateBounds("", textState, graphicsState, pageHeight);

        // Assert
        bounds.Should().Be(default(Rect), "empty text should return empty rect");

        _output.WriteLine("Empty text returns empty rect");
    }

    [Fact]
    public void CalculateBounds_WithNullText_ShouldReturnEmptyRect()
    {
        // Arrange
        var textState = new PdfTextState { FontSize = 12 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds for null text");

        // Act
        var bounds = _calculator.CalculateBounds(null!, textState, graphicsState, pageHeight);

        // Assert
        bounds.Should().Be(default(Rect), "null text should return empty rect");

        _output.WriteLine("Null text returns empty rect");
    }

    [Fact]
    public void CalculateBounds_WithZeroFontSize_ShouldReturnEmptyRect()
    {
        // Arrange
        var text = "Test";
        var textState = new PdfTextState { FontSize = 0 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds with zero font size");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        bounds.Should().Be(default(Rect), "zero font size should return empty rect");

        _output.WriteLine("Zero font size returns empty rect");
    }

    [Fact]
    public void CalculateBounds_WithLargerFontSize_ShouldHaveLargerBounds()
    {
        // Arrange
        var text = "Test";
        var textState12 = new PdfTextState { FontSize = 12 };
        var textState24 = new PdfTextState { FontSize = 24 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Compare bounds with different font sizes");

        // Act
        var bounds12 = _calculator.CalculateBounds(text, textState12, graphicsState, pageHeight);
        var bounds24 = _calculator.CalculateBounds(text, textState24, graphicsState, pageHeight);

        // Assert
        bounds24.Width.Should().BeGreaterThan(bounds12.Width, "larger font should have larger width");
        bounds24.Height.Should().BeGreaterThan(bounds12.Height, "larger font should have larger height");

        _output.WriteLine($"12pt: W={bounds12.Width}, H={bounds12.Height}");
        _output.WriteLine($"24pt: W={bounds24.Width}, H={bounds24.Height}");
    }

    [Fact]
    public void CalculateBounds_WithTextTranslation_ShouldOffsetPosition()
    {
        // Arrange
        var text = "Test";
        var textState = new PdfTextState { FontSize = 12 };
        textState.TranslateText(100, 200);
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds with text translation (100, 200)");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        bounds.X.Should().Be(100, "X position should be translated");
        // Y is converted from PDF coordinates, so we check it's calculated
        bounds.Y.Should().BeGreaterThanOrEqualTo(0, "Y should be valid");

        _output.WriteLine($"Translated bounds: X={bounds.X}, Y={bounds.Y}");
    }

    [Fact]
    public void CalculateBounds_WithGraphicsTransformation_ShouldApplyTransformation()
    {
        // Arrange
        var text = "Test";
        var textState = new PdfTextState { FontSize = 12 };
        var graphicsState = new PdfGraphicsState
        {
            TransformationMatrix = PdfMatrix.CreateTranslation(50, 100)
        };
        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds with graphics transformation");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        bounds.X.Should().Be(50, "graphics transformation should offset X");
        bounds.Should().NotBe(default(Rect), "bounds should be calculated");

        _output.WriteLine($"Transformed bounds: X={bounds.X}, Y={bounds.Y}");
    }

    [Fact]
    public void CalculateBounds_WithCharacterSpacing_ShouldIncreaseWidth()
    {
        // Arrange
        var text = "Test";
        var textStateNoSpacing = new PdfTextState { FontSize = 12, CharacterSpacing = 0 };
        var textStateWithSpacing = new PdfTextState { FontSize = 12, CharacterSpacing = 5 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Compare bounds with and without character spacing");

        // Act
        var boundsNoSpacing = _calculator.CalculateBounds(text, textStateNoSpacing, graphicsState, pageHeight);
        var boundsWithSpacing = _calculator.CalculateBounds(text, textStateWithSpacing, graphicsState, pageHeight);

        // Assert
        boundsWithSpacing.Width.Should().BeGreaterThan(boundsNoSpacing.Width,
            "character spacing should increase width");

        _output.WriteLine($"No spacing: W={boundsNoSpacing.Width}");
        _output.WriteLine($"With spacing (5): W={boundsWithSpacing.Width}");
    }

    [Fact]
    public void CalculateBounds_WithWordSpacing_ShouldIncreaseWidthForSpaces()
    {
        // Arrange
        var text = "Hello World";
        var textStateNoSpacing = new PdfTextState { FontSize = 12, WordSpacing = 0 };
        var textStateWithSpacing = new PdfTextState { FontSize = 12, WordSpacing = 10 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Compare bounds with and without word spacing");

        // Act
        var boundsNoSpacing = _calculator.CalculateBounds(text, textStateNoSpacing, graphicsState, pageHeight);
        var boundsWithSpacing = _calculator.CalculateBounds(text, textStateWithSpacing, graphicsState, pageHeight);

        // Assert
        boundsWithSpacing.Width.Should().BeGreaterThan(boundsNoSpacing.Width,
            "word spacing should increase width for text with spaces");

        _output.WriteLine($"No word spacing: W={boundsNoSpacing.Width}");
        _output.WriteLine($"With word spacing (10): W={boundsWithSpacing.Width}");
    }

    [Fact]
    public void CalculateBounds_WithHorizontalScaling_ShouldScaleWidth()
    {
        // Arrange
        var text = "Test";
        var textState100 = new PdfTextState { FontSize = 12, HorizontalScaling = 100 };
        var textState150 = new PdfTextState { FontSize = 12, HorizontalScaling = 150 };
        var textState50 = new PdfTextState { FontSize = 12, HorizontalScaling = 50 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Compare bounds with different horizontal scaling");

        // Act
        var bounds100 = _calculator.CalculateBounds(text, textState100, graphicsState, pageHeight);
        var bounds150 = _calculator.CalculateBounds(text, textState150, graphicsState, pageHeight);
        var bounds50 = _calculator.CalculateBounds(text, textState50, graphicsState, pageHeight);

        // Assert
        bounds150.Width.Should().BeGreaterThan(bounds100.Width, "150% scaling should increase width");
        bounds50.Width.Should().BeLessThan(bounds100.Width, "50% scaling should decrease width");
        bounds150.Width.Should().BeApproximately(bounds100.Width * 1.5, 1, "150% should be ~1.5x width");
        bounds50.Width.Should().BeApproximately(bounds100.Width * 0.5, 1, "50% should be ~0.5x width");

        _output.WriteLine($"100% scaling: W={bounds100.Width}");
        _output.WriteLine($"150% scaling: W={bounds150.Width}");
        _output.WriteLine($"50% scaling: W={bounds50.Width}");
    }

    [Fact]
    public void CalculateBounds_WithCombinedTextAndGraphicsTransforms_ShouldApplyBoth()
    {
        // Arrange
        var text = "Test";
        var textState = new PdfTextState { FontSize = 12 };
        textState.TranslateText(10, 20);
        var graphicsState = new PdfGraphicsState
        {
            TransformationMatrix = PdfMatrix.CreateTranslation(50, 100)
        };
        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds with both text and graphics transformations");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        // Text translation (10, 20) + graphics translation (50, 100) = (60, 120) in PDF coords
        bounds.X.Should().Be(60, "should apply both text and graphics X translation");
        bounds.Should().NotBe(default(Rect), "bounds should be calculated");

        _output.WriteLine($"Combined transform bounds: X={bounds.X}, Y={bounds.Y}");
    }

    [Fact]
    public void CalculateBounds_CoordinateConversion_ShouldConvertFromPdfToAvalonia()
    {
        // Arrange
        var text = "Test";
        var textState = new PdfTextState { FontSize = 12 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Verify PDF to Avalonia coordinate conversion");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        // Y coordinate should be converted from PDF (bottom-left origin) to Avalonia (top-left origin)
        // For text at PDF Y=0, Avalonia Y should be near pageHeight
        bounds.Y.Should().BeGreaterThanOrEqualTo(0, "Y should be in valid Avalonia coordinates");
        bounds.Y.Should().BeLessThanOrEqualTo(pageHeight, "Y should be within page bounds");

        _output.WriteLine($"Avalonia coordinates: Y={bounds.Y} (page height={pageHeight})");
    }

    [Fact]
    public void CalculateBounds_WithLongerText_ShouldHaveWiderBounds()
    {
        // Arrange
        var shortText = "Hi";
        var longText = "Hello World";
        var textState = new PdfTextState { FontSize = 12 };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;
        _output.WriteLine("Test: Compare bounds for short vs long text");

        // Act
        var shortBounds = _calculator.CalculateBounds(shortText, textState, graphicsState, pageHeight);
        var longBounds = _calculator.CalculateBounds(longText, textState, graphicsState, pageHeight);

        // Assert
        longBounds.Width.Should().BeGreaterThan(shortBounds.Width, "longer text should have greater width");
        longBounds.Height.Should().Be(shortBounds.Height, "height should be same (same font size)");

        _output.WriteLine($"Short text '{shortText}': W={shortBounds.Width}");
        _output.WriteLine($"Long text '{longText}': W={longBounds.Width}");
    }

    [Fact]
    public void FontMetrics_Default_ShouldHaveStandardValues()
    {
        // Arrange & Act
        _output.WriteLine("Test: Verify default font metrics");
        var metrics = FontMetrics.Default;

        // Assert
        metrics.AverageCharWidth.Should().Be(600, "default average char width");
        metrics.Ascent.Should().Be(750, "default ascent");
        metrics.Descent.Should().Be(-250, "default descent");
        metrics.CapHeight.Should().Be(700, "default cap height");

        _output.WriteLine($"Default metrics: AvgWidth={metrics.AverageCharWidth}, " +
                         $"Ascent={metrics.Ascent}, Descent={metrics.Descent}");
    }

    [Fact]
    public void CalculateBounds_WithComplexScenario_ShouldCalculateCorrectly()
    {
        // Arrange - simulate real-world scenario
        var text = "CONFIDENTIAL";
        var textState = new PdfTextState
        {
            FontSize = 18,
            CharacterSpacing = 1.5,
            WordSpacing = 0,
            HorizontalScaling = 110
        };
        textState.TranslateText(50, 700);

        var graphicsState = new PdfGraphicsState
        {
            TransformationMatrix = PdfMatrix.CreateScale(1.0, 1.0)
        };

        var pageHeight = 792.0;
        _output.WriteLine("Test: Calculate bounds for complex scenario");

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        bounds.Should().NotBe(default(Rect), "bounds should be calculated");
        bounds.X.Should().Be(50, "X position from text translation");
        bounds.Width.Should().BeGreaterThan(0, "should have width");
        bounds.Height.Should().BeGreaterThan(0, "should have height");
        bounds.Height.Should().BeApproximately(18, 1, "height should be approximately font size");

        _output.WriteLine($"Complex scenario bounds: X={bounds.X}, Y={bounds.Y}, " +
                         $"W={bounds.Width}, H={bounds.Height}");
    }

    [Fact]
    public void CalculateBounds_WithRotation_ShouldProduceNonAxisAlignedDimensions()
    {
        // Arrange
        var text = "Rotate";
        var textState = new PdfTextState { FontSize = 12 };
        var angle = Math.PI / 4; // 45 degrees
        textState.TextMatrix = new PdfMatrix
        {
            A = Math.Cos(angle),
            B = Math.Sin(angle),
            C = -Math.Sin(angle),
            D = Math.Cos(angle),
            E = 100,
            F = 200
        };
        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert
        bounds.X.Should().BeGreaterThan(90);
        bounds.Y.Should().BeLessThan(700);
        bounds.Width.Should().BeGreaterThan(0);
        bounds.Height.Should().BeGreaterThan(0);
    }

    // ========================================================================
    // COORDINATE SYSTEM VERIFICATION TESTS
    // ========================================================================
    // These tests verify that TextBoundsCalculator produces bounds in the
    // SAME coordinate system as RedactionService expects (Avalonia coordinates).

    [Fact]
    public void CalculateBounds_UsesCoordinateConverter_MatchesExpectedConversion()
    {
        // This test verifies that TextBoundsCalculator uses the centralized
        // CoordinateConverter.TextBoundsToPdfPointsTopLeft method

        // Arrange: Text at known PDF position
        var textState = new PdfTextState
        {
            FontSize = 14,
            CharacterSpacing = 0,
            WordSpacing = 0,
            HorizontalScaling = 100
        };
        textState.TranslateText(50, 720); // PDF coordinates (bottom-left origin)

        var graphicsState = new PdfGraphicsState(); // Identity matrix
        var pageHeight = 792.0;
        var text = "Test";

        // Act: Calculate bounds using TextBoundsCalculator
        var calculatorBounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Expected: Using CoordinateConverter.TextBoundsToPdfPointsTopLeft directly
        // Text at PDF Y=720 (baseline), height=14, so top is at Y=734
        // Avalonia Y = pageHeight - top = 792 - 734 = 58
        var expectedBounds = PdfEditor.Services.CoordinateConverter.TextBoundsToPdfPointsTopLeft(
            50,      // X from translation
            720,     // Y baseline in PDF coords
            calculatorBounds.Width,  // Use calculated width (depends on font metrics)
            14,      // Height = font size
            pageHeight);

        // Assert: Coordinates should be in same system (Avalonia top-left)
        // X should match exactly
        calculatorBounds.X.Should().BeApproximately(expectedBounds.X, 0.01,
            "X coordinate should match CoordinateConverter output");

        // Y may differ slightly due to ascent/descent vs simple fontSize
        // TextBoundsCalculator uses font ascent (typically 0.75 * fontSize above baseline)
        // CoordinateConverter.TextBoundsToPdfPointsTopLeft uses fontSize
        // Allow tolerance of ~0.5 * fontSize for font metrics difference
        calculatorBounds.Y.Should().BeApproximately(expectedBounds.Y, textState.FontSize * 0.5,
            "Y coordinate should approximately match - both use Avalonia top-left origin (difference is font ascent vs fontSize)");

        // Height will differ: calculator uses ascent+descent, converter uses fontSize
        calculatorBounds.Height.Should().BeInRange(textState.FontSize * 0.8, textState.FontSize * 1.3,
            "Height should be proportional to font size (ascent+descent vs simple fontSize)");

        _output.WriteLine($"TextBoundsCalculator: ({calculatorBounds.X:F2},{calculatorBounds.Y:F2},{calculatorBounds.Width:F2}x{calculatorBounds.Height:F2})");
        _output.WriteLine($"CoordinateConverter:  ({expectedBounds.X:F2},{expectedBounds.Y:F2},{expectedBounds.Width:F2}x{expectedBounds.Height:F2})");
        _output.WriteLine($"✓ Both use Avalonia coordinates (top-left origin, PDF points)");
    }

    [Theory]
    [InlineData(720, 14, 792)]  // Text near top of Letter page
    [InlineData(100, 12, 792)]  // Text near bottom of Letter page
    [InlineData(770, 10, 842)]  // Text near top of A4 page
    [InlineData(50, 16, 842)]   // Text near bottom of A4 page
    public void CalculateBounds_ProducesAvaloniaCoordinates_DifferentPageHeights(
        double pdfY, double fontSize, double pageHeight)
    {
        // Arrange
        var textState = new PdfTextState
        {
            FontSize = fontSize,
            CharacterSpacing = 0,
            WordSpacing = 0,
            HorizontalScaling = 100
        };
        textState.TranslateText(100, pdfY);

        var graphicsState = new PdfGraphicsState();
        var text = "Test";

        // Act
        var bounds = _calculator.CalculateBounds(text, textState, graphicsState, pageHeight);

        // Assert: Y should be in Avalonia coordinates (0 = top, pageHeight = bottom)
        bounds.Y.Should().BeGreaterThanOrEqualTo(0, "Avalonia Y should be >= 0");
        bounds.Y.Should().BeLessThanOrEqualTo(pageHeight, "Avalonia Y should be <= pageHeight");

        // Calculate expected Avalonia Y using CoordinateConverter
        var pdfTop = pdfY + fontSize;
        var expectedAvaloniaY = PdfEditor.Services.CoordinateConverter.PdfYToAvaloniaY(pdfTop, pageHeight);

        // With ascent/descent, Y position includes font ascent above baseline (~0.75 * fontSize)
        // Allow tolerance for font metrics
        bounds.Y.Should().BeApproximately(expectedAvaloniaY, fontSize * 0.5,
            $"Y coordinate should approximate CoordinateConverter.PdfYToAvaloniaY for PDF Y={pdfY} on page height={pageHeight} (allowing for font ascent)");

        _output.WriteLine($"PDF Y={pdfY}, fontSize={fontSize}, pageHeight={pageHeight} → Avalonia Y={bounds.Y:F2}");
    }

    [Fact]
    public void CalculateBounds_TextNearTop_LowAvaloniaY()
    {
        // Arrange: Text near TOP of page (high PDF Y value)
        var textState = new PdfTextState { FontSize = 12 };
        textState.TranslateText(100, 760); // PDF Y=760 (near top of 792pt page)

        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;

        // Act
        var bounds = _calculator.CalculateBounds("Top", textState, graphicsState, pageHeight);

        // Assert: Near top in PDF → low Y in Avalonia (top-left origin)
        bounds.Y.Should().BeLessThan(50,
            "Text near top of page (PDF Y=760) should have low Avalonia Y (< 50)");

        _output.WriteLine($"Text at PDF Y=760 → Avalonia Y={bounds.Y:F2} (near top = low Y)");
    }

    [Fact]
    public void CalculateBounds_TextNearBottom_HighAvaloniaY()
    {
        // Arrange: Text near BOTTOM of page (low PDF Y value)
        var textState = new PdfTextState { FontSize = 12 };
        textState.TranslateText(100, 50); // PDF Y=50 (near bottom of 792pt page)

        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;

        // Act
        var bounds = _calculator.CalculateBounds("Bottom", textState, graphicsState, pageHeight);

        // Assert: Near bottom in PDF → high Y in Avalonia (top-left origin)
        bounds.Y.Should().BeGreaterThan(700,
            "Text near bottom of page (PDF Y=50) should have high Avalonia Y (> 700)");

        _output.WriteLine($"Text at PDF Y=50 → Avalonia Y={bounds.Y:F2} (near bottom = high Y)");
    }

    [Fact]
    public void CalculateBounds_MatchesRedactionServiceExpectations()
    {
        // This is a CRITICAL test: verify the coordinate system matches
        // what RedactionService expects for intersection testing

        // Arrange: Simulate redaction scenario
        // User selects area at image pixels (150, 100, 300, 50) at 150 DPI
        var imageSelection = new Rect(150, 100, 300, 50);

        // Convert to PDF points (top-left origin) as RedactionService does
        var redactionArea = PdfEditor.Services.CoordinateConverter.ImageSelectionToPdfPointsTopLeft(
            imageSelection, 150);

        // Text at same location in PDF coordinates
        var textState = new PdfTextState
        {
            FontSize = 24,  // Height matches selection: 50 * 72/150 = 24
            CharacterSpacing = 0,
            WordSpacing = 0,
            HorizontalScaling = 100
        };
        // Position to match selection: (150, 100) pixels * 72/150 = (72, 48) points
        // But we need PDF Y (bottom-left), so: Y_pdf = pageHeight - avaloniaY - height
        // avaloniaY = 48, height = 24, pageHeight = 792
        // Y_pdf = 792 - 48 - 24 = 720
        textState.TranslateText(72, 720);

        var graphicsState = new PdfGraphicsState();
        var pageHeight = 792.0;

        // Act: Calculate text bounds
        var textBounds = _calculator.CalculateBounds("REDACT", textState, graphicsState, pageHeight);

        // Assert: Text bounds and redaction area should be in SAME coordinate system
        _output.WriteLine($"Redaction area: ({redactionArea.X:F2},{redactionArea.Y:F2},{redactionArea.Width:F2}x{redactionArea.Height:F2})");
        _output.WriteLine($"Text bounds:    ({textBounds.X:F2},{textBounds.Y:F2},{textBounds.Width:F2}x{textBounds.Height:F2})");

        // Both should have Y near 48 (top-left origin)
        // Allow tolerance for font ascent/descent variations
        textBounds.Y.Should().BeApproximately(redactionArea.Y, 8,
            "Text bounds and redaction area should use SAME Y coordinate system (Avalonia top-left)");

        // Verify they can intersect (using Avalonia Rect.IntersectsWith)
        var intersects = textBounds.Intersects(redactionArea);
        _output.WriteLine($"Intersection test result: {intersects}");

        // They should intersect since they're at the same position
        intersects.Should().BeTrue(
            "Text bounds and redaction area at same position should intersect - " +
            "this proves both use the SAME coordinate system (Avalonia top-left origin, PDF points)");
    }
}
