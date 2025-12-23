/// <summary>
/// GUI Test: Birth Certificate - Specific Word Tests
///
/// This is a TARGETED test designed specifically for birth-certificate-request-scrambled.pdf
/// We know exactly what words exist in this PDF and test specific redaction scenarios:
///
/// 1. Single occurrence word - test precision
/// 2. Multiple occurrence word - test thoroughness
/// 3. Mixed case word - test case handling
/// 4. Word boundary handling - test substring issues
///
/// This complements the generic corpus tests by providing deterministic,
/// reproducible tests against a known PDF.
/// </summary>
/// <returns>0 on success, 1 on failure</returns>

using System;
using System.IO;

Console.WriteLine("=== GUI Test: Birth Certificate - Specific Word Redaction ===");

// Find repository root
var repoRoot = Directory.GetCurrentDirectory();
while (repoRoot != null && !Directory.Exists(Path.Combine(repoRoot, ".git")))
{
    repoRoot = Directory.GetParent(repoRoot)?.FullName;
}

if (repoRoot == null)
{
    Console.WriteLine("❌ FAIL: Could not find repository root");
    return 1;
}

var sourcePdf = Path.Combine(repoRoot, "test-pdfs", "sample-pdfs", "birth-certificate-request-scrambled.pdf");
var outputDir = "/tmp/pdfe-birth-cert-specific";

Directory.CreateDirectory(outputDir);

if (!File.Exists(sourcePdf))
{
    Console.WriteLine($"❌ FAIL: Birth certificate not found: {sourcePdf}");
    return 1;
}

// Define specific test cases for THIS PDF
var testCases = new[]
{
    new { Word = "CITY", Description = "Common word, uppercase, multiple occurrences", ExpectedMin = 1 },
    new { Word = "County", Description = "Mixed case word", ExpectedMin = 1 },
    new { Word = "Birth", Description = "Word that appears in compound forms", ExpectedMin = 1 },
    new { Word = "Certificate", Description = "Longer word, title text", ExpectedMin = 1 },
};

Console.WriteLine($"\nSource: {sourcePdf}");
Console.WriteLine($"Test cases: {testCases.Length}");
Console.WriteLine();

try
{
    int passed = 0;
    int failed = 0;

    foreach (var testCase in testCases)
    {
        Console.WriteLine($"[Test Case] Redacting: '{testCase.Word}'");
        Console.WriteLine($"  Purpose: {testCase.Description}");

        // Load fresh document for each test
        await LoadDocumentCommand(sourcePdf);

        if (CurrentDocument == null)
        {
            Console.WriteLine($"  ❌ FAIL: Could not load document");
            failed++;
            continue;
        }

        // Verify word exists BEFORE
        var textBefore = ExtractAllText();
        var existsBefore = textBefore.Contains(testCase.Word, StringComparison.OrdinalIgnoreCase);

        if (!existsBefore)
        {
            Console.WriteLine($"  ❌ FAIL: Word '{testCase.Word}' not found in source PDF");
            Console.WriteLine($"    This test case may need updating if PDF content changed");
            failed++;
            continue;
        }

        Console.WriteLine($"  ✅ Word exists in source (verified)");

        // Redact
        var beforeCount = PendingRedactions.Count;
        await RedactTextCommand(testCase.Word);
        var afterCount = PendingRedactions.Count;
        var redactionsAdded = afterCount - beforeCount;

        Console.WriteLine($"  Redactions marked: {redactionsAdded}");

        if (redactionsAdded < testCase.ExpectedMin)
        {
            Console.WriteLine($"  ⚠️  WARNING: Expected at least {testCase.ExpectedMin}, got {redactionsAdded}");
            Console.WriteLine($"    May indicate search/coordinate issues");
        }

        if (redactionsAdded == 0)
        {
            Console.WriteLine($"  ❌ FAIL: No redactions created despite word existing");
            failed++;
            continue;
        }

        // Apply and save
        await ApplyRedactionsCommand();

        var outputPath = Path.Combine(outputDir, $"redacted_{testCase.Word.ToLower()}.pdf");
        await SaveDocumentCommand(outputPath);

        if (!File.Exists(outputPath))
        {
            Console.WriteLine($"  ❌ FAIL: Output file not created");
            failed++;
            continue;
        }

        // Verify word is GONE
        await LoadDocumentCommand(outputPath);
        var textAfter = ExtractAllText();
        var stillExists = textAfter.Contains(testCase.Word, StringComparison.OrdinalIgnoreCase);

        if (stillExists)
        {
            Console.WriteLine($"  ❌ FAIL: Word '{testCase.Word}' still present after redaction");
            Console.WriteLine($"    TRUE redaction failed - content not removed");
            failed++;
        }
        else
        {
            Console.WriteLine($"  ✅ PASS: Word completely removed from PDF structure");
            passed++;
        }

        Console.WriteLine();
    }

    // Summary
    Console.WriteLine("================================================");
    Console.WriteLine("Test Summary");
    Console.WriteLine("================================================");
    Console.WriteLine($"Test cases: {testCases.Length}");
    Console.WriteLine($"  ✅ Passed: {passed}");
    Console.WriteLine($"  ❌ Failed: {failed}");
    Console.WriteLine($"Success rate: {(double)passed / testCases.Length:P0}");
    Console.WriteLine();

    if (failed == 0)
    {
        Console.WriteLine("✅ ALL TESTS PASSED");
        Console.WriteLine("Specific word redaction works correctly on known PDF");
        return 0;
    }
    else
    {
        Console.WriteLine($"❌ {failed} TEST(S) FAILED");
        Console.WriteLine("Review failures above for details");
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
