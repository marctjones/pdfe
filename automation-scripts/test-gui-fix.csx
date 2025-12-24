#!/usr/bin/env dotnet-script
#r "nuget: Avalonia.Headless, 11.1.3"
#r "../PdfEditor/bin/Debug/net8.0/PdfEditor.dll"

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.ApplicationLifetimes;
using PdfEditor;
using PdfEditor.ViewModels;
using UglyToad.PdfPig;

Console.WriteLine("==============================================");
Console.WriteLine("GUI Fix Verification Test");
Console.WriteLine("==============================================");
Console.WriteLine();

var testPdfPath = "/home/marc/pdfe/test-pdfs/birth-certificate.pdf";
var outputPath = "/tmp/gui_fix_test.pdf";

if (!File.Exists(testPdfPath))
{
    Console.WriteLine("❌ Test PDF not found: " + testPdfPath);
    return 1;
}

// Extract text BEFORE redaction
Console.WriteLine("Extracting text BEFORE redaction...");
string textBefore;
using (var doc = PdfDocument.Open(testPdfPath))
{
    var page = doc.GetPage(1);
    textBefore = string.Join("", page.Letters.Select(l => l.Value));
}

Console.WriteLine($"Text length BEFORE: {textBefore.Length}");
Console.WriteLine($"First 200 chars: {textBefore.Substring(0, Math.Min(200, textBefore.Length))}");
Console.WriteLine();

// Initialize Avalonia headless
Console.WriteLine("Initializing Avalonia headless environment...");
AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions
    {
        UseHeadlessDrawing = true
    })
    .SetupWithoutStarting();

// Create ViewModel and load document
Console.WriteLine("Loading document into ViewModel...");
var viewModel = new MainWindowViewModel();
await viewModel.LoadDocumentCommand.Execute(testPdfPath);

if (viewModel.CurrentDocument == null)
{
    Console.WriteLine("❌ Failed to load document");
    return 1;
}

Console.WriteLine($"✅ Document loaded: {viewModel.PageCount} pages");
Console.WriteLine();

// Simulate GUI redaction by finding and marking "Birth"
Console.WriteLine("Searching for 'Birth' to redact...");

// Find text positions using the ViewModel's extraction
var page1Text = viewModel.CurrentPageText;
Console.WriteLine($"Page text length: {page1Text?.Length ?? 0}");

if (string.IsNullOrEmpty(page1Text) || !page1Text.Contains("Birth", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("⚠️ Cannot find 'Birth' in extracted text");
    Console.WriteLine($"Text preview: {page1Text?.Substring(0, Math.Min(200, page1Text?.Length ?? 0))}");
    return 1;
}

// For simplicity, just redact a fixed area where "Birth" typically appears
// (In real GUI, user would select with mouse)
var redactionArea = new Avalonia.Rect(100, 100, 100, 30); // Example area

Console.WriteLine($"Marking redaction area: {redactionArea}");
await viewModel.MarkRedactionCommand.Execute(redactionArea);

Console.WriteLine($"Pending redactions: {viewModel.PendingRedactions.Count}");
Console.WriteLine();

// Apply all redactions
Console.WriteLine("Applying redactions...");
await viewModel.ApplyAllRedactionsCommand.Execute();

Console.WriteLine("✅ Redactions applied");
Console.WriteLine();

// Save the document
Console.WriteLine($"Saving to: {outputPath}");
await viewModel.SaveAsCommand.Execute(outputPath);

Console.WriteLine("✅ Document saved");
Console.WriteLine();

// Extract text AFTER redaction
Console.WriteLine("Extracting text AFTER redaction...");
string textAfter;
using (var doc = PdfDocument.Open(outputPath))
{
    var page = doc.GetPage(1);
    textAfter = string.Join("", page.Letters.Select(l => l.Value));
}

Console.WriteLine($"Text length AFTER: {textAfter.Length}");
Console.WriteLine($"First 200 chars: {textAfter.Substring(0, Math.Min(200, textAfter.Length))}");
Console.WriteLine();

// Check for corruption
Console.WriteLine("==============================================");
Console.WriteLine("Corruption Check");
Console.WriteLine("==============================================");

// Check 1: Text should be shorter (we removed something)
if (textAfter.Length >= textBefore.Length)
{
    Console.WriteLine($"⚠️ WARNING: Text length INCREASED or stayed same ({textBefore.Length} → {textAfter.Length})");
}
else
{
    Console.WriteLine($"✅ Text length decreased ({textBefore.Length} → {textAfter.Length})");
}

// Check 2: Look for the specific corruption pattern from user's log
var corruptionPatterns = new[] { "hidceenrttiiffiiccaattieon", "ii", "ff", "cc", "aa", "tt", "ee" };
var foundCorruption = false;

foreach (var pattern in corruptionPatterns)
{
    if (textAfter.Contains(pattern))
    {
        Console.WriteLine($"❌ CORRUPTION DETECTED: Found pattern '{pattern}'");
        foundCorruption = true;
    }
}

if (!foundCorruption)
{
    Console.WriteLine("✅ No known corruption patterns found");
}

// Check 3: Character frequency comparison
var charCountsBefore = textBefore.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
var charCountsAfter = textAfter.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

var doubledChars = 0;
foreach (var (character, countAfter) in charCountsAfter)
{
    if (charCountsBefore.ContainsKey(character))
    {
        var countBefore = charCountsBefore[character];
        if (countAfter > countBefore)
        {
            Console.WriteLine($"⚠️ Character '{character}' increased: {countBefore} → {countAfter}");
            doubledChars++;
        }
    }
}

if (doubledChars == 0)
{
    Console.WriteLine("✅ No character doubling detected");
}
else
{
    Console.WriteLine($"❌ {doubledChars} characters showed increased frequency (possible doubling)");
}

Console.WriteLine();
Console.WriteLine("==============================================");
Console.WriteLine("Test Complete");
Console.WriteLine("==============================================");

if (foundCorruption || doubledChars > 0)
{
    Console.WriteLine("❌ CORRUPTION DETECTED - Fix did not work");
    return 1;
}
else
{
    Console.WriteLine("✅ NO CORRUPTION - Fix appears to work!");
    return 0;
}
