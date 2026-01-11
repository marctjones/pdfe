using FluentAssertions;
using Pdfe.Core.Content;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;
using Xunit;

namespace Pdfe.Core.Tests.Content;

public class ContentStreamTests
{
    [Fact]
    public void Empty_ContentStream_HasZeroOperators()
    {
        var stream = new ContentStream();
        stream.Count.Should().Be(0);
        stream.Operators.Should().BeEmpty();
    }

    [Fact]
    public void Append_SingleOperator_IncreasesCount()
    {
        var stream = new ContentStream();
        var op = ContentOperator.SaveState();

        var newStream = stream.Append(op);

        newStream.Count.Should().Be(1);
        newStream[0].Name.Should().Be("q");
    }

    [Fact]
    public void Append_DoesNotModifyOriginal()
    {
        var stream = new ContentStream();
        var op = ContentOperator.SaveState();

        stream.Append(op);

        stream.Count.Should().Be(0, "original should be immutable");
    }

    [Fact]
    public void Filter_RemovesMatchingOperators()
    {
        var ops = new List<ContentOperator>
        {
            ContentOperator.SaveState(),
            ContentOperator.SetFillRgb(1, 0, 0),
            ContentOperator.RestoreState()
        };
        var stream = new ContentStream(ops);

        var filtered = stream.Filter(op => op.Name != "rg");

        filtered.Count.Should().Be(2);
        filtered.Operators.All(op => op.Name != "rg").Should().BeTrue();
    }

    [Fact]
    public void RemoveIntersecting_RemovesOperatorsInArea()
    {
        var ops = new List<ContentOperator>();

        // Create an operator with a known bounding box
        var textOp = new ContentOperator("Tj", new[] { new PdfString("Test") });
        textOp.BoundingBox = new PdfRectangle(100, 100, 200, 120);
        ops.Add(textOp);

        // Create an operator outside the area
        var textOp2 = new ContentOperator("Tj", new[] { new PdfString("Other") });
        textOp2.BoundingBox = new PdfRectangle(300, 100, 400, 120);
        ops.Add(textOp2);

        var stream = new ContentStream(ops);
        var redactionArea = new PdfRectangle(50, 50, 250, 150);

        var redacted = stream.RemoveIntersecting(redactionArea);

        redacted.Count.Should().Be(1);
        redacted[0].TextContent.Should().BeNull(); // textOp2 doesn't have TextContent set
    }

    [Fact]
    public void RemoveCategory_RemovesOperatorsOfSpecificCategory()
    {
        var ops = new List<ContentOperator>
        {
            ContentOperator.BeginText(),
            ContentOperator.ShowText("Hello"),
            ContentOperator.EndText(),
            ContentOperator.SaveState(),
            ContentOperator.Rectangle(0, 0, 100, 100),
            ContentOperator.Fill(),
            ContentOperator.RestoreState()
        };
        var stream = new ContentStream(ops);

        var withoutText = stream.RemoveCategory(OperatorCategory.TextShowing);

        // Should remove only Tj (TextShowing), keep BT/ET (TextObject)
        withoutText.Operators.Any(op => op.Name == "Tj").Should().BeFalse();
        withoutText.Operators.Any(op => op.Name == "BT").Should().BeTrue();
    }

    [Fact]
    public void Concat_CombinesTwoStreams()
    {
        var stream1 = new ContentStream(new[] { ContentOperator.SaveState() });
        var stream2 = new ContentStream(new[] { ContentOperator.RestoreState() });

        var combined = stream1.Concat(stream2);

        combined.Count.Should().Be(2);
        combined[0].Name.Should().Be("q");
        combined[1].Name.Should().Be("Q");
    }

    [Fact]
    public void Redact_RemovesContentAndAddsMarker()
    {
        var ops = new List<ContentOperator>();
        var textOp = new ContentOperator("Tj", new[] { new PdfString("Secret") });
        textOp.BoundingBox = new PdfRectangle(100, 100, 200, 120);
        ops.Add(textOp);

        var stream = new ContentStream(ops);
        var redactionArea = new PdfRectangle(50, 50, 250, 150);

        var redacted = stream.Redact(redactionArea, (0, 0, 0)); // black marker

        // Original text should be removed, marker added (q, rg, re, f, Q)
        redacted.Operators.Any(op => op.Name == "Tj").Should().BeFalse();
        redacted.Operators.Any(op => op.Name == "re").Should().BeTrue();
        redacted.Operators.Any(op => op.Name == "f").Should().BeTrue();
    }
}

public class ContentOperatorTests
{
    [Fact]
    public void Factory_SaveState_CreatesCorrectOperator()
    {
        var op = ContentOperator.SaveState();

        op.Name.Should().Be("q");
        op.Operands.Should().BeEmpty();
        op.Category.Should().Be(OperatorCategory.GraphicsState);
    }

    [Fact]
    public void Factory_Rectangle_CreatesCorrectOperator()
    {
        var op = ContentOperator.Rectangle(10, 20, 100, 50);

        op.Name.Should().Be("re");
        op.Operands.Should().HaveCount(4);
        ((PdfReal)op.Operands[0]).Value.Should().Be(10);
        ((PdfReal)op.Operands[1]).Value.Should().Be(20);
        ((PdfReal)op.Operands[2]).Value.Should().Be(100);
        ((PdfReal)op.Operands[3]).Value.Should().Be(50);
    }

    [Fact]
    public void Factory_SetFillRgb_CreatesCorrectOperator()
    {
        var op = ContentOperator.SetFillRgb(0.5, 0.25, 0.75);

        op.Name.Should().Be("rg");
        op.Operands.Should().HaveCount(3);
        op.Category.Should().Be(OperatorCategory.Color);
    }

    [Fact]
    public void IntersectsWith_ReturnsTrueForOverlapping()
    {
        var op = new ContentOperator("Tj")
        {
            BoundingBox = new PdfRectangle(100, 100, 200, 150)
        };

        var overlapping = new PdfRectangle(150, 120, 250, 200);

        op.IntersectsWith(overlapping).Should().BeTrue();
    }

    [Fact]
    public void IntersectsWith_ReturnsFalseForNonOverlapping()
    {
        var op = new ContentOperator("Tj")
        {
            BoundingBox = new PdfRectangle(100, 100, 200, 150)
        };

        var nonOverlapping = new PdfRectangle(300, 100, 400, 150);

        op.IntersectsWith(nonOverlapping).Should().BeFalse();
    }

    [Fact]
    public void IntersectsWith_ReturnsFalseWhenNoBoundingBox()
    {
        var op = ContentOperator.SaveState(); // No bounding box

        var area = new PdfRectangle(0, 0, 1000, 1000);

        op.IntersectsWith(area).Should().BeFalse();
    }

    [Fact]
    public void IsContainedIn_ReturnsTrueWhenFullyInside()
    {
        var op = new ContentOperator("Tj")
        {
            BoundingBox = new PdfRectangle(150, 150, 180, 170)
        };

        var container = new PdfRectangle(100, 100, 200, 200);

        op.IsContainedIn(container).Should().BeTrue();
    }

    [Fact]
    public void IsContainedIn_ReturnsFalseWhenPartiallyOutside()
    {
        var op = new ContentOperator("Tj")
        {
            BoundingBox = new PdfRectangle(150, 150, 250, 170)
        };

        var container = new PdfRectangle(100, 100, 200, 200);

        op.IsContainedIn(container).Should().BeFalse();
    }
}
