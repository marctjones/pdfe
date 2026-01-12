using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Pdfe.Core.Document;
using Pdfe.Core.Primitives;
using Pdfe.Core.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;
using Xunit.Abstractions;

namespace Pdfe.Core.Tests.Text;

/// <summary>
/// Diagnostic tests to compare letter position calculations between PdfPig and Pdfe.Core.
/// Issue #306: CharacterLevelSelectionTests failures due to position misalignment.
/// </summary>
public class PositionComparisonTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();

    public PositionComparisonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    #region Data Models

    public class LetterPosition
    {
        public string Character { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // "PdfPig" or "Pdfe.Core"
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

    #region Test Scenarios

    [Fact]
    public void Compare_SimpleWord_Arial()
    {
        // Test: "Hello" in Arial 12pt at (100, 700)
        var pdfPath = CreateSimpleTextPdf("Hello", "Arial", 12, 100, 700);

        var comparison = ComparePositions(pdfPath, "Hello");

        LogComparison(comparison, "Simple_Word_Arial");
        AnalyzeDifferences(comparison);
    }

    [Fact]
    public void Compare_Sentence_TimesRoman()
    {
        // Test: "The quick brown fox" in Times-Roman 12pt at (100, 700)
        var pdfPath = CreateSimpleTextPdf("The quick brown fox", "Times-Roman", 12, 100, 700);

        var comparison = ComparePositions(pdfPath, "The quick brown fox");

        LogComparison(comparison, "Sentence_TimesRoman");
        AnalyzeDifferences(comparison);
    }

    [Fact]
    public void Compare_DifferentFonts()
    {
        // Test same text with different fonts
        var fonts = new[] { "Arial", "Times-Roman", "Courier", "Helvetica" };
        var text = "Test";

        foreach (var font in fonts)
        {
            var pdfPath = CreateSimpleTextPdf(text, font, 12, 100, 700);
            var comparison = ComparePositions(pdfPath, text);

            _output.WriteLine($"\n=== Font: {font} ===");
            LogComparison(comparison, $"Font_{font}");
        }
    }

    [Fact]
    public void Compare_DifferentSizes()
    {
        // Test same text with different font sizes
        var sizes = new[] { 8, 10, 12, 14, 18, 24, 36 };
        var text = "Size";

        foreach (var size in sizes)
        {
            var pdfPath = CreateSimpleTextPdf(text, "Arial", size, 100, 700);
            var comparison = ComparePositions(pdfPath, text);

            _output.WriteLine($"\n=== Size: {size}pt ===");
            LogComparison(comparison, $"Size_{size}pt");
        }
    }

    [Fact]
    public void Compare_SpecialCharacters()
    {
        // Test with different character types
        var testStrings = new[]
        {
            "ABC",           // Uppercase
            "xyz",           // Lowercase
            "123",           // Numbers
            "!@#$",          // Punctuation
            "AaBbCc",        // Mixed case
        };

        foreach (var text in testStrings)
        {
            var pdfPath = CreateSimpleTextPdf(text, "Arial", 12, 100, 700);
            var comparison = ComparePositions(pdfPath, text);

            _output.WriteLine($"\n=== Text: '{text}' ===");
            LogComparison(comparison, $"Chars_{text}");
        }
    }

    [Fact]
    public void Compare_DifferentPositions()
    {
        // Test same text at different positions on page
        var positions = new[] { (50, 750), (100, 700), (200, 500), (400, 300) };
        var text = "Pos";

        foreach (var (x, y) in positions)
        {
            var pdfPath = CreateSimpleTextPdf(text, "Arial", 12, x, y);
            var comparison = ComparePositions(pdfPath, text);

            _output.WriteLine($"\n=== Position: ({x}, {y}) ===");
            LogComparison(comparison, $"Pos_{x}_{y}");
        }
    }

    [Fact]
    public void Compare_MultiLine()
    {
        // Test multi-line text to check vertical positioning
        var pdfPath = CreateMultiLineTextPdf();

        // Compare each line
        var line1 = ComparePositions(pdfPath, "Line 1");
        var line2 = ComparePositions(pdfPath, "Line 2");
        var line3 = ComparePositions(pdfPath, "Line 3");

        _output.WriteLine("\n=== Line 1 ===");
        LogComparison(line1, "MultiLine_Line1");

        _output.WriteLine("\n=== Line 2 ===");
        LogComparison(line2, "MultiLine_Line2");

        _output.WriteLine("\n=== Line 3 ===");
        LogComparison(line3, "MultiLine_Line3");
    }

    #endregion

    #region Position Extraction

    private List<PositionComparison> ComparePositions(string pdfPath, string expectedText)
    {
        var pdfPigPositions = ExtractPositions_PdfPig(pdfPath, expectedText);
        var pdfeCorePositions = ExtractPositions_PdfeCore(pdfPath, expectedText);

        var comparisons = new List<PositionComparison>();

        // Match letters by index
        for (int i = 0; i < Math.Min(pdfPigPositions.Count, pdfeCorePositions.Count); i++)
        {
            var pigPos = pdfPigPositions[i];
            var corePos = pdfeCorePositions[i];

            comparisons.Add(new PositionComparison
            {
                Character = pigPos.Character[0],
                Index = i,
                PdfPig = pigPos,
                PdfeCore = corePos
            });
        }

        return comparisons;
    }

    private List<LetterPosition> ExtractPositions_PdfPig(string pdfPath, string searchText)
    {
        var positions = new List<LetterPosition>();

        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = document.GetPage(1);

        foreach (var word in page.GetWords())
        {
            if (word.Text.Contains(searchText))
            {
                foreach (var letter in word.Letters)
                {
                    if (searchText.Contains(letter.Value))
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
            }
        }

        return positions;
    }

    private List<LetterPosition> ExtractPositions_PdfeCore(string pdfPath, string searchText)
    {
        var positions = new List<LetterPosition>();

        using var stream = File.OpenRead(pdfPath);
        using var document = Pdfe.Core.Document.PdfDocument.Open(stream);
        var page = document.GetPage(1);

        foreach (var letter in page.Letters)
        {
            if (searchText.Contains(letter.Value))
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

        return positions;
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

        foreach (var comp in comparisons)
        {
            var status = comp.IsSignificantDifference ? "⚠ DIFF" : "✓ OK";
            _output.WriteLine(
                $"{comp.Index,-4} {comp.Character,-5} " +
                $"{comp.DeltaLeft,-8:F2} {comp.DeltaRight,-8:F2} " +
                $"{comp.DeltaBottom,-8:F2} {comp.DeltaTop,-8:F2} " +
                $"{comp.MaxDelta,-8:F2} {status,-10}");
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
        _output.WriteLine($"  Significant:     {significantDiffs}/{comparisons.Count} letters");

        // Check for patterns
        var leftConsistent = comparisons.All(c => Math.Abs(c.DeltaLeft - avgDeltaLeft) < 0.1);
        var rightConsistent = comparisons.All(c => Math.Abs(c.DeltaRight - avgDeltaRight) < 0.1);

        _output.WriteLine($"\nPatterns:");
        _output.WriteLine($"  Left offset consistent:  {(leftConsistent ? "YES" : "NO")}");
        _output.WriteLine($"  Right offset consistent: {(rightConsistent ? "YES" : "NO")}");
    }

    #endregion

    #region PDF Creation Helpers

    private string CreateSimpleTextPdf(string text, string font, int fontSize, double x, double y)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"diagnostic_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(tempFile);

        // Create PDF using PdfSharpCore (same as tests)
        var document = new PdfSharpCore.Pdf.PdfDocument();
        var page = document.AddPage();
        page.Width = PdfSharpCore.Drawing.XUnit.FromPoint(612);  // Letter size
        page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(792);

        using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
        var pdfFont = new PdfSharpCore.Drawing.XFont(font, fontSize);
        gfx.DrawString(text, pdfFont, PdfSharpCore.Drawing.XBrushes.Black, x, y);

        document.Save(tempFile);

        return tempFile;
    }

    private string CreateMultiLineTextPdf()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"diagnostic_multiline_{Guid.NewGuid()}.pdf");
        _tempFiles.Add(tempFile);

        var document = new PdfSharpCore.Pdf.PdfDocument();
        var page = document.AddPage();
        page.Width = PdfSharpCore.Drawing.XUnit.FromPoint(612);
        page.Height = PdfSharpCore.Drawing.XUnit.FromPoint(792);

        using var gfx = PdfSharpCore.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharpCore.Drawing.XFont("Arial", 12);

        gfx.DrawString("Line 1", font, PdfSharpCore.Drawing.XBrushes.Black, 100, 700);
        gfx.DrawString("Line 2", font, PdfSharpCore.Drawing.XBrushes.Black, 100, 680);
        gfx.DrawString("Line 3", font, PdfSharpCore.Drawing.XBrushes.Black, 100, 660);

        document.Save(tempFile);

        return tempFile;
    }

    #endregion
}
