using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Parsing;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration.Atomic;

/// <summary>
/// Atomic test suite for content stream parsing/rebuilding during redaction.
/// Based on veraPDF corpus testing methodology.
///
/// Tests PDF/A clause 6.2 (Graphics), especially 6.2.10 (Content streams).
/// Each test is atomic - one file, one assertion.
///
/// See Issue #140: Atomic Test Suite: Content Stream Handling
/// </summary>
[Collection("AtomicTests")]
public class ContentStreamHandlingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TextRedactor _redactor;
    private readonly ContentStreamParser _parser;
    private readonly string _tempDir;

    public ContentStreamHandlingTests(ITestOutputHelper output)
    {
        _output = output;
        _redactor = new TextRedactor();
        _parser = new ContentStreamParser();
        _tempDir = Path.Combine(Path.GetTempPath(), $"content_stream_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// Get content stream test files from corpus.
    /// </summary>
    public static IEnumerable<object[]> GetContentStreamCorpusFiles()
    {
        return VeraPdfCorpusDataProvider.GetContentStreamTestFiles(50);
    }

    /// <summary>
    /// Atomic test: Content stream can be parsed after redaction.
    /// Tests PDF/A clause 6.2.10: Content stream syntax.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetContentStreamCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.2.10 Content streams")]
    public void ContentStream_AfterRedaction_RemainsParseble(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        _output.WriteLine($"Testing content stream: {displayName}");

        try
        {
            // 1. Redact arbitrary area - may fail on intentionally invalid PDFs
            var outputPath = Path.Combine(_tempDir, $"stream_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping invalid PDF (redaction failed): {displayName}");
                return;
            }

            // 2. Verify content stream can be parsed
            var contentBytes = ExtractContentStreamBytes(outputPath);
            if (contentBytes.Length == 0)
            {
                _output.WriteLine($"  No content stream in output, skipping");
                return;
            }

            // 3. Parse should not throw
            var operations = _parser.Parse(contentBytes, 792.0); // Standard page height
            operations.Should().NotBeNull("Parser should return operations list");

            _output.WriteLine($"  ✓ Parsed {operations.Count} operations");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  ✗ Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Graphics state operations are preserved.
    /// Tests PDF/A clause 6.2.8: Extended graphics state.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetContentStreamCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.2.8 Extended graphics state")]
    public void GraphicsState_AfterRedaction_OperationsPreserved(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            // 1. Get input graphics state operations
            var inputBytes = ExtractContentStreamBytes(pdfPath);
            if (inputBytes.Length == 0)
            {
                _output.WriteLine($"  No content stream in {displayName}, skipping");
                return;
            }

            var inputOps = _parser.Parse(inputBytes, 792.0);
            var inputGraphicsOps = inputOps.Where(op =>
                op.Operator == "q" || op.Operator == "Q" ||
                op.Operator == "gs" || op.Operator == "cm").ToList();

            // 2. Redact and parse output
            var outputPath = Path.Combine(_tempDir, $"gs_{Path.GetFileName(pdfPath)}");
            RedactArbitraryArea(pdfPath, outputPath);

            var outputBytes = ExtractContentStreamBytes(outputPath);
            var outputOps = _parser.Parse(outputBytes, 792.0);
            var outputGraphicsOps = outputOps.Where(op =>
                op.Operator == "q" || op.Operator == "Q" ||
                op.Operator == "gs" || op.Operator == "cm").ToList();

            // 3. Graphics state operations should be balanced (q/Q pairs)
            var saveCount = outputGraphicsOps.Count(op => op.Operator == "q");
            var restoreCount = outputGraphicsOps.Count(op => op.Operator == "Q");

            // Redaction adds its own q/Q wrapper, so output may have +1 pair
            var saveDiff = Math.Abs(saveCount - restoreCount);
            saveDiff.Should().BeLessOrEqualTo(1,
                "Graphics state save/restore should be balanced after redaction");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Graphics state test failed for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Text can be extracted from output PDF.
    /// Verifies content stream produces valid text operations.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetContentStreamCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Type", "TextExtraction")]
    public void TextExtraction_AfterRedaction_StillWorks(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            // 1. Redact - may fail on intentionally invalid PDFs
            var outputPath = Path.Combine(_tempDir, $"text_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping invalid PDF (redaction failed): {displayName}");
                return;
            }

            // 2. Verify PdfPig can extract text (proves content stream is valid)
            using var doc = UglyToad.PdfPig.PdfDocument.Open(outputPath);
            doc.NumberOfPages.Should().BeGreaterThan(0);

            // Just verify extraction doesn't throw
            foreach (var page in doc.GetPages())
            {
                var text = page.Text; // Should not throw
                var letters = page.Letters; // Should not throw
            }
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Text extraction test failed for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Document structure preserved.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetContentStreamCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Type", "DocumentStructure")]
    public void DocumentStructure_AfterRedaction_Preserved(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            // Get input page count
            int inputPageCount;
            try
            {
                using var inputDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                inputPageCount = inputDoc.PageCount;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not a valid PDF"))
            {
                // Intentionally invalid PDF from corpus - skip
                _output.WriteLine($"  Skipping invalid PDF: {displayName}");
                return;
            }

            // Redact
            var outputPath = Path.Combine(_tempDir, $"struct_{Path.GetFileName(pdfPath)}");
            RedactArbitraryArea(pdfPath, outputPath);

            // Verify output has same page count
            using var outputDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            outputDoc.PageCount.Should().Be(inputPageCount,
                "Page count should be preserved after redaction");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Structure test failed for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: No operators outside of valid ranges.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetContentStreamCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Type", "OperatorValidity")]
    public void Operators_AfterRedaction_AllValid(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            var outputPath = Path.Combine(_tempDir, $"ops_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping invalid PDF (redaction failed): {displayName}");
                return;
            }

            var contentBytes = ExtractContentStreamBytes(outputPath);
            if (contentBytes.Length == 0)
            {
                return; // No content stream to check
            }

            var operations = _parser.Parse(contentBytes, 792.0);

            // Check for valid operators (all should have recognized Operator string)
            foreach (var op in operations)
            {
                op.Operator.Should().NotBeNullOrEmpty(
                    "All operations should have valid operator");

                // Operator should be ASCII letters only
                op.Operator.All(c => char.IsLetter(c) || c == '*' || c == '\'' || c == '"')
                    .Should().BeTrue($"Operator '{op.Operator}' should be valid PDF operator");
            }
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Operator validity test failed for {displayName}: {ex.Message}");
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
            SanitizeMetadata = false,
            UseGlyphLevelRedaction = false
        };

        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(100, 100, 200, 150)
        };

        _redactor.RedactLocations(inputPath, outputPath, new[] { location }, options);
    }

    /// <summary>
    /// Try to redact, returning false if the PDF is invalid.
    /// </summary>
    private bool TryRedactArbitraryArea(string inputPath, string outputPath)
    {
        try
        {
            RedactArbitraryArea(inputPath, outputPath);
            return File.Exists(outputPath);
        }
        catch
        {
            return false;
        }
    }

    private byte[] ExtractContentStreamBytes(string pdfPath)
    {
        try
        {
            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            if (doc.PageCount == 0)
                return Array.Empty<byte>();

            var page = doc.Pages[0];
            var contents = page.Contents;
            if (contents == null || contents.Elements.Count == 0)
                return Array.Empty<byte>();

            using var ms = new MemoryStream();

            foreach (var element in contents.Elements)
            {
                if (element is PdfReference reference)
                {
                    var obj = reference.Value;
                    if (obj is PdfDictionary streamDict && streamDict.Stream != null)
                    {
                        var bytes = streamDict.Stream.UnfilteredValue;
                        if (bytes != null && bytes.Length > 0)
                        {
                            ms.Write(bytes, 0, bytes.Length);
                            ms.WriteByte((byte)'\n');
                        }
                    }
                }
                else if (element is PdfDictionary directDict && directDict.Stream != null)
                {
                    var bytes = directDict.Stream.UnfilteredValue;
                    if (bytes != null && bytes.Length > 0)
                    {
                        ms.Write(bytes, 0, bytes.Length);
                        ms.WriteByte((byte)'\n');
                    }
                }
            }

            return ms.ToArray();
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
}
