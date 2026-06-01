using System;
using System.Diagnostics;
using System.IO;
using SkiaSharp;

namespace Pdfe.Rendering.Differential;

/// <summary>
/// Shells out to <c>mutool draw</c> (MuPDF) to render a page, providing a
/// reference rendering we can diff our own SkiaRenderer output against.
///
/// Why mutool: it's the most rigorously-tested OSS PDF renderer (Apache-2.0
/// CLI; the engine is AGPL-3.0 but we only invoke the CLI as a subprocess,
/// which doesn't propagate the AGPL into our own code). Crucially, it's
/// independent of our codebase — when its output differs from ours, the
/// disagreement is informative.
///
/// All methods return null when mutool isn't available so tests can degrade
/// to Skipped rather than fail in environments without it (CI windows-latest,
/// for example).
/// </summary>
public static class MutoolReferenceRenderer
{
    private static readonly Lazy<bool> _available = new(() =>
    {
        // mutool is one of those CLIs that exits non-zero when invoked
        // without a real command — even `mutool --version` returns 1.
        // So instead of a version probe, just see if it's launchable.
        // ProcessStartInfo.Start throws Win32Exception when the file
        // can't be found, which is the only failure we care about here.
        try
        {
            var psi = new ProcessStartInfo("mutool", "draw")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(2000);
            return true; // launched successfully — any exit code means it's installed
        }
        catch
        {
            return false;
        }
    });

    /// <summary>True when <c>mutool</c> is on PATH and responds to --version.</summary>
    public static bool IsAvailable => _available.Value;

    /// <summary>
    /// Render <paramref name="pageNumber"/> (1-based) of <paramref name="pdfPath"/>
    /// at <paramref name="dpi"/> via <c>mutool draw -F png</c>. Returns null on
    /// any failure (timeout, non-zero exit, missing output, decode failure).
    /// </summary>
    public static SKBitmap? RenderPage(string pdfPath, int pageNumber, int dpi, int timeoutMs = 30_000)
    {
        if (!IsAvailable) return null;

        var outPath = Path.Combine(Path.GetTempPath(),
            $"pdfe-mutool-ref-{Guid.NewGuid():N}.png");

        try
        {
            var psi = new ProcessStartInfo("mutool",
                $"draw -o \"{outPath}\" -F png -r {dpi} \"{pdfPath}\" {pageNumber}")
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
