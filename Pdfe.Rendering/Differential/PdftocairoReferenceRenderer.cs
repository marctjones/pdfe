using System;
using System.Diagnostics;
using System.IO;
using SkiaSharp;

namespace Pdfe.Rendering.Differential;

/// <summary>
/// Shells out to <c>pdftocairo</c> (Poppler) to render a page,
/// providing a second reference rendering alongside <c>mutool draw</c>.
///
/// Why two oracles: when pdfe disagrees with mutool, we can't tell
/// whether pdfe is wrong or mutool is wrong. With a second independent
/// engine (Poppler/Cairo, which is what pdf.js's own test suite uses
/// for reference renders), we get consensus voting: pdfe is treated
/// as correct if it matches *either* mutool or pdftocairo. A
/// disagreement with both is a real bug.
///
/// Mutool is GPL/AGPL; Poppler is GPL. Both are CLI subprocesses, so
/// the licensing stays out of pdfe's binary — same model as mutool.
///
/// Returns null on missing tool, timeout, non-zero exit, or decode
/// failure — same contract as <see cref="MutoolReferenceRenderer"/>.
/// </summary>
public static class PdftocairoReferenceRenderer
{
    private static readonly Lazy<bool> _available = new(() =>
    {
        try
        {
            var psi = new ProcessStartInfo("pdftocairo", "-v")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(2000);
            return true; // any exit code means it's installed
        }
        catch
        {
            return false;
        }
    });

    public static bool IsAvailable => _available.Value;

    /// <summary>
    /// Render <paramref name="pageNumber"/> (1-based) at <paramref name="dpi"/>
    /// via <c>pdftocairo -png -singlefile</c>. Returns null on any failure.
    /// </summary>
    public static SKBitmap? RenderPage(string pdfPath, int pageNumber, int dpi, int timeoutMs = 30_000)
    {
        if (!IsAvailable) return null;

        // pdftocairo's -singlefile mode emits exactly <prefix>.png with
        // no further suffix — easier to predict than the multi-file mode.
        var outPrefix = Path.Combine(Path.GetTempPath(),
            $"pdfe-pdftocairo-ref-{Guid.NewGuid():N}");
        var outPath = outPrefix + ".png";

        try
        {
            var psi = new ProcessStartInfo("pdftocairo",
                $"-png -singlefile -r {dpi} -f {pageNumber} -l {pageNumber} \"{pdfPath}\" \"{outPrefix}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            if (p.ExitCode != 0) return null;
            if (!File.Exists(outPath)) return null;

            return SKBitmap.Decode(outPath);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(outPath); } catch { }
        }
    }
}
