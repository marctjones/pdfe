using System;
using System.Diagnostics;
using System.IO;

namespace Pdfe.Rendering.Tests.Differential;

/// <summary>
/// Shells out to <c>mutool draw -F txt</c> to extract a PDF page's
/// text. Used by <see cref="TextExtractionDifferentialTests"/> as the
/// reference oracle for our own <c>page.Text</c>.
///
/// Why text extraction matters as a separate layer: the pixel-level
/// differential harness will catch the Betterment-class symptom
/// ("Bet ter ment" wide-spaced glyphs) — but pixel diffs are slow
/// and produce a lot of false-positive noise from sub-pixel AA.
/// Text extraction is essentially free (no rasterization), strictly
/// content-level, and many bugs that show up as pixel disagreements
/// also show up as text-extraction disagreements with much cleaner
/// signal. So this is a faster, more discriminating bug detector for
/// the same class of glyph-mapping / encoding / CMap bugs.
/// </summary>
internal static class MutoolTextExtractor
{
    /// <summary>
    /// Extract text from a single page (1-based). Returns null when
    /// mutool isn't available or refuses (timeout / non-zero exit).
    /// </summary>
    public static string? ExtractPage(string pdfPath, int pageNumber, int timeoutMs = 30_000)
    {
        if (!MutoolReferenceRenderer.IsAvailable) return null;

        var outPath = Path.Combine(Path.GetTempPath(),
            $"pdfe-mutool-text-{Guid.NewGuid():N}.txt");
        try
        {
            // -F txt makes mutool emit plain UTF-8 text (extracted by
            // its own text-merge engine, which is independent of pdfe).
            var psi = new ProcessStartInfo("mutool",
                $"draw -o \"{outPath}\" -F txt \"{pdfPath}\" {pageNumber}")
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

            return File.ReadAllText(outPath);
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
