using Xunit;
using FluentAssertions;
using PdfEditor.Redaction;

namespace PdfEditor.Redaction.Tests.Unit;

/// <summary>
/// Tests for PDF/A level detection.
/// </summary>
public class PdfADetectorTests
{
    #region XMP Parsing Tests

    [Fact]
    public void ParseFromXmp_PdfA1a_DetectsCorrectly()
    {
        // Arrange
        var xmp = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<x:xmpmeta xmlns:x=""adobe:ns:meta/"">
  <rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#"">
    <rdf:Description rdf:about="""" xmlns:pdfaid=""http://www.aiim.org/pdfa/ns/id/"">
      <pdfaid:part>1</pdfaid:part>
      <pdfaid:conformance>A</pdfaid:conformance>
    </rdf:Description>
  </rdf:RDF>
</x:xmpmeta>";

        // Act
        var level = PdfADetector.ParseFromXmp(xmp);

        // Assert
        level.Should().Be(PdfALevel.PdfA_1a);
    }

    [Fact]
    public void ParseFromXmp_PdfA1b_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>1</pdfaid:part><pdfaid:conformance>B</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_1b);
    }

    [Fact]
    public void ParseFromXmp_PdfA2a_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>2</pdfaid:part><pdfaid:conformance>A</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_2a);
    }

    [Fact]
    public void ParseFromXmp_PdfA2b_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>2</pdfaid:part><pdfaid:conformance>B</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_2b);
    }

    [Fact]
    public void ParseFromXmp_PdfA2u_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>2</pdfaid:part><pdfaid:conformance>U</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_2u);
    }

    [Fact]
    public void ParseFromXmp_PdfA3a_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>3</pdfaid:part><pdfaid:conformance>A</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_3a);
    }

    [Fact]
    public void ParseFromXmp_PdfA3b_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>3</pdfaid:part><pdfaid:conformance>B</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_3b);
    }

    [Fact]
    public void ParseFromXmp_PdfA3u_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>3</pdfaid:part><pdfaid:conformance>U</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_3u);
    }

    [Fact]
    public void ParseFromXmp_PdfA4_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>4</pdfaid:part>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_4);
    }

    [Fact]
    public void ParseFromXmp_PdfA4e_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>4</pdfaid:part><pdfaid:conformance>E</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_4e);
    }

    [Fact]
    public void ParseFromXmp_PdfA4f_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>4</pdfaid:part><pdfaid:conformance>F</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_4f);
    }

    [Fact]
    public void ParseFromXmp_NoPdfANamespace_ReturnsNone()
    {
        var xmp = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<x:xmpmeta xmlns:x=""adobe:ns:meta/"">
  <rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#"">
    <rdf:Description rdf:about="""">
      <dc:title>Test Document</dc:title>
    </rdf:Description>
  </rdf:RDF>
</x:xmpmeta>";

        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.None);
    }

    [Fact]
    public void ParseFromXmp_EmptyString_ReturnsNone()
    {
        var level = PdfADetector.ParseFromXmp("");
        level.Should().Be(PdfALevel.None);
    }

    [Fact]
    public void ParseFromXmp_Null_ReturnsNone()
    {
        var level = PdfADetector.ParseFromXmp(null!);
        level.Should().Be(PdfALevel.None);
    }

    [Fact]
    public void ParseFromXmp_LowercaseConformance_DetectsCorrectly()
    {
        var xmp = @"<pdfaid:part>1</pdfaid:part><pdfaid:conformance>a</pdfaid:conformance>";
        var level = PdfADetector.ParseFromXmp(xmp);
        level.Should().Be(PdfALevel.PdfA_1a);
    }

    #endregion

    #region Display Name Tests

    [Theory]
    [InlineData(PdfALevel.None, "Not PDF/A")]
    [InlineData(PdfALevel.PdfA_1a, "PDF/A-1a")]
    [InlineData(PdfALevel.PdfA_1b, "PDF/A-1b")]
    [InlineData(PdfALevel.PdfA_2a, "PDF/A-2a")]
    [InlineData(PdfALevel.PdfA_2b, "PDF/A-2b")]
    [InlineData(PdfALevel.PdfA_2u, "PDF/A-2u")]
    [InlineData(PdfALevel.PdfA_3a, "PDF/A-3a")]
    [InlineData(PdfALevel.PdfA_3b, "PDF/A-3b")]
    [InlineData(PdfALevel.PdfA_3u, "PDF/A-3u")]
    [InlineData(PdfALevel.PdfA_4, "PDF/A-4")]
    [InlineData(PdfALevel.PdfA_4e, "PDF/A-4e")]
    [InlineData(PdfALevel.PdfA_4f, "PDF/A-4f")]
    public void GetDisplayName_ReturnsCorrectName(PdfALevel level, string expected)
    {
        PdfADetector.GetDisplayName(level).Should().Be(expected);
    }

    #endregion

    #region ISO Standard Tests

    [Theory]
    [InlineData(PdfALevel.PdfA_1a, "ISO 19005-1")]
    [InlineData(PdfALevel.PdfA_1b, "ISO 19005-1")]
    [InlineData(PdfALevel.PdfA_2a, "ISO 19005-2")]
    [InlineData(PdfALevel.PdfA_2b, "ISO 19005-2")]
    [InlineData(PdfALevel.PdfA_2u, "ISO 19005-2")]
    [InlineData(PdfALevel.PdfA_3a, "ISO 19005-3")]
    [InlineData(PdfALevel.PdfA_3b, "ISO 19005-3")]
    [InlineData(PdfALevel.PdfA_3u, "ISO 19005-3")]
    [InlineData(PdfALevel.PdfA_4, "ISO 19005-4")]
    [InlineData(PdfALevel.PdfA_4e, "ISO 19005-4")]
    [InlineData(PdfALevel.PdfA_4f, "ISO 19005-4")]
    [InlineData(PdfALevel.None, "N/A")]
    public void GetIsoStandard_ReturnsCorrectStandard(PdfALevel level, string expected)
    {
        PdfADetector.GetIsoStandard(level).Should().Be(expected);
    }

    #endregion

    #region File Detection Tests

    [Fact]
    public void Detect_NonExistentFile_ReturnsNone()
    {
        var level = PdfADetector.Detect("/path/that/does/not/exist.pdf");
        level.Should().Be(PdfALevel.None);
    }

    #endregion
}
