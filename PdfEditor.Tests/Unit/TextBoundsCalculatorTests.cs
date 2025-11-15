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
}
