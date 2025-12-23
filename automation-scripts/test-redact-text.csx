/// <summary>
/// GUI Test Script: Redact Text
/// Tests the complete redaction workflow: load → redact → apply → save → verify.
/// </summary>
/// <returns>0 on success, 1 on failure</returns>

using System;
using System.IO;
using System.Diagnostics;

Console.WriteLine("=== GUI Test: Redact Text Workflow ===");

// Test configuration
var sourcePdf = args.Length > 0 ? args[0] : "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
var outputPdf = args.Length > 1 ? args[1] : "/tmp/pdfe-test-redacted.pdf";
var textToRedact = args.Length > 2 ? args[2] : "TORRINGTON";

// Clean up previous test output
if (File.Exists(outputPdf))
{
    File.Delete(outputPdf);
}

// Validate input
if (!File.Exists(sourcePdf))
{
    Console.WriteLine($"❌ FAIL: Source PDF not found: {sourcePdf}");
    return 1;
}

try
{
    // Step 1: Load document
    Console.WriteLine($"\n[1/5] Loading document: {sourcePdf}");
    await LoadDocumentCommand.Execute(sourcePdf);

    if (CurrentDocument == null)
    {
        Console.WriteLine("❌ FAIL: Document not loaded");
        return 1;
    }
    Console.WriteLine($"✅ Loaded {CurrentDocument.PageCount} pages");

    // Step 2: Redact text
    Console.WriteLine($"\n[2/5] Redacting text: '{textToRedact}'");
    var initialPendingCount = PendingRedactions.Count;

    await RedactTextCommand.Execute(textToRedact);

    var newPendingCount = PendingRedactions.Count;
    var redactionsAdded = newPendingCount - initialPendingCount;

    if (redactionsAdded <= 0)
    {
        Console.WriteLine($"❌ FAIL: No redactions added (before: {initialPendingCount}, after: {newPendingCount})");
        return 1;
    }
    Console.WriteLine($"✅ Added {redactionsAdded} redaction area(s) (total pending: {newPendingCount})");

    // Step 3: Apply redactions
    Console.WriteLine($"\n[3/5] Applying redactions");
    await ApplyRedactionsCommand.Execute();

    if (PendingRedactions.Count > 0)
    {
        Console.WriteLine($"❌ FAIL: Pending redactions not cleared (count: {PendingRedactions.Count})");
        return 1;
    }
    Console.WriteLine($"✅ All redactions applied");

    // Step 4: Save document
    Console.WriteLine($"\n[4/5] Saving to: {outputPdf}");
    await SaveDocumentCommand.Execute(outputPdf);

    if (!File.Exists(outputPdf))
    {
        Console.WriteLine($"❌ FAIL: Output file not created");
        return 1;
    }
    Console.WriteLine($"✅ Document saved ({new FileInfo(outputPdf).Length} bytes)");

    // Step 5: Verify redaction using external tool (pdfer)
    Console.WriteLine($"\n[5/5] Verifying redaction with pdfer");
    var pdferPath = "/home/marc/pdfe/PdfEditor.Redaction.Cli/bin/Release/net8.0/pdfer";

    if (!File.Exists(pdferPath))
    {
        Console.WriteLine($"⚠️  WARNING: pdfer not found at {pdferPath}, skipping verification");
    }
    else
    {
        var verifyProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pdferPath,
                Arguments = $"verify \"{outputPdf}\" \"{textToRedact}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        verifyProcess.Start();
        var output = await verifyProcess.StandardOutput.ReadToEndAsync();
        var error = await verifyProcess.StandardError.ReadToEndAsync();
        await verifyProcess.WaitForExitAsync();

        if (verifyProcess.ExitCode == 0)
        {
            Console.WriteLine($"✅ Verification PASS: '{textToRedact}' not found in PDF");
        }
        else
        {
            Console.WriteLine($"❌ FAIL: Verification failed (exit code: {verifyProcess.ExitCode})");
            Console.WriteLine($"  Output: {output}");
            Console.WriteLine($"  Error: {error}");
            return 1;
        }
    }

    // All checks passed
    Console.WriteLine($"\n✅ SUCCESS: Complete redaction workflow validated");
    Console.WriteLine($"  Source: {sourcePdf}");
    Console.WriteLine($"  Output: {outputPdf}");
    Console.WriteLine($"  Redacted: '{textToRedact}'");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ FAIL: Exception during workflow");
    Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine($"  Stack trace: {ex.StackTrace}");
    return 1;
}
