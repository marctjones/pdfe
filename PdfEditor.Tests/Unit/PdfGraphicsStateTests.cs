using Xunit;
using FluentAssertions;
using PdfEditor.Services.Redaction;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for PdfGraphicsState - graphics state tracking
/// </summary>
public class PdfGraphicsStateTests
{
    private readonly ITestOutputHelper _output;

    public PdfGraphicsStateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Arrange & Act
        _output.WriteLine("Test: Creating new graphics state");
        var state = new PdfGraphicsState();

        // Assert
        state.TransformationMatrix.Should().NotBeNull("transformation matrix should be initialized");
        state.TransformationMatrix.A.Should().Be(1, "default matrix A should be identity");
        state.TransformationMatrix.B.Should().Be(0, "default matrix B should be identity");
        state.TransformationMatrix.C.Should().Be(0, "default matrix C should be identity");
        state.TransformationMatrix.D.Should().Be(1, "default matrix D should be identity");
        state.TransformationMatrix.E.Should().Be(0, "default matrix E should be identity");
        state.TransformationMatrix.F.Should().Be(0, "default matrix F should be identity");

        state.LineWidth.Should().Be(1.0, "default line width should be 1.0");
        state.StrokeColor.Should().NotBeNull("stroke color should be initialized");
        state.FillColor.Should().NotBeNull("fill color should be initialized");
        state.LineDashPattern.Should().NotBeNull("dash pattern should be initialized");
        state.LineDashPattern.Should().BeEmpty("default dash pattern should be empty");
        state.LineDashPhase.Should().Be(0, "default dash phase should be 0");

        _output.WriteLine("Graphics state initialized with correct defaults");
    }

    [Fact]
    public void TransformationMatrix_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfGraphicsState();
        var newMatrix = PdfMatrix.CreateTranslation(100, 200);
        _output.WriteLine("Test: Modify transformation matrix");

        // Act
        state.TransformationMatrix = newMatrix;

        // Assert
        state.TransformationMatrix.Should().BeSameAs(newMatrix, "should store the new matrix reference");
        state.TransformationMatrix.E.Should().Be(100, "translation X should be updated");
        state.TransformationMatrix.F.Should().Be(200, "translation Y should be updated");

        _output.WriteLine($"Matrix updated to translation ({newMatrix.E}, {newMatrix.F})");
    }

    [Fact]
    public void LineWidth_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfGraphicsState();
        _output.WriteLine("Test: Modify line width");

        // Act
        state.LineWidth = 5.5;

        // Assert
        state.LineWidth.Should().Be(5.5, "line width should be updated");

        _output.WriteLine("Line width updated to 5.5");
    }

    [Fact]
    public void StrokeColor_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfGraphicsState();
        var redColor = new PdfColor
        {
            Components = new[] { 1.0, 0.0, 0.0 },
            Space = ColorSpace.RGB
        };
        _output.WriteLine("Test: Modify stroke color");

        // Act
        state.StrokeColor = redColor;

        // Assert
        state.StrokeColor.Should().BeSameAs(redColor, "should store the new color reference");
        state.StrokeColor.Components.Should().BeEquivalentTo(new[] { 1.0, 0.0, 0.0 }, "RGB red color");
        state.StrokeColor.Space.Should().Be(ColorSpace.RGB, "color space should be RGB");

        _output.WriteLine("Stroke color updated to RGB red");
    }

    [Fact]
    public void FillColor_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfGraphicsState();
        var blueColor = new PdfColor
        {
            Components = new[] { 0.0, 0.0, 1.0 },
            Space = ColorSpace.RGB
        };
        _output.WriteLine("Test: Modify fill color");

        // Act
        state.FillColor = blueColor;

        // Assert
        state.FillColor.Should().BeSameAs(blueColor, "should store the new color reference");
        state.FillColor.Components.Should().BeEquivalentTo(new[] { 0.0, 0.0, 1.0 }, "RGB blue color");
        state.FillColor.Space.Should().Be(ColorSpace.RGB, "color space should be RGB");

        _output.WriteLine("Fill color updated to RGB blue");
    }

    [Fact]
    public void LineDashPattern_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfGraphicsState();
        var dashPattern = new[] { 3.0, 5.0, 3.0, 5.0 };
        _output.WriteLine($"Test: Modify dash pattern to [{string.Join(", ", dashPattern)}]");

        // Act
        state.LineDashPattern = dashPattern;

        // Assert
        state.LineDashPattern.Should().BeSameAs(dashPattern, "should store the new pattern reference");
        state.LineDashPattern.Should().BeEquivalentTo(dashPattern, "pattern values should match");

        _output.WriteLine("Dash pattern updated");
    }

    [Fact]
    public void LineDashPhase_ShouldBeModifiable()
    {
        // Arrange
        var state = new PdfGraphicsState();
        _output.WriteLine("Test: Modify dash phase");

        // Act
        state.LineDashPhase = 10;

        // Assert
        state.LineDashPhase.Should().Be(10, "dash phase should be updated");

        _output.WriteLine("Dash phase updated to 10");
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var original = new PdfGraphicsState
        {
            TransformationMatrix = PdfMatrix.CreateTranslation(50, 100),
            LineWidth = 2.5,
            StrokeColor = new PdfColor { Components = new[] { 1.0, 0.0, 0.0 }, Space = ColorSpace.RGB },
            FillColor = new PdfColor { Components = new[] { 0.0, 1.0, 0.0 }, Space = ColorSpace.RGB },
            LineDashPattern = new[] { 3.0, 5.0 },
            LineDashPhase = 7
        };
        _output.WriteLine("Test: Clone graphics state");

        // Act
        var clone = original.Clone();

        // Assert - verify all properties are copied
        clone.Should().NotBeSameAs(original, "clone should be a different instance");
        clone.TransformationMatrix.Should().NotBeSameAs(original.TransformationMatrix, "matrix should be deep copied");
        clone.TransformationMatrix.E.Should().Be(50, "matrix translation X should match");
        clone.TransformationMatrix.F.Should().Be(100, "matrix translation Y should match");
        clone.LineWidth.Should().Be(2.5, "line width should match");
        clone.StrokeColor.Should().BeSameAs(original.StrokeColor, "color references are shared (shallow for colors)");
        clone.FillColor.Should().BeSameAs(original.FillColor, "color references are shared (shallow for colors)");
        clone.LineDashPattern.Should().NotBeSameAs(original.LineDashPattern, "dash pattern should be deep copied");
        clone.LineDashPattern.Should().BeEquivalentTo(new[] { 3.0, 5.0 }, "pattern values should match");
        clone.LineDashPhase.Should().Be(7, "dash phase should match");

        _output.WriteLine("Clone created successfully with all properties copied");
    }

    [Fact]
    public void Clone_ModifyingClone_ShouldNotAffectOriginal()
    {
        // Arrange
        var original = new PdfGraphicsState
        {
            TransformationMatrix = PdfMatrix.CreateTranslation(50, 100),
            LineWidth = 2.5,
            LineDashPattern = new[] { 3.0, 5.0 },
            LineDashPhase = 7
        };
        _output.WriteLine("Test: Modify clone and verify original unchanged");

        // Act
        var clone = original.Clone();

        // Modify the clone
        clone.TransformationMatrix.E = 999;
        clone.TransformationMatrix.F = 888;
        clone.LineWidth = 99.9;
        clone.LineDashPattern[0] = 111.0;
        clone.LineDashPhase = 999;

        // Assert - original should be unchanged
        original.TransformationMatrix.E.Should().Be(50, "original matrix E should be unchanged");
        original.TransformationMatrix.F.Should().Be(100, "original matrix F should be unchanged");
        original.LineWidth.Should().Be(2.5, "original line width should be unchanged");
        original.LineDashPattern[0].Should().Be(3.0, "original dash pattern should be unchanged");
        original.LineDashPhase.Should().Be(7, "original dash phase should be unchanged");

        // Clone should have the modifications
        clone.TransformationMatrix.E.Should().Be(999, "clone matrix E should be modified");
        clone.TransformationMatrix.F.Should().Be(888, "clone matrix F should be modified");
        clone.LineWidth.Should().Be(99.9, "clone line width should be modified");
        clone.LineDashPattern[0].Should().Be(111.0, "clone dash pattern should be modified");
        clone.LineDashPhase.Should().Be(999, "clone dash phase should be modified");

        _output.WriteLine("Clone is independent from original - modifications isolated");
    }

    [Fact]
    public void Clone_ModifyingDashPattern_ShouldNotMutateOriginalArray()
    {
        // Arrange
        var original = new PdfGraphicsState
        {
            LineDashPattern = new[] { 1.0, 2.0, 3.0 }
        };

        // Act
        var clone = original.Clone();
        clone.LineDashPattern[0] = 42.0;

        // Assert
        original.LineDashPattern[0].Should().Be(1.0, "original dash pattern should remain unchanged");
        clone.LineDashPattern[0].Should().Be(42.0, "clone dash pattern should reflect modification");
    }

    [Fact]
    public void Clone_MultipleClones_ShouldAllBeIndependent()
    {
        // Arrange
        var original = new PdfGraphicsState
        {
            LineWidth = 1.0,
            LineDashPhase = 0
        };
        _output.WriteLine("Test: Create multiple independent clones");

        // Act
        var clone1 = original.Clone();
        var clone2 = original.Clone();
        var clone3 = clone1.Clone();

        clone1.LineWidth = 10.0;
        clone2.LineWidth = 20.0;
        clone3.LineWidth = 30.0;

        // Assert
        original.LineWidth.Should().Be(1.0, "original should be unchanged");
        clone1.LineWidth.Should().Be(10.0, "clone1 should have its own value");
        clone2.LineWidth.Should().Be(20.0, "clone2 should have its own value");
        clone3.LineWidth.Should().Be(30.0, "clone3 should have its own value");

        _output.WriteLine("All clones are independent");
    }

    [Fact]
    public void GraphicsState_WithComplexState_ShouldCloneCorrectly()
    {
        // Arrange
        var complexMatrix = new PdfMatrix
        {
            A = 2.0, B = 0.5,
            C = 0.3, D = 1.5,
            E = 100, F = 200
        };

        var state = new PdfGraphicsState
        {
            TransformationMatrix = complexMatrix,
            LineWidth = 3.75,
            StrokeColor = new PdfColor
            {
                Components = new[] { 0.5, 0.3, 0.7, 0.1 },
                Space = ColorSpace.CMYK
            },
            FillColor = new PdfColor
            {
                Components = new[] { 0.8 },
                Space = ColorSpace.Gray
            },
            LineDashPattern = new[] { 5.0, 3.0, 1.0, 3.0 },
            LineDashPhase = 12
        };
        _output.WriteLine("Test: Clone complex graphics state");

        // Act
        var clone = state.Clone();

        // Assert
        clone.TransformationMatrix.A.Should().Be(2.0);
        clone.TransformationMatrix.B.Should().Be(0.5);
        clone.TransformationMatrix.C.Should().Be(0.3);
        clone.TransformationMatrix.D.Should().Be(1.5);
        clone.TransformationMatrix.E.Should().Be(100);
        clone.TransformationMatrix.F.Should().Be(200);
        clone.LineWidth.Should().Be(3.75);
        clone.StrokeColor.Components.Should().BeEquivalentTo(new[] { 0.5, 0.3, 0.7, 0.1 });
        clone.StrokeColor.Space.Should().Be(ColorSpace.CMYK);
        clone.FillColor.Components.Should().BeEquivalentTo(new[] { 0.8 });
        clone.FillColor.Space.Should().Be(ColorSpace.Gray);
        clone.LineDashPattern.Should().BeEquivalentTo(new[] { 5.0, 3.0, 1.0, 3.0 });
        clone.LineDashPhase.Should().Be(12);

        _output.WriteLine("Complex state cloned correctly with all properties");
    }
}
