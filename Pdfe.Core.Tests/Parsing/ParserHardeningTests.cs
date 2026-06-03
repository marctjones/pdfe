using System;
using System.Text;
using System.Threading;
using AwesomeAssertions;
using Pdfe.Core.Content;
using Pdfe.Core.Parsing;
using Xunit;

namespace Pdfe.Core.Tests.Parsing;

/// <summary>
/// #346: the parsers must survive hostile input — bound recursion (deeply
/// nested arrays/dicts) with a typed exception instead of a StackOverflow, and
/// honor a CancellationToken so a caller can bound a runaway parse.
/// </summary>
public class ParserHardeningTests
{
    // ---- recursion-depth guard ----

    [Fact]
    public void PdfParser_DeeplyNestedArray_ThrowsTypedException_NotStackOverflow()
    {
        var data = Encoding.ASCII.GetBytes(new string('[', 5000));
        var parser = new PdfParser(data);

        var act = () => parser.ParseObject();

        act.Should().Throw<PdfParseException>().WithMessage("*nesting depth*");
    }

    [Fact]
    public void PdfParser_DeeplyNestedDictionary_ThrowsTypedException()
    {
        var data = Encoding.ASCII.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("<< /a ", 5000)));
        var parser = new PdfParser(data);

        var act = () => parser.ParseObject();

        act.Should().Throw<PdfParseException>().WithMessage("*nesting depth*");
    }

    [Fact]
    public void ContentStreamParser_DeeplyNestedArray_ThrowsTypedException()
    {
        // A content-stream operand of 5000 nested arrays would recurse
        // ParseArray->ParseToken->ParseArray to a StackOverflow without a guard.
        var content = Encoding.ASCII.GetBytes(new string('[', 5000));
        var parser = new ContentStreamParser(content);

        var act = () => parser.Parse();

        act.Should().Throw<PdfParseException>().WithMessage("*nesting depth*");
    }

    [Fact]
    public void ContentStreamParser_LegitimateNesting_ParsesFine()
    {
        // A normal TJ array with a modest nested array still parses.
        var content = Encoding.ASCII.GetBytes("BT /F1 12 Tf [ (a) -10 (b) [1 2] ] TJ ET");
        var parser = new ContentStreamParser(content);

        var result = parser.Parse();

        result.Operators.Should().Contain(op => op.Name == "TJ");
    }

    // ---- cancellation ----

    [Fact]
    public void ContentStreamParser_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new ContentStreamParser(Encoding.ASCII.GetBytes("BT /F1 12 Tf (hello) Tj ET"));

        var act = () => parser.Parse(cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void ContentStreamParser_DefaultToken_DoesNotCancel()
    {
        var parser = new ContentStreamParser(Encoding.ASCII.GetBytes("BT /F1 12 Tf (hello) Tj ET"));

        var act = () => parser.Parse(); // CancellationToken.None

        act.Should().NotThrow();
    }

    [Fact]
    public void PdfParser_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new PdfParser(Encoding.ASCII.GetBytes("[1 2 3]")) { CancellationToken = cts.Token };

        var act = () => parser.ParseObject();

        act.Should().Throw<OperationCanceledException>();
    }
}
