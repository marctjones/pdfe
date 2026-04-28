using Pdfe.Core.Primitives;

namespace Pdfe.Rendering;

/// <summary>
/// Evaluates PDF Function dictionaries (§7.10) to produce output values.
/// Supports Types 0 (sampled), 2 (exponential), 3 (stitching).
/// </summary>
internal static class PdfFunctionEvaluator
{
    /// <summary>
    /// Evaluates a PDF function at parameter t ∈ [0,1].
    /// Returns an array of output component values, or null if the function can't be evaluated.
    /// </summary>
    public static double[]? Evaluate(PdfObject? funcObj, double t)
    {
        if (funcObj == null)
            return null;

        // Handle array of functions (one per component) — rare but valid
        if (funcObj is PdfArray arr)
        {
            var results = new List<double>();
            foreach (var item in arr)
            {
                var r = Evaluate(item, t);
                if (r != null)
                    results.AddRange(r);
            }
            return results.Count > 0 ? results.ToArray() : null;
        }

        if (funcObj is not PdfDictionary func)
            return null;

        var funcType = func.GetInt("FunctionType", -1);
        return funcType switch
        {
            0 => EvaluateSampled(func, t),
            2 => EvaluateExponential(func, t),
            3 => EvaluateStitching(func, t),
            _ => null
        };
    }

    /// <summary>
    /// Type 2: f(x) = C0 + x^N * (C1 - C0)
    /// </summary>
    private static double[] EvaluateExponential(PdfDictionary func, double t)
    {
        var n = func.GetNumber("N", 1.0);
        var c0 = GetNumberArray(func, "C0") ?? new[] { 0.0 };
        var c1 = GetNumberArray(func, "C1") ?? new[] { 1.0 };
        var len = Math.Max(c0.Length, c1.Length);
        var result = new double[len];

        for (int i = 0; i < len; i++)
        {
            var v0 = i < c0.Length ? c0[i] : 0;
            var v1 = i < c1.Length ? c1[i] : 1;
            result[i] = v0 + Math.Pow(t, n) * (v1 - v0);
        }

        return result;
    }

    /// <summary>
    /// Type 3: piecewise using Bounds/Encode to select and remap sub-functions
    /// </summary>
    private static double[]? EvaluateStitching(PdfDictionary func, double t)
    {
        var functions = func.GetOptional("Functions") as PdfArray;
        var boundsArr = GetNumberArray(func, "Bounds") ?? Array.Empty<double>();
        var encodeArr = GetNumberArray(func, "Encode") ?? Array.Empty<double>();

        if (functions == null || functions.Count == 0)
            return null;

        // Find which sub-function interval t falls into
        int idx = 0;
        for (int i = 0; i < boundsArr.Length; i++)
        {
            if (t < boundsArr[i])
                break;
            idx = i + 1;
        }
        idx = Math.Clamp(idx, 0, functions.Count - 1);

        // Encode t for the sub-function
        double tMin = idx == 0 ? 0 : boundsArr[idx - 1];
        double tMax = idx < boundsArr.Length ? boundsArr[idx] : 1;
        double e0 = encodeArr.Length > idx * 2 ? encodeArr[idx * 2] : 0;
        double e1 = encodeArr.Length > idx * 2 + 1 ? encodeArr[idx * 2 + 1] : 1;
        double tEnc = tMax > tMin ? e0 + (t - tMin) / (tMax - tMin) * (e1 - e0) : e0;

        return Evaluate(functions[idx], tEnc);
    }

    /// <summary>
    /// Type 0: sampled — linear interpolation in table
    /// </summary>
    private static double[]? EvaluateSampled(PdfDictionary func, double t)
    {
        // Get sample count (first dimension only for 1D input)
        var sizeArr = GetNumberArray(func, "Size");
        if (sizeArr == null || sizeArr.Length == 0)
            return null;

        int n = (int)sizeArr[0];
        if (n <= 1)
            return null;

        var bps = func.GetInt("BitsPerSample", 8);
        var rangeArr = GetNumberArray(func, "Range");
        var streamData = (func as PdfStream)?.DecodedData;

        if (streamData == null)
            return null;

        // Determine output components from Range
        int outComps = rangeArr != null ? rangeArr.Length / 2 : 1;
        int totalSamples = n * outComps;
        double maxVal = (1 << bps) - 1;

        // Read samples
        var samples = new double[totalSamples];
        for (int i = 0; i < Math.Min(totalSamples, streamData.Length); i++)
            samples[i] = streamData[i] / maxVal;

        // Linear interpolation
        double idx = t * (n - 1);
        int lo = (int)Math.Floor(idx);
        int hi = Math.Min(lo + 1, n - 1);
        double frac = idx - lo;

        var result = new double[outComps];
        for (int c = 0; c < outComps; c++)
        {
            double v0 = lo * outComps + c < samples.Length ? samples[lo * outComps + c] : 0;
            double v1 = hi * outComps + c < samples.Length ? samples[hi * outComps + c] : v0;
            result[c] = v0 + frac * (v1 - v0);

            // Map to Range if provided
            if (rangeArr != null && c * 2 + 1 < rangeArr.Length)
                result[c] = result[c] * (rangeArr[c * 2 + 1] - rangeArr[c * 2]) + rangeArr[c * 2];
        }

        return result;
    }

    private static double[]? GetNumberArray(PdfDictionary dict, string key)
    {
        var obj = dict.GetOptional(key);
        if (obj is PdfArray arr)
        {
            var result = new double[arr.Count];
            for (int i = 0; i < arr.Count; i++)
                result[i] = arr.GetNumber(i);
            return result;
        }

        return null;
    }
}
