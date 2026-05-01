using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Rendering;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Pdfe.Rendering.Tests.Differential;

/// <summary>
/// Exploratory mode of the differential harness — runs against the full
/// pdf.js test corpus (~684 PDFs) and emits a JSON report instead of
/// passing/failing the build.
///
/// Why a report and not a normal test:
///   • The pdf.js corpus is intentionally diverse and surfaces ~250+
///     real disagreements with mutool today. Failing those would block
///     CI; allowlisting each one would mean ~250 hand-written reasons.
///   • Skipping them was the previous compromise — and it hid the
///     failures behind a benign-looking Skip count.
///   • A report file makes every result *visible* without conflating
///     "we don't render this correctly" with "this test was skipped
///     because it can't run." Each PDF is recorded as PASS / FAIL /
///     PARSE_ERROR / MISSING_PAGE / etc., with metrics.
///
/// The report is written to:
///   Pdfe.Rendering.Tests/bin/.../exploratory-report.json
///
/// To run it:
///   dotnet test Pdfe.Rendering.Tests --filter "Trait=Exploratory"
///
/// Default `dotnet test` skips this class entirely so CI sees only the
/// gating tests (smoke + Isartor) and reports honest pass/fail counts.
/// Run this manually when you want to know "how many pdf.js fixtures
/// does pdfe currently render correctly" — the count goes up as we fix
/// bugs and down when something regresses.
/// </summary>
[Trait("Category", "Exploratory")]
public sealed class ExploratoryDifferentialTests
{
    private readonly ITestOutputHelper _output;

    public ExploratoryDifferentialTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Exploratory")]
    public void GenerateCorpusReport()
    {
        Skip.IfNot(MutoolReferenceRenderer.IsAvailable,
            "mutool not on PATH — install mupdf-tools to generate the exploratory report");

        var root = LocateRepoRoot()!;
        var corpus = Path.Combine(root, "test-pdfs", "pdfjs");
        Skip.If(!Directory.Exists(corpus),
            "pdf.js corpus not downloaded — run scripts/download-pdfjs-corpus.sh");

        var entries = new List<CorpusEntry>();
        var pdfs = Directory.EnumerateFiles(corpus, "*.pdf").OrderBy(p => p).ToList();
        _output.WriteLine($"Scanning {pdfs.Count} PDFs in {corpus}...");

        foreach (var pdf in pdfs)
        {
            var rel = Path.GetRelativePath(root, pdf);
            var entry = ScanOne(rel, pdf);
            entries.Add(entry);
        }

        // Tally + emit summary.
        var counts = new Dictionary<string, int>();
        foreach (var e in entries)
        {
            counts.TryGetValue(e.Status, out var c);
            counts[e.Status] = c + 1;
        }

        _output.WriteLine("");
        _output.WriteLine("=== Corpus report ===");
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            _output.WriteLine($"  {kv.Value,4}  {kv.Key}");
        _output.WriteLine("");
        _output.WriteLine($"  Total: {entries.Count}");

        // Write the JSON report next to the test binary so a CI
        // step can upload it as an artifact.
        var reportPath = Path.Combine(AppContext.BaseDirectory, "exploratory-report.json");
        var json = JsonSerializer.Serialize(new
        {
            generatedUtc = DateTime.UtcNow.ToString("o"),
            corpus = "test-pdfs/pdfjs",
            counts,
            total = entries.Count,
            entries,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json);
        _output.WriteLine($"  report: {reportPath}");

        // The report itself is the output. We assert only that the
        // report was generated for at least 100 PDFs — anything below
        // that means corpus discovery broke.
        entries.Count.Should().BeGreaterThan(100,
            "exploratory report should cover the full pdf.js corpus");
    }

    private CorpusEntry ScanOne(string rel, string pdfPath)
    {
        var entry = new CorpusEntry { Path = rel };

        // Try to render page 1 with pdfe.
        SKBitmap? pdfeBitmap = null;
        try
        {
            using var doc = PdfDocument.Open(pdfPath);
            entry.PageCount = doc.PageCount;
            if (doc.PageCount == 0)
            {
                entry.Status = "EMPTY_DOC";
                return entry;
            }
            var renderer = new SkiaRenderer();
            pdfeBitmap = renderer.RenderPage(doc.GetPage(1), new RenderOptions { Dpi = 150 });
        }
        catch (Exception ex)
        {
            entry.Status = "PARSE_ERROR";
            entry.ErrorType = ex.GetType().Name;
            entry.ErrorMessage = Truncate(ex.Message, 200);
            return entry;
        }

        if (pdfeBitmap == null)
        {
            entry.Status = "RENDER_NULL";
            return entry;
        }

        // Reference render via mutool.
        var mutoolBitmap = MutoolReferenceRenderer.RenderPage(pdfPath, 1, 150);
        if (mutoolBitmap == null)
        {
            entry.Status = "MUTOOL_REFUSED";
            pdfeBitmap.Dispose();
            return entry;
        }

        // Normalize dims if needed.
        try
        {
            if (pdfeBitmap.Width != mutoolBitmap.Width || pdfeBitmap.Height != mutoolBitmap.Height)
            {
                using var resized = DifferentialMetrics.ResizeMatch(
                    pdfeBitmap, mutoolBitmap.Width, mutoolBitmap.Height);
                pdfeBitmap.Dispose();
                pdfeBitmap = resized.Copy();
            }

            var report = DifferentialMetrics.Compare(pdfeBitmap, mutoolBitmap);
            entry.DiffFraction = report.DifferingPixelFraction;
            entry.Mae = report.MeanAbsoluteError;
            // Same thresholds as the gating harness.
            entry.Status = (report.DifferingPixelFraction <= 0.10
                         && report.MeanAbsoluteError <= 32.0)
                ? "PASS"
                : "DIFF";
        }
        catch (Exception ex)
        {
            entry.Status = "COMPARE_ERROR";
            entry.ErrorType = ex.GetType().Name;
            entry.ErrorMessage = Truncate(ex.Message, 200);
        }
        finally
        {
            pdfeBitmap?.Dispose();
            mutoolBitmap.Dispose();
        }

        return entry;
    }

    private static string Truncate(string s, int n) =>
        s.Length <= n ? s : s.Substring(0, n) + "…";

    private static string? LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "pdfe.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    public sealed class CorpusEntry
    {
        public string Path { get; set; } = "";
        public string Status { get; set; } = "UNKNOWN";
        public int PageCount { get; set; }
        public double DiffFraction { get; set; }
        public double Mae { get; set; }
        public string? ErrorType { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
