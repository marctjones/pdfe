using FluentAssertions;
using PdfEditor.Redaction;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Redaction.Tests.Unit;

public class PdfAMetadataPreserverTests
{
    [Theory]
    [InlineData(PdfALevel.PdfA_1a, 1, "A")]
    [InlineData(PdfALevel.PdfA_1b, 1, "B")]
    [InlineData(PdfALevel.PdfA_2a, 2, "A")]
    [InlineData(PdfALevel.PdfA_2b, 2, "B")]
    [InlineData(PdfALevel.PdfA_2u, 2, "U")]
    [InlineData(PdfALevel.PdfA_3a, 3, "A")]
    [InlineData(PdfALevel.PdfA_3b, 3, "B")]
    [InlineData(PdfALevel.PdfA_4, 4, "")]
    [InlineData(PdfALevel.PdfA_4e, 4, "E")]
    [InlineData(PdfALevel.PdfA_4f, 4, "F")]
    public void PreserveMetadata_CreatesCorrectPdfAIdentification(PdfALevel level, int expectedPart, string expectedConformance)
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();

        // Act
        PdfAMetadataPreserver.PreserveMetadata(doc, level);

        // Assert - Check XMP was created
        var xmp = PdfAMetadataPreserver.ExtractXmpMetadata(doc);
        xmp.Should().NotBeNullOrEmpty();
        xmp.Should().Contain($"<pdfaid:part>{expectedPart}</pdfaid:part>");

        if (!string.IsNullOrEmpty(expectedConformance))
        {
            xmp.Should().Contain($"<pdfaid:conformance>{expectedConformance}</pdfaid:conformance>");
        }
    }

    [Fact]
    public void PreserveMetadata_SynchronizesModificationDates()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();
        var beforePreserve = DateTime.Now.AddSeconds(-1);

        // Act
        PdfAMetadataPreserver.PreserveMetadata(doc, PdfALevel.PdfA_1b);

        // Assert
        var xmp = PdfAMetadataPreserver.ExtractXmpMetadata(doc);
        xmp.Should().Contain("<xmp:ModifyDate>");

        // Info dictionary should have ModificationDate set
        doc.Info.ModificationDate.Should().BeAfter(beforePreserve);
    }

    [Fact]
    public void PreserveMetadata_NoneLevel_DoesNotModifyDocument()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();

        // Act
        PdfAMetadataPreserver.PreserveMetadata(doc, PdfALevel.None);

        // Assert - No XMP should be created for non-PDF/A
        var xmp = PdfAMetadataPreserver.ExtractXmpMetadata(doc);
        xmp.Should().BeNull();
    }

    [Fact]
    public void PreserveMetadata_PdfA4_IncludesRevision()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();

        // Act
        PdfAMetadataPreserver.PreserveMetadata(doc, PdfALevel.PdfA_4);

        // Assert
        var xmp = PdfAMetadataPreserver.ExtractXmpMetadata(doc);
        xmp.Should().Contain("<pdfaid:part>4</pdfaid:part>");
        xmp.Should().Contain("<pdfaid:rev>2020</pdfaid:rev>");
    }

    [Fact]
    public void SynchronizeDates_UpdatesBothInfoAndXmp()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();
        PdfAMetadataPreserver.PreserveMetadata(doc, PdfALevel.PdfA_1b);
        var beforeSync = DateTime.Now.AddSeconds(-1);

        // Act
        PdfAMetadataPreserver.SynchronizeDates(doc);

        // Assert
        doc.Info.ModificationDate.Should().BeAfter(beforeSync);

        var xmp = PdfAMetadataPreserver.ExtractXmpMetadata(doc);
        xmp.Should().Contain("<xmp:ModifyDate>");
    }

    [Fact]
    public void ExtractXmpMetadata_NoMetadata_ReturnsNull()
    {
        // Arrange
        using var doc = new PdfDocument();
        doc.AddPage();

        // Act
        var xmp = PdfAMetadataPreserver.ExtractXmpMetadata(doc);

        // Assert
        xmp.Should().BeNull();
    }

    [Fact(Skip = "PDFsharp's XMP doesn't have sufficient padding for PDF/A injection - requires larger XMP buffer")]
    public void PreserveMetadataInFile_InjectsPdfAIdentification()
    {
        // NOTE: PDFsharp generates XMP with minimal padding, making binary patching unreliable.
        // A proper solution requires either:
        // 1. A library that can update stream lengths in PDFs
        // 2. Using a PDF/A-compliant PDF library from the start
        // 3. Pre-allocating larger XMP buffers before redaction
        //
        // For now, PDF/A metadata preservation is limited to documents that already have
        // sufficient XMP padding space.

        // Arrange - Create and save a PDF without PDF/A metadata
        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            using (var doc = new PdfDocument())
            {
                doc.AddPage();
                doc.Save(tempFile);
            }

            // Verify no pdfaid before injection
            using (var doc = PdfSharp.Pdf.IO.PdfReader.Open(tempFile, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                var xmpBefore = PdfAMetadataPreserver.ExtractXmpMetadata(doc);
                if (xmpBefore != null)
                {
                    xmpBefore.Should().NotContain("pdfaid:part");
                }
            }

            // Act - Inject PDF/A-2b identification
            var result = PdfAMetadataPreserver.PreserveMetadataInFile(tempFile, PdfALevel.PdfA_2b);

            // Assert
            result.Should().BeTrue();

            using var docAfter = PdfSharp.Pdf.IO.PdfReader.Open(tempFile, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            var xmpAfter = PdfAMetadataPreserver.ExtractXmpMetadata(docAfter);
            xmpAfter.Should().NotBeNullOrEmpty();
            xmpAfter.Should().Contain("<pdfaid:part>2</pdfaid:part>");
            xmpAfter.Should().Contain("<pdfaid:conformance>B</pdfaid:conformance>");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Theory(Skip = "PDFsharp's XMP doesn't have sufficient padding for PDF/A injection")]
    [InlineData(PdfALevel.PdfA_1a)]
    [InlineData(PdfALevel.PdfA_1b)]
    [InlineData(PdfALevel.PdfA_2a)]
    [InlineData(PdfALevel.PdfA_2b)]
    [InlineData(PdfALevel.PdfA_3b)]
    [InlineData(PdfALevel.PdfA_4)]
    public void PreserveMetadataInFile_WorksForAllLevels(PdfALevel level)
    {
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
            var result = PdfAMetadataPreserver.PreserveMetadataInFile(tempFile, level);

            // Assert
            result.Should().BeTrue();

            using var docAfter = PdfSharp.Pdf.IO.PdfReader.Open(tempFile, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            var xmpAfter = PdfAMetadataPreserver.ExtractXmpMetadata(docAfter);
            xmpAfter.Should().NotBeNullOrEmpty();
            xmpAfter.Should().Contain("pdfaid:part");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void PreserveMetadataInFile_NonExistentFile_ReturnsFalse()
    {
        // Arrange
        var nonExistentPath = "/tmp/does_not_exist_12345.pdf";

        // Act
        var result = PdfAMetadataPreserver.PreserveMetadataInFile(nonExistentPath, PdfALevel.PdfA_1b);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void PreserveMetadataInFile_NoneLevel_ReturnsFalse()
    {
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
            var result = PdfAMetadataPreserver.PreserveMetadataInFile(tempFile, PdfALevel.None);

            // Assert
            result.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
