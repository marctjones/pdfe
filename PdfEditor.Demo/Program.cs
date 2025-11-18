using PdfEditor.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using Avalonia;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace PdfEditor.Demo;

/// <summary>
/// Demonstration program that:
/// 1. Creates a PDF with text and shapes
/// 2. Applies black box redactions
/// 3. Saves the redacted PDF
/// 4. Re-opens and verifies content is removed
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== PDF Redaction Demonstration ===\n");

        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "RedactionDemo");
        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"Output directory: {outputDir}\n");

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var redactionService = new RedactionService(
            loggerFactory.CreateLogger<RedactionService>(),
            loggerFactory
        );

        // Test 1: Simple text redaction
        Console.WriteLine("--- Test 1: Simple Text Redaction ---");
        TestSimpleTextRedaction(redactionService, outputDir);
        Console.WriteLine();

        // Test 2: Complex document with shapes and text
        Console.WriteLine("--- Test 2: Complex Document with Shapes and Text ---");
        TestComplexRedaction(redactionService, outputDir);
        Console.WriteLine();

        // Test 3: Random redaction areas
        Console.WriteLine("--- Test 3: Random Redaction Areas ---");
        TestRandomRedaction(redactionService, outputDir);
        Console.WriteLine();

        Console.WriteLine($"\n=== All demonstrations complete! ===");
        Console.WriteLine($"Check the files in: {outputDir}");
        Console.WriteLine("\nYou can open the PDFs to visually inspect the redactions.");
    }

    static void TestSimpleTextRedaction(RedactionService redactionService, string outputDir)
    {
        // Step 1: Create PDF with known content
        var originalPdf = Path.Combine(outputDir, "01_simple_original.pdf");
        CreateSimpleTestPdf(originalPdf);
        Console.WriteLine($"✓ Created: {Path.GetFileName(originalPdf)}");

        // Step 2: Verify original content
        var textBefore = ExtractText(originalPdf);
        Console.WriteLine($"  Content before: {textBefore.Trim()}");

        // Step 3: Apply black box redaction
        var redactedPdf = Path.Combine(outputDir, "01_simple_redacted.pdf");
        var document = PdfReader.Open(originalPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact the word "CONFIDENTIAL" (at position 100, 100)
        var redactionArea = new Rect(90, 90, 150, 30);
        Console.WriteLine($"  Applying black box at: X={redactionArea.X}, Y={redactionArea.Y}, " +
                         $"W={redactionArea.Width}, H={redactionArea.Height}");

        redactionService.RedactArea(page, redactionArea, renderDpi: 72);

        document.Save(redactedPdf);
        document.Dispose();
        Console.WriteLine($"✓ Saved: {Path.GetFileName(redactedPdf)}");

        // Step 4: Re-open and verify content is removed
        var textAfter = ExtractText(redactedPdf);
        Console.WriteLine($"  Content after: {textAfter.Trim()}");

        // Step 5: Verification
        if (textBefore.Contains("CONFIDENTIAL") && !textAfter.Contains("CONFIDENTIAL"))
        {
            Console.WriteLine($"  ✓ SUCCESS: 'CONFIDENTIAL' was removed from PDF structure");
        }
        else
        {
            Console.WriteLine($"  ✗ FAILED: Content was not properly removed");
        }
    }

    static void TestComplexRedaction(RedactionService redactionService, string outputDir)
    {
        // Step 1: Create complex PDF
        var originalPdf = Path.Combine(outputDir, "02_complex_original.pdf");
        CreateComplexTestPdf(originalPdf);
        Console.WriteLine($"✓ Created: {Path.GetFileName(originalPdf)}");

        // Step 2: Verify original content
        var textBefore = ExtractText(originalPdf);
        var itemsBefore = new[] { "SECRET-DATA", "PUBLIC-INFO", "CONFIDENTIAL-123" };
        Console.WriteLine("  Content items before:");
        foreach (var item in itemsBefore)
        {
            var exists = textBefore.Contains(item);
            Console.WriteLine($"    {item}: {(exists ? "EXISTS" : "MISSING")}");
        }

        // Step 3: Apply black boxes over sensitive items only
        var redactedPdf = Path.Combine(outputDir, "02_complex_redacted.pdf");
        var document = PdfReader.Open(originalPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Redact "SECRET-DATA" (at 100, 100)
        Console.WriteLine("  Applying black box over 'SECRET-DATA'");
        redactionService.RedactArea(page, new Rect(90, 90, 140, 25), renderDpi: 72);

        // Redact "CONFIDENTIAL-123" (at 100, 300)
        Console.WriteLine("  Applying black box over 'CONFIDENTIAL-123'");
        redactionService.RedactArea(page, new Rect(90, 290, 200, 25), renderDpi: 72);

        // Redact blue rectangle (shape at 50, 400)
        Console.WriteLine("  Applying black box over blue rectangle");
        redactionService.RedactArea(page, new Rect(45, 395, 110, 60), renderDpi: 72);

        document.Save(redactedPdf);
        document.Dispose();
        Console.WriteLine($"✓ Saved: {Path.GetFileName(redactedPdf)}");

        // Step 4: Re-open and verify
        var textAfter = ExtractText(redactedPdf);
        Console.WriteLine("  Content items after:");

        var removedItems = new[] { "SECRET-DATA", "CONFIDENTIAL-123" };
        var preservedItems = new[] { "PUBLIC-INFO" };

        bool allRemoved = true;
        foreach (var item in removedItems)
        {
            var exists = textAfter.Contains(item);
            Console.WriteLine($"    {item}: {(exists ? "STILL EXISTS (FAILED)" : "REMOVED ✓")}");
            if (exists) allRemoved = false;
        }

        bool allPreserved = true;
        foreach (var item in preservedItems)
        {
            var exists = textAfter.Contains(item);
            Console.WriteLine($"    {item}: {(exists ? "PRESERVED ✓" : "REMOVED (FAILED)")}");
            if (!exists) allPreserved = false;
        }

        // Step 5: Verification
        if (allRemoved && allPreserved)
        {
            Console.WriteLine($"  ✓ SUCCESS: Targeted content removed, other content preserved");
        }
        else
        {
            Console.WriteLine($"  ✗ FAILED: Redaction did not work as expected");
        }
    }

    static void TestRandomRedaction(RedactionService redactionService, string outputDir)
    {
        // Step 1: Create grid PDF
        var originalPdf = Path.Combine(outputDir, "03_random_original.pdf");
        CreateGridTestPdf(originalPdf);
        Console.WriteLine($"✓ Created: {Path.GetFileName(originalPdf)}");

        // Step 2: Count original content
        var textBefore = ExtractText(originalPdf);
        var wordsBefore = CountWords(textBefore);
        Console.WriteLine($"  Words before redaction: {wordsBefore}");

        // Step 3: Apply random black boxes
        var redactedPdf = Path.Combine(outputDir, "03_random_redacted.pdf");
        var document = PdfReader.Open(originalPdf, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var random = new Random(12345);
        var numRedactions = 3;

        Console.WriteLine($"  Applying {numRedactions} random black boxes:");
        for (int i = 0; i < numRedactions; i++)
        {
            var x = random.Next(50, 400);
            var y = random.Next(50, 600);
            var width = random.Next(60, 120);
            var height = random.Next(40, 80);

            var area = new Rect(x, y, width, height);
            Console.WriteLine($"    Box {i + 1}: X={x}, Y={y}, W={width}, H={height}");
            redactionService.RedactArea(page, area, renderDpi: 72);
        }

        document.Save(redactedPdf);
        document.Dispose();
        Console.WriteLine($"✓ Saved: {Path.GetFileName(redactedPdf)}");

        // Step 4: Re-open and verify
        var textAfter = ExtractText(redactedPdf);
        var wordsAfter = CountWords(textAfter);
        Console.WriteLine($"  Words after redaction: {wordsAfter}");

        // Step 5: Verification
        if (wordsAfter < wordsBefore && wordsAfter > 0)
        {
            var removed = wordsBefore - wordsAfter;
            Console.WriteLine($"  ✓ SUCCESS: {removed} words removed, {wordsAfter} words preserved");
        }
        else if (wordsAfter == wordsBefore)
        {
            Console.WriteLine($"  ✗ FAILED: No content was removed");
        }
        else
        {
            Console.WriteLine($"  ✗ FAILED: All content was removed (should preserve some)");
        }
    }

    // Helper methods

    static void CreateSimpleTestPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 14, XFontStyleEx.Bold);

        gfx.DrawString("CONFIDENTIAL", font, XBrushes.Red, new XPoint(100, 100));
        gfx.DrawString("This is public information", new XFont("Arial", 12), XBrushes.Black, new XPoint(100, 200));

        document.Save(outputPath);
        document.Dispose();
    }

    static void CreateComplexTestPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        // Text at different positions
        gfx.DrawString("SECRET-DATA", font, XBrushes.Red, new XPoint(100, 100));
        gfx.DrawString("PUBLIC-INFO", font, XBrushes.Green, new XPoint(100, 200));
        gfx.DrawString("CONFIDENTIAL-123", font, XBrushes.Red, new XPoint(100, 300));

        // Shapes
        gfx.DrawRectangle(XBrushes.LightBlue, new XRect(50, 400, 100, 50));
        gfx.DrawRectangle(XBrushes.LightGreen, new XRect(300, 400, 100, 50));

        document.Save(outputPath);
        document.Dispose();
    }

    static void CreateGridTestPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);

        // Create grid of text
        for (int x = 100; x <= 500; x += 100)
        {
            for (int y = 100; y <= 700; y += 100)
            {
                gfx.DrawString($"Cell({x},{y})", font, XBrushes.Black, new XPoint(x, y));
            }
        }

        document.Save(outputPath);
        document.Dispose();
    }

    static string ExtractText(string pdfPath)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var text = new System.Text.StringBuilder();

            foreach (var page in document.GetPages())
            {
                text.AppendLine(page.Text);
            }

            return text.ToString();
        }
        catch (Exception ex)
        {
            return $"Error extracting text: {ex.Message}";
        }
    }

    static int CountWords(string text)
    {
        return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
