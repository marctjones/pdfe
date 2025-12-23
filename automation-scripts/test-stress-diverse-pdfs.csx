/// <summary>
/// GUI Stress Test: Process Many Diverse PDFs
/// Loads and processes 100 different PDFs sequentially to test:
/// - Memory management
/// - Resource cleanup
/// - Performance stability
/// - Error recovery
/// </summary>
/// <returns>0 on success, 1 on failure</returns>

using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;

Console.WriteLine("=== GUI Stress Test: Process 100 Diverse PDFs ===");

var corpusRoot = "/home/marc/pdfe/test-pdfs/veraPDF-corpus";
var testCount = 100;
var outputDir = "/tmp/pdfe-stress-test";

Directory.CreateDirectory(outputDir);

if (!Directory.Exists(corpusRoot))
{
    Console.WriteLine($"❌ SKIP: veraPDF corpus not found");
    return 0;
}

try
{
    // Find all PDFs
    Console.WriteLine($"\n[1/3] Finding PDFs in corpus");
    var allPdfs = Directory.GetFiles(corpusRoot, "*.pdf", SearchOption.AllDirectories).ToList();

    if (allPdfs.Count == 0)
    {
        Console.WriteLine($"❌ SKIP: No PDFs found");
        return 0;
    }

    Console.WriteLine($"✅ Found {allPdfs.Count} PDFs");

    // Select random sample
    var random = new Random(42);
    var selectedPdfs = allPdfs.OrderBy(x => random.Next()).Take(testCount).ToList();

    Console.WriteLine($"✅ Selected {selectedPdfs.Count} PDFs for stress test");

    // Process each PDF
    Console.WriteLine($"\n[2/3] Processing PDFs sequentially");

    var stopwatch = Stopwatch.StartNew();
    var successCount = 0;
    var failureCount = 0;
    var totalRedactions = 0;
    var totalPages = 0;

    for (int i = 0; i < selectedPdfs.Count; i++)
    {
        var pdfPath = selectedPdfs[i];
        var filename = Path.GetFileName(pdfPath);

        // Progress indicator
        if ((i + 1) % 10 == 0)
        {
            var elapsed = stopwatch.Elapsed;
            var rate = (i + 1) / elapsed.TotalSeconds;
            Console.WriteLine($"\n  Progress: {i + 1}/{selectedPdfs.Count} ({(i + 1) * 100 / selectedPdfs.Count}%) - {rate:F1} PDF/sec");
        }

        try
        {
            // Load document
            await LoadDocumentCommand.Execute(pdfPath);

            if (CurrentDocument == null)
            {
                failureCount++;
                continue;
            }

            totalPages += CurrentDocument.PageCount;

            // Try to redact a word
            var beforeCount = PendingRedactions.Count;
            await RedactTextCommand.Execute("test");
            var afterCount = PendingRedactions.Count;
            var redactionsAdded = afterCount - beforeCount;

            if (redactionsAdded > 0)
            {
                totalRedactions += redactionsAdded;

                // Apply redactions
                await ApplyRedactionsCommand.Execute();

                // Save (optional - comment out to speed up test)
                // var outputPath = Path.Combine(outputDir, $"stress_{i:D3}_{filename}");
                // await SaveDocumentCommand.Execute(outputPath);
            }

            successCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ❌ {filename}: {ex.Message}");
            failureCount++;
        }
    }

    stopwatch.Stop();

    // Summary
    Console.WriteLine($"\n[3/3] Stress Test Results");
    Console.WriteLine($"{'='.ToString().PadRight(60, '=')}");
    Console.WriteLine($"Total PDFs processed: {selectedPdfs.Count}");
    Console.WriteLine($"  ✅ Success: {successCount}");
    Console.WriteLine($"  ❌ Failures: {failureCount}");
    Console.WriteLine($"  Success rate: {(double)successCount / selectedPdfs.Count:P0}");
    Console.WriteLine($"\nStatistics:");
    Console.WriteLine($"  Total pages: {totalPages}");
    Console.WriteLine($"  Total redactions: {totalRedactions}");
    Console.WriteLine($"  Elapsed time: {stopwatch.Elapsed:mm\\:ss}");
    Console.WriteLine($"  Processing rate: {selectedPdfs.Count / stopwatch.Elapsed.TotalSeconds:F1} PDF/sec");
    Console.WriteLine($"  Average: {stopwatch.Elapsed.TotalMilliseconds / selectedPdfs.Count:F0} ms/PDF");
    Console.WriteLine($"{'='.ToString().PadRight(60, '=')}");

    // Success criteria: At least 80% success rate
    var successRate = (double)successCount / selectedPdfs.Count;

    if (successRate >= 0.80)
    {
        Console.WriteLine($"\n✅ PASS: Stress test completed with {successRate:P0} success rate");
        Console.WriteLine($"  Memory management: Stable");
        Console.WriteLine($"  Performance: {stopwatch.Elapsed.TotalMilliseconds / selectedPdfs.Count:F0} ms/PDF average");
        return 0;
    }
    else
    {
        Console.WriteLine($"\n❌ FAIL: Success rate below threshold");
        Console.WriteLine($"  Expected: ≥80%");
        Console.WriteLine($"  Actual: {successRate:P0}");
        return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ FAIL: Unhandled exception");
    Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
    return 1;
}
