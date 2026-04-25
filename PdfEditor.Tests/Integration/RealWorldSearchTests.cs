using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pdfe.Core.Document;
using PdfEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Search regression tests against real-world PDFs (the Pragmatic
/// "Business Success with Open Source" title and our multilingual
/// CJK fixture). The synthetic <see cref="PdfSearchService"/> tests
/// pass against PDFs created by <c>TestPdfGenerator</c>, but those
/// don't exercise the embedded-CFF subsets, multi-font runs,
/// browser-flipped Tm, and uniXXXX glyph naming that real PDFs use.
/// User-reported "search doesn't find anything" came from this gap.
/// </summary>
public class RealWorldSearchTests
{
    private readonly ITestOutputHelper _out;
    public RealWorldSearchTests(ITestOutputHelper o) { _out = o; }

    private const string PragmaticBook =
        "/home/marc/Downloads/business-success-with-open-source_P1.0.pdf";
    private const string CjkFixture =
        "../../../../test-pdfs/sample-pdfs/multilingual-noto-cjk.pdf";

    private static PdfSearchService NewService() =>
        new(NullLogger<PdfSearchService>.Instance);

    [Fact]
    public void PragmaticBook_PageText_ContainsExpectedWords()
    {
        if (!File.Exists(PragmaticBook)) return;

        using var doc = PdfDocument.Open(PragmaticBook);
        // Page 3 we already verified renders "Early Praise for Business
        // Success with Open Source" in the rendering tests — its text
        // extraction should yield those tokens.
        var page = doc.GetPage(3);
        var text = page.Text ?? string.Empty;
        var words = page.GetWords().Select(w => w.Text).ToList();
        _out.WriteLine($"page 3: {text.Length} chars, {words.Count} words");
        _out.WriteLine($"first 12 words: {string.Join(" | ", words.Take(12))}");

        text.Should().NotBeNullOrWhiteSpace(
            "PdfPage.Text must extract something from this page");
        words.Should().NotBeEmpty(
            "PdfPage.GetWords() must return at least some words");
        text.ToLowerInvariant().Should().Contain("open source",
            "page 3 contains 'Open Source' multiple times");
    }

    [Fact]
    public void PragmaticBook_Search_FindsCommonWord()
    {
        if (!File.Exists(PragmaticBook)) return;

        var matches = NewService().Search(PragmaticBook, "open source");
        _out.WriteLine($"Found {matches.Count} matches");
        if (matches.Count > 0)
        {
            var byPage = matches.GroupBy(m => m.PageIndex)
                .OrderBy(g => g.Key)
                .Take(5)
                .Select(g => $"page {g.Key + 1}: {g.Count()}");
            _out.WriteLine($"first pages: {string.Join(", ", byPage)}");
        }

        matches.Should().NotBeEmpty(
            "the phrase 'open source' must appear many times in this 455-page book");
        matches.Should().HaveCountGreaterThan(20,
            "expected dozens of hits across the book; got " + matches.Count);
        matches.Should().OnlyContain(m => m.Width > 0 && m.Height > 0,
            "every match needs a real bounding box for highlight rendering");
    }

    [Fact]
    public void PragmaticBook_Search_CaseSensitiveDistinguishesCases()
    {
        if (!File.Exists(PragmaticBook)) return;

        var svc = NewService();
        var insensitive = svc.Search(PragmaticBook, "Open", caseSensitive: false);
        var sensitiveUpper = svc.Search(PragmaticBook, "Open", caseSensitive: true);
        var sensitiveLower = svc.Search(PragmaticBook, "open", caseSensitive: true);

        // Same letter sequence — case-insensitive sums upper + lower.
        insensitive.Count.Should().BeGreaterOrEqualTo(sensitiveUpper.Count);
        insensitive.Count.Should().BeGreaterOrEqualTo(sensitiveLower.Count);
        sensitiveUpper.Should().NotBeEmpty("'Open' (capital O) must appear");
        sensitiveLower.Should().NotBeEmpty("'open' (lowercase o) must appear");
    }

    [Fact]
    public void PragmaticBook_Search_WholeWordIsolatesTokens()
    {
        if (!File.Exists(PragmaticBook)) return;

        var svc = NewService();
        var word = svc.Search(PragmaticBook, "the",
            caseSensitive: false, wholeWordsOnly: true);
        var substring = svc.Search(PragmaticBook, "the",
            caseSensitive: false, wholeWordsOnly: false);

        word.Should().NotBeEmpty();
        substring.Count.Should().BeGreaterThan(word.Count,
            "substring matches 'the' inside 'these', 'theme', 'whether', etc., " +
            "so the count should exceed the whole-word count");
    }

    [Fact]
    public void PragmaticBook_Search_BoundingBoxesAreInPdfPoints()
    {
        // The match coordinates should be plausible PDF-point values for the
        // page (this 455-page book has 540×648 pages). Pre-fix the GUI used
        // a 150-DPI scale to translate match coords to viewer DIPs but the
        // viewer renders at 120 — symptom: highlights either drifted right
        // or were clipped off the visible area entirely. The service itself
        // returns PDF points; the GUI's job is the conversion. This test
        // pins the contract for the service.
        if (!File.Exists(PragmaticBook)) return;

        using var doc = PdfDocument.Open(PragmaticBook);
        var page = doc.GetPage(3);
        var pageW = page.Width;  // 540 pts
        var pageH = page.Height; // 648 pts

        var matches = NewService().Search(PragmaticBook, "Open").ToList();
        var pageMatches = matches.Where(m => m.PageIndex == 2).ToList();
        pageMatches.Should().NotBeEmpty("page 3 contains 'Open'");

        foreach (var m in pageMatches)
        {
            m.X.Should().BeInRange(0, pageW, "X must be within page");
            m.Y.Should().BeInRange(0, pageH, "Y must be within page");
            m.Width.Should().BeInRange(1, pageW, "Width must be plausible");
            m.Height.Should().BeInRange(1, pageH / 4, "Height must be glyph-scale, not page-scale");
        }
    }

    [Fact]
    public void CjkFixture_Search_FindsLatinWord()
    {
        if (!File.Exists(CjkFixture)) return;

        // The fixture has English, zh-Hans, zh-Hant, ja, ko. At minimum
        // the Latin part ("Mixed:" / "English:") must be searchable —
        // the CJK Type0 path is harder and is exercised in a separate
        // test with xfail behaviour if needed.
        var matches = NewService().Search(CjkFixture, "English");
        matches.Should().NotBeEmpty("'English' appears in the fixture");
    }
}
