using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Parsing;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit.ContentStream;

/// <summary>
/// Tests to verify that font information is correctly propagated from Tf to Tj operators.
/// Issue #167: Font reference corruption after glyph-level redaction.
/// </summary>
public class FontPropagationTests
{
    [Fact]
    public void Parse_TfThenTj_FontNameIsPropagated()
    {
        // Arrange - Simple content stream with BT, Tf, Td, Tj, ET
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "/F1 12 Tf\n" +
            "100 700 Td\n" +
            "(Hello World) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act
        var operations = parser.Parse(contentBytes, 792);

        // Assert
        var textOp = operations.OfType<TextOperation>().FirstOrDefault();

        textOp.Should().NotBeNull("Should find a TextOperation");
        textOp!.FontName.Should().Be("/F1", "FontName should be captured from preceding Tf operator");
        textOp.FontSize.Should().Be(12, "FontSize should be captured from preceding Tf operator");
    }

    [Fact]
    public void Parse_MultipleTfOperators_EachTextOpGetsCorrectFont()
    {
        // Arrange - Content stream with font changes
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "/F1 12 Tf\n" +
            "100 700 Td\n" +
            "(First text) Tj\n" +
            "/F2 14 Tf\n" +
            "0 -20 Td\n" +
            "(Second text) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act
        var operations = parser.Parse(contentBytes, 792);

        // Assert
        var textOps = operations.OfType<TextOperation>().ToList();

        textOps.Should().HaveCount(2);
        textOps[0].FontName.Should().Be("/F1");
        textOps[0].FontSize.Should().Be(12);
        textOps[1].FontName.Should().Be("/F2");
        textOps[1].FontSize.Should().Be(14);
    }

    [Fact]
    public void Parse_NoTfBeforeTj_FontNameIsNull()
    {
        // Arrange - Content stream with no Tf before Tj (shouldn't happen in valid PDF)
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "100 700 Td\n" +
            "(Orphan text) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act
        var operations = parser.Parse(contentBytes, 792);

        // Assert
        var textOp = operations.OfType<TextOperation>().FirstOrDefault();

        textOp.Should().NotBeNull("Should find a TextOperation");
        textOp!.FontName.Should().BeNull("FontName should be null when no Tf precedes Tj");
    }

    [Fact]
    public void Build_SecondBtBlockWithoutTf_InjectsTfFromTextOperation()
    {
        // Arrange - Issue #167: Simulate what happens after glyph-level redaction
        // When the first BT block is removed, the second block loses its font state
        // The ContentStreamBuilder.Build() should inject Tf before Tj
        //
        // CRITICAL: TextOperation.FontSize contains the EFFECTIVE size (Tf * Tm scale).
        // When injecting Tf, we must use the RAW Tf size, not the effective size.
        // Since there's no prior Tf operator in this test, the raw size defaults to 1.0.

        var builder = new PdfEditor.Redaction.ContentStream.Building.ContentStreamBuilder();

        // Create operations like what would remain after redaction:
        // - First BT/ET block with Tf was removed during redaction
        // - Second BT/ET block has Tj but no Tf (font info is in TextOperation)
        var operations = new List<PdfOperation>
        {
            new TextStateOperation
            {
                Operator = "BT",
                Operands = new List<object>(),
                StreamPosition = 100
            },
            new TextStateOperation
            {
                Operator = "Td",
                Operands = new List<object> { 110.0, 280.0 },
                StreamPosition = 110
            },
            new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { "Separate shape" },
                StreamPosition = 120,
                Text = "Separate shape",
                FontName = "/F0",
                FontSize = 10,  // This is EFFECTIVE size - should NOT be used for Tf injection
                Glyphs = new List<GlyphPosition>()
            },
            new TextStateOperation
            {
                Operator = "ET",
                Operands = new List<object>(),
                StreamPosition = 130
            }
        };

        // Act
        var result = builder.Build(operations);
        var contentString = System.Text.Encoding.ASCII.GetString(result);

        // Assert - should contain Tf injection with RAW size (1.0 default), NOT effective size
        // The effective size (10) is already baked into the Tm matrix or other state
        contentString.Should().Contain("/F0 1 Tf", "Tf should be injected with raw size (1.0 default), not effective size");
        contentString.Should().Contain("(Separate shape) Tj", "Tj should be preserved");
    }

    [Fact]
    public void Build_WithPriorTfOperator_UsesRawTfSizeNotEffectiveSize()
    {
        // Arrange - Issue #186: Font size explosion bug
        // When a prior Tf operator exists, we should use its RAW size (typically 1)
        // NOT the effective size from TextOperation.FontSize (which includes Tm scaling)
        //
        // Real-world scenario: Birth certificate PDF has "/TT0 1 Tf" with size in Tm matrix.
        // Without this fix, we were injecting "/TT0 10.02 Tf" which caused ~100pt text!

        var builder = new PdfEditor.Redaction.ContentStream.Building.ContentStreamBuilder();

        var operations = new List<PdfOperation>
        {
            // First BT block - has the original Tf with size 1
            new TextStateOperation
            {
                Operator = "BT",
                Operands = new List<object>(),
                StreamPosition = 10
            },
            new TextStateOperation
            {
                Operator = "Tf",
                Operands = new List<object> { "/TT0", 1.0 },  // RAW Tf size is 1
                StreamPosition = 20
            },
            new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { "First text" },
                StreamPosition = 30,
                Text = "First text",
                FontName = "/TT0",
                FontSize = 10.02,  // EFFECTIVE size (1 * 10.02 from Tm) - should be ignored for Tf
                Glyphs = new List<GlyphPosition>()
            },
            new TextStateOperation
            {
                Operator = "ET",
                Operands = new List<object>(),
                StreamPosition = 40
            },
            // Second BT block - no Tf, needs injection
            new TextStateOperation
            {
                Operator = "BT",
                Operands = new List<object>(),
                StreamPosition = 50
            },
            new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { "Second text" },
                StreamPosition = 60,
                Text = "Second text",
                FontName = "/TT0",
                FontSize = 10.02,  // EFFECTIVE size - should be ignored for Tf
                Glyphs = new List<GlyphPosition>()
            },
            new TextStateOperation
            {
                Operator = "ET",
                Operands = new List<object>(),
                StreamPosition = 70
            }
        };

        // Act
        var result = builder.Build(operations);
        var contentString = System.Text.Encoding.ASCII.GetString(result);

        // Assert - All Tf operators should use size 1 (the raw Tf size), NOT 10.02
        var tfMatches = System.Text.RegularExpressions.Regex.Matches(contentString, @"/TT0 ([\d.]+) Tf");
        tfMatches.Count.Should().BeGreaterOrEqualTo(2, "Should have at least 2 Tf operators");

        foreach (System.Text.RegularExpressions.Match match in tfMatches)
        {
            var size = double.Parse(match.Groups[1].Value);
            size.Should().BeApproximately(1.0, 0.01,
                "All Tf operators should use raw size (1), not effective size (10.02)");
        }

        // Should NOT contain the wrong size
        contentString.Should().NotContain("/TT0 10.02 Tf",
            "Should NOT inject effective size - this causes font explosion bug!");
        contentString.Should().NotContain("/TT0 10 Tf",
            "Should NOT inject effective size - this causes font explosion bug!");
    }

    /// <summary>
    /// Issue #XXX: Text matrix scaling must be preserved during redaction.
    /// Many PDFs use Tf with size 1 and encode the actual font size in the Tm matrix.
    /// For example: "/F1 1 Tf" + "9 0 0 9 x y Tm" → effective size = 9pt.
    ///
    /// When we reconstruct operations after redaction, we must preserve this scaling.
    /// </summary>
    [Fact]
    public void Parse_TmWithFontScaling_EffectiveFontSizeIsCalculated()
    {
        // Arrange - Content stream using Tm matrix for font scaling (common pattern)
        // This is how the birth certificate PDF works: Tf 1, Tm has the actual size
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "/TT0 1 Tf\n" +               // Font size 1 in Tf
            "9 0 0 9 50.4 715.92 Tm\n" +  // But 9x scaling in Tm matrix
            "(Hello World) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act
        var operations = parser.Parse(contentBytes, 792);

        // Assert
        var textOp = operations.OfType<TextOperation>().FirstOrDefault();

        textOp.Should().NotBeNull("Should find a TextOperation");
        textOp!.FontName.Should().Be("/TT0");

        // The FontSize should be the EFFECTIVE size (Tf size * Tm scale)
        // 1 * 9 = 9pt
        textOp.FontSize.Should().BeApproximately(9.0, 0.1,
            "FontSize should be effective size (Tf * Tm scale), not just Tf value");
    }

    [Fact]
    public void Parse_TmWithVariousFontScales_AllCapturedCorrectly()
    {
        // Arrange - Content stream with different font sizes via Tm
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "/F1 1 Tf\n" +
            "10.02 0 0 10.02 50 750 Tm\n" +  // 10.02pt
            "(Title) Tj\n" +
            "ET\n" +
            "BT\n" +
            "/F1 1 Tf\n" +
            "4.02 0 0 4.02 50 700 Tm\n" +    // 4.02pt (small text)
            "(Footnote) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act
        var operations = parser.Parse(contentBytes, 792);

        // Assert
        var textOps = operations.OfType<TextOperation>().ToList();

        textOps.Should().HaveCount(2);
        textOps[0].FontSize.Should().BeApproximately(10.02, 0.01, "First text should be ~10.02pt");
        textOps[1].FontSize.Should().BeApproximately(4.02, 0.01, "Second text should be ~4.02pt");
    }

    [Fact]
    public void Parse_TfWithActualSize_FontSizePreserved()
    {
        // Arrange - Traditional pattern: Tf has the actual size, Tm is identity or for positioning only
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "BT\n" +
            "/F1 12 Tf\n" +             // Font size 12 in Tf
            "1 0 0 1 50 700 Tm\n" +      // Identity Tm (just positioning)
            "(Normal text) Tj\n" +
            "ET\n");

        var parser = new ContentStreamParser();

        // Act
        var operations = parser.Parse(contentBytes, 792);

        // Assert
        var textOp = operations.OfType<TextOperation>().FirstOrDefault();

        textOp.Should().NotBeNull();
        textOp!.FontSize.Should().BeApproximately(12.0, 0.1,
            "FontSize should be 12 (Tf=12 * Tm_scale=1)");
    }
}
