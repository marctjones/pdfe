using FluentAssertions;
using PdfEditor.Redaction.GlyphLevel;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Tests for ContentStreamValidator - validates PDF content stream structure.
/// Issue #126: Add validation of reconstructed content streams.
/// </summary>
public class ContentStreamValidatorTests
{
    private readonly ContentStreamValidator _validator = new();

    #region BT/ET Balance Tests

    [Fact]
    public void Validate_BalancedBtEt_ReturnsValid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "Tf", Operands = new object[] { "/F1", 12.0 }, BoundingBox = new PdfRectangle() },
            new TextOperation { Operator = "Tj", Operands = new object[] { "Hello" }, Text = "Hello", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingEt_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "Tf", Operands = new object[] { "/F1", 12.0 }, BoundingBox = new PdfRectangle() },
            new TextOperation { Operator = "Tj", Operands = new object[] { "Hello" }, Text = "Hello", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle() }
            // Missing ET
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Unbalanced"));
    }

    [Fact]
    public void Validate_ExtraEt_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle(), StreamPosition = 10 }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ET without matching BT"));
    }

    [Fact]
    public void Validate_NestedBt_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle(), StreamPosition = 5 },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Nested BT"));
    }

    #endregion

    #region Graphics State Tests

    [Fact]
    public void Validate_BalancedQQ_ReturnsValid()
    {
        var ops = new List<PdfOperation>
        {
            new StateOperation { Operator = "q", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new StateOperation { Operator = "Q", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnbalancedQ_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new StateOperation { Operator = "q", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new StateOperation { Operator = "q", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new StateOperation { Operator = "Q", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
            // Missing second Q
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("q operators without matching Q"));
    }

    [Fact]
    public void Validate_ExtraRestore_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new StateOperation { Operator = "Q", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle(), StreamPosition = 0 }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Q (restore) without matching q"));
    }

    #endregion

    #region Font Before Text Tests

    [Fact]
    public void Validate_TfBeforeTj_ReturnsValid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "Tf", Operands = new object[] { "/F1", 12.0 }, BoundingBox = new PdfRectangle() },
            new TextOperation { Operator = "Tj", Operands = new object[] { "text" }, Text = "text", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_TjWithoutTf_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            // Missing Tf
            new TextOperation { Operator = "Tj", Operands = new object[] { "text" }, Text = "text", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle(), StreamPosition = 5 },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Text-showing operator") && e.Contains("without preceding Tf"));
    }

    [Fact]
    public void Validate_TJWithoutTf_ReturnsInvalid()
    {
        var ops = new List<PdfOperation>
        {
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextOperation { Operator = "TJ", Operands = new object[] { new object[] { "text" } }, Text = "text", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle(), StreamPosition = 3 },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TJ") && e.Contains("without preceding Tf"));
    }

    #endregion

    #region Multiple Text Blocks

    [Fact]
    public void Validate_MultipleBalancedBlocks_ReturnsValid()
    {
        var ops = new List<PdfOperation>
        {
            // Block 1
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "Tf", Operands = new object[] { "/F1", 12.0 }, BoundingBox = new PdfRectangle() },
            new TextOperation { Operator = "Tj", Operands = new object[] { "First" }, Text = "First", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            // Block 2
            new TextStateOperation { Operator = "BT", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "Tf", Operands = new object[] { "/F2", 10.0 }, BoundingBox = new PdfRectangle() },
            new TextOperation { Operator = "Tj", Operands = new object[] { "Second" }, Text = "Second", Glyphs = Array.Empty<GlyphPosition>(), BoundingBox = new PdfRectangle() },
            new TextStateOperation { Operator = "ET", Operands = Array.Empty<object>(), BoundingBox = new PdfRectangle() }
        };

        var result = _validator.Validate(ops);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Empty Operations

    [Fact]
    public void Validate_EmptyOperations_ReturnsValid()
    {
        var result = _validator.Validate(new List<PdfOperation>());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion
}
