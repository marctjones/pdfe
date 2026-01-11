using FluentAssertions;
using Pdfe.Core.Content;
using Pdfe.Core.Document;
using Pdfe.Core.Operations;
using Pdfe.Core.Primitives;
using Xunit;

namespace Pdfe.Core.Tests.Operations;

public class PdfRedactionTests
{
    [Fact]
    public void RedactionResult_WasRedacted_FalseWhenNoRedaction()
    {
        var result = new RedactionResult();

        result.WasRedacted.Should().BeFalse();
    }

    [Fact]
    public void RedactionResult_WasRedacted_TrueWhenOperatorsRemoved()
    {
        var result = new RedactionResult
        {
            OperatorsRemoved = 5
        };

        result.WasRedacted.Should().BeTrue();
    }

    [Fact]
    public void RedactionResult_ToString_DescribesRedaction()
    {
        var result = new RedactionResult
        {
            AreasRedacted = 2,
            TextOccurrencesRedacted = 3,
            OperatorsRemoved = 10
        };

        var str = result.ToString();

        str.Should().Contain("2 areas");
        str.Should().Contain("3 text occurrences");
        str.Should().Contain("10 operators removed");
    }

    [Fact]
    public void ContentOperator_Categories_AreCorrect()
    {
        ContentOperator.SaveState().Category.Should().Be(OperatorCategory.GraphicsState);
        ContentOperator.RestoreState().Category.Should().Be(OperatorCategory.GraphicsState);
        ContentOperator.Rectangle(0, 0, 10, 10).Category.Should().Be(OperatorCategory.PathConstruction);
        ContentOperator.Fill().Category.Should().Be(OperatorCategory.PathPainting);
        ContentOperator.Stroke().Category.Should().Be(OperatorCategory.PathPainting);
        ContentOperator.BeginText().Category.Should().Be(OperatorCategory.TextObject);
        ContentOperator.EndText().Category.Should().Be(OperatorCategory.TextObject);
        ContentOperator.ShowText("test").Category.Should().Be(OperatorCategory.TextShowing);
        ContentOperator.SetFillRgb(0, 0, 0).Category.Should().Be(OperatorCategory.Color);
    }
}

public class ContentStreamParserWriterRoundtripTests
{
    [Fact]
    public void ParseAndWrite_SimpleContent_PreservesStructure()
    {
        // Simple content: save state, draw rectangle, fill, restore
        var originalBytes = System.Text.Encoding.Latin1.GetBytes("q\n100 200 50 30 re\nf\nQ\n");

        var parser = new ContentStreamParser(originalBytes);
        var stream = parser.Parse();

        // Should have 4 operators
        stream.Count.Should().Be(4);
        stream[0].Name.Should().Be("q");
        stream[1].Name.Should().Be("re");
        stream[2].Name.Should().Be("f");
        stream[3].Name.Should().Be("Q");

        // Write back
        var writer = new ContentStreamWriter();
        var outputBytes = writer.Write(stream);
        var output = System.Text.Encoding.Latin1.GetString(outputBytes);

        // Should contain same operators
        output.Should().Contain("q");
        output.Should().Contain("re");
        output.Should().Contain("f");
        output.Should().Contain("Q");
    }

    [Fact]
    public void Parse_TextOperators_ExtractsTextContent()
    {
        var content = "BT\n/F1 12 Tf\n100 700 Td\n(Hello World) Tj\nET\n";
        var bytes = System.Text.Encoding.Latin1.GetBytes(content);

        var parser = new ContentStreamParser(bytes);
        var stream = parser.Parse();

        // Find the Tj operator
        var tjOp = stream.Operators.FirstOrDefault(op => op.Name == "Tj");
        tjOp.Should().NotBeNull();
        tjOp!.TextContent.Should().Be("Hello World");
    }

    [Fact]
    public void Parse_ColorOperators_RecognizesCategory()
    {
        var content = "0.5 g\n0.1 0.2 0.3 rg\n1 0 0 RG\n";
        var bytes = System.Text.Encoding.Latin1.GetBytes(content);

        var parser = new ContentStreamParser(bytes);
        var stream = parser.Parse();

        stream.Operators.All(op => op.Category == OperatorCategory.Color).Should().BeTrue();
    }

    [Fact]
    public void Filter_AfterParse_RemovesTargetedOperators()
    {
        var content = "q\nBT\n/F1 12 Tf\n(Secret) Tj\nET\nQ\n";
        var bytes = System.Text.Encoding.Latin1.GetBytes(content);

        var parser = new ContentStreamParser(bytes);
        var stream = parser.Parse();

        // Filter out text showing operators
        var filtered = stream.RemoveCategory(OperatorCategory.TextShowing);

        // Should not have Tj anymore
        filtered.Operators.Any(op => op.Name == "Tj").Should().BeFalse();

        // But should still have other operators
        filtered.Operators.Any(op => op.Name == "q").Should().BeTrue();
        filtered.Operators.Any(op => op.Name == "BT").Should().BeTrue();
        filtered.Operators.Any(op => op.Name == "ET").Should().BeTrue();
    }

    [Fact]
    public void Writer_EscapesSpecialCharacters()
    {
        var op = ContentOperator.ShowText("Hello (World) with \\ backslash");
        var stream = new ContentStream(new[] { op });

        var writer = new ContentStreamWriter();
        var bytes = writer.Write(stream);
        var output = System.Text.Encoding.Latin1.GetString(bytes);

        output.Should().Contain("\\(");  // Escaped parenthesis
        output.Should().Contain("\\)");  // Escaped parenthesis
        output.Should().Contain("\\\\"); // Escaped backslash
    }
}
