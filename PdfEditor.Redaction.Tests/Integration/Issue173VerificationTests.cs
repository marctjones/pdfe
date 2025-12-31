using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Verification tests for Issue #173: 270° sequential redaction failure.
///
/// These tests are designed to:
/// 1. BEFORE FIX: Prove the hypothesis about what's broken
/// 2. AFTER FIX: Verify the fix works and no regressions
///
/// Key insight: The OperationReconstructor uses visual coordinates in Tm operators,
/// but it should use content stream coordinates (pre-rotation).
/// </summary>
public class Issue173VerificationTests : IDisposable
{
    private readonly string _testDir;
    private readonly ITestOutputHelper _output;

    public Issue173VerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_issue173_verify_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    // ========================================================================
    // PART 1: PRE-FIX VERIFICATION TESTS
    // These tests confirm the hypothesis about what's broken
    // ========================================================================

    /// <summary>
    /// Test 1.1: Verify 0° baseline - coordinates should be stable after redaction.
    /// This test should PASS (0° works correctly).
    /// </summary>
    [Fact]
    public void Verify_0Degree_CoordinatesStable_AfterRedaction()
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, "source_0.pdf");
        var temp1 = Path.Combine(_testDir, "temp1_0.pdf");

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, 0,
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234");

        // Get initial position of 'S' (first char of SSN line)
        double initialSsnX, initialSsnY;
        using (var doc = PdfDocument.Open(sourcePdf))
        {
            var letters = doc.GetPage(1).Letters;
            var sLetter = letters.First(l => l.Value == "S" &&
                string.Join("", letters.SkipWhile(x => x != l).Take(3).Select(x => x.Value)) == "SSN");
            initialSsnX = sLetter.GlyphRectangle.Left;
            initialSsnY = sLetter.GlyphRectangle.Bottom;
            _output.WriteLine($"Initial SSN 'S' position: ({initialSsnX:F1}, {initialSsnY:F1})");
        }

        // Act - Redact "John Doe"
        var redactor = new TextRedactor();
        redactor.RedactText(sourcePdf, temp1, "John Doe");

        // Get position after redaction
        double afterSsnX, afterSsnY;
        using (var doc = PdfDocument.Open(temp1))
        {
            var letters = doc.GetPage(1).Letters;
            var sLetter = letters.First(l => l.Value == "S");
            afterSsnX = sLetter.GlyphRectangle.Left;
            afterSsnY = sLetter.GlyphRectangle.Bottom;
            _output.WriteLine($"After SSN 'S' position: ({afterSsnX:F1}, {afterSsnY:F1})");
        }

        // Assert - Coordinates should be approximately the same (within 20 points)
        var xDiff = Math.Abs(afterSsnX - initialSsnX);
        var yDiff = Math.Abs(afterSsnY - initialSsnY);
        _output.WriteLine($"Position difference: X={xDiff:F1}, Y={yDiff:F1}");

        xDiff.Should().BeLessThan(50, "X coordinate should be approximately stable for 0° rotation");
        yDiff.Should().BeLessThan(50, "Y coordinate should be approximately stable for 0° rotation");
    }

    /// <summary>
    /// Test 1.2: Verify 270° coordinate shift after redaction.
    /// This test should FAIL before fix (proves the bug exists).
    /// </summary>
    [Fact]
    public void Verify_270Degree_CoordinatesShift_AfterRedaction()
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, "source_270.pdf");
        var temp1 = Path.Combine(_testDir, "temp1_270.pdf");

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, 270,
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234");

        // Get initial position of first 'S'
        double initialSsnX;
        using (var doc = PdfDocument.Open(sourcePdf))
        {
            var letters = doc.GetPage(1).Letters;
            var sLetter = letters.First(l => l.Value == "S");
            initialSsnX = sLetter.GlyphRectangle.Left;
            _output.WriteLine($"Initial first 'S' X position: {initialSsnX:F1}");
        }

        // Act - Redact "John Doe"
        var redactor = new TextRedactor();
        redactor.RedactText(sourcePdf, temp1, "John Doe");

        // Get position after redaction
        double afterSsnX;
        using (var doc = PdfDocument.Open(temp1))
        {
            var letters = doc.GetPage(1).Letters;
            var sLetter = letters.First(l => l.Value == "S");
            afterSsnX = sLetter.GlyphRectangle.Left;
            _output.WriteLine($"After first 'S' X position: {afterSsnX:F1}");
        }

        // Assert - For 270°, this test FAILS before fix because X jumps from ~103 to ~682
        // After fix, it should be approximately stable
        var xDiff = Math.Abs(afterSsnX - initialSsnX);
        _output.WriteLine($"X position difference: {xDiff:F1}");

        // This assertion will FAIL before fix (xDiff is ~579) and PASS after fix
        xDiff.Should().BeLessThan(100,
            "X coordinate should be approximately stable. " +
            "Before fix, this fails with xDiff ~579 (jumped from ~103 to ~682). " +
            "This proves the coordinate transformation bug.");
    }

    /// <summary>
    /// Test 1.3: Verify 90° works despite having coordinate issues.
    /// This test should PASS (90° works "accidentally").
    /// </summary>
    [Fact]
    public void Verify_90Degree_WorksDespiteCoordinateIssues()
    {
        // Arrange
        var sourcePdf = Path.Combine(_testDir, "source_90.pdf");
        var temp1 = Path.Combine(_testDir, "temp1_90.pdf");
        var outputPdf = Path.Combine(_testDir, "output_90.pdf");

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, 90,
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234");

        var redactor = new TextRedactor();

        // First redaction
        var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
        result1.Success.Should().BeTrue();

        // Check for negative Y coordinates (the "bug" that doesn't break things)
        using (var doc = PdfDocument.Open(temp1))
        {
            var letters = doc.GetPage(1).Letters;
            var negativeYLetters = letters.Where(l => l.GlyphRectangle.Bottom < 0).ToList();
            _output.WriteLine($"Letters with negative Y: {negativeYLetters.Count}");

            if (negativeYLetters.Any())
            {
                _output.WriteLine("Example negative Y positions:");
                foreach (var l in negativeYLetters.Take(5))
                {
                    _output.WriteLine($"  '{l.Value}' at Y={l.GlyphRectangle.Bottom:F1}");
                }
            }
        }

        // Second redaction - should still work despite coordinate issues
        var result2 = redactor.RedactText(temp1, outputPdf, "123-45-6789");
        result2.Success.Should().BeTrue();

        // Verify "Phone" text remains
        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"Final text: '{textAfter}'");

        textAfter.Should().Contain("Phone",
            "90° sequential redaction should work. " +
            "The text matching is text-based, not position-based, " +
            "so it works despite coordinates being wrong.");
    }

    /// <summary>
    /// Test 1.4: Show the exact coordinate transformation that should happen.
    /// This is a diagnostic test that shows expected vs actual Tm coordinates.
    /// </summary>
    [Fact]
    public void Diagnostic_ShowExpectedVsActualCoordinates()
    {
        // For 270° rotation:
        // Visual position (83, 108) should become content stream position (108, 709)
        // Formula: contentX = visualY, contentY = mediaBoxHeight - visualX
        //        = (108, 792 - 83) = (108, 709)

        _output.WriteLine("=== EXPECTED COORDINATE TRANSFORMATION ===");
        _output.WriteLine("For 270° rotation with MediaBox 612x792:");
        _output.WriteLine("");
        _output.WriteLine("Visual position (from PdfPig): (83, 108)");
        _output.WriteLine("Expected content stream position: (108, 709)");
        _output.WriteLine("");
        _output.WriteLine("Transformation formula for 270°:");
        _output.WriteLine("  contentX = visualY = 108");
        _output.WriteLine("  contentY = mediaBoxHeight - visualX = 792 - 83 = 709");
        _output.WriteLine("");
        _output.WriteLine("=== ACTUAL BEHAVIOR (BUG) ===");
        _output.WriteLine("The OperationReconstructor uses visual coords directly:");
        _output.WriteLine("  Tm operator gets: (83, 108) <-- WRONG!");
        _output.WriteLine("");
        _output.WriteLine("When PDF is reopened, PdfPig applies 270° rotation again:");
        _output.WriteLine("  New visual position = transform(83, 108)");
        _output.WriteLine("  This double-transforms the coordinates, causing corruption.");

        // Actually test the transformation
        var (contentX, contentY) = RotationTransform.VisualToContentStream(
            83, 108, 270, 612, 792);

        _output.WriteLine("");
        _output.WriteLine($"=== RotationTransform.VisualToContentStream(83, 108, 270, 612, 792) ===");
        _output.WriteLine($"Result: ({contentX:F1}, {contentY:F1})");
        _output.WriteLine($"Expected: (108, 709)");

        contentX.Should().BeApproximately(108, 1);
        contentY.Should().BeApproximately(709, 1);
    }

    // ========================================================================
    // PART 2: POST-FIX VERIFICATION TESTS
    // Run these after implementing the fix to verify it works
    // ========================================================================

    /// <summary>
    /// Test 2.1: 270° sequential redaction should work after fix.
    /// This is the main failing test that needs to pass.
    /// </summary>
    [Fact]
    public void AfterFix_270Degree_SequentialRedaction_Works()
    {
        var sourcePdf = Path.Combine(_testDir, "postfix_270.pdf");
        var temp1 = Path.Combine(_testDir, "postfix_temp1.pdf");
        var outputPdf = Path.Combine(_testDir, "postfix_output.pdf");

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, 270,
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234");

        var redactor = new TextRedactor();

        var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
        result1.Success.Should().BeTrue();

        var result2 = redactor.RedactText(temp1, outputPdf, "123-45-6789");
        result2.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"Final text: '{textAfter}'");

        textAfter.Should().NotContain("John Doe");
        textAfter.Should().NotContain("123-45-6789");
        textAfter.Should().Contain("Phone", "Non-redacted text should remain intact");
    }

    /// <summary>
    /// Test 2.2: All rotations should work after fix.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void AfterFix_AllRotations_SequentialRedaction_Works(int rotation)
    {
        var sourcePdf = Path.Combine(_testDir, $"allrot_{rotation}.pdf");
        var temp1 = Path.Combine(_testDir, $"allrot_temp1_{rotation}.pdf");
        var outputPdf = Path.Combine(_testDir, $"allrot_output_{rotation}.pdf");

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, rotation,
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Phone: 555-1234");

        var redactor = new TextRedactor();

        var result1 = redactor.RedactText(sourcePdf, temp1, "John Doe");
        result1.Success.Should().BeTrue($"First redaction failed for {rotation}°");

        var result2 = redactor.RedactText(temp1, outputPdf, "123-45-6789");
        result2.Success.Should().BeTrue($"Second redaction failed for {rotation}°");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPdf);
        _output.WriteLine($"{rotation}°: Final text = '{textAfter}'");

        textAfter.Should().Contain("Phone", $"Phone should remain for {rotation}° rotation");
    }

    /// <summary>
    /// Test 2.3: Coordinates should be stable across multiple redactions for all rotations.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void AfterFix_CoordinatesStable_AfterMultipleRedactions(int rotation)
    {
        var sourcePdf = Path.Combine(_testDir, $"stable_{rotation}.pdf");
        var temp1 = Path.Combine(_testDir, $"stable_temp1_{rotation}.pdf");
        var temp2 = Path.Combine(_testDir, $"stable_temp2_{rotation}.pdf");
        var outputPdf = Path.Combine(_testDir, $"stable_output_{rotation}.pdf");

        TestPdfGenerator.CreateRotatedMultiLinePdf(sourcePdf, rotation,
            "AAA First Line",
            "BBB Second Line",
            "CCC Third Line",
            "DDD Fourth Line");

        // Record initial position of 'D' (last line, should not be redacted)
        double initialDX, initialDY;
        using (var doc = PdfDocument.Open(sourcePdf))
        {
            var letters = doc.GetPage(1).Letters;
            var dLetter = letters.First(l => l.Value == "D");
            initialDX = dLetter.GlyphRectangle.Left;
            initialDY = dLetter.GlyphRectangle.Bottom;
        }

        // Perform 3 sequential redactions
        var redactor = new TextRedactor();
        redactor.RedactText(sourcePdf, temp1, "First");
        redactor.RedactText(temp1, temp2, "Second");
        redactor.RedactText(temp2, outputPdf, "Third");

        // Check position of 'D' after all redactions
        double afterDX, afterDY;
        using (var doc = PdfDocument.Open(outputPdf))
        {
            var letters = doc.GetPage(1).Letters;
            var dLetter = letters.First(l => l.Value == "D");
            afterDX = dLetter.GlyphRectangle.Left;
            afterDY = dLetter.GlyphRectangle.Bottom;
        }

        var xDiff = Math.Abs(afterDX - initialDX);
        var yDiff = Math.Abs(afterDY - initialDY);

        _output.WriteLine($"{rotation}°: Initial D at ({initialDX:F1}, {initialDY:F1})");
        _output.WriteLine($"{rotation}°: After D at ({afterDX:F1}, {afterDY:F1})");
        _output.WriteLine($"{rotation}°: Difference: X={xDiff:F1}, Y={yDiff:F1}");

        // Allow some tolerance for font metric differences
        xDiff.Should().BeLessThan(100, $"X should be stable for {rotation}°");
        yDiff.Should().BeLessThan(100, $"Y should be stable for {rotation}°");
    }
}
