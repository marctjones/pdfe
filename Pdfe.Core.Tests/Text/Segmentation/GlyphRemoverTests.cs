using FluentAssertions;
using Pdfe.Core.Content;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;
using Pdfe.Core.Text;
using Pdfe.Core.Text.Segmentation;
using Xunit;

namespace Pdfe.Core.Tests.Text.Segmentation;

public class GlyphRemoverTests
{
    private readonly GlyphRemover _remover = new();

    private static Letter L(string value, double x, double width = 7, double y = 700, double h = 12)
        => new(value, new PdfRectangle(x, y, x + width, y + h),
               12, "TestFont", x, y, width, (int)value[0]);

    // Build the letters for a left-to-right text line.
    private static IReadOnlyList<Letter> LettersFor(string text, double x0 = 100, double y = 700, double w = 7)
    {
        var list = new List<Letter>();
        for (int i = 0; i < text.Length; i++)
            list.Add(L(text[i].ToString(), x0 + i * w, w, y));
        return list;
    }

    // Build an ops list representing `BT /F1 12 Tf x y Tm (text) Tj ET` plus
    // optional extras like non-text ops at the start.
    private static List<ContentOperator> SimpleTextBlock(string text, double x, double y)
    {
        return new List<ContentOperator>
        {
            ContentOperator.BeginText(),
            new("Tf", new PdfObject[] { new PdfName("F1"), new PdfReal(12) }),
            ContentOperator.TextMatrix(12, 0, 0, 12, x, y),
            WithText(new ContentOperator("Tj", new PdfObject[] { new PdfString(text) }), text),
            ContentOperator.EndText(),
        };
    }

    // TextContent is normally populated by the parser; in tests we set it
    // directly since we're synthesizing operators.
    private static ContentOperator WithText(ContentOperator op, string text)
    {
        op.TextContent = text;
        return op;
    }

    [Fact]
    public void Process_EmptyOps_ReturnsEmpty()
    {
        var result = _remover.ProcessOperations(
            Array.Empty<ContentOperator>(),
            Array.Empty<Letter>(),
            new PdfRectangle(0, 0, 100, 100));
        result.Should().BeEmpty();
    }

    [Fact]
    public void Process_NoIntersection_CopiesOperatorsVerbatim()
    {
        var ops = SimpleTextBlock("HELLO", x: 100, y: 700);
        var letters = LettersFor("HELLO");
        var redactionArea = new PdfRectangle(400, 700, 500, 720); // far to the right

        var result = _remover.ProcessOperations(ops, letters, redactionArea);

        result.Should().HaveCount(ops.Count);
        result.Select(o => o.Name).Should().Equal("BT", "Tf", "Tm", "Tj", "ET");
        // Same Tj operand.
        (result[3].Operands[0] as PdfString)!.Value.Should().Be("HELLO");
    }

    [Fact]
    public void Process_EntireBlockOverlaps_ReplacesWithReconstructedBlock()
    {
        var ops = SimpleTextBlock("HELLO", x: 100, y: 700);
        var letters = LettersFor("HELLO");
        // Box covers every letter.
        var redactionArea = new PdfRectangle(50, 650, 300, 780);

        var result = _remover.ProcessOperations(ops, letters, redactionArea);

        // Original block is gone; no Tj emitted because every segment was
        // removed in TextSegmenter. Expected output = [] (the reconstructed
        // emission path skips segments with 0 entries).
        result.Select(o => o.Name).Should().BeEmpty();
    }

    [Fact]
    public void Process_PartialOverlap_KeepsOriginalBlockStructureAndAppendsReconstructed()
    {
        // Two text ops in the same block: one outside the redaction area,
        // one fully inside it. Original block's state ops survive because
        // the outside text-op needs them; the intersecting text-op is
        // stripped and replaced by a freshly-built BT…ET afterwards.
        var ops = new List<ContentOperator>
        {
            ContentOperator.BeginText(),
            new("Tf", new PdfObject[] { new PdfName("F1"), new PdfReal(12) }),
            ContentOperator.TextMatrix(12, 0, 0, 12, 100, 700),
            WithText(new ContentOperator("Tj", new PdfObject[] { new PdfString("HELLO") }), "HELLO"),
            ContentOperator.TextMatrix(12, 0, 0, 12, 400, 700),
            WithText(new ContentOperator("Tj", new PdfObject[] { new PdfString("WORLD") }), "WORLD"),
            ContentOperator.EndText(),
        };

        // Letters laid out at [100..134] for HELLO and [400..434] for WORLD.
        var letters = LettersFor("HELLO").Concat(LettersFor("WORLD", x0: 400)).ToList();

        // Box covers WORLD only.
        var redactionArea = new PdfRectangle(395, 650, 440, 780);

        var result = _remover.ProcessOperations(ops, letters, redactionArea);

        // Original block should still contain HELLO (kept-as-is) and the
        // state ops, but not WORLD's Tj. Reconstructed sequence may follow.
        var tjStrings = result.Where(o => o.Name == "Tj")
            .Select(o => (o.Operands[^1] as PdfString)?.Value ?? "")
            .ToList();

        tjStrings.Should().Contain("HELLO", "non-intersecting text stays intact");
        tjStrings.Should().NotContain("WORLD", "intersecting text is stripped from the original block");
        // All WORLD glyphs overlapped the redaction box so every segment was
        // removed — the reconstructed block emits nothing for it.
        tjStrings.Where(s => s != "HELLO").Should().BeEmpty();

        // Structural: first op must still be BT.
        result[0].Name.Should().Be("BT");
        // Somewhere before the end, there's an ET for the original block.
        result.Select(o => o.Name).Should().Contain("ET");
    }

    [Fact]
    public void Process_NonTextOperatorsPassThrough()
    {
        // Shows a re + f fill outside any text block survives unmodified.
        var ops = new List<ContentOperator>
        {
            ContentOperator.Rectangle(10, 10, 100, 100),
            ContentOperator.Fill(),
        };
        var result = _remover.ProcessOperations(
            ops,
            Array.Empty<Letter>(),
            new PdfRectangle(0, 0, 1000, 1000)); // even covering the rect

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("re");
        result[1].Name.Should().Be("f");
    }

    [Fact]
    public void Process_ReconstructedBlock_UsesAmbientTextState()
    {
        // Non-default text state set before the Tj that gets redacted+reconstructed;
        // the reconstructed block should carry those non-defaults back out.
        var ops = new List<ContentOperator>
        {
            ContentOperator.BeginText(),
            new("Tf", new PdfObject[] { new PdfName("F1"), new PdfReal(10) }),
            new("Tc", new PdfObject[] { new PdfReal(0.5) }),
            new("Tw", new PdfObject[] { new PdfReal(2.0) }),
            ContentOperator.TextMatrix(10, 0, 0, 10, 100, 700),
            WithText(new ContentOperator("Tj", new PdfObject[] { new PdfString("HELLO WORLD") }), "HELLO WORLD"),
            ContentOperator.EndText(),
        };
        var letters = LettersFor("HELLO WORLD");
        // Redact just "WORLD".
        var redactionArea = new PdfRectangle(
            letters[6].GlyphRectangle.Left,
            letters[6].GlyphRectangle.Bottom,
            letters[10].GlyphRectangle.Right,
            letters[10].GlyphRectangle.Top);

        var result = _remover.ProcessOperations(ops, letters, redactionArea);

        // Since the single Tj had letters that intersected, the whole
        // original block is replaced wholesale by the reconstructed one.
        // Tc + Tw must come back out because they weren't at their defaults
        // when the original Tj was drawn.
        result.Select(o => o.Name).Should().Contain("Tc");
        result.Select(o => o.Name).Should().Contain("Tw");
        result.Select(o => o.Name).Should().Contain("Tj");
        var reconTj = result.First(o => o.Name == "Tj");
        ((PdfString)reconTj.Operands[0]).Value.Should().StartWith("HELLO");
    }
}
