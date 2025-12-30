using FluentAssertions;
using PdfEditor.Redaction;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class VeraPdfValidatorTests
{
    [Fact]
    public void IsAvailable_ReturnsBoolean()
    {
        // Act - just check it doesn't throw
        var result = VeraPdfValidator.IsAvailable();

        // Assert - it's either available or not (both are valid)
        (result == true || result == false).Should().BeTrue();
    }

    [Fact]
    public void Validate_NonExistentFile_ReturnsError()
    {
        // Arrange
        var nonExistentPath = "/tmp/does_not_exist_12345.pdf";

        // Act
        var result = VeraPdfValidator.Validate(nonExistentPath);

        // Assert
        result.IsCompliant.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public void Validate_ValidPdf_ReturnsResult()
    {
        Skip.IfNot(VeraPdfValidator.IsAvailable(), "veraPDF not installed");

        // Arrange - Create a simple PDF
        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.Save(tempFile);
            }

            // Act
            var result = VeraPdfValidator.Validate(tempFile, "0"); // Auto-detect

            // Assert - We get a result (may or may not be compliant)
            result.Should().NotBeNull();
            result.RawOutput.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetPath_ReturnsPathIfAvailable()
    {
        // Act
        if (VeraPdfValidator.IsAvailable())
        {
            var path = VeraPdfValidator.GetPath();
            path.Should().NotBeNullOrEmpty();
        }
        else
        {
            var path = VeraPdfValidator.GetPath();
            path.Should().BeNull();
        }
    }

    [SkippableFact]
    public void ValidateWithDetails_ReturnsDetailedReport()
    {
        Skip.IfNot(VeraPdfValidator.IsAvailable(), "veraPDF not installed");

        // Arrange
        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.Save(tempFile);
            }

            // Act
            var result = VeraPdfValidator.ValidateWithDetails(tempFile);

            // Assert
            result.Should().NotBeNull();
            result.RawOutput.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
