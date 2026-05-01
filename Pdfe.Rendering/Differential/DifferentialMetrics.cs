using System;
using System.IO;
using SkiaSharp;

namespace Pdfe.Rendering.Differential;

/// <summary>
/// Computes per-pixel diff statistics between two equally-sized bitmaps.
/// Used by <see cref="DifferentialRenderingTests"/> to flag pages where
/// pdfe's output diverges meaningfully from <c>mutool draw</c>.
/// </summary>
public sealed record DifferentialReport(
    int Width,
    int Height,
    int PixelCount,
    int DifferingPixels,
    double DifferingPixelFraction,
    double MeanAbsoluteError,
    double MaxAbsoluteError)
{
    public override string ToString() =>
        $"{Width}x{Height} px — diff {DifferingPixelFraction:P2} " +
        $"({DifferingPixels:N0}/{PixelCount:N0}), MAE {MeanAbsoluteError:F1}/255, " +
        $"max-pixel-Δ {MaxAbsoluteError:F0}/255";
}

public static class DifferentialMetrics
{
    /// <summary>
    /// Per-channel L1 threshold above which a pixel is considered to
    /// differ. Calibrated against the IRS / SCOTUS / CDC smoke corpus:
    /// at 64/255, two visually-identical renderings of the same page
    /// (same fonts, same layout, only sub-pixel AA + hinting drift) sit
    /// well below the suite-level "differing-pixel fraction" gate, while
    /// real disagreements (wrong font, wide spacing, missing glyphs)
    /// produce 30%+ differing pixels and trip the gate cleanly.
    /// </summary>
    private const int PerPixelTolerance = 64;

    /// <summary>
    /// Compare two bitmaps. The bitmaps must have the same dimensions —
    /// callers should normalize via <see cref="ResizeMatch"/> if not.
    /// </summary>
    public static DifferentialReport Compare(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException(
                $"Dimension mismatch: {a.Width}x{a.Height} vs {b.Width}x{b.Height}. " +
                $"Use ResizeMatch first if intentional.");

        int w = a.Width, h = a.Height;
        long pixelCount = (long)w * h;
        long differingPixels = 0;
        long sumL1 = 0;
        int maxL1 = 0;

        // GetPixel is slow per-call but correct across all SKColorTypes.
        // For our test sizes (~1700×2200 ≈ 3.7M pixels) this is ~1s,
        // acceptable for test-suite use.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                int dR = Math.Abs(pa.Red   - pb.Red);
                int dG = Math.Abs(pa.Green - pb.Green);
                int dB = Math.Abs(pa.Blue  - pb.Blue);
                int l1 = dR + dG + dB;          // 0..765
                int worst = Math.Max(dR, Math.Max(dG, dB)); // 0..255

                sumL1 += l1;
                if (worst > maxL1) maxL1 = worst;
                if (worst > PerPixelTolerance) differingPixels++;
            }
        }

        return new DifferentialReport(
            Width: w,
            Height: h,
            PixelCount: (int)pixelCount,
            DifferingPixels: (int)differingPixels,
            DifferingPixelFraction: (double)differingPixels / pixelCount,
            // sumL1 is over 3 channels per pixel; divide by 3 for mean
            // per-channel L1 in 0..255 units.
            MeanAbsoluteError: (double)sumL1 / (pixelCount * 3.0),
            MaxAbsoluteError: maxL1);
    }

    /// <summary>
    /// Resize <paramref name="src"/> to (<paramref name="targetW"/>, <paramref name="targetH"/>)
    /// using a high-quality scaler. Used when the two renderers produce
    /// slightly different bitmap sizes due to rounding (mutool floors,
    /// pdfe rounds-to-nearest, etc.).
    /// </summary>
    public static SKBitmap ResizeMatch(SKBitmap src, int targetW, int targetH)
    {
        if (src.Width == targetW && src.Height == targetH)
            return src.Copy();
        return src.Resize(new SKImageInfo(targetW, targetH), SKFilterQuality.High)
            ?? throw new InvalidOperationException("Resize failed");
    }

    /// <summary>
    /// Save a side-by-side diff PNG to <paramref name="path"/>: left = pdfe,
    /// middle = mutool, right = pixel-difference highlighted in red. Useful
    /// when a test fails so the dev can eyeball what diverged.
    /// </summary>
    public static void SaveTriptych(string path, SKBitmap pdfe, SKBitmap mutool, SKBitmap? diff = null)
    {
        diff ??= BuildDiffOverlay(pdfe, mutool);
        int w = pdfe.Width;
        int h = pdfe.Height;
        int gap = 8;

        using var combined = new SKBitmap(w * 3 + gap * 2, h);
        using var canvas = new SKCanvas(combined);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(pdfe,   0, 0);
        canvas.DrawBitmap(mutool, w + gap, 0);
        canvas.DrawBitmap(diff,   2 * (w + gap), 0);
        canvas.Flush();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var img  = SKImage.FromBitmap(combined);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        using var fs   = File.OpenWrite(path);
        data.SaveTo(fs);
    }

    /// <summary>
    /// Build a diff bitmap: grey background + red pixels where the two
    /// inputs differ. Cheap to compute; not statistically meaningful.
    /// </summary>
    public static SKBitmap BuildDiffOverlay(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException("Dimension mismatch");

        var diff = new SKBitmap(a.Width, a.Height);
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                int worst = Math.Max(Math.Max(
                    Math.Abs(pa.Red - pb.Red),
                    Math.Abs(pa.Green - pb.Green)),
                    Math.Abs(pa.Blue - pb.Blue));
                if (worst > PerPixelTolerance)
                    diff.SetPixel(x, y, new SKColor(255, 32, 32));
                else
                    diff.SetPixel(x, y, new SKColor(240, 240, 240));
            }
        }
        return diff;
    }
}
