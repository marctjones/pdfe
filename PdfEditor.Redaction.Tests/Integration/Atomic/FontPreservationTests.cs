using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration.Atomic;

/// <summary>
/// Atomic test suite for font preservation during redaction.
/// Based on veraPDF corpus testing methodology.
///
/// Tests PDF/A clause 6.3 (Fonts): Font information must be preserved during redaction.
/// Each test is atomic - one file, one assertion.
///
/// See Issue #139: Atomic Test Suite: Font Preservation
/// </summary>
[Collection("AtomicTests")]
public class FontPreservationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TextRedactor _redactor;
    private readonly string _tempDir;

    public FontPreservationTests(ITestOutputHelper output)
    {
        _output = output;
        _redactor = new TextRedactor();
        _tempDir = Path.Combine(Path.GetTempPath(), $"font_preservation_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// Get font test files from corpus.
    /// </summary>
    public static IEnumerable<object[]> GetFontCorpusFiles()
    {
        return VeraPdfCorpusDataProvider.GetFontTestFiles(50);
    }

    /// <summary>
    /// Atomic test: Redaction preserves font dictionaries.
    /// Tests PDF/A clause 6.3: Font information must be preserved.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFontCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.3 Fonts")]
    public void FontPreservation_AfterRedaction_FontDictionariesPreserved(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        _output.WriteLine($"Testing font preservation: {displayName}");

        try
        {
            // 1. Get baseline font information
            var inputFonts = ExtractFontInfo(pdfPath);
            _output.WriteLine($"  Input fonts: {inputFonts.Count}");

            if (inputFonts.Count == 0)
            {
                _output.WriteLine("  No fonts found, skipping font preservation check");
                return;
            }

            // 2. Redact arbitrary area (doesn't matter if there's text)
            var outputPath = Path.Combine(_tempDir, $"redacted_{Path.GetFileName(pdfPath)}");
            RedactArbitraryArea(pdfPath, outputPath);

            // 3. Verify atomic requirement: Font dictionaries preserved
            var outputFonts = ExtractFontInfo(outputPath);
            _output.WriteLine($"  Output fonts: {outputFonts.Count}");

            // Font count should be preserved
            outputFonts.Count.Should().Be(inputFonts.Count,
                "Redaction must preserve font count (PDF/A clause 6.3)");

            // Each font's key properties should be preserved
            foreach (var (fontName, inputFont) in inputFonts)
            {
                outputFonts.Should().ContainKey(fontName,
                    $"Font {fontName} should be preserved after redaction");

                if (outputFonts.TryGetValue(fontName, out var outputFont))
                {
                    // Check key font properties
                    outputFont.Type.Should().Be(inputFont.Type,
                        $"Font {fontName} type should be preserved");
                    outputFont.Subtype.Should().Be(inputFont.Subtype,
                        $"Font {fontName} subtype should be preserved");
                    outputFont.BaseFont.Should().Be(inputFont.BaseFont,
                        $"Font {fontName} base font should be preserved");
                }
            }

            _output.WriteLine($"  ✓ All fonts preserved");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  ✗ Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Font encoding is preserved after redaction.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFontCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.3.2 Encoding")]
    public void FontEncoding_AfterRedaction_EncodingPreserved(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            var inputFonts = ExtractFontInfo(pdfPath);
            if (inputFonts.Count == 0)
            {
                _output.WriteLine($"  No fonts in {displayName}, skipping");
                return;
            }

            var outputPath = Path.Combine(_tempDir, $"encoding_{Path.GetFileName(pdfPath)}");
            RedactArbitraryArea(pdfPath, outputPath);

            var outputFonts = ExtractFontInfo(outputPath);

            foreach (var (fontName, inputFont) in inputFonts)
            {
                if (outputFonts.TryGetValue(fontName, out var outputFont))
                {
                    outputFont.Encoding.Should().Be(inputFont.Encoding,
                        $"Font {fontName} encoding should be preserved");
                }
            }
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Encoding test failed for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Document remains valid after redaction.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFontCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Type", "DocumentValidity")]
    public void DocumentValidity_AfterRedaction_RemainsOpenable(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            var outputPath = Path.Combine(_tempDir, $"valid_{Path.GetFileName(pdfPath)}");
            RedactArbitraryArea(pdfPath, outputPath);

            // Verify we can reopen the document
            using var doc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            doc.PageCount.Should().BeGreaterThan(0, "Document should have pages after redaction");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Validity test failed for {displayName}: {ex.Message}");
            throw;
        }
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private void RedactArbitraryArea(string inputPath, string outputPath)
    {
        var options = new RedactionOptions
        {
            DrawVisualMarker = true,
            SanitizeMetadata = false,  // Preserve metadata for comparison
            UseGlyphLevelRedaction = false  // Use simple redaction for speed
        };

        // Redact a small arbitrary area
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(100, 100, 200, 150)
        };

        _redactor.RedactLocations(inputPath, outputPath, new[] { location }, options);
    }

    private Dictionary<string, FontInfo> ExtractFontInfo(string pdfPath)
    {
        var fonts = new Dictionary<string, FontInfo>();

        try
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);

            foreach (var page in doc.Pages)
            {
                var resources = page.Elements.GetDictionary("/Resources");
                if (resources == null) continue;

                var fontDict = resources.Elements.GetDictionary("/Font");
                if (fontDict == null) continue;

                foreach (var key in fontDict.Elements.Keys)
                {
                    if (fonts.ContainsKey(key)) continue;

                    var fontRef = fontDict.Elements.GetDictionary(key);
                    if (fontRef == null)
                    {
                        var reference = fontDict.Elements.GetReference(key);
                        if (reference?.Value is PdfDictionary refDict)
                        {
                            fontRef = refDict;
                        }
                    }

                    if (fontRef != null)
                    {
                        fonts[key] = new FontInfo
                        {
                            Type = fontRef.Elements.GetName("/Type") ?? "",
                            Subtype = fontRef.Elements.GetName("/Subtype") ?? "",
                            BaseFont = fontRef.Elements.GetName("/BaseFont") ?? "",
                            Encoding = fontRef.Elements.GetName("/Encoding") ?? ""
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  Error extracting fonts: {ex.Message}");
        }

        return fonts;
    }

    private record FontInfo
    {
        public string Type { get; init; } = "";
        public string Subtype { get; init; } = "";
        public string BaseFont { get; init; } = "";
        public string Encoding { get; init; } = "";
    }
}
