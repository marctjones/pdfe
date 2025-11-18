using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using PdfSharp.Pdf.IO;
using System.Text;

namespace PdfEditor.Validator;

/// <summary>
/// Command-line tool to validate PDF redaction and detect content under black boxes
///
/// Usage:
///   PdfEditor.Validator analyze &lt;pdf-file&gt;           - Analyze PDF and show all content
///   PdfEditor.Validator extract-text &lt;pdf-file&gt;      - Extract all text from PDF
///   PdfEditor.Validator compare &lt;before.pdf&gt; &lt;after.pdf&gt; - Compare two PDFs
///   PdfEditor.Validator find-hidden &lt;pdf-file&gt;      - Find content that might be hidden
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return;
        }

        var command = args[0].ToLower();

        try
        {
            switch (command)
            {
                case "analyze":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: PDF file path required");
                        Console.WriteLine("Usage: PdfEditor.Validator analyze <pdf-file>");
                        return;
                    }
                    AnalyzePdf(args[1]);
                    break;

                case "extract-text":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: PDF file path required");
                        Console.WriteLine("Usage: PdfEditor.Validator extract-text <pdf-file>");
                        return;
                    }
                    ExtractText(args[1]);
                    break;

                case "compare":
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Error: Two PDF file paths required");
                        Console.WriteLine("Usage: PdfEditor.Validator compare <before.pdf> <after.pdf>");
                        return;
                    }
                    ComparePdfs(args[1], args[2]);
                    break;

                case "find-hidden":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: PDF file path required");
                        Console.WriteLine("Usage: PdfEditor.Validator find-hidden <pdf-file>");
                        return;
                    }
                    FindHiddenContent(args[1]);
                    break;

                case "content-stream":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: PDF file path required");
                        Console.WriteLine("Usage: PdfEditor.Validator content-stream <pdf-file>");
                        return;
                    }
                    ShowContentStream(args[1]);
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}");
                    ShowHelp();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (args.Contains("--verbose"))
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine(@"
PDF Redaction Validator - Detect and analyze PDF content

USAGE:
    PdfEditor.Validator <command> [options]

COMMANDS:
    analyze <pdf-file>
        Analyze PDF and show detailed content information including:
        - All text with positions
        - Page count
        - Content stream size

    extract-text <pdf-file>
        Extract all text from PDF (even if hidden under black boxes)
        Use this to verify if sensitive text was actually removed

    compare <before.pdf> <after.pdf>
        Compare two PDFs (before/after redaction) and show:
        - Text that was removed
        - Text that was preserved
        - Content differences

    find-hidden <pdf-file>
        Attempt to find content that might be hidden under other objects
        Looks for overlapping elements and covered text

    content-stream <pdf-file>
        Show raw PDF content stream (advanced)
        Useful for debugging redaction implementation

OPTIONS:
    --verbose       Show detailed error messages

EXAMPLES:
    # Extract all text from a redacted PDF
    PdfEditor.Validator extract-text redacted.pdf

    # Compare before and after redaction
    PdfEditor.Validator compare original.pdf redacted.pdf

    # Analyze a PDF in detail
    PdfEditor.Validator analyze document.pdf

    # Find potentially hidden content
    PdfEditor.Validator find-hidden suspicious.pdf

VALIDATION WORKFLOW:
    1. Run 'extract-text' on redacted PDF
    2. Search output for sensitive terms
    3. If found, redaction failed (text still in PDF structure)
    4. If not found, redaction succeeded
");
    }

    static void AnalyzePdf(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: File not found: {pdfPath}");
            return;
        }

        Console.WriteLine($"=== Analyzing PDF: {Path.GetFileName(pdfPath)} ===\n");

        using var document = PdfDocument.Open(pdfPath);

        Console.WriteLine($"Pages: {document.NumberOfPages}");
        Console.WriteLine($"PDF Version: {document.Version}");
        Console.WriteLine();

        for (int i = 0; i < document.NumberOfPages; i++)
        {
            var page = document.GetPage(i + 1);

            Console.WriteLine($"--- Page {i + 1} ---");
            Console.WriteLine($"Size: {page.Width} x {page.Height} points");

            var words = page.GetWords();
            Console.WriteLine($"Words found: {words.Count()}");

            if (words.Any())
            {
                Console.WriteLine("\nText content with positions:");
                foreach (var word in words.Take(50)) // Show first 50 words
                {
                    Console.WriteLine($"  \"{word.Text}\" at ({word.BoundingBox.Left:F1}, {word.BoundingBox.Bottom:F1})");
                }

                if (words.Count() > 50)
                {
                    Console.WriteLine($"  ... and {words.Count() - 50} more words");
                }
            }

            Console.WriteLine();
        }

        // Show content stream size
        using var pdfSharpDoc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        for (int i = 0; i < pdfSharpDoc.PageCount; i++)
        {
            var page = pdfSharpDoc.Pages[i];
            var contentStream = page.Contents.Elements.FirstOrDefault();

            if (contentStream is PdfSharp.Pdf.PdfDictionary dict && dict.Stream != null)
            {
                Console.WriteLine($"Page {i + 1} content stream: {dict.Stream.Value.Length} bytes");
            }
        }
    }

    static void ExtractText(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: File not found: {pdfPath}");
            return;
        }

        Console.WriteLine($"=== Extracting text from: {Path.GetFileName(pdfPath)} ===\n");

        using var document = PdfDocument.Open(pdfPath);

        var allText = new StringBuilder();

        for (int i = 0; i < document.NumberOfPages; i++)
        {
            var page = document.GetPage(i + 1);
            allText.AppendLine($"=== Page {i + 1} ===");
            allText.AppendLine(page.Text);
            allText.AppendLine();
        }

        var text = allText.ToString();
        Console.WriteLine(text);

        // Summary
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        Console.WriteLine($"\n=== Summary ===");
        Console.WriteLine($"Total characters: {text.Length}");
        Console.WriteLine($"Total words: {words.Length}");
        Console.WriteLine($"Total pages: {document.NumberOfPages}");
    }

    static void ComparePdfs(string beforePath, string afterPath)
    {
        if (!File.Exists(beforePath))
        {
            Console.WriteLine($"Error: File not found: {beforePath}");
            return;
        }

        if (!File.Exists(afterPath))
        {
            Console.WriteLine($"Error: File not found: {afterPath}");
            return;
        }

        Console.WriteLine($"=== Comparing PDFs ===");
        Console.WriteLine($"Before: {Path.GetFileName(beforePath)}");
        Console.WriteLine($"After:  {Path.GetFileName(afterPath)}");
        Console.WriteLine();

        // Extract text from both
        var textBefore = ExtractAllText(beforePath);
        var textAfter = ExtractAllText(afterPath);

        // Get unique words
        var wordsBefore = new HashSet<string>(
            textBefore.Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ':', ';' },
                StringSplitOptions.RemoveEmptyEntries)
        );

        var wordsAfter = new HashSet<string>(
            textAfter.Split(new[] { ' ', '\n', '\r', '\t', ',', '.', ':', ';' },
                StringSplitOptions.RemoveEmptyEntries)
        );

        // Find differences
        var removed = wordsBefore.Except(wordsAfter).ToList();
        var added = wordsAfter.Except(wordsBefore).ToList();
        var preserved = wordsBefore.Intersect(wordsAfter).ToList();

        Console.WriteLine($"=== Statistics ===");
        Console.WriteLine($"Words in BEFORE: {wordsBefore.Count}");
        Console.WriteLine($"Words in AFTER:  {wordsAfter.Count}");
        Console.WriteLine($"Words REMOVED:   {removed.Count}");
        Console.WriteLine($"Words ADDED:     {added.Count}");
        Console.WriteLine($"Words PRESERVED: {preserved.Count}");
        Console.WriteLine();

        if (removed.Any())
        {
            Console.WriteLine($"=== REMOVED Content ({removed.Count} unique words) ===");
            foreach (var word in removed.OrderBy(w => w).Take(50))
            {
                Console.WriteLine($"  - {word}");
            }
            if (removed.Count > 50)
            {
                Console.WriteLine($"  ... and {removed.Count - 50} more");
            }
            Console.WriteLine();
        }

        if (added.Any())
        {
            Console.WriteLine($"=== ADDED Content ({added.Count} unique words) ===");
            foreach (var word in added.OrderBy(w => w).Take(50))
            {
                Console.WriteLine($"  + {word}");
            }
            if (added.Count > 50)
            {
                Console.WriteLine($"  ... and {added.Count - 50} more");
            }
            Console.WriteLine();
        }

        // Redaction verification
        Console.WriteLine($"=== Redaction Verification ===");
        if (removed.Any() && preserved.Any())
        {
            Console.WriteLine("✓ GOOD: Some content removed, some preserved (selective redaction)");
        }
        else if (removed.Any() && !preserved.Any())
        {
            Console.WriteLine("⚠ WARNING: All content removed (might be too aggressive)");
        }
        else if (!removed.Any() && preserved.Any())
        {
            Console.WriteLine("✗ FAILED: No content removed (redaction did not work)");
        }
    }

    static void FindHiddenContent(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: File not found: {pdfPath}");
            return;
        }

        Console.WriteLine($"=== Searching for hidden content in: {Path.GetFileName(pdfPath)} ===\n");

        using var document = PdfDocument.Open(pdfPath);

        bool foundHidden = false;

        for (int i = 0; i < document.NumberOfPages; i++)
        {
            var page = document.GetPage(i + 1);
            var words = page.GetWords().ToList();

            Console.WriteLine($"Page {i + 1}: Found {words.Count} words");

            // Look for overlapping text (potential hidden content)
            for (int j = 0; j < words.Count; j++)
            {
                for (int k = j + 1; k < words.Count; k++)
                {
                    var word1 = words[j];
                    var word2 = words[k];

                    // Check if bounding boxes overlap
                    if (BoundingBoxesOverlap(word1.BoundingBox, word2.BoundingBox))
                    {
                        foundHidden = true;
                        Console.WriteLine($"  ⚠ Overlapping text detected:");
                        Console.WriteLine($"    \"{word1.Text}\" at ({word1.BoundingBox.Left:F1}, {word1.BoundingBox.Bottom:F1})");
                        Console.WriteLine($"    \"{word2.Text}\" at ({word2.BoundingBox.Left:F1}, {word2.BoundingBox.Bottom:F1})");
                        Console.WriteLine($"    → One may be hidden under the other");
                        Console.WriteLine();
                    }
                }
            }
        }

        if (!foundHidden)
        {
            Console.WriteLine("✓ No obviously overlapping text found");
        }

        // Note about limitations
        Console.WriteLine("\nNOTE: This tool can only detect overlapping TEXT.");
        Console.WriteLine("It cannot detect:");
        Console.WriteLine("  - Text hidden under shapes/graphics");
        Console.WriteLine("  - Text covered by images");
        Console.WriteLine("  - Text outside visible page area");
        Console.WriteLine("\nFor complete validation, use 'extract-text' and manually verify.");
    }

    static void ShowContentStream(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: File not found: {pdfPath}");
            return;
        }

        Console.WriteLine($"=== Content Stream for: {Path.GetFileName(pdfPath)} ===\n");

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);

        for (int i = 0; i < document.PageCount; i++)
        {
            Console.WriteLine($"--- Page {i + 1} ---");

            var page = document.Pages[i];
            var contentStream = page.Contents.Elements.FirstOrDefault();

            if (contentStream is PdfSharp.Pdf.PdfDictionary dict && dict.Stream != null)
            {
                var bytes = dict.Stream.Value;
                var content = Encoding.ASCII.GetString(bytes);

                Console.WriteLine($"Stream size: {bytes.Length} bytes");
                Console.WriteLine("\nContent (first 2000 characters):");
                Console.WriteLine(content.Substring(0, Math.Min(2000, content.Length)));

                if (content.Length > 2000)
                {
                    Console.WriteLine($"\n... and {content.Length - 2000} more characters");
                }
            }
            else
            {
                Console.WriteLine("No content stream found");
            }

            Console.WriteLine();
        }
    }

    static string ExtractAllText(string pdfPath)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(pdfPath);

        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    static bool BoundingBoxesOverlap(UglyToad.PdfPig.Core.PdfRectangle box1, UglyToad.PdfPig.Core.PdfRectangle box2)
    {
        return box1.Left < box2.Right &&
               box1.Right > box2.Left &&
               box1.Bottom < box2.Top &&
               box1.Top > box2.Bottom;
    }
}
