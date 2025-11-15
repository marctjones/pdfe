using Xunit;
using FluentAssertions;
using PdfEditor.Services.Redaction;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for PdfTextState - text state tracking
/// </summary>
public class PdfTextStateTests
{
    private readonly ITestOutputHelper _output;

    public PdfTextStateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        _output.WriteLine("Test: Creating new text state");
        var state = new PdfTextState();

        // Assert
        state.FontName.Should().BeNull("font name is not set by default");
        state.FontSize.Should().Be(12, "default font size should be 12");
        state.CharacterSpacing.Should().Be(0, "default character spacing should be 0");
        state.WordSpacing.Should().Be(0, "default word spacing should be 0");
        state.HorizontalScaling.Should().Be(100.0, "default horizontal scaling should be 100%");
        state.Leading.Should().Be(0, "default leading should be 0");
        state.Rise.Should().Be(0, "default text rise should be 0");
        state.RenderingMode.Should().Be(0, "default rendering mode should be 0 (fill)");

        state.TextMatrix.Should().NotBeNull("text matrix should be initialized");
        state.TextMatrix.A.Should().Be(1, "text matrix should be identity");
        state.TextMatrix.D.Should().Be(1, "text matrix should be identity");

        state.TextLineMatrix.Should().NotBeNull("text line matrix should be initialized");
        state.TextLineMatrix.A.Should().Be(1, "text line matrix should be identity");
        state.TextLineMatrix.D.Should().Be(1, "text line matrix should be identity");

        state.FontResource.Should().BeNull("font resource is not set by default");

        _output.WriteLine("Text state initialized with correct defaults");
    }

    [Fact]
    public void FontProperties_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfTextState();
        _output.WriteLine("Test: Modify font properties");

        // Act
        state.FontName = "Helvetica";
        state.FontSize = 24.0;

        // Assert
        state.FontName.Should().Be("Helvetica", "font name should be updated");
        state.FontSize.Should().Be(24.0, "font size should be updated");

        _output.WriteLine("Font properties updated: Helvetica 24pt");
    }

    [Fact]
    public void SpacingProperties_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfTextState();
        _output.WriteLine("Test: Modify spacing properties");

        // Act
        state.CharacterSpacing = 2.5;
        state.WordSpacing = 5.0;
        state.Leading = 14.0;

        // Assert
        state.CharacterSpacing.Should().Be(2.5, "character spacing should be updated");
        state.WordSpacing.Should().Be(5.0, "word spacing should be updated");
        state.Leading.Should().Be(14.0, "leading should be updated");

        _output.WriteLine("Spacing properties updated");
    }

    [Fact]
    public void HorizontalScaling_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfTextState();
        _output.WriteLine("Test: Modify horizontal scaling");

        // Act
        state.HorizontalScaling = 150.0;

        // Assert
        state.HorizontalScaling.Should().Be(150.0, "horizontal scaling should be 150%");

        _output.WriteLine("Horizontal scaling updated to 150%");
    }

    [Fact]
    public void RenderingMode_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfTextState();
        _output.WriteLine("Test: Modify rendering mode");

        // Act
        state.RenderingMode = 2; // stroke text

        // Assert
        state.RenderingMode.Should().Be(2, "rendering mode should be updated");

        _output.WriteLine("Rendering mode updated to 2 (stroke)");
    }

    [Fact]
    public void ResetMatrices_ShouldResetBothMatricesToIdentity()
    {
        // Arrange
        var state = new PdfTextState();
        state.TextMatrix = PdfMatrix.CreateTranslation(100, 200);
        state.TextLineMatrix = PdfMatrix.CreateScale(2, 3);
        _output.WriteLine("Test: Reset matrices to identity");

        // Act
        state.ResetMatrices();

        // Assert
        state.TextMatrix.A.Should().Be(1, "text matrix should be identity");
        state.TextMatrix.B.Should().Be(0);
        state.TextMatrix.C.Should().Be(0);
        state.TextMatrix.D.Should().Be(1);
        state.TextMatrix.E.Should().Be(0, "text matrix translation should be reset");
        state.TextMatrix.F.Should().Be(0);

        state.TextLineMatrix.A.Should().Be(1, "text line matrix should be identity");
        state.TextLineMatrix.B.Should().Be(0);
        state.TextLineMatrix.C.Should().Be(0);
        state.TextLineMatrix.D.Should().Be(1);
        state.TextLineMatrix.E.Should().Be(0);
        state.TextLineMatrix.F.Should().Be(0);

        _output.WriteLine("Both matrices reset to identity");
    }

    [Fact]
    public void TranslateText_ShouldUpdateBothMatrices()
    {
        // Arrange
        var state = new PdfTextState();
        _output.WriteLine("Test: Translate text by (50, 100)");

        // Act
        state.TranslateText(50, 100);

        // Assert
        state.TextMatrix.E.Should().Be(50, "text matrix should have translation X");
        state.TextMatrix.F.Should().Be(100, "text matrix should have translation Y");
        state.TextLineMatrix.E.Should().Be(50, "text line matrix should have translation X");
        state.TextLineMatrix.F.Should().Be(100, "text line matrix should have translation Y");

        _output.WriteLine($"Text translated to ({state.TextMatrix.E}, {state.TextMatrix.F})");
    }

    [Fact]
    public void TranslateText_Multiple_ShouldAccumulate()
    {
        // Arrange
        var state = new PdfTextState();
        _output.WriteLine("Test: Multiple text translations");

        // Act
        state.TranslateText(10, 20);
        state.TranslateText(5, 10);

        // Assert
        state.TextMatrix.E.Should().Be(15, "translations should accumulate X");
        state.TextMatrix.F.Should().Be(30, "translations should accumulate Y");

        _output.WriteLine($"Accumulated translation: ({state.TextMatrix.E}, {state.TextMatrix.F})");
    }

    [Fact]
    public void SetTextMatrix_ShouldSetBothMatrices()
    {
        // Arrange
        var state = new PdfTextState();
        var newMatrix = new PdfMatrix
        {
            A = 2, B = 0.5,
            C = 0.3, D = 1.5,
            E = 100, F = 200
        };
        _output.WriteLine("Test: Set text matrix");

        // Act
        state.SetTextMatrix(newMatrix);

        // Assert
        state.TextMatrix.Should().NotBeSameAs(newMatrix, "should create a clone, not use reference");
        state.TextMatrix.A.Should().Be(2, "text matrix A should match");
        state.TextMatrix.B.Should().Be(0.5, "text matrix B should match");
        state.TextMatrix.E.Should().Be(100, "text matrix E should match");
        state.TextMatrix.F.Should().Be(200, "text matrix F should match");

        state.TextLineMatrix.A.Should().Be(2, "text line matrix should match");
        state.TextLineMatrix.E.Should().Be(100, "text line matrix E should match");
        state.TextLineMatrix.F.Should().Be(200, "text line matrix F should match");

        _output.WriteLine("Both matrices set to new values");
    }

    [Fact]
    public void MoveToNextLine_ShouldUseLeading()
    {
        // Arrange
        var state = new PdfTextState();
        state.Leading = 14.0;
        state.TranslateText(50, 100); // Start at (50, 100)
        _output.WriteLine("Test: Move to next line with leading 14");

        // Act
        state.MoveToNextLine();

        // Assert
        state.TextMatrix.E.Should().Be(50, "X position should not change");
        state.TextMatrix.F.Should().Be(86, "Y position should decrease by leading (100 - 14 = 86)");

        _output.WriteLine($"Moved to next line: ({state.TextMatrix.E}, {state.TextMatrix.F})");
    }

    [Fact]
    public void MoveToNextLine_Multiple_ShouldMoveDownMultipleTimes()
    {
        // Arrange
        var state = new PdfTextState();
        state.Leading = 12.0;
        state.TranslateText(0, 100);
        _output.WriteLine("Test: Move to next line 3 times");

        // Act
        state.MoveToNextLine();
        state.MoveToNextLine();
        state.MoveToNextLine();

        // Assert
        // Starting at 100, move down by 12 three times: 100 - 12 - 12 - 12 = 64
        state.TextMatrix.F.Should().Be(64, "Y should decrease by leading * 3");

        _output.WriteLine($"After 3 line moves: Y = {state.TextMatrix.F}");
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var original = new PdfTextState
        {
            FontName = "Times-Roman",
            FontSize = 18.0,
            CharacterSpacing = 1.5,
            WordSpacing = 3.0,
            HorizontalScaling = 120.0,
            Leading = 20.0,
            Rise = 2.0,
            RenderingMode = 1,
            TextMatrix = PdfMatrix.CreateTranslation(50, 100),
            TextLineMatrix = PdfMatrix.CreateTranslation(50, 100)
        };
        _output.WriteLine("Test: Clone text state");

        // Act
        var clone = original.Clone();

        // Assert
        clone.Should().NotBeSameAs(original, "clone should be different instance");
        clone.FontName.Should().Be("Times-Roman", "font name should match");
        clone.FontSize.Should().Be(18.0, "font size should match");
        clone.CharacterSpacing.Should().Be(1.5, "character spacing should match");
        clone.WordSpacing.Should().Be(3.0, "word spacing should match");
        clone.HorizontalScaling.Should().Be(120.0, "horizontal scaling should match");
        clone.Leading.Should().Be(20.0, "leading should match");
        clone.Rise.Should().Be(2.0, "rise should match");
        clone.RenderingMode.Should().Be(1, "rendering mode should match");

        clone.TextMatrix.Should().NotBeSameAs(original.TextMatrix, "text matrix should be deep copied");
        clone.TextMatrix.E.Should().Be(50, "text matrix E should match");
        clone.TextMatrix.F.Should().Be(100, "text matrix F should match");

        clone.TextLineMatrix.Should().NotBeSameAs(original.TextLineMatrix, "text line matrix should be deep copied");

        _output.WriteLine("Clone created with all properties copied");
    }

    [Fact]
    public void Clone_ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new PdfTextState
        {
            FontName = "Helvetica",
            FontSize = 12.0,
            TextMatrix = PdfMatrix.CreateTranslation(10, 20)
        };
        _output.WriteLine("Test: Modify clone and verify original unchanged");

        // Act
        var clone = original.Clone();
        clone.FontName = "Arial";
        clone.FontSize = 24.0;
        clone.TextMatrix.E = 999;
        clone.TextMatrix.F = 888;

        // Assert
        original.FontName.Should().Be("Helvetica", "original font name should be unchanged");
        original.FontSize.Should().Be(12.0, "original font size should be unchanged");
        original.TextMatrix.E.Should().Be(10, "original matrix E should be unchanged");
        original.TextMatrix.F.Should().Be(20, "original matrix F should be unchanged");

        clone.FontName.Should().Be("Arial", "clone font name should be modified");
        clone.FontSize.Should().Be(24.0, "clone font size should be modified");
        clone.TextMatrix.E.Should().Be(999, "clone matrix E should be modified");
        clone.TextMatrix.F.Should().Be(888, "clone matrix F should be modified");

        _output.WriteLine("Clone is independent from original");
    }

    [Fact]
    public void TextState_WithComplexState_ShouldCloneCorrectly()
    {
        // Arrange
        var complexMatrix = new PdfMatrix
        {
            A = 1.5, B = 0.2,
            C = 0.1, D = 1.3,
            E = 75, F = 150
        };

        var state = new PdfTextState
        {
            FontName = "Courier-Bold",
            FontSize = 16.5,
            CharacterSpacing = 0.8,
            WordSpacing = 2.5,
            HorizontalScaling = 95.0,
            Leading = 18.0,
            Rise = 3.5,
            RenderingMode = 2,
            TextMatrix = complexMatrix.Clone(),
            TextLineMatrix = complexMatrix.Clone()
        };
        _output.WriteLine("Test: Clone complex text state");

        // Act
        var clone = state.Clone();

        // Assert
        clone.FontName.Should().Be("Courier-Bold");
        clone.FontSize.Should().Be(16.5);
        clone.CharacterSpacing.Should().Be(0.8);
        clone.WordSpacing.Should().Be(2.5);
        clone.HorizontalScaling.Should().Be(95.0);
        clone.Leading.Should().Be(18.0);
        clone.Rise.Should().Be(3.5);
        clone.RenderingMode.Should().Be(2);
        clone.TextMatrix.A.Should().Be(1.5);
        clone.TextMatrix.B.Should().Be(0.2);
        clone.TextMatrix.E.Should().Be(75);
        clone.TextMatrix.F.Should().Be(150);

        _output.WriteLine("Complex text state cloned correctly");
    }

    [Fact]
    public void TranslateText_AfterReset_ShouldStartFromIdentity()
    {
        // Arrange
        var state = new PdfTextState();
        state.TranslateText(100, 200);
        _output.WriteLine("Test: Translate after reset");

        // Act
        state.ResetMatrices();
        state.TranslateText(10, 20);

        // Assert
        state.TextMatrix.E.Should().Be(10, "should translate from identity, not accumulate");
        state.TextMatrix.F.Should().Be(20, "should translate from identity, not accumulate");

        _output.WriteLine("Translation started fresh from identity after reset");
    }
}
