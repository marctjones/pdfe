using System;
using Xunit;
using FluentAssertions;
using PdfEditor.Services.Redaction;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Unit;

/// <summary>
/// Unit tests for PdfMatrix - transformation matrix mathematics
/// </summary>
public class PdfMatrixTests
{
    private readonly ITestOutputHelper _output;

    public PdfMatrixTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Identity_ShouldCreateIdentityMatrix()
    {
        // Arrange & Act
        _output.WriteLine("Test: Creating identity matrix");
        var matrix = PdfMatrix.Identity;

        // Assert
        matrix.A.Should().Be(1, "identity matrix A should be 1");
        matrix.B.Should().Be(0, "identity matrix B should be 0");
        matrix.C.Should().Be(0, "identity matrix C should be 0");
        matrix.D.Should().Be(1, "identity matrix D should be 1");
        matrix.E.Should().Be(0, "identity matrix E should be 0");
        matrix.F.Should().Be(0, "identity matrix F should be 0");
        
        _output.WriteLine($"Identity matrix: [{matrix.A} {matrix.B} {matrix.C} {matrix.D} {matrix.E} {matrix.F}]");
    }

    [Fact]
    public void Transform_WithIdentityMatrix_ShouldReturnSamePoint()
    {
        // Arrange
        var matrix = PdfMatrix.Identity;
        var x = 100.0;
        var y = 200.0;
        _output.WriteLine($"Test: Transform point ({x}, {y}) with identity matrix");

        // Act
        var (resultX, resultY) = matrix.Transform(x, y);

        // Assert
        resultX.Should().Be(x, "identity transformation should not change X");
        resultY.Should().Be(y, "identity transformation should not change Y");
        
        _output.WriteLine($"Result: ({resultX}, {resultY})");
    }

    [Fact]
    public void CreateTranslation_ShouldCreateTranslationMatrix()
    {
        // Arrange & Act
        var tx = 50.0;
        var ty = 100.0;
        _output.WriteLine($"Test: Create translation matrix ({tx}, {ty})");
        
        var matrix = PdfMatrix.CreateTranslation(tx, ty);

        // Assert
        matrix.A.Should().Be(1, "translation doesn't affect A");
        matrix.B.Should().Be(0, "translation doesn't affect B");
        matrix.C.Should().Be(0, "translation doesn't affect C");
        matrix.D.Should().Be(1, "translation doesn't affect D");
        matrix.E.Should().Be(tx, "E should be translation X");
        matrix.F.Should().Be(ty, "F should be translation Y");
        
        _output.WriteLine($"Translation matrix: [{matrix.A} {matrix.B} {matrix.C} {matrix.D} {matrix.E} {matrix.F}]");
    }

    [Fact]
    public void Transform_WithTranslation_ShouldOffsetPoint()
    {
        // Arrange
        var translation = PdfMatrix.CreateTranslation(50, 100);
        var x = 10.0;
        var y = 20.0;
        _output.WriteLine($"Test: Transform ({x}, {y}) with translation (50, 100)");

        // Act
        var (resultX, resultY) = translation.Transform(x, y);

        // Assert
        resultX.Should().Be(60, "X should be offset by 50");
        resultY.Should().Be(120, "Y should be offset by 100");
        
        _output.WriteLine($"Result: ({resultX}, {resultY})");
    }

    [Fact]
    public void CreateScale_ShouldCreateScalingMatrix()
    {
        // Arrange & Act
        var sx = 2.0;
        var sy = 3.0;
        _output.WriteLine($"Test: Create scaling matrix ({sx}, {sy})");
        
        var matrix = PdfMatrix.CreateScale(sx, sy);

        // Assert
        matrix.A.Should().Be(sx, "A should be scale X");
        matrix.B.Should().Be(0, "scaling doesn't affect B");
        matrix.C.Should().Be(0, "scaling doesn't affect C");
        matrix.D.Should().Be(sy, "D should be scale Y");
        matrix.E.Should().Be(0, "scaling doesn't affect E");
        matrix.F.Should().Be(0, "scaling doesn't affect F");
        
        _output.WriteLine($"Scale matrix: [{matrix.A} {matrix.B} {matrix.C} {matrix.D} {matrix.E} {matrix.F}]");
    }

    [Fact]
    public void Transform_WithScale_ShouldScalePoint()
    {
        // Arrange
        var scale = PdfMatrix.CreateScale(2, 3);
        var x = 10.0;
        var y = 20.0;
        _output.WriteLine($"Test: Transform ({x}, {y}) with scale (2, 3)");

        // Act
        var (resultX, resultY) = scale.Transform(x, y);

        // Assert
        resultX.Should().Be(20, "X should be scaled by 2");
        resultY.Should().Be(60, "Y should be scaled by 3");
        
        _output.WriteLine($"Result: ({resultX}, {resultY})");
    }

    [Fact]
    public void Multiply_IdentityWithIdentity_ShouldReturnIdentity()
    {
        // Arrange
        var m1 = PdfMatrix.Identity;
        var m2 = PdfMatrix.Identity;
        _output.WriteLine("Test: Multiply identity * identity");

        // Act
        var result = m1.Multiply(m2);

        // Assert
        result.A.Should().Be(1);
        result.B.Should().Be(0);
        result.C.Should().Be(0);
        result.D.Should().Be(1);
        result.E.Should().Be(0);
        result.F.Should().Be(0);
        
        _output.WriteLine("Result is identity matrix");
    }

    [Fact]
    public void Multiply_TranslationThenScale_ShouldCombineTransformations()
    {
        // Arrange
        var translation = PdfMatrix.CreateTranslation(10, 20);
        var scale = PdfMatrix.CreateScale(2, 3);
        _output.WriteLine("Test: Multiply translation(10, 20) * scale(2, 3)");

        // Act
        var combined = translation.Multiply(scale);
        var (x, y) = combined.Transform(5, 10);

        // Assert
        // Combined transformation: first translate (5+10=15, 10+20=30), then scale (15*2=30, 30*3=90)
        x.Should().Be(30, "should apply translation then scale to X");
        y.Should().Be(90, "should apply translation then scale to Y");

        _output.WriteLine($"Combined transform of (5, 10) = ({x}, {y})");
    }

    [Fact]
    public void Multiply_ScaleThenTranslation_ShouldCombineInCorrectOrder()
    {
        // Arrange
        var scale = PdfMatrix.CreateScale(2, 3);
        var translation = PdfMatrix.CreateTranslation(10, 20);
        _output.WriteLine("Test: Multiply scale(2, 3) * translation(10, 20)");

        // Act
        var combined = scale.Multiply(translation);
        var (x, y) = combined.Transform(5, 10);

        // Assert
        // Different order: first scale (5*2=10, 10*3=30), then translate (10+10=20, 30+20=50)
        x.Should().Be(20, "should apply scale then translation to X");
        y.Should().Be(50, "should apply scale then translation to Y");

        _output.WriteLine($"Combined transform of (5, 10) = ({x}, {y})");
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var original = PdfMatrix.CreateTranslation(50, 100);
        _output.WriteLine("Test: Clone matrix");

        // Act
        var clone = original.Clone();
        
        // Modify clone
        clone.E = 999;
        clone.F = 888;

        // Assert
        original.E.Should().Be(50, "original E should be unchanged");
        original.F.Should().Be(100, "original F should be unchanged");
        clone.E.Should().Be(999, "clone E should be modified");
        clone.F.Should().Be(888, "clone F should be modified");
        
        _output.WriteLine("Clone is independent from original");
    }

    [Fact]
    public void FromArray_WithValidArray_ShouldCreateMatrix()
    {
        // Arrange
        var values = new double[] { 1, 2, 3, 4, 5, 6 };
        _output.WriteLine($"Test: Create matrix from array [{string.Join(", ", values)}]");

        // Act
        var matrix = PdfMatrix.FromArray(values);

        // Assert
        matrix.A.Should().Be(1);
        matrix.B.Should().Be(2);
        matrix.C.Should().Be(3);
        matrix.D.Should().Be(4);
        matrix.E.Should().Be(5);
        matrix.F.Should().Be(6);
        
        _output.WriteLine($"Matrix created: [{matrix.A} {matrix.B} {matrix.C} {matrix.D} {matrix.E} {matrix.F}]");
    }

    [Fact]
    public void FromArray_WithInvalidArray_ShouldThrowException()
    {
        // Arrange
        var invalidArray = new double[] { 1, 2, 3 }; // Only 3 elements
        _output.WriteLine("Test: Create matrix from invalid array (should throw)");

        // Act & Assert
        var act = () => PdfMatrix.FromArray(invalidArray);
        
        act.Should().Throw<ArgumentException>()
            .WithMessage("*6 elements*", "array must have 6 elements");
        
        _output.WriteLine("Correctly threw ArgumentException for invalid array");
    }

    [Fact]
    public void Transform_WithComplexMatrix_ShouldCalculateCorrectly()
    {
        // Arrange
        // Create a matrix with all components
        var matrix = new PdfMatrix
        {
            A = 2, B = 0.5,
            C = 0.5, D = 2,
            E = 10, F = 20
        };
        var x = 5.0;
        var y = 10.0;
        _output.WriteLine($"Test: Transform ({x}, {y}) with complex matrix");
        _output.WriteLine($"Matrix: [{matrix.A} {matrix.B} {matrix.C} {matrix.D} {matrix.E} {matrix.F}]");

        // Act
        var (resultX, resultY) = matrix.Transform(x, y);

        // Assert
        // resultX = A*x + C*y + E = 2*5 + 0.5*10 + 10 = 10 + 5 + 10 = 25
        // resultY = B*x + D*y + F = 0.5*5 + 2*10 + 20 = 2.5 + 20 + 20 = 42.5
        resultX.Should().Be(25, "X calculation should be A*x + C*y + E");
        resultY.Should().Be(42.5, "Y calculation should be B*x + D*y + F");
        
        _output.WriteLine($"Result: ({resultX}, {resultY})");
    }

    [Fact]
    public void Multiply_WithComplexMatrices_ShouldFollowMatrixMultiplicationRules()
    {
        // Arrange
        var m1 = new PdfMatrix { A = 2, B = 0, C = 0, D = 2, E = 10, F = 20 }; // Scale(2,2) + Translate(10,20)
        var m2 = new PdfMatrix { A = 1, B = 0, C = 0, D = 1, E = 5, F = 10 };  // Translate(5,10)
        _output.WriteLine("Test: Multiply two complex matrices");

        // Act
        var result = m1.Multiply(m2);

        // Assert
        // Matrix multiplication: result = m1 * m2
        // A = m1.A * m2.A + m1.B * m2.C = 2*1 + 0*0 = 2
        // D = m1.C * m2.B + m1.D * m2.D = 0*0 + 2*1 = 2
        // E = m1.E * m2.A + m1.F * m2.C + m2.E = 10*1 + 20*0 + 5 = 15
        // F = m1.E * m2.B + m1.F * m2.D + m2.F = 10*0 + 20*1 + 10 = 30
        
        result.A.Should().Be(2);
        result.D.Should().Be(2);
        result.E.Should().Be(15);
        result.F.Should().Be(30);
        
        _output.WriteLine($"Result matrix: [{result.A} {result.B} {result.C} {result.D} {result.E} {result.F}]");
    }

    [Fact]
    public void Multiply_OrderMatters_ShouldProduceDifferentResults()
    {
        // Arrange
        var scale = PdfMatrix.CreateScale(2, 2);
        var translate = PdfMatrix.CreateTranslation(10, 20);
        _output.WriteLine("Test: Verify matrix multiply order matters");

        // Act
        var scaleThenTranslate = scale.Multiply(translate);
        var translateThenScale = translate.Multiply(scale);

        // Assert
        scaleThenTranslate.E.Should().NotBe(translateThenScale.E);
        translateThenScale.E.Should().Be(20, "translation applied after scaling doubles X offset");
        translateThenScale.F.Should().Be(40, "translation applied after scaling doubles Y offset");
        _output.WriteLine($"Scale→Translate translation: ({scaleThenTranslate.E},{scaleThenTranslate.F}), Translate→Scale: ({translateThenScale.E},{translateThenScale.F})");
    }

    [Fact]
    public void Transform_WithRotationMatrix_ShouldRotatePoint()
    {
        // Arrange
        var angle = Math.PI / 2; // 90 degrees
        var rotation = new PdfMatrix
        {
            A = Math.Cos(angle),
            B = Math.Sin(angle),
            C = -Math.Sin(angle),
            D = Math.Cos(angle),
            E = 0,
            F = 0
        };
        _output.WriteLine("Test: Rotate point (1,0) by 90 degrees");

        // Act
        var (x, y) = rotation.Transform(1, 0);

        // Assert
        x.Should().BeApproximately(0, 1e-6);
        y.Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    public void CreateScale_WithZero_ShouldCollapseAxis()
    {
        // Arrange
        var matrix = PdfMatrix.CreateScale(0, 2);

        // Act
        var (x, y) = matrix.Transform(5, 5);

        // Assert
        x.Should().Be(0, "zero scale on X collapses coordinate");
        y.Should().Be(10, "Y should still scale by 2");
    }
}
