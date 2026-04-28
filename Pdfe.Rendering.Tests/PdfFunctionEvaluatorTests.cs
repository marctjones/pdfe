using FluentAssertions;
using Pdfe.Core.Primitives;
using Xunit;

namespace Pdfe.Rendering.Tests;

public class PdfFunctionEvaluatorTests
{
    [Fact]
    public void Evaluate_Null_ReturnsNull()
    {
        var result = PdfFunctionEvaluator.Evaluate(null, 0.5);
        result.Should().BeNull();
    }

    [Fact]
    public void Evaluate_UnknownType_ReturnsNull()
    {
        var func = new PdfDictionary { ["FunctionType"] = new PdfInteger(99) };
        var result = PdfFunctionEvaluator.Evaluate(func, 0.5);
        result.Should().BeNull();
    }

    [Fact]
    public void Evaluate_Type2_AtZero_ReturnsValue()
    {
        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(0) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(1) }
        };

        var result = PdfFunctionEvaluator.Evaluate(func, 0.0);
        result.Should().NotBeNull();
        result!.Length.Should().Be(1);
        result[0].Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void Evaluate_Type2_AtOne_ReturnsValue()
    {
        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(0) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(1) }
        };

        var result = PdfFunctionEvaluator.Evaluate(func, 1.0);
        result.Should().NotBeNull();
        result![0].Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void Evaluate_Type2_AtHalf_InterpolatesCorrectly()
    {
        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(10) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(20) }
        };

        var result = PdfFunctionEvaluator.Evaluate(func, 0.5);
        result.Should().NotBeNull();
        result![0].Should().BeApproximately(15.0, 0.001);
    }

    [Fact]
    public void Evaluate_Type2_MultiComponent_ReturnsArray()
    {
        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(1), (PdfObject)new PdfInteger(0), (PdfObject)new PdfInteger(0) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(0), (PdfObject)new PdfInteger(0), (PdfObject)new PdfInteger(1) }
        };

        var result = PdfFunctionEvaluator.Evaluate(func, 0.5);
        result.Should().NotBeNull();
        result!.Length.Should().Be(3);
        result[0].Should().BeApproximately(0.5, 0.001);
        result[1].Should().BeApproximately(0.0, 0.001);
        result[2].Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void Evaluate_Type2_WithExponent_AppliesPowerLaw()
    {
        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(2),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(0) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(1) }
        };

        var result = PdfFunctionEvaluator.Evaluate(func, 0.5);
        result.Should().NotBeNull();
        result![0].Should().BeApproximately(0.25, 0.001);
    }

    [Fact]
    public void Evaluate_Array_OfFunctions_EvaluatesAll()
    {
        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(0) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(1) }
        };

        var funcArray = new PdfArray { (PdfObject)func, (PdfObject)func, (PdfObject)func };
        var result = PdfFunctionEvaluator.Evaluate(funcArray, 0.5);

        result.Should().NotBeNull();
        result!.Length.Should().Be(3);
        result[0].Should().BeApproximately(0.5, 0.001);
        result[1].Should().BeApproximately(0.5, 0.001);
        result[2].Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void Evaluate_Type3_Stitching_SelectsCorrectFunction()
    {
        var func1 = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfInteger(0) },
            ["C1"] = new PdfArray { (PdfObject)new PdfReal(0.5) }
        };

        var func2 = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(2),
            ["N"] = new PdfInteger(1),
            ["C0"] = new PdfArray { (PdfObject)new PdfReal(0.5) },
            ["C1"] = new PdfArray { (PdfObject)new PdfInteger(1) }
        };

        var func = new PdfDictionary
        {
            ["FunctionType"] = new PdfInteger(3),
            ["Functions"] = new PdfArray { (PdfObject)func1, (PdfObject)func2 },
            ["Bounds"] = new PdfArray { (PdfObject)new PdfReal(0.5) },
            ["Encode"] = new PdfArray
            {
                (PdfObject)new PdfInteger(0), (PdfObject)new PdfInteger(1),
                (PdfObject)new PdfInteger(0), (PdfObject)new PdfInteger(1)
            }
        };

        var result1 = PdfFunctionEvaluator.Evaluate(func, 0.25);
        result1.Should().NotBeNull();
        result1![0].Should().BeLessThan(0.5);

        var result2 = PdfFunctionEvaluator.Evaluate(func, 0.75);
        result2.Should().NotBeNull();
        result2![0].Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Evaluate_Type0_Sampled_DoesNotCrash()
    {
        // Type 0 sampled function requires binary data which is complex to set up
        // Just verify it handles null gracefully
        var func = new PdfDictionary { ["FunctionType"] = new PdfInteger(0) };
        var result = PdfFunctionEvaluator.Evaluate(func, 0.5);
        // Should return null because Size is missing
        result.Should().BeNull();
    }
}
