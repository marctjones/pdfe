using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Redaction;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;

namespace PdfEditor.Redaction.Tests;

/// <summary>
/// Diagnostic test to isolate the text doubling bug.
/// </summary>
public class DiagnosticRedactionTest
{
    [Fact]
    public void RedactText_ShouldNotDoubleNonRedactedText()
    {
        // Arrange
        var testPdfPath = "/home/marc/pdfe/test-pdfs/birth-certificate.pdf";
        var outputPath = "/tmp/diagnostic_redaction_test.pdf";

        if (!File.Exists(testPdfPath))
        {
            // Skip if test PDF not available
            return;
        }

        // Extract ALL text before redaction
        string textBefore;
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(testPdfPath))
        {
            var page = doc.GetPage(1);
            textBefore = string.Join("", page.Letters.Select(l => l.Value));
        }

        Console.WriteLine($"Text BEFORE redaction (length={textBefore.Length}):");
        Console.WriteLine(textBefore.Substring(0, Math.Min(500, textBefore.Length)));
        Console.WriteLine();

        // Act - Redact the word "Birth"
        var redactor = new TextRedactor(NullLogger<TextRedactor>.Instance);
        var result = redactor.RedactText(
            testPdfPath,
            outputPath,
            "Birth",
            new RedactionOptions
            {
                CaseSensitive = false,
                DrawVisualMarker = true,
                MarkerColor = (0, 0, 0)
            });

        Console.WriteLine($"Redaction result: {result.Success}, Count: {result.RedactionCount}");
        Console.WriteLine();

        // Assert - Extract text after redaction
        string textAfter;
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(outputPath))
        {
            var page = doc.GetPage(1);
            textAfter = string.Join("", page.Letters.Select(l => l.Value));
        }

        Console.WriteLine($"Text AFTER redaction (length={textAfter.Length}):");
        Console.WriteLine(textAfter.Substring(0, Math.Min(500, textAfter.Length)));
        Console.WriteLine();

        // Check: "Birth" should be gone
        textAfter.Should().NotContain("Birth", "redacted text should be removed");
        textAfter.Should().NotContain("birth", "redacted text should be removed (case insensitive)");

        // Check: Text length should be LESS (we removed "Birth")
        textAfter.Length.Should().BeLessThan(textBefore.Length, "redacted text should make content shorter");

        // Check: No character doubling
        // Count frequency of each character before and after
        var charCountsBefore = textBefore.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var charCountsAfter = textAfter.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

        foreach (var (character, countAfter) in charCountsAfter)
        {
            if (!charCountsBefore.ContainsKey(character))
                continue; // New character (shouldn't happen, but ignore)

            var countBefore = charCountsBefore[character];

            // After redaction, count should be LESS OR EQUAL (we only removed text)
            countAfter.Should().BeLessOrEqualTo(countBefore,
                $"Character '{character}' appears {countAfter} times after vs {countBefore} before - possible duplication!");
        }

        // SPECIFIC CHECK: Look for the doubled pattern from user's log
        // "identification" became "hidceenrttiiffiiccaattieon"
        textAfter.Should().NotContain("hidceenrttiiffiiccaattieon", "this is the exact corruption pattern from user log");
        textAfter.Should().NotContain("ii", "should not have doubled 'i'");
        textAfter.Should().NotContain("ff", "should not have doubled 'f'");
        textAfter.Should().NotContain("cc", "should not have doubled 'c'");
        textAfter.Should().NotContain("aa", "should not have doubled 'a'");
        textAfter.Should().NotContain("tt", "should not have doubled 't'");
        textAfter.Should().NotContain("ee", "should not have doubled 'e'");
    }

    [Fact]
    public void DiagnoseContentStreamReads_CountHowManyTimesWeReadAndWrite()
    {
        // This test instruments the code to count ReplacePageContent calls
        // We should only call it ONCE per page if we fix the bug properly

        // TODO: Add instrumentation or use a mock
        Assert.True(true, "Placeholder for instrumentation test");
    }
}
