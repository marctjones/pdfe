/// <summary>
/// GUI Test Script: veraPDF Corpus Sample
/// Tests GUI redaction against a diverse sample of PDFs from the veraPDF corpus.
///
/// Validates that the GUI can handle:
/// - PDF/A-1a, 1b, 2a, 2b, 2u, 3b, 4, 4e, 4f standards
/// - PDF/UA-1 and UA-2 (accessibility)
/// - ISO 32000-1 reference files
/// - Various PDF structures and edge cases
/// </summary>
/// <returns>0 on success, 1 on failure</returns>

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

Console.WriteLine("=== GUI Test: veraPDF Corpus Sample ===");

// Configuration
var corpusRoot = "/home/marc/pdfe/test-pdfs/veraPDF-corpus";
var sampleSize = 20; // Test with 20 random PDFs
var outputDir = "/tmp/pdfe-corpus-test";

Directory.CreateDirectory(outputDir);

// Check if corpus exists
if (!Directory.Exists(corpusRoot))
{
    Console.WriteLine($"❌ SKIP: veraPDF corpus not found at {corpusRoot}");
    Console.WriteLine("  Run: ./scripts/download-test-pdfs.sh");
    return 0; // Skip, not fail
}

try
{
    // Find all PDFs in corpus
    Console.WriteLine($"\n[1/4] Scanning veraPDF corpus");
    var allPdfs = Directory.GetFiles(corpusRoot, "*.pdf", SearchOption.AllDirectories).ToList();

    if (allPdfs.Count == 0)
    {
        Console.WriteLine($"❌ SKIP: No PDFs found in corpus");
        return 0;
    }

    Console.WriteLine($"✅ Found {allPdfs.Count} PDFs in corpus");

    // Select diverse sample across different standards
    Console.WriteLine($"\n[2/4] Selecting diverse sample of {sampleSize} PDFs");

    var categories = new Dictionary<string, List<string>>
    {
        ["PDF/A-1a"] = new List<string>(),
        ["PDF/A-1b"] = new List<string>(),
        ["PDF/A-2a"] = new List<string>(),
        ["PDF/A-2b"] = new List<string>(),
        ["PDF/A-2u"] = new List<string>(),
        ["PDF/A-3b"] = new List<string>(),
        ["PDF/A-4"] = new List<string>(),
        ["PDF/UA-1"] = new List<string>(),
        ["ISO32000"] = new List<string>(),
    };

    // Categorize PDFs
    foreach (var pdf in allPdfs)
    {
        var path = pdf.ToLower();
        if (path.Contains("pdf-a-1a") || path.Contains("pdfa-1a")) categories["PDF/A-1a"].Add(pdf);
        else if (path.Contains("pdf-a-1b") || path.Contains("pdfa-1b")) categories["PDF/A-1b"].Add(pdf);
        else if (path.Contains("pdf-a-2a") || path.Contains("pdfa-2a")) categories["PDF/A-2a"].Add(pdf);
        else if (path.Contains("pdf-a-2b") || path.Contains("pdfa-2b")) categories["PDF/A-2b"].Add(pdf);
        else if (path.Contains("pdf-a-2u") || path.Contains("pdfa-2u")) categories["PDF/A-2u"].Add(pdf);
        else if (path.Contains("pdf-a-3b") || path.Contains("pdfa-3b")) categories["PDF/A-3b"].Add(pdf);
        else if (path.Contains("pdf-a-4") || path.Contains("pdfa-4")) categories["PDF/A-4"].Add(pdf);
        else if (path.Contains("pdfua-1") || path.Contains("pdf-ua-1")) categories["PDF/UA-1"].Add(pdf);
        else if (path.Contains("iso") && path.Contains("32000")) categories["ISO32000"].Add(pdf);
    }

    // Sample from each category
    var random = new Random(42); // Fixed seed for reproducibility
    var selectedPdfs = new List<(string category, string path)>();
    var pdfsPerCategory = Math.Max(1, sampleSize / categories.Count);

    foreach (var (category, pdfs) in categories)
    {
        if (pdfs.Count > 0)
        {
            var count = Math.Min(pdfsPerCategory, pdfs.Count);
            var samples = pdfs.OrderBy(x => random.Next()).Take(count);

            foreach (var pdf in samples)
            {
                selectedPdfs.Add((category, pdf));
            }

            Console.WriteLine($"  {category}: {count} PDF(s)");
        }
    }

    // Ensure we have exactly sampleSize PDFs
    while (selectedPdfs.Count > sampleSize)
    {
        selectedPdfs.RemoveAt(selectedPdfs.Count - 1);
    }

    Console.WriteLine($"\n✅ Selected {selectedPdfs.Count} diverse PDFs");

    // Test each PDF
    Console.WriteLine($"\n[3/4] Testing redaction on each PDF");

    int successCount = 0;
    int failureCount = 0;
    int skipCount = 0;

    var results = new List<(string category, string filename, string status, string message)>();

    foreach (var (category, pdfPath) in selectedPdfs)
    {
        var filename = Path.GetFileName(pdfPath);
        Console.WriteLine($"\n  Testing: {filename} ({category})");

        try
        {
            // Load document
            await LoadDocumentCommand.Execute(pdfPath);

            if (CurrentDocument == null)
            {
                results.Add((category, filename, "SKIP", "Failed to load"));
                skipCount++;
                continue;
            }

            var pageCount = CurrentDocument.PageCount;
            Console.WriteLine($"    Pages: {pageCount}");

            // Try to redact a common word (if document has extractable text)
            // Using "the" as it's likely to appear in many documents
            var beforeCount = PendingRedactions.Count;
            await RedactTextCommand.Execute("the");
            var afterCount = PendingRedactions.Count;
            var redactionsAdded = afterCount - beforeCount;

            if (redactionsAdded > 0)
            {
                Console.WriteLine($"    Redactions: {redactionsAdded} area(s) created");

                // Apply redactions
                await ApplyRedactionsCommand.Execute();

                // Save to output
                var outputPath = Path.Combine(outputDir, $"redacted_{filename}");
                await SaveDocumentCommand.Execute(outputPath);

                if (File.Exists(outputPath))
                {
                    Console.WriteLine($"    ✅ SUCCESS - Redacted and saved");
                    results.Add((category, filename, "SUCCESS", $"{redactionsAdded} redactions"));
                    successCount++;
                }
                else
                {
                    Console.WriteLine($"    ❌ FAIL - Output file not created");
                    results.Add((category, filename, "FAIL", "Output not created"));
                    failureCount++;
                }
            }
            else
            {
                // No text to redact - still a success (document processed without error)
                Console.WriteLine($"    ⚠️  SKIP - No extractable text");
                results.Add((category, filename, "SKIP", "No extractable text"));
                skipCount++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ❌ FAIL - Exception: {ex.Message}");
            results.Add((category, filename, "FAIL", ex.Message));
            failureCount++;
        }
    }

    // Summary
    Console.WriteLine($"\n[4/4] Test Summary");
    Console.WriteLine($"{'='.ToString().PadRight(60, '=')}");
    Console.WriteLine($"Total PDFs tested: {selectedPdfs.Count}");
    Console.WriteLine($"  ✅ Success: {successCount}");
    Console.WriteLine($"  ❌ Failures: {failureCount}");
    Console.WriteLine($"  ⚠️  Skipped: {skipCount}");
    Console.WriteLine($"{'='.ToString().PadRight(60, '=')}");

    // Results by category
    Console.WriteLine($"\nResults by PDF Standard:");
    foreach (var category in categories.Keys)
    {
        var categoryResults = results.Where(r => r.category == category).ToList();
        if (categoryResults.Count > 0)
        {
            var success = categoryResults.Count(r => r.status == "SUCCESS");
            var total = categoryResults.Count;
            Console.WriteLine($"  {category}: {success}/{total} succeeded");
        }
    }

    // Detailed failures
    var failures = results.Where(r => r.status == "FAIL").ToList();
    if (failures.Count > 0)
    {
        Console.WriteLine($"\nFailures:");
        foreach (var (category, filename, _, message) in failures)
        {
            Console.WriteLine($"  ❌ {filename} ({category}): {message}");
        }
    }

    // Success criteria: At least 70% success rate
    var successRate = (double)successCount / selectedPdfs.Count;
    Console.WriteLine($"\nSuccess rate: {successRate:P0}");

    if (successRate >= 0.70)
    {
        Console.WriteLine($"✅ PASS: Success rate meets threshold (≥70%)");
        return 0;
    }
    else
    {
        Console.WriteLine($"❌ FAIL: Success rate below threshold (expected ≥70%, got {successRate:P0})");
        return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ FAIL: Unhandled exception");
    Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine($"  Stack trace: {ex.StackTrace}");
    return 1;
}
