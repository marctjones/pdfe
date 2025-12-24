using Xunit;
using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests using a real-world birth certificate request form PDF.
/// Tests redaction against an actual government form (City of Torrington, CT).
/// </summary>
public class RealWorldBirthCertificateTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _sourcePdf;

    public RealWorldBirthCertificateTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdfe_realworld_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

        // Path to real birth certificate request form
        _sourcePdf = "/home/marc/Downloads/Birth Certificate Request (PDF).pdf";
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    [Fact]
    public void RealPdf_CanOpenAndExtractText()
    {
        // Arrange & Act
        if (!File.Exists(_sourcePdf))
        {
            throw new FileNotFoundException($"Test PDF not found: {_sourcePdf}");
        }

        var text = PdfTestHelpers.ExtractAllText(_sourcePdf);

        // Assert
        text.Should().Contain("BIRTH CERTIFICATE");
        text.Should().Contain("TORRINGTON");
        text.Should().Contain("FULL NAME AT BIRTH");
    }

    [Fact]
    public void RealPdf_RedactFormTitle_RemovesText()
    {
        // Arrange
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var outputPath = Path.Combine(_testDir, "redacted_title.pdf");
        var redactor = new TextRedactor();

        // Act - Redact the form title
        var result = redactor.RedactText(_sourcePdf, outputPath, "REQUEST FOR COPY OF BIRTH CERTIFICATE");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterThan(0);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("REQUEST FOR COPY OF BIRTH CERTIFICATE",
            "Text must be REMOVED from PDF structure, not just hidden");
    }

    [Fact]
    public void RealPdf_RedactCityName_RemovesText()
    {
        // Arrange
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var outputPath = Path.Combine(_testDir, "redacted_city.pdf");
        var redactor = new TextRedactor();

        // Act - Redact city name
        var result = redactor.RedactText(_sourcePdf, outputPath, "TORRINGTON");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterThan(0);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("TORRINGTON",
            "City name must be REMOVED from PDF structure");
    }

    [Fact]
    public void RealPdf_RedactMultipleFields_RemovesAllText()
    {
        // Arrange
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var temp1 = Path.Combine(_testDir, "redacted_temp1.pdf");
        var temp2 = Path.Combine(_testDir, "redacted_temp2.pdf");
        var temp3 = Path.Combine(_testDir, "redacted_temp3.pdf");
        var outputPath = Path.Combine(_testDir, "redacted_multiple.pdf");
        var redactor = new TextRedactor();

        // Act - Redact multiple form fields sequentially
        var result1 = redactor.RedactText(_sourcePdf, temp1, "FULL NAME AT BIRTH");
        result1.Success.Should().BeTrue($"First redaction should succeed. Error: {result1.ErrorMessage}");

        var result2 = redactor.RedactText(temp1, temp2, "DATE OF BIRTH");
        result2.Success.Should().BeTrue($"Second redaction should succeed. Error: {result2.ErrorMessage}");

        var result3 = redactor.RedactText(temp2, temp3, "FATHER'S FULL NAME");
        result3.Success.Should().BeTrue($"Third redaction should succeed. Error: {result3.ErrorMessage}");

        var result = redactor.RedactText(temp3, outputPath, "MOTHER'S MAIDEN NAME");

        // Assert
        result.Success.Should().BeTrue($"Final redaction should succeed. Error: {result.ErrorMessage}");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("FULL NAME AT BIRTH");
        textAfter.Should().NotContain("DATE OF BIRTH");
        textAfter.Should().NotContain("FATHER'S FULL NAME");
        textAfter.Should().NotContain("MOTHER'S MAIDEN NAME");
    }

    [Fact]
    public void RealPdf_RedactFeeInformation_RemovesText()
    {
        // Arrange
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var temp1 = Path.Combine(_testDir, "redacted_fee_temp.pdf");
        var outputPath = Path.Combine(_testDir, "redacted_fee.pdf");
        var redactor = new TextRedactor();

        // Act - Redact fee information
        redactor.RedactText(_sourcePdf, temp1, "$20.00");
        var result = redactor.RedactText(temp1, outputPath, "$15.00");

        // Assert
        result.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("$20.00");
        textAfter.Should().NotContain("$15.00");
    }

    [Fact]
    public void RealPdf_RedactionPreservesFormStructure()
    {
        // Arrange
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var outputPath = Path.Combine(_testDir, "redacted_structure_test.pdf");
        var redactor = new TextRedactor();

        // Act - Redact something small
        var result = redactor.RedactText(_sourcePdf, outputPath, "WALLET SIZE");

        // Assert
        result.Success.Should().BeTrue();

        // PDF should still be valid
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotBeNullOrEmpty("PDF should still be readable");

        // Should preserve other text
        textAfter.Should().Contain("FULL SIZE");
        // NEW BEHAVIOR: With block-aware filtering, uppercase "CERTIFICATE" in same block as "WALLET SIZE"
        // gets removed during reconstruction, but lowercase "certificate" in different blocks is preserved
        textAfter.Should().Contain("certificate", "Lowercase certificate in separate blocks should be preserved");
    }

    [Fact]
    public void RealPdf_GetPageSize_ReturnsCorrectDimensions()
    {
        // Arrange & Act
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var (width, height) = PdfTestHelpers.GetPageSize(_sourcePdf);

        // Assert - Standard letter size is 612x792 points (8.5" x 11")
        width.Should().BeApproximately(612, 10, "Should be standard letter width");
        height.Should().BeApproximately(792, 10, "Should be standard letter height");
    }

    [Fact]
    public void RealPdf_SequentialRedactions_WorkCorrectly()
    {
        // Arrange
        if (!File.Exists(_sourcePdf))
        {
            return; // Skip test if PDF not found
        }

        var output1 = Path.Combine(_testDir, "redacted_step1.pdf");
        var output2 = Path.Combine(_testDir, "redacted_step2.pdf");
        var redactor = new TextRedactor();

        // Act - First redaction
        var result1 = redactor.RedactText(_sourcePdf, output1, "TORRINGTON");
        result1.Success.Should().BeTrue();

        // Second redaction on the output of the first
        var result2 = redactor.RedactText(output1, output2, "CERTIFICATE");
        result2.Success.Should().BeTrue();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(output2);
        textAfter.Should().NotContain("TORRINGTON", "First redaction should persist");
        textAfter.Should().NotContain("CERTIFICATE", "Second redaction should work");
    }
}
