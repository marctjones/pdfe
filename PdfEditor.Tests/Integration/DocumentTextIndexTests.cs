using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pdfe.Core.Document;
using PdfEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// The text index pre-extracts every page's text on document open so
/// search-as-you-type doesn't re-walk the document on every keystroke.
/// Tests pin: index parity with live search, and that the indexed
/// search beats the live walk on the second query.
/// </summary>
public class DocumentTextIndexTests
{
    private readonly ITestOutputHelper _out;
    public DocumentTextIndexTests(ITestOutputHelper o) { _out = o; }

    private const string PragmaticBook =
        "/home/marc/Downloads/business-success-with-open-source_P1.0.pdf";

    [Fact]
    public async Task IndexedSearch_ReturnsSameMatches_AsLiveSearch()
    {
        if (!File.Exists(PragmaticBook)) return;

        using var doc = PdfDocument.Open(PragmaticBook);
        var svc = new PdfSearchService(NullLogger<PdfSearchService>.Instance);

        // Build the index.
        var idx = new DocumentTextIndex(doc, NullLogger.Instance);
        await idx.BuildAsync();
        idx.IsReady.Should().BeTrue();

        // Run the same query both ways.
        var liveMatches = svc.Search(doc, "Open Source");
        var indexedMatches = svc.Search(idx, "Open Source");

        _out.WriteLine($"live: {liveMatches.Count}, indexed: {indexedMatches.Count}");

        indexedMatches.Count.Should().Be(liveMatches.Count,
            "indexed search should find exactly the same matches as live search");

        // Match data should be identical (page, text, geometry).
        for (int i = 0; i < liveMatches.Count; i++)
        {
            indexedMatches[i].PageIndex.Should().Be(liveMatches[i].PageIndex);
            indexedMatches[i].MatchedText.Should().Be(liveMatches[i].MatchedText);
            indexedMatches[i].X.Should().BeApproximately(liveMatches[i].X, 0.01);
            indexedMatches[i].Y.Should().BeApproximately(liveMatches[i].Y, 0.01);
        }
    }

    [Fact]
    public async Task IndexedSearch_IsFasterThanLiveSearchSecondQuery()
    {
        // Pin the user-visible win: once the index is built, subsequent
        // searches are no longer linear in page-text-extraction cost. On
        // the Pragmatic book that drops the per-search wall time from
        // ~30 s to a few hundred ms.
        if (!File.Exists(PragmaticBook)) return;

        using var doc = PdfDocument.Open(PragmaticBook);
        var svc = new PdfSearchService(NullLogger<PdfSearchService>.Instance);

        // Build index (cost not counted in the comparison — it's the
        // up-front cost the user already pays at document open).
        var idx = new DocumentTextIndex(doc, NullLogger.Instance);
        await idx.BuildAsync();

        // Warm-up live search so any first-call JIT/IO costs aren't unfair.
        _ = svc.Search(doc, "Brasseur");

        var swLive = Stopwatch.StartNew();
        var liveMatches = svc.Search(doc, "the");
        swLive.Stop();

        var swIdx = Stopwatch.StartNew();
        var idxMatches = svc.Search(idx, "the");
        swIdx.Stop();

        _out.WriteLine($"live: {swLive.ElapsedMilliseconds} ms ({liveMatches.Count} matches)");
        _out.WriteLine($"index: {swIdx.ElapsedMilliseconds} ms ({idxMatches.Count} matches)");

        idxMatches.Count.Should().Be(liveMatches.Count);
        swIdx.ElapsedMilliseconds.Should().BeLessThan(swLive.ElapsedMilliseconds,
            "indexed search must be faster than live walking");
        // And typically by a wide margin — the indexed walk is dominated
        // by the substring scan over already-extracted text.
        swIdx.ElapsedMilliseconds.Should().BeLessThan(swLive.ElapsedMilliseconds / 2,
            "indexed search should be at least 2× faster (in practice ~10×)");
    }

    [Fact]
    public async Task IndexBuildAsync_ReportsProgressUntilCompletion()
    {
        if (!File.Exists(PragmaticBook)) return;
        using var doc = PdfDocument.Open(PragmaticBook);
        var idx = new DocumentTextIndex(doc, NullLogger.Instance);

        // Use a synchronous IProgress so callback ordering matches the
        // worker's emission order (Progress<T> dispatches via captured
        // SyncContext, which can reorder).
        var reports = new System.Collections.Generic.List<(int done, int total)>();
        var progress = new SyncProgress<(int Done, int Total)>(p =>
        {
            lock (reports) reports.Add((p.Done, p.Total));
        });

        await idx.BuildAsync(progress);

        reports.Should().NotBeEmpty();
        var maxDone = reports.Max(r => r.done);
        var totalPages = reports[0].total;
        maxDone.Should().Be(totalPages,
            "final report should mark every page indexed");
        for (int i = 1; i < reports.Count; i++)
            reports[i].done.Should().BeGreaterThanOrEqualTo(reports[i - 1].done,
                "progress should be monotonic");
    }

    private sealed class SyncProgress<T> : System.IProgress<T>
    {
        private readonly System.Action<T> _h;
        public SyncProgress(System.Action<T> h) { _h = h; }
        public void Report(T value) => _h(value);
    }
}
