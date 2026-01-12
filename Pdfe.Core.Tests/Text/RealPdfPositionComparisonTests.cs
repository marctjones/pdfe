using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Text;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace Pdfe.Core.Tests.Text;

/// <summary>
/// Position comparison tests using REAL PDFs from veraPDF corpus.
/// Issue #306: Test with real-world PDFs instead of PdfSharpCore-generated ones.
/// </summary>
public class RealPdfPositionComparisonTests
{
    private readonly ITestOutputHelper _output;
    private const string VeraPdfCorpusBase = "/home/marc/Projects/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master";

    public RealPdfPositionComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Data Models (reused from PositionComparisonTests)

    public class LetterPosition
    {
        public string Character { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Bottom { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
        public double Width => Right - Left;
        public double Height => Top - Bottom;
        public double CenterX => (Left + Right) / 2;
        public double CenterY => (Bottom + Top) / 2;
        public double BaselineY { get; set; }
    }

    public class PositionComparison
    {
        public char Character { get; set; }
        public int Index { get; set; }
        public LetterPosition PdfPig { get; set; } = null!;
        public LetterPosition PdfeCore { get; set; } = null!;
        public double DeltaLeft => Math.Abs(PdfPig.Left - PdfeCore.Left);
        public double DeltaRight => Math.Abs(PdfPig.Right - PdfeCore.Right);
        public double DeltaBottom => Math.Abs(PdfPig.Bottom - PdfeCore.Bottom);
        public double DeltaTop => Math.Abs(PdfPig.Top - PdfeCore.Top);
        public double DeltaCenterX => Math.Abs(PdfPig.CenterX - PdfeCore.CenterX);
        public double DeltaCenterY => Math.Abs(PdfPig.CenterY - PdfeCore.CenterY);
        public double MaxDelta => new[] { DeltaLeft, DeltaRight, DeltaBottom, DeltaTop }.Max();
        public bool IsSignificantDifference => MaxDelta > 0.5;
    }

    #endregion

    #region Test Cases with Real PDFs

    [SkippableFact]
    public void Compare_VeraPDF_Heading_Document()
    {
        var pdfPath = Path.Combine(VeraPdfCorpusBase,
            "PDF_UA-1/7.4 Headings/7.4.4 Unnumbered headings/7.4.4-t03-fail-a.pdf");

        Skip.IfNot(File.Exists(pdfPath), $"Test PDF not found: {pdfPath}");

        _output.WriteLine($"Testing with: {Path.GetFileName(pdfPath)}");

        // Extract first 10 characters from PdfPig to see what text exists
        var pigLetters = ExtractAllLetters_PdfPig(pdfPath).Take(20).ToList();
        var coreLetters = ExtractAllLetters_PdfeCore(pdfPath).Take(20).ToList();

        _output.WriteLine($"\nPdfPig found {pigLetters.Count} letters");
        _output.WriteLine($"Pdfe.Core found {coreLetters.Count} letters");

        if (pigLetters.Count == 0 || coreLetters.Count == 0)
        {
            _output.WriteLine("⚠ One or both libraries failed to extract text!");
            return;
        }

        // Display first few letters from each library
        _output.WriteLine("\nFirst 10 letters from PdfPig:");
        foreach (var letter in pigLetters.Take(10))
        {
            _output.WriteLine($"  '{letter.Character}' at ({letter.Left:F2}, {letter.Bottom:F2})");
        }

        _output.WriteLine("\nFirst 10 letters from Pdfe.Core:");
        foreach (var letter in coreLetters.Take(10))
        {
            _output.WriteLine($"  '{letter.Character}' at ({letter.Left:F2}, {letter.Bottom:F2})");
        }

        // Try to match letters by character and approximate position
        var comparisons = MatchLettersByContent(pigLetters, coreLetters);

        if (comparisons.Count > 0)
        {
            _output.WriteLine($"\n=== Matched {comparisons.Count} letters ===");
            LogComparison(comparisons, "VeraPDF_Heading");
            AnalyzeDifferences(comparisons);
        }
        else
        {
            _output.WriteLine("\n⚠ Could not match any letters between libraries");
        }
    }

    [SkippableFact]
    public void Compare_VeraPDF_OptionalContent_Document()
    {
        var pdfPath = Path.Combine(VeraPdfCorpusBase,
            "PDF_UA-1/7.10 Optional content/7.10-t01-pass-a.pdf");

        Skip.IfNot(File.Exists(pdfPath), $"Test PDF not found: {pdfPath}");

        _output.WriteLine($"Testing with: {Path.GetFileName(pdfPath)}");

        var pigLetters = ExtractAllLetters_PdfPig(pdfPath).Take(50).ToList();
        var coreLetters = ExtractAllLetters_PdfeCore(pdfPath).Take(50).ToList();

        _output.WriteLine($"\nPdfPig found {pigLetters.Count} letters");
        _output.WriteLine($"Pdfe.Core found {coreLetters.Count} letters");

        if (pigLetters.Count > 0 && coreLetters.Count > 0)
        {
            var comparisons = MatchLettersByContent(pigLetters, coreLetters);

            if (comparisons.Count > 0)
            {
                _output.WriteLine($"\n=== Matched {comparisons.Count} letters ===");
                LogComparison(comparisons, "VeraPDF_OptionalContent");
                AnalyzeDifferences(comparisons);
            }
        }
    }

    [SkippableFact]
    public void Compare_Multiple_VeraPDF_Documents()
    {
        var testPdfs = new[]
        {
            "PDF_UA-1/7.4 Headings/7.4.4 Unnumbered headings/7.4.4-t01-pass-a.pdf",
            "PDF_UA-1/7.4 Headings/7.4.4 Unnumbered headings/7.4.4-t02-pass-a.pdf",
            "PDF_UA-1/7.10 Optional content/7.10-t01-pass-a.pdf",
        };

        var allComparisons = new List<PositionComparison>();

        foreach (var relativePath in testPdfs)
        {
            var pdfPath = Path.Combine(VeraPdfCorpusBase, relativePath);

            if (!File.Exists(pdfPath))
            {
                _output.WriteLine($"⚠ Skipping: {Path.GetFileName(pdfPath)} (not found)");
                continue;
            }

            _output.WriteLine($"\n{'=',-60}");
            _output.WriteLine($"Testing: {Path.GetFileName(pdfPath)}");
            _output.WriteLine($"{'=',-60}");

            var pigLetters = ExtractAllLetters_PdfPig(pdfPath).Take(30).ToList();
            var coreLetters = ExtractAllLetters_PdfeCore(pdfPath).Take(30).ToList();

            _output.WriteLine($"PdfPig: {pigLetters.Count} letters, Pdfe.Core: {coreLetters.Count} letters");

            if (pigLetters.Count > 0 && coreLetters.Count > 0)
            {
                var comparisons = MatchLettersByContent(pigLetters, coreLetters);

                if (comparisons.Count > 0)
                {
                    _output.WriteLine($"Matched: {comparisons.Count} letters");
                    allComparisons.AddRange(comparisons);

                    // Show brief stats for this document
                    var avgDelta = comparisons.Average(c => c.MaxDelta);
                    var maxDelta = comparisons.Max(c => c.MaxDelta);
                    _output.WriteLine($"Avg Δ: {avgDelta:F2} pt, Max Δ: {maxDelta:F2} pt");
                }
            }
        }

        // Overall statistics across all documents
        if (allComparisons.Count > 0)
        {
            _output.WriteLine($"\n{'=',-60}");
            _output.WriteLine($"OVERALL STATISTICS ({allComparisons.Count} letters across {testPdfs.Length} documents)");
            _output.WriteLine($"{'=',-60}");
            AnalyzeDifferences(allComparisons);
        }
        else
        {
            _output.WriteLine("\n⚠ No letters matched across any documents");
        }
    }

    #endregion

    #region Extraction Methods

    private List<LetterPosition> ExtractAllLetters_PdfPig(string pdfPath)
    {
        var positions = new List<LetterPosition>();

        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var page = document.GetPage(1);

            foreach (var letter in page.Letters)
            {
                positions.Add(new LetterPosition
                {
                    Character = letter.Value,
                    Source = "PdfPig",
                    Left = letter.GlyphRectangle.Left,
                    Bottom = letter.GlyphRectangle.Bottom,
                    Right = letter.GlyphRectangle.Right,
                    Top = letter.GlyphRectangle.Top,
                    BaselineY = letter.StartBaseLine.Y
                });
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"PdfPig extraction error: {ex.Message}");
        }

        return positions;
    }

    private List<LetterPosition> ExtractAllLetters_PdfeCore(string pdfPath)
    {
        var positions = new List<LetterPosition>();

        try
        {
            using var stream = File.OpenRead(pdfPath);
            using var document = Pdfe.Core.Document.PdfDocument.Open(stream);
            var page = document.GetPage(1);

            foreach (var letter in page.Letters)
            {
                positions.Add(new LetterPosition
                {
                    Character = letter.Value,
                    Source = "Pdfe.Core",
                    Left = letter.GlyphRectangle.Left,
                    Bottom = letter.GlyphRectangle.Bottom,
                    Right = letter.GlyphRectangle.Right,
                    Top = letter.GlyphRectangle.Top,
                    BaselineY = letter.StartBaseLine.Y
                });
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Pdfe.Core extraction error: {ex.Message}");
        }

        return positions;
    }

    /// <summary>
    /// Match letters by character content and approximate Y position (within 10pt vertical band).
    /// This is more robust than exact position matching when positions differ significantly.
    /// </summary>
    private List<PositionComparison> MatchLettersByContent(
        List<LetterPosition> pigLetters,
        List<LetterPosition> coreLetters)
    {
        var comparisons = new List<PositionComparison>();
        var usedCoreIndices = new HashSet<int>();

        foreach (var pigLetter in pigLetters)
        {
            // Find matching letter in core: same character, similar Y position (±10pt tolerance)
            for (int i = 0; i < coreLetters.Count; i++)
            {
                if (usedCoreIndices.Contains(i))
                    continue;

                var coreLetter = coreLetters[i];

                // Match if: same character AND similar Y position
                if (coreLetter.Character == pigLetter.Character &&
                    Math.Abs(coreLetter.CenterY - pigLetter.CenterY) < 10)
                {
                    comparisons.Add(new PositionComparison
                    {
                        Character = pigLetter.Character[0],
                        Index = comparisons.Count,
                        PdfPig = pigLetter,
                        PdfeCore = coreLetter
                    });

                    usedCoreIndices.Add(i);
                    break; // Found a match, move to next PdfPig letter
                }
            }
        }

        return comparisons;
    }

    #endregion

    #region Analysis and Logging

    private void LogComparison(List<PositionComparison> comparisons, string testName)
    {
        _output.WriteLine($"\n{'=',-60}");
        _output.WriteLine($"Position Comparison: {testName}");
        _output.WriteLine($"{'=',-60}");
        _output.WriteLine($"{"Idx",-4} {"Char",-5} {"dLeft",-8} {"dRight",-8} {"dBottom",-8} {"dTop",-8} {"MaxΔ",-8} {"Status",-10}");
        _output.WriteLine($"{'-',-60}");

        foreach (var comp in comparisons.Take(20)) // Limit to first 20 for readability
        {
            var status = comp.IsSignificantDifference ? "⚠ DIFF" : "✓ OK";
            _output.WriteLine(
                $"{comp.Index,-4} {comp.Character,-5} " +
                $"{comp.DeltaLeft,-8:F2} {comp.DeltaRight,-8:F2} " +
                $"{comp.DeltaBottom,-8:F2} {comp.DeltaTop,-8:F2} " +
                $"{comp.MaxDelta,-8:F2} {status,-10}");
        }

        if (comparisons.Count > 20)
        {
            _output.WriteLine($"... ({comparisons.Count - 20} more letters)");
        }

        _output.WriteLine($"{'-',-60}");
    }

    private void AnalyzeDifferences(List<PositionComparison> comparisons)
    {
        if (!comparisons.Any()) return;

        var avgDeltaLeft = comparisons.Average(c => c.DeltaLeft);
        var avgDeltaRight = comparisons.Average(c => c.DeltaRight);
        var avgDeltaBottom = comparisons.Average(c => c.DeltaBottom);
        var avgDeltaTop = comparisons.Average(c => c.DeltaTop);
        var maxDeltaOverall = comparisons.Max(c => c.MaxDelta);
        var significantDiffs = comparisons.Count(c => c.IsSignificantDifference);

        _output.WriteLine($"\nAnalysis:");
        _output.WriteLine($"  Average dLeft:   {avgDeltaLeft:F3} pt");
        _output.WriteLine($"  Average dRight:  {avgDeltaRight:F3} pt");
        _output.WriteLine($"  Average dBottom: {avgDeltaBottom:F3} pt");
        _output.WriteLine($"  Average dTop:    {avgDeltaTop:F3} pt");
        _output.WriteLine($"  Max Delta:       {maxDeltaOverall:F3} pt");
        _output.WriteLine($"  Significant:     {significantDiffs}/{comparisons.Count} letters ({(100.0 * significantDiffs / comparisons.Count):F1}%)");

        // Check for patterns
        var leftConsistent = comparisons.All(c => Math.Abs(c.DeltaLeft - avgDeltaLeft) < 0.1);
        var rightConsistent = comparisons.All(c => Math.Abs(c.DeltaRight - avgDeltaRight) < 0.1);

        _output.WriteLine($"\nPatterns:");
        _output.WriteLine($"  Left offset consistent:  {(leftConsistent ? "YES" : "NO")}");
        _output.WriteLine($"  Right offset consistent: {(rightConsistent ? "YES" : "NO")}");

        // Statistical analysis
        if (maxDeltaOverall < 1.0)
        {
            _output.WriteLine($"\n✅ EXCELLENT: Max delta < 1pt - positions are very close!");
        }
        else if (maxDeltaOverall < 5.0)
        {
            _output.WriteLine($"\n✓ GOOD: Max delta < 5pt - positions are reasonably close");
        }
        else if (maxDeltaOverall < 10.0)
        {
            _output.WriteLine($"\n⚠ MODERATE: Max delta < 10pt - noticeable differences");
        }
        else
        {
            _output.WriteLine($"\n❌ SIGNIFICANT: Max delta ≥ 10pt - major position discrepancies");
        }
    }

    #endregion
}
