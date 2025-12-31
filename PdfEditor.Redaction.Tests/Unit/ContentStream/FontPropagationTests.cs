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
                FontSize = 10,
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

        // Assert - should contain Tf injection
        contentString.Should().Contain("/F0 10 Tf", "Tf should be injected before Tj when BT block has no Tf");
        contentString.Should().Contain("(Separate shape) Tj", "Tj should be preserved");
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
