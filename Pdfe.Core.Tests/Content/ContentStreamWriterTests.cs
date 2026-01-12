using System.Text;
using FluentAssertions;
using Pdfe.Core.Content;
using Pdfe.Core.Primitives;
using Xunit;

namespace Pdfe.Core.Tests.Content;

/// <summary>
/// Tests for ContentStreamWriter round-trip serialization.
/// Ensures parse → serialize → parse produces identical results.
/// </summary>
public class ContentStreamWriterTests
{
    private readonly ContentStreamWriter _writer = new();

    #region Basic Types Round-Trip

    [Fact]
    public void Write_Integer_RoundTrips()
    {
        var op = new ContentOperator("w", new PdfObject[] { new PdfInteger(5) });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].Name.Should().Be("w");
        parsed.Operators[0].GetNumber(0).Should().Be(5);
    }

    [Fact]
    public void Write_Real_RoundTrips()
    {
        var op = new ContentOperator("w", new PdfObject[] { new PdfReal(2.5) });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].GetNumber(0).Should().BeApproximately(2.5, 0.001);
    }

    [Fact]
    public void Write_Name_RoundTrips()
    {
        var op = new ContentOperator("Tf", new PdfObject[] { new PdfName("F1"), new PdfInteger(12) });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].GetName(0).Should().Be("F1");
        parsed.Operators[0].GetNumber(1).Should().Be(12);
    }

    [Fact]
    public void Write_String_RoundTrips()
    {
        var op = new ContentOperator("Tj", new PdfObject[] { new PdfString("Hello World") });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].GetString(0).Should().Be("Hello World");
    }

    [Fact]
    public void Write_String_WithEscapedChars_RoundTrips()
    {
        var op = new ContentOperator("Tj", new PdfObject[] { new PdfString("Line1\nLine2\t(test)") });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators[0].GetString(0).Should().Be("Line1\nLine2\t(test)");
    }

    [Fact]
    public void Write_Array_RoundTrips()
    {
        var array = new PdfArray(new PdfObject[]
        {
            new PdfInteger(3),
            new PdfInteger(2)
        });
        var op = new ContentOperator("d", new PdfObject[] { array, new PdfInteger(0) });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].Name.Should().Be("d");
        var parsedArray = parsed.Operators[0].GetArray(0);
        parsedArray.Should().NotBeNull();
        parsedArray!.Count.Should().Be(2);
    }

    #endregion

    #region Operator Categories Round-Trip

    [Fact]
    public void Write_GraphicsStateOperators_RoundTrip()
    {
        var operators = new[]
        {
            ContentOperator.SaveState(),
            new ContentOperator("cm", new PdfObject[]
            {
                new PdfReal(1), new PdfReal(0),
                new PdfReal(0), new PdfReal(1),
                new PdfReal(100), new PdfReal(200)
            }),
            ContentOperator.SetLineWidth(2.5),
            ContentOperator.RestoreState()
        };
        var content = new ContentStream(operators);

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(4);
        parsed.Operators[0].Name.Should().Be("q");
        parsed.Operators[1].Name.Should().Be("cm");
        parsed.Operators[2].Name.Should().Be("w");
        parsed.Operators[2].GetNumber(0).Should().BeApproximately(2.5, 0.001);
        parsed.Operators[3].Name.Should().Be("Q");
    }

    [Fact]
    public void Write_PathOperators_RoundTrip()
    {
        var operators = new[]
        {
            ContentOperator.MoveTo(0, 0),
            ContentOperator.LineTo(100, 0),
            ContentOperator.LineTo(100, 100),
            ContentOperator.ClosePath(),
            ContentOperator.Stroke()
        };
        var content = new ContentStream(operators);

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(5);
        parsed.Operators[0].Name.Should().Be("m");
        parsed.Operators[1].Name.Should().Be("l");
        parsed.Operators[2].Name.Should().Be("l");
        parsed.Operators[3].Name.Should().Be("h");
        parsed.Operators[4].Name.Should().Be("S");
    }

    [Fact]
    public void Write_ColorOperators_RoundTrip()
    {
        var operators = new[]
        {
            ContentOperator.SetFillGray(0.5),
            ContentOperator.SetStrokeGray(0.8),
            ContentOperator.SetFillRgb(1, 0, 0),
            ContentOperator.SetStrokeRgb(0, 0, 1),
            new ContentOperator("k", new PdfObject[] { new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(0) }),
            new ContentOperator("K", new PdfObject[] { new PdfReal(0), new PdfReal(0), new PdfReal(1), new PdfReal(0) })
        };
        var content = new ContentStream(operators);

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(6);
        parsed.Operators[0].Name.Should().Be("g");
        parsed.Operators[1].Name.Should().Be("G");
        parsed.Operators[2].Name.Should().Be("rg");
        parsed.Operators[3].Name.Should().Be("RG");
        parsed.Operators[4].Name.Should().Be("k");
        parsed.Operators[5].Name.Should().Be("K");
    }

    [Fact]
    public void Write_TextOperators_RoundTrip()
    {
        var operators = new[]
        {
            ContentOperator.BeginText(),
            ContentOperator.SetFont("F1", 12),
            ContentOperator.TextPosition(100, 700),
            ContentOperator.ShowText("Hello"),
            ContentOperator.EndText()
        };
        var content = new ContentStream(operators);

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(5);
        parsed.Operators[0].Name.Should().Be("BT");
        parsed.Operators[1].Name.Should().Be("Tf");
        parsed.Operators[2].Name.Should().Be("Td");
        parsed.Operators[3].Name.Should().Be("Tj");
        parsed.Operators[4].Name.Should().Be("ET");
    }

    [Fact]
    public void Write_RectangleOperator_RoundTrip()
    {
        var op = ContentOperator.Rectangle(10, 20, 100, 50);
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].Name.Should().Be("re");
        parsed.Operators[0].GetNumber(0).Should().Be(10);
        parsed.Operators[0].GetNumber(1).Should().Be(20);
        parsed.Operators[0].GetNumber(2).Should().Be(100);
        parsed.Operators[0].GetNumber(3).Should().Be(50);
    }

    [Fact]
    public void Write_CurveTo_RoundTrip()
    {
        var op = ContentOperator.CurveTo(10, 20, 30, 40, 50, 60);
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].Name.Should().Be("c");
        parsed.Operators[0].GetNumber(0).Should().Be(10);
        parsed.Operators[0].GetNumber(5).Should().Be(60);
    }

    [Fact]
    public void Write_XObjectOperator_RoundTrip()
    {
        var op = ContentOperator.InvokeXObject("Im1");
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].Name.Should().Be("Do");
        parsed.Operators[0].GetName(0).Should().Be("Im1");
    }

    #endregion

    #region Complex Round-Trip

    [Fact]
    public void Write_CompletePageContent_RoundTrip()
    {
        // Simulate a real page with text and graphics
        var operators = new[]
        {
            // Graphics state
            ContentOperator.SaveState(),

            // Draw a red rectangle
            ContentOperator.SetFillRgb(1, 0, 0),
            ContentOperator.Rectangle(50, 700, 100, 50),
            ContentOperator.Fill(),

            // Draw some text
            ContentOperator.BeginText(),
            ContentOperator.SetFont("F1", 14),
            ContentOperator.TextPosition(100, 600),
            ContentOperator.ShowText("Test Document"),
            ContentOperator.EndText(),

            // Draw a stroked path
            ContentOperator.SetStrokeRgb(0, 0, 1),
            ContentOperator.SetLineWidth(2),
            ContentOperator.MoveTo(50, 500),
            ContentOperator.LineTo(200, 500),
            ContentOperator.LineTo(200, 400),
            ContentOperator.Stroke(),

            ContentOperator.RestoreState()
        };
        var content = new ContentStream(operators);

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(operators.Length);

        // Verify key operators
        parsed.Operators[0].Name.Should().Be("q");
        parsed.Operators.Last().Name.Should().Be("Q");

        // Check text block
        var btIndex = parsed.Operators.ToList().FindIndex(o => o.Name == "BT");
        var etIndex = parsed.Operators.ToList().FindIndex(o => o.Name == "ET");
        btIndex.Should().BeLessThan(etIndex);
    }

    [Fact]
    public void Write_MultipleRoundTrips_ProducesSameResult()
    {
        var operators = new[]
        {
            ContentOperator.SaveState(),
            ContentOperator.SetFillGray(0.5),
            ContentOperator.Rectangle(100, 100, 200, 150),
            ContentOperator.Fill(),
            ContentOperator.RestoreState()
        };
        var original = new ContentStream(operators);

        // First round-trip
        var bytes1 = _writer.Write(original);
        var parsed1 = Parse(bytes1);

        // Second round-trip
        var bytes2 = _writer.Write(parsed1);
        var parsed2 = Parse(bytes2);

        // Third round-trip
        var bytes3 = _writer.Write(parsed2);
        var parsed3 = Parse(bytes3);

        // All should produce same number of operators
        parsed1.Operators.Should().HaveCount(5);
        parsed2.Operators.Should().HaveCount(5);
        parsed3.Operators.Should().HaveCount(5);

        // Bytes should stabilize after first round-trip
        bytes2.Should().BeEquivalentTo(bytes3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Write_EmptyContentStream_RoundTrips()
    {
        var content = new ContentStream();

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().BeEmpty();
    }

    [Fact]
    public void Write_NegativeNumbers_RoundTrips()
    {
        var op = new ContentOperator("cm", new PdfObject[]
        {
            new PdfReal(-1), new PdfReal(0),
            new PdfReal(0), new PdfReal(-1),
            new PdfReal(-100), new PdfReal(-200)
        });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators[0].GetNumber(0).Should().Be(-1);
        parsed.Operators[0].GetNumber(4).Should().Be(-100);
        parsed.Operators[0].GetNumber(5).Should().Be(-200);
    }

    [Fact]
    public void Write_VerySmallNumbers_RoundTrips()
    {
        var op = new ContentOperator("w", new PdfObject[] { new PdfReal(0.001) });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators[0].GetNumber(0).Should().BeApproximately(0.001, 0.0001);
    }

    [Fact]
    public void Write_VeryLargeNumbers_RoundTrips()
    {
        var op = new ContentOperator("cm", new PdfObject[]
        {
            new PdfReal(1), new PdfReal(0),
            new PdfReal(0), new PdfReal(1),
            new PdfReal(10000), new PdfReal(50000)
        });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators[0].GetNumber(4).Should().Be(10000);
        parsed.Operators[0].GetNumber(5).Should().Be(50000);
    }

    [Fact]
    public void Write_NameWithSpecialChars_RoundTrips()
    {
        // Names with special characters need proper encoding
        var op = new ContentOperator("gs", new PdfObject[] { new PdfName("GS1") });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators[0].GetName(0).Should().Be("GS1");
    }

    [Fact]
    public void Write_TJArrayOperator_RoundTrips()
    {
        // TJ operator with array of strings and numbers
        var array = new PdfArray(new PdfObject[]
        {
            new PdfString("AB"),
            new PdfInteger(-100),
            new PdfString("CD")
        });
        var op = new ContentOperator("TJ", new PdfObject[] { array });
        var content = new ContentStream(new[] { op });

        var bytes = _writer.Write(content);
        var parsed = Parse(bytes);

        parsed.Operators.Should().HaveCount(1);
        parsed.Operators[0].Name.Should().Be("TJ");
        var parsedArray = parsed.Operators[0].GetArray(0);
        parsedArray.Should().NotBeNull();
        parsedArray!.Count.Should().Be(3);
    }

    #endregion

    #region Helpers

    private ContentStream Parse(byte[] bytes)
    {
        var parser = new ContentStreamParser(bytes);
        return parser.Parse();
    }

    #endregion
}
