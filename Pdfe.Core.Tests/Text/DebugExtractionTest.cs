using System;
using System.IO;
using System.Linq;
using Pdfe.Core.Document;
using Xunit;
using Xunit.Abstractions;

namespace Pdfe.Core.Tests.Text;

/// <summary>
/// Debug test to understand why Pdfe.Core isn't extracting text from real PDFs.
/// </summary>
public class DebugExtractionTest
{
    private readonly ITestOutputHelper _output;

    public DebugExtractionTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void Debug_VeraPDF_TextExtraction()
    {
        var pdfPath = "/home/marc/Projects/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_UA-1/7.4 Headings/7.4.4 Unnumbered headings/7.4.4-t03-fail-a.pdf";

        Skip.IfNot(File.Exists(pdfPath), $"PDF not found: {pdfPath}");

        _output.WriteLine($"Testing: {Path.GetFileName(pdfPath)}");

        try
        {
            using var stream = File.OpenRead(pdfPath);
            using var doc = PdfDocument.Open(stream);

            _output.WriteLine($"✓ Document opened: {doc.PageCount} pages");

            var page = doc.GetPage(1);
            _output.WriteLine($"✓ Page 1: {page.Width:F2}x{page.Height:F2} pts");

            // Try Text property
            var text = page.Text;
            _output.WriteLine($"\nPage.Text length: {text.Length}");
            if (text.Length > 0)
            {
                _output.WriteLine($"First 200 chars: '{text.Substring(0, Math.Min(200, text.Length))}'");
            }

            // Try Letters property
            var letters = page.Letters;
            _output.WriteLine($"\nPage.Letters count: {letters.Count}");
            if (letters.Count > 0)
            {
                _output.WriteLine("\nFirst 10 letters:");
                for (int i = 0; i < Math.Min(10, letters.Count); i++)
                {
                    var letter = letters[i];
                    _output.WriteLine($"  [{i}] '{letter.Value}' at ({letter.GlyphRectangle.Left:F2}, {letter.GlyphRectangle.Bottom:F2})");
                }
            }
            else
            {
                _output.WriteLine("⚠ No letters extracted!");
            }

            // Try GetWords
            var words = page.GetWords();
            _output.WriteLine($"\nPage.GetWords() count: {words.Count}");
            if (words.Count > 0)
            {
                _output.WriteLine("\nFirst 5 words:");
                for (int i = 0; i < Math.Min(5, words.Count); i++)
                {
                    var word = words[i];
                    _output.WriteLine($"  [{i}] Text='{word.Text}', Letters={word.Letters.Count}");
                }
            }

            // Check content stream
            var contentBytes = page.GetContentStreamBytes();
            _output.WriteLine($"\nContent stream size: {contentBytes.Length} bytes");
            if (contentBytes.Length > 0)
            {
                var contentPreview = System.Text.Encoding.ASCII.GetString(contentBytes.Take(200).ToArray());
                _output.WriteLine($"Content preview: {contentPreview}");
            }

            // Check fonts
            var fonts = page.GetFonts().ToList();
            _output.WriteLine($"\nFonts on page: {fonts.Count}");
            foreach (var (name, font) in fonts)
            {
                _output.WriteLine($"  Font: {name}");
                if (font.ContainsKey("BaseFont"))
                {
                    _output.WriteLine($"    BaseFont: {font.GetNameOrNull("BaseFont")}");
                }
                if (font.ContainsKey("Subtype"))
                {
                    _output.WriteLine($"    Subtype: {font.GetNameOrNull("Subtype")}");
                }
                // Check if font has ToUnicode
                if (font.ContainsKey("ToUnicode"))
                {
                    _output.WriteLine($"    ✓ Has ToUnicode CMap");
                }
                else
                {
                    _output.WriteLine($"    ⚠ No ToUnicode CMap");
                }
            }

            // Try direct TextExtractor to see if it catches errors
            _output.WriteLine($"\n=== Testing TextExtractor directly ===");
            try
            {
                var extractor = new Pdfe.Core.Text.TextExtractor(page);
                var directText = extractor.ExtractText();
                _output.WriteLine($"Direct extraction result: '{directText}'");
                _output.WriteLine($"Length: {directText.Length}");
            }
            catch (Exception extractEx)
            {
                _output.WriteLine($"❌ TextExtractor error: {extractEx.Message}");
                _output.WriteLine($"Stack: {extractEx.StackTrace}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"\n❌ ERROR: {ex.Message}");
            _output.WriteLine($"Type: {ex.GetType().Name}");
            _output.WriteLine($"Stack:\n{ex.StackTrace}");

            if (ex.InnerException != null)
            {
                _output.WriteLine($"\nInner exception: {ex.InnerException.Message}");
            }
        }
    }

    [SkippableFact]
    public void Debug_Compare_PdfPig_vs_PdfeCore()
    {
        var pdfPath = "/home/marc/Projects/pdfe/test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_UA-1/7.4 Headings/7.4.4 Unnumbered headings/7.4.4-t03-fail-a.pdf";

        Skip.IfNot(File.Exists(pdfPath), $"PDF not found: {pdfPath}");

        _output.WriteLine("=== PdfPig Extraction ===");
        try
        {
            using var pigDoc = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var pigPage = pigDoc.GetPage(1);
            _output.WriteLine($"Page size: {pigPage.Width:F2}x{pigPage.Height:F2}");
            _output.WriteLine($"Letters: {pigPage.Letters.Count}");
            _output.WriteLine($"Words: {pigPage.GetWords().Count()}");

            if (pigPage.Letters.Any())
            {
                var first = pigPage.Letters.First();
                _output.WriteLine($"First letter: '{first.Value}' at ({first.GlyphRectangle.Left:F2}, {first.GlyphRectangle.Bottom:F2})");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"ERROR: {ex.Message}");
        }

        _output.WriteLine("\n=== Pdfe.Core Extraction ===");
        try
        {
            using var stream = File.OpenRead(pdfPath);
            using var coreDoc = PdfDocument.Open(stream);
            var corePage = coreDoc.GetPage(1);
            _output.WriteLine($"Page size: {corePage.Width:F2}x{corePage.Height:F2}");
            _output.WriteLine($"Letters: {corePage.Letters.Count}");
            _output.WriteLine($"Words: {corePage.GetWords().Count}");
            _output.WriteLine($"Text length: {corePage.Text.Length}");

            if (corePage.Letters.Any())
            {
                var first = corePage.Letters.First();
                _output.WriteLine($"First letter: '{first.Value}' at ({first.GlyphRectangle.Left:F2}, {first.GlyphRectangle.Bottom:F2})");
            }
            else
            {
                _output.WriteLine("⚠ No letters extracted by Pdfe.Core!");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"ERROR: {ex.Message}");
            if (ex.InnerException != null)
            {
                _output.WriteLine($"Inner: {ex.InnerException.Message}");
            }
        }
    }
}
