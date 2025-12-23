using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using Xunit;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Integration tests simulating birth certificate redaction scenarios.
/// These tests verify the redaction library handles common PII patterns
/// found in official documents like birth certificates.
/// </summary>
public class BirthCertificateRedactionTests : IDisposable
{
    private readonly string _tempDir;

    public BirthCertificateRedactionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"birth_cert_tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void RedactBirthCertificate_RemovesChildName()
    {
        // Arrange - Create a birth certificate-like document
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact the child's name
        var result = redactor.RedactText(inputPath, outputPath, "JANE DOE");

        // Assert
        result.Success.Should().BeTrue();
        result.RedactionCount.Should().BeGreaterOrEqualTo(1);

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("JANE DOE", "Child name must be removed from PDF structure");

        // Output should be a valid PDF
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactBirthCertificate_RemovesSocialSecurityNumber()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact the SSN
        var result = redactor.RedactText(inputPath, outputPath, "123-45-6789");

        // Assert
        result.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("123-45-6789", "SSN must be removed from PDF structure");
    }

    [Fact]
    public void RedactBirthCertificate_RemovesDateOfBirth()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact the date of birth
        var result = redactor.RedactText(inputPath, outputPath, "01/15/2020");

        // Assert
        result.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("01/15/2020", "Date of birth must be removed from PDF structure");
    }

    [Fact]
    public void RedactBirthCertificate_RemovesHospitalName()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact the hospital name
        var result = redactor.RedactText(inputPath, outputPath, "General Hospital");

        // Assert
        result.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("General Hospital", "Hospital name must be removed from PDF structure");
    }

    [Fact]
    public void RedactBirthCertificate_RemovesParentNames()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact mother's name
        var result1 = redactor.RedactText(inputPath, outputPath, "MARY DOE");

        // Assert
        result1.Success.Should().BeTrue();

        // Redact father's name
        var result2 = redactor.RedactText(outputPath, outputPath, "JOHN DOE");
        result2.Success.Should().BeTrue();

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("MARY DOE", "Mother's name must be removed");
        textAfter.Should().NotContain("JOHN DOE", "Father's name must be removed");
    }

    [Fact]
    public void RedactBirthCertificate_MultiplePIIFields()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact the first field
        var result = redactor.RedactText(inputPath, outputPath, "JANE DOE");

        // Assert
        result.Success.Should().BeTrue("Redaction of 'JANE DOE' should succeed");

        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("JANE DOE", "'JANE DOE' must be removed from PDF structure");

        // Verify PDF is still valid
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactBirthCertificate_PreservesPdfStructure()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var pageCountBefore = PdfTestHelpers.GetPageCount(inputPath);
        var sizeBefore = PdfTestHelpers.GetPageSize(inputPath);

        var redactor = new TextRedactor();

        // Act - Redact a specific name
        var result = redactor.RedactText(inputPath, outputPath, "JANE DOE");

        // Assert - PDF structure should be preserved
        result.Success.Should().BeTrue();

        var pageCountAfter = PdfTestHelpers.GetPageCount(outputPath);
        var sizeAfter = PdfTestHelpers.GetPageSize(outputPath);

        pageCountAfter.Should().Be(pageCountBefore, "Page count should be preserved");
        sizeAfter.Width.Should().Be(sizeBefore.Width, "Page width should be preserved");
        sizeAfter.Height.Should().Be(sizeBefore.Height, "Page height should be preserved");

        // The redacted text should be gone
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("JANE DOE", "Name must be removed from PDF structure");

        // Output should be a valid PDF
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactBirthCertificate_SequentialRedactionsPreserveValidity()
    {
        // Arrange - Test that sequential redactions result in valid PDFs
        var inputPath = Path.Combine(_tempDir, "birth_cert.pdf");
        var outputPath = Path.Combine(_tempDir, "birth_cert_redacted.pdf");

        CreateBirthCertificatePdf(inputPath);

        var textBefore = PdfTestHelpers.ExtractAllText(inputPath);
        textBefore.Should().Contain("JANE DOE", "Input should contain the name");

        var redactor = new TextRedactor();

        // Act - Redact the name
        var result = redactor.RedactText(inputPath, outputPath, "JANE DOE");

        // Assert - Should succeed and produce valid PDF
        result.Success.Should().BeTrue($"Redaction should succeed, but got error: {result.ErrorMessage}");
        File.Exists(outputPath).Should().BeTrue("Output file should be created");
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue("Output should be a valid PDF");

        // Verify text was redacted
        var textAfter = PdfTestHelpers.ExtractAllText(outputPath);
        textAfter.Should().NotContain("JANE DOE", "Name should be redacted");
    }

    /// <summary>
    /// Creates a sample birth certificate-like PDF for testing.
    /// </summary>
    private void CreateBirthCertificatePdf(string outputPath)
    {
        TestPdfGenerator.CreateMultiLineTextPdf(outputPath,
            "CERTIFICATE OF LIVE BIRTH",
            "State of Testing",
            "Certificate Number: CERT-2020-123456",
            "CHILD INFORMATION",
            "Name: JANE DOE",
            "Date of Birth: 01/15/2020",
            "Time of Birth: 10:30 AM",
            "Sex: Female",
            "Place of Birth: General Hospital",
            "City: Springfield",
            "PARENT INFORMATION",
            "Mother: MARY DOE",
            "Father: JOHN DOE",
            "REGISTRATION",
            "SSN Assigned: 123-45-6789",
            "Registrar: State Vital Records Office"
        );
    }
}
