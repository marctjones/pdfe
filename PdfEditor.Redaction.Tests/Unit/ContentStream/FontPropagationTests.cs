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
}
