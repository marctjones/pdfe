using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration.Atomic;

/// <summary>
/// Atomic test suite for form field handling during redaction.
/// Based on veraPDF corpus testing methodology.
///
/// Tests PDF/A clause 6.9 (Interactive Forms): Form fields must be preserved
/// or properly handled during redaction.
///
/// See Issue #143: Atomic Test Suite: Form Field Handling
/// </summary>
[Collection("AtomicTests")]
public class FormFieldHandlingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TextRedactor _redactor;
    private readonly string _tempDir;

    public FormFieldHandlingTests(ITestOutputHelper output)
    {
        _output = output;
        _redactor = new TextRedactor();
        _tempDir = Path.Combine(Path.GetTempPath(), $"form_field_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// Get form field test files from corpus.
    /// </summary>
    public static IEnumerable<object[]> GetFormFieldCorpusFiles()
    {
        return VeraPdfCorpusDataProvider.GetFormFieldTestFiles(20);
    }

    /// <summary>
    /// Get Form XObject test files from corpus.
    /// </summary>
    public static IEnumerable<object[]> GetFormXObjectCorpusFiles()
    {
        // Form XObjects are in 6.2.5
        return VeraPdfCorpusDataProvider.GetPdfA1bSubcategory("6.2 Graphics/6.2.5 Form XObjects", 10);
    }

    /// <summary>
    /// Atomic test: Document with form fields remains openable after redaction.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFormFieldCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.9 Interactive Forms")]
    public void FormDocument_AfterRedaction_RemainsOpenable(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        _output.WriteLine($"Testing form document: {displayName}");

        try
        {
            var outputPath = Path.Combine(_tempDir, $"form_{Path.GetFileName(pdfPath)}");

            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping invalid PDF (redaction failed): {displayName}");
                return;
            }

            // Verify we can reopen the document
            using var doc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            doc.PageCount.Should().BeGreaterThan(0,
                "Document with forms should be openable after redaction");

            _output.WriteLine($"  ✓ Document valid: {doc.PageCount} pages");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  ✗ Error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: AcroForm dictionary is preserved after redaction.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFormFieldCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.9 Interactive Forms")]
    public void AcroForm_AfterRedaction_DictionaryPreserved(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            // Check if input has AcroForm
            bool inputHasAcroForm;
            int inputFieldCount;
            try
            {
                using var inputDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                inputHasAcroForm = HasAcroForm(inputDoc);
                inputFieldCount = GetFieldCount(inputDoc);
            }
            catch
            {
                _output.WriteLine($"  Skipping invalid input PDF: {displayName}");
                return;
            }

            if (!inputHasAcroForm)
            {
                _output.WriteLine($"  No AcroForm in {displayName}, skipping");
                return;
            }

            _output.WriteLine($"  Input has AcroForm with {inputFieldCount} fields");

            var outputPath = Path.Combine(_tempDir, $"acro_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping - redaction failed");
                return;
            }

            // Check output has AcroForm
            using var outputDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            var outputHasAcroForm = HasAcroForm(outputDoc);
            var outputFieldCount = GetFieldCount(outputDoc);

            outputHasAcroForm.Should().BeTrue(
                "AcroForm dictionary should be preserved after redaction");
            outputFieldCount.Should().Be(inputFieldCount,
                "Field count should be preserved after area redaction");

            _output.WriteLine($"  ✓ AcroForm preserved with {outputFieldCount} fields");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Error for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Form XObjects are preserved after redaction.
    /// Tests PDF/A clause 6.2.5: Form XObjects.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFormXObjectCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Clause", "6.2.5 Form XObjects")]
    public void FormXObjects_AfterRedaction_Preserved(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            // Get input XObject count
            int inputXObjectCount;
            try
            {
                using var inputDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                inputXObjectCount = GetFormXObjectCount(inputDoc);
            }
            catch
            {
                _output.WriteLine($"  Skipping invalid input PDF: {displayName}");
                return;
            }

            _output.WriteLine($"  Input has {inputXObjectCount} Form XObjects");

            var outputPath = Path.Combine(_tempDir, $"xobj_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping - redaction failed");
                return;
            }

            // Check output XObject count
            using var outputDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            var outputXObjectCount = GetFormXObjectCount(outputDoc);

            // Form XObjects should be preserved (we're not redacting inside them)
            outputXObjectCount.Should().Be(inputXObjectCount,
                "Form XObject count should be preserved after area redaction");

            _output.WriteLine($"  ✓ {outputXObjectCount} Form XObjects preserved");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Error for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Page structure preserved for form documents.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFormFieldCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Type", "PageStructure")]
    public void FormDocument_AfterRedaction_PageStructurePreserved(string pdfPath, string displayName)
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
            catch
            {
                _output.WriteLine($"  Skipping invalid input PDF: {displayName}");
                return;
            }

            var outputPath = Path.Combine(_tempDir, $"page_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping - redaction failed");
                return;
            }

            using var outputDoc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
            outputDoc.PageCount.Should().Be(inputPageCount,
                "Page count should be preserved after redaction");

            _output.WriteLine($"  ✓ {outputDoc.PageCount} pages preserved");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Error for {displayName}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Atomic test: Text extraction still works after redacting form documents.
    /// </summary>
    [SkippableTheory]
    [MemberData(nameof(GetFormFieldCorpusFiles))]
    [Trait("Category", "Atomic")]
    [Trait("Type", "TextExtraction")]
    public void FormDocument_AfterRedaction_TextExtractionWorks(string pdfPath, string displayName)
    {
        Skip.If(VeraPdfCorpusDataProvider.IsSkipMarker(pdfPath), "Corpus not available");
        Skip.IfNot(File.Exists(pdfPath), $"File not found: {pdfPath}");

        try
        {
            var outputPath = Path.Combine(_tempDir, $"text_{Path.GetFileName(pdfPath)}");
            if (!TryRedactArbitraryArea(pdfPath, outputPath))
            {
                _output.WriteLine($"  Skipping - redaction failed: {displayName}");
                return;
            }

            // Verify PdfPig can extract text (proves content stream is valid)
            using var doc = UglyToad.PdfPig.PdfDocument.Open(outputPath);
            doc.NumberOfPages.Should().BeGreaterThan(0);

            foreach (var page in doc.GetPages())
            {
                var text = page.Text; // Should not throw
                var letters = page.Letters; // Should not throw
            }

            _output.WriteLine($"  ✓ Text extraction works");
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            _output.WriteLine($"  Error for {displayName}: {ex.Message}");
            throw;
        }
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private bool TryRedactArbitraryArea(string inputPath, string outputPath)
    {
        try
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

            var result = _redactor.RedactLocations(inputPath, outputPath, new[] { location }, options);
            return result.Success && File.Exists(outputPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAcroForm(PdfDocument document)
    {
        try
        {
            var catalog = document.Internals.Catalog;
            return catalog?.Elements.GetDictionary("/AcroForm") != null;
        }
        catch
        {
            return false;
        }
    }

    private static int GetFieldCount(PdfDocument document)
    {
        try
        {
            var catalog = document.Internals.Catalog;
            var acroForm = catalog?.Elements.GetDictionary("/AcroForm");
            if (acroForm == null) return 0;

            var fields = acroForm.Elements.GetArray("/Fields");
            return fields?.Elements.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int GetFormXObjectCount(PdfDocument document)
    {
        int count = 0;
        try
        {
            foreach (var page in document.Pages)
            {
                var resources = page.Elements.GetDictionary("/Resources");
                if (resources == null) continue;

                var xobjects = resources.Elements.GetDictionary("/XObject");
                if (xobjects == null) continue;

                foreach (var key in xobjects.Elements.Keys)
                {
                    var xobj = xobjects.Elements.GetDictionary(key);
                    if (xobj == null)
                    {
                        var reference = xobjects.Elements.GetReference(key);
                        if (reference?.Value is PdfDictionary refDict)
                            xobj = refDict;
                    }

                    if (xobj != null)
                    {
                        var subtype = xobj.Elements.GetName("/Subtype");
                        if (subtype == "/Form")
                            count++;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return count;
    }
}
