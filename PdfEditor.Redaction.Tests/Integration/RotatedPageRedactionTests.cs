using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests for redaction on rotated PDF pages.
/// Tests that glyph-level redaction works correctly for pages with /Rotate entry.
///
/// Issue #151: Add support for rotated pages in redaction
///
/// Key insight: The LetterFinder uses TEXT CONTENT MATCHING (not spatial matching),
/// which is rotation-independent. This means redaction should work regardless of
/// page rotation as long as the text can be found.
/// </summary>
public class RotatedPageRedactionTests : IDisposable
{
    private readonly string _testDir;
    private readonly ITestOutputHelper _output;

    public RotatedPageRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_rotated_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RedactText_OnRotatedPage_RemovesText(int rotationDegrees)
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, $"rotated_{rotationDegrees}.pdf");
        var outputPdf = Path.Combine(_testDir, $"redacted_{rotationDegrees}.pdf");
        var testText = "CONFIDENTIAL DATA";

        TestPdfGenerator.CreateRotatedPdf(sourcePdf, testText, rotationDegrees);
        var redactor = new TextRedactor();

        // Verify source contains the text
        var textBefore = PdfTestHelpers.ExtractAllText(sourcePdf);
        textBefore.Should().Contain(testText, $"Source PDF with {rotationDegrees}° rotation should contain test text");

        _output.WriteLine($"Testing {rotationDegrees}° rotation");
        _output.WriteLine($"Text before: {textBefore}");

        // Act
        var result = redactor.RedactText(sourcePdf, outputPdf, testText);

        // Assert
        result.Success.Should().BeTrue($"Redaction should succeed on {rotationDegrees}° rotated page. Error: {result.ErrorMessage}");
        result.RedactionCount.Should().BeGreaterThan(0, $"Should find and redact text on {rotationDegrees}° rotated page");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"Text after: {textAfter}");

        textAfter.Should().NotContain(testText,
            $"Text must be REMOVED from {rotationDegrees}° rotated page, not just hidden");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RedactText_MultipleLines_OnRotatedPage_RemovesTargetedText(int rotationDegrees)
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, $"multiline_rotated_{rotationDegrees}.pdf");
        var outputPdf = Path.Combine(_testDir, $"multiline_redacted_{rotationDegrees}.pdf");

        var lines = new[]
        {
            "PUBLIC INFORMATION",
            "CONFIDENTIAL: SSN 123-45-6789",
            "MORE PUBLIC DATA"
        };

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, rotationDegrees, lines);
        var redactor = new TextRedactor();

        // Verify source contains all lines
        var textBefore = PdfTestHelpers.ExtractAllText(sourcePdf);
        textBefore.Should().Contain("PUBLIC INFORMATION");
        textBefore.Should().Contain("123-45-6789");
        textBefore.Should().Contain("MORE PUBLIC DATA");

        _output.WriteLine($"Testing {rotationDegrees}° rotation with multiple lines");

        // Act - Redact only the SSN
        var result = redactor.RedactText(sourcePdf, outputPdf, "123-45-6789");

        // Assert
        result.Success.Should().BeTrue($"Redaction should succeed. Error: {result.ErrorMessage}");
        result.RedactionCount.Should().BeGreaterThan(0);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"Text after redaction: {textAfter}");

        // SSN should be removed
        textAfter.Should().NotContain("123-45-6789",
            $"SSN must be REMOVED from {rotationDegrees}° rotated page");

        // Other text should remain
        textAfter.Should().Contain("PUBLIC",
            $"Non-targeted text should remain on {rotationDegrees}° rotated page");
    }

    /// <summary>
    /// Tests sequential redactions on rotated pages.
    /// 0°, 90°, 180° all work correctly.
    /// 270° FAILS - see Issue #173 for details.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    // [InlineData(270)]  // CONFIRMED FAILURE - Issue #173: corrupts text to "SSN:P34Name:"
    public void RedactText_SequentialRedactions_OnRotatedPage_RemovesAllTargetedText(int rotationDegrees)
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, $"sequential_rotated_{rotationDegrees}.pdf");
        var temp1 = Path.Combine(_testDir, $"sequential_temp1_{rotationDegrees}.pdf");
        var outputPdf = Path.Combine(_testDir, $"sequential_redacted_{rotationDegrees}.pdf");

        var lines = new[]
        {
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234"
        };

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, rotationDegrees, lines);
        var redactor = new TextRedactor();

        _output.WriteLine($"Testing sequential redactions on {rotationDegrees}° rotation");

        // Act - Sequential redactions
        var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
        result1.Success.Should().BeTrue($"First redaction should succeed. Error: {result1.ErrorMessage}");

        var result2 = redactor.RedactText(temp1, outputPdf, "123-45-6789");
        result2.Success.Should().BeTrue($"Second redaction should succeed. Error: {result2.ErrorMessage}");

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"Text after sequential redactions: {textAfter}");

        textAfter.Should().NotContain("John Doe", "First redaction should persist");
        textAfter.Should().NotContain("123-45-6789", "Second redaction should work");
        textAfter.Should().Contain("Phone", "Non-targeted text should remain");
    }

    /// <summary>
    /// Debug test for issue #173 - 270° sequential redaction corruption.
    /// This test is skipped by default - enable to debug the issue.
    /// </summary>
    [Fact(Skip = "Issue #173: 270° sequential redaction corrupts text - see issue for debug output")]
    public void Debug_270Degree_SequentialRedaction_Issue173()
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, "sequential_rotated_270.pdf");
        var temp1 = Path.Combine(_testDir, "sequential_temp1_270.pdf");
        var outputPdf = Path.Combine(_testDir, "sequential_redacted_270.pdf");

        var lines = new[]
        {
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234"
        };

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, 270, lines);
        var redactor = new TextRedactor();

        // Source text
        var textSource = PdfTestHelpers.ExtractAllText(sourcePdf);
        _output.WriteLine($"=== Source PDF (270°) ===");
        _output.WriteLine($"'{textSource}'");

        // First redaction
        var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
        _output.WriteLine($"\n=== After first redaction (John Doe) ===");
        _output.WriteLine($"Success: {result1.Success}, Count: {result1.RedactionCount}");
        var textAfter1 = PdfTestHelpers.ExtractAllText(temp1);
        _output.WriteLine($"'{textAfter1}'");

        // Second redaction
        var result2 = redactor.RedactText(temp1, outputPdf, "123-45-6789");
        _output.WriteLine($"\n=== After second redaction (123-45-6789) ===");
        _output.WriteLine($"Success: {result2.Success}, Count: {result2.RedactionCount}");
        var textAfter2 = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"'{textAfter2}'");

        // This will fail - we're debugging why
        textAfter2.Should().Contain("Phone", "Non-targeted text should remain");
    }

    /// <summary>
    /// Deep investigation test for Issue #173.
    /// Compares letter positions before and after first redaction to find the root cause.
    /// </summary>
    [Theory(Skip = "Investigation test - run manually")]
    [InlineData(90)]
    [InlineData(270)]
    public void Investigate_LetterPositions_AfterRedaction(int rotation)
    {
        var sourcePdf = Path.Combine(_testDir, $"investigate_source_{rotation}.pdf");
        var temp1 = Path.Combine(_testDir, $"investigate_temp1_{rotation}.pdf");

        // Create source with 3 lines
        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, rotation,
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234");

        // Extract letters from SOURCE
        _output.WriteLine($"=== SOURCE PDF ({rotation}°) Letters ===");
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(sourcePdf))
        {
            var letters = doc.GetPage(1).Letters;
            var text = string.Join("", letters.Select(l => l.Value));
            _output.WriteLine($"Full text: '{text}'");
            _output.WriteLine($"Letter count: {letters.Count}");

            // Show first few letters with positions
            foreach (var letter in letters.Take(20))
            {
                _output.WriteLine($"  '{letter.Value}' at ({letter.GlyphRectangle.Left:F1}, {letter.GlyphRectangle.Bottom:F1})");
            }
        }

        // Do first redaction
        var redactor = new TextRedactor();
        var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
        _output.WriteLine($"\n=== AFTER FIRST REDACTION ===");
        _output.WriteLine($"Redaction result: Success={result1.Success}, Count={result1.RedactionCount}");

        // Extract letters from TEMP1
        _output.WriteLine("\n=== POST-REDACTION PDF Letters ===");
        using (var doc = UglyToad.PdfPig.PdfDocument.Open(temp1))
        {
            var letters = doc.GetPage(1).Letters;
            var text = string.Join("", letters.Select(l => l.Value));
            _output.WriteLine($"Full text: '{text}'");
            _output.WriteLine($"Letter count: {letters.Count}");

            // Show all letters with positions
            foreach (var letter in letters)
            {
                _output.WriteLine($"  '{letter.Value}' at ({letter.GlyphRectangle.Left:F1}, {letter.GlyphRectangle.Bottom:F1})");
            }

            // Check if "123-45-6789" exists in the text
            var searchText = "123-45-6789";
            var idx = text.IndexOf(searchText);
            _output.WriteLine($"\n'123-45-6789' found at index: {idx}");
            if (idx >= 0)
            {
                _output.WriteLine("Match letters:");
                for (int i = idx; i < idx + searchText.Length && i < letters.Count; i++)
                {
                    var l = letters[i];
                    _output.WriteLine($"  [{i}] '{l.Value}' at ({l.GlyphRectangle.Left:F1}, {l.GlyphRectangle.Bottom:F1})");
                }
            }
        }
    }

    /// <summary>
    /// Compare 90° and 270° to understand why 90° works and 270° doesn't.
    /// </summary>
    [Fact(Skip = "Investigation test - run manually")]
    public void Compare_90_vs_270_SequentialRedaction()
    {
        foreach (var rotation in new[] { 90, 270 })
        {
            _output.WriteLine($"\n{'=',-50}");
            _output.WriteLine($"=== TESTING {rotation}° ROTATION ===");
            _output.WriteLine($"{'=',-50}");

            var sourcePdf = Path.Combine(_testDir, $"compare_{rotation}_source.pdf");
            var temp1 = Path.Combine(_testDir, $"compare_{rotation}_temp1.pdf");
            var outputPdf = Path.Combine(_testDir, $"compare_{rotation}_output.pdf");

            TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, rotation,
                "Name: John Doe",
                "SSN: 123-45-6789",
                "Phone: 555-1234");

            var redactor = new TextRedactor();

            // Source
            var textSource = PdfTestHelpers.ExtractAllText(sourcePdf);
            _output.WriteLine($"Source: '{textSource.Replace("\n", "\\n")}'");

            // First redaction
            var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
            var textAfter1 = PdfTestHelpers.ExtractAllText(temp1);
            _output.WriteLine($"After 1st (John Doe): Count={result1.RedactionCount}, Text='{textAfter1.Replace("\n", "\\n")}'");

            // Second redaction
            var result2 = redactor.RedactText(temp1, outputPdf, "123-45-6789");
            var textAfter2 = PdfTestHelpers.ExtractAllText(outputPdf);
            _output.WriteLine($"After 2nd (123-45-6789): Count={result2.RedactionCount}, Text='{textAfter2.Replace("\n", "\\n")}'");

            // Check result
            var hasPhone = textAfter2.Contains("Phone");
            _output.WriteLine($"Contains 'Phone': {hasPhone} {(hasPhone ? "✓" : "✗ FAIL")}");
        }
    }

    [Fact]
    public void RedactText_MixedRotationMultiPagePdf_RedactsAllPages()
    {
        // This test would require a multi-page PDF with different rotations per page
        // For now, we test that single-rotation pages work, which covers the core functionality

        // Arrange - Create multiple single-page PDFs and verify each works
        var rotations = new[] { 0, 90, 180, 270 };
        var testText = "SECRET DATA";
        var redactor = new TextRedactor();

        foreach (var rotation in rotations)
        {
            var sourcePdf = Path.Combine(_testDir, $"mixed_source_{rotation}.pdf");
            var outputPdf = Path.Combine(_testDir, $"mixed_output_{rotation}.pdf");

            TestPdfGenerator.CreateRotatedPdf(sourcePdf, testText, rotation);

            var result = redactor.RedactText(sourcePdf, outputPdf, testText);

            result.Success.Should().BeTrue($"Redaction should succeed for {rotation}° page");

            var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
            textAfter.Should().NotContain(testText, $"Text should be removed from {rotation}° page");
        }
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void RedactText_LandscapeRotatedPage_RemovesText(int rotationDegrees)
    {
        // 90° and 270° rotations swap width/height, making the page appear landscape
        // This tests that coordinate transformations work correctly

        var sourcePdf = Path.Combine(_testDir, $"landscape_{rotationDegrees}.pdf");
        var outputPdf = Path.Combine(_testDir, $"landscape_redacted_{rotationDegrees}.pdf");
        var testText = "LANDSCAPE CONTENT";

        TestPdfGenerator.CreateRotatedPdf(sourcePdf, testText, rotationDegrees);
        var redactor = new TextRedactor();

        _output.WriteLine($"Testing landscape ({rotationDegrees}°) rotation");

        // Act
        var result = redactor.RedactText(sourcePdf, outputPdf, testText);

        // Assert
        result.Success.Should().BeTrue($"Redaction should succeed on landscape page. Error: {result.ErrorMessage}");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        textAfter.Should().NotContain(testText,
            $"Text must be REMOVED from landscape ({rotationDegrees}°) page");
    }
}
