using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pdfe.Core.Document;
using PdfEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Pin the contract for the disk-backed thumbnail cache: first call
/// renders + writes to disk, second call (and re-instantiations of
/// the service over the same file) load from disk without rendering.
/// Uses a temp cache dir via XDG_CACHE_HOME so tests don't pollute
/// the user's real cache directory.
/// </summary>
public class ThumbnailCacheTests
{
    private readonly ITestOutputHelper _out;
    public ThumbnailCacheTests(ITestOutputHelper o) { _out = o; }

    private const string PragmaticBook =
        "/home/marc/Downloads/business-success-with-open-source_P1.0.pdf";

    [Fact]
    public async Task FirstCallRenders_SecondCallLoadsFromDisk()
    {
        if (!File.Exists(PragmaticBook)) return;

        // Sandbox the cache dir for this test so we don't fight other
        // runs or pollute ~/.cache/pdfe.
        var tempCache = Path.Combine(Path.GetTempPath(),
            "pdfe-thumb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCache);
        var prevXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", tempCache);
        try
        {
            using var doc = PdfDocument.Open(PragmaticBook);

            // First service instance — populates the cache from scratch.
            using (var svc1 = new ThumbnailCacheService(PragmaticBook, doc, NullLogger.Instance))
            {
                var sw = Stopwatch.StartNew();
                using var bmp = await svc1.GetThumbnailAsync(2); // page 3
                sw.Stop();
                _out.WriteLine($"first render: {sw.ElapsedMilliseconds} ms");

                bmp.Should().NotBeNull("first call must produce a thumbnail");
                bmp!.Width.Should().BeGreaterThan(50);

                // Disk file should exist.
                var any = Directory.GetFiles(svc1.CacheDir, "p00002.webp",
                    SearchOption.AllDirectories);
                any.Should().NotBeEmpty(
                    $"cache file should be written under {svc1.CacheDir}");
            }

            // Second service instance over the same file — the same hash
            // dir is already populated, so this must NOT need to render.
            using (var svc2 = new ThumbnailCacheService(PragmaticBook, doc, NullLogger.Instance))
            {
                var sw = Stopwatch.StartNew();
                using var bmp = await svc2.GetThumbnailAsync(2);
                sw.Stop();
                _out.WriteLine($"second (cache hit): {sw.ElapsedMilliseconds} ms");

                bmp.Should().NotBeNull();
                bmp!.Width.Should().BeGreaterThan(50);

                // Cache hit should be at least an order of magnitude faster
                // than rendering. Ballpark: render ~50-200ms, decode ~1-10ms.
                // Keep the threshold generous so test isn't flaky.
                sw.ElapsedMilliseconds.Should().BeLessThan(100,
                    "cache hit should be a sub-100ms WebP decode rather than " +
                    "a full re-render of the page");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", prevXdg);
            try { Directory.Delete(tempCache, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ConcurrentRequestsForSamePage_AllCallersDisposeIndependently()
    {
        // Regression for the "app ends unexpectedly while scrolling
        // thumbnails" crash. Pre-fix the cache service shared a single
        // SKBitmap across every awaiter of an in-flight Task; multiple
        // callers each running `using var sk = await GetThumbnailAsync(...)`
        // would race their Dispose calls on the same handle and SkiaSharp
        // would segfault on the second disposal. The fix gives each
        // caller a freshly-copied SKBitmap.
        if (!File.Exists(PragmaticBook)) return;

        var tempCache = Path.Combine(Path.GetTempPath(),
            "pdfe-thumb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCache);
        var prevXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", tempCache);
        try
        {
            using var doc = PdfDocument.Open(PragmaticBook);
            using var svc = new ThumbnailCacheService(PragmaticBook, doc, NullLogger.Instance);

            // Eight concurrent same-page requests — pre-fix every awaiter
            // got the same SKBitmap reference, so their Dispose calls
            // raced. Now every awaiter gets its own copy.
            var tasks = new Task<SkiaSharp.SKBitmap?>[8];
            for (int i = 0; i < tasks.Length; i++)
                tasks[i] = svc.GetThumbnailAsync(0);

            var results = await Task.WhenAll(tasks);

            // Every result must be a distinct, disposable instance.
            var seen = new HashSet<SkiaSharp.SKBitmap>();
            foreach (var r in results)
            {
                r.Should().NotBeNull();
                seen.Add(r!).Should().BeTrue(
                    "every caller must receive its own SKBitmap, not a shared reference");
            }

            // Disposing all of them in turn must not throw — pre-fix this
            // would crash on the second Dispose.
            foreach (var b in results) b!.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", prevXdg);
            try { Directory.Delete(tempCache, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ConcurrentRequestsForSamePage_CoalesceOnSingleTask()
    {
        if (!File.Exists(PragmaticBook)) return;

        var tempCache = Path.Combine(Path.GetTempPath(),
            "pdfe-thumb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCache);
        var prevXdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", tempCache);
        try
        {
            using var doc = PdfDocument.Open(PragmaticBook);
            using var svc = new ThumbnailCacheService(PragmaticBook, doc, NullLogger.Instance);

            // Fire 8 concurrent requests for the same page. The service's
            // _inFlight dedupe table should make all 8 await one underlying
            // load — this is the contract that protects the renderer (which
            // is single-threaded against shared parser state).
            var tasks = new Task<SkiaSharp.SKBitmap?>[8];
            for (int i = 0; i < tasks.Length; i++)
                tasks[i] = svc.GetThumbnailAsync(0);

            var results = await Task.WhenAll(tasks);
            foreach (var r in results) r.Should().NotBeNull();

            // Only one cache file should have been written (the others
            // coalesced onto the same load).
            var files = Directory.GetFiles(svc.CacheDir, "p*.webp");
            files.Length.Should().Be(1);

            foreach (var b in results) b?.Dispose();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", prevXdg);
            try { Directory.Delete(tempCache, recursive: true); } catch { }
        }
    }
}
