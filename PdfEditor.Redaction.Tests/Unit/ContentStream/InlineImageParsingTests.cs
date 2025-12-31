using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Building;
using PdfEditor.Redaction.ContentStream.Parsing;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Unit.ContentStream;

/// <summary>
/// Tests for inline image (BI...ID...EI) parsing and serialization in the redaction library.
/// </summary>
public class InlineImageParsingTests
{
    private readonly ITestOutputHelper _output;
    private readonly ContentStreamParser _parser;
    private readonly ContentStreamBuilder _builder;

    public InlineImageParsingTests(ITestOutputHelper output)
    {
        _output = output;
        _parser = new ContentStreamParser();
        _builder = new ContentStreamBuilder();
    }

    [Fact]
    public void Parse_InlineImage_DetectsAndExtractsProperties()
    {
        // Arrange - Content stream with inline image
        var contentStream = @"q
100 0 0 100 50 50 cm
BI
/W 10 /H 10 /BPC 8 /CS /G
ID
0123456789
EI
Q
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);

        // Act
        var operations = _parser.Parse(bytes, 792);

        // Assert
        var inlineImageOps = operations.OfType<InlineImageOperation>().ToList();
        _output.WriteLine($"Found {inlineImageOps.Count} inline image operation(s)");

        inlineImageOps.Should().HaveCount(1, "should detect one inline image");

        var img = inlineImageOps[0];
        _output.WriteLine($"Image width: {img.ImageWidth}");
        _output.WriteLine($"Image height: {img.ImageHeight}");
        _output.WriteLine($"Bits per component: {img.BitsPerComponent}");
        _output.WriteLine($"Color space: {img.ColorSpace}");
        _output.WriteLine($"Raw bytes length: {img.RawBytes.Length}");

        img.ImageWidth.Should().Be(10);
        img.ImageHeight.Should().Be(10);
        img.BitsPerComponent.Should().Be(8);
        img.ColorSpace.Should().Be("/G");
    }

    [Fact]
    public void Parse_InlineImage_CalculatesBoundingBoxFromCTM()
    {
        // Arrange - Content stream with CTM transformation before inline image
        var contentStream = @"q
50 0 0 50 100 200 cm
BI
/W 10 /H 10 /BPC 8 /CS /G
ID
0123456789
EI
Q
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);

        // Act
        var operations = _parser.Parse(bytes, 792);

        // Assert
        var img = operations.OfType<InlineImageOperation>().First();
        _output.WriteLine($"Bounding box: ({img.BoundingBox.Left:F2}, {img.BoundingBox.Bottom:F2}) - ({img.BoundingBox.Right:F2}, {img.BoundingBox.Top:F2})");

        // CTM: 50 0 0 50 100 200 means:
        // - Scale by 50 in X and Y
        // - Translate to (100, 200)
        // Image is defined in unit square [0,0] to [1,1]
        // So bounds should be approximately (100, 200) to (150, 250)
        img.BoundingBox.Left.Should().BeApproximately(100, 1);
        img.BoundingBox.Bottom.Should().BeApproximately(200, 1);
        img.BoundingBox.Right.Should().BeApproximately(150, 1);
        img.BoundingBox.Top.Should().BeApproximately(250, 1);
    }

    [Fact]
    public void Parse_ContentWithNoInlineImages_ReturnsNoInlineImageOperations()
    {
        // Arrange - Content stream without inline images
        var contentStream = @"BT
/F1 12 Tf
100 700 Td
(Hello World) Tj
ET
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);

        // Act
        var operations = _parser.Parse(bytes, 792);

        // Assert
        var inlineImageOps = operations.OfType<InlineImageOperation>().ToList();
        inlineImageOps.Should().BeEmpty();
    }

    [Fact]
    public void Parse_InlineImage_WithFilter_ExtractsFilterProperty()
    {
        // Arrange - Content stream with filtered inline image (ASCIIHexDecode)
        var contentStream = @"q
100 0 0 100 50 50 cm
BI
/W 2 /H 2 /BPC 8 /CS /RGB /F /AHx
ID
FFFF00
00FF00
0000FF
FF00FF
>
EI
Q
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);

        // Act
        var operations = _parser.Parse(bytes, 792);

        // Assert
        var img = operations.OfType<InlineImageOperation>().First();
        _output.WriteLine($"Filter: {img.Filter}");

        img.Filter.Should().Be("/AHx");
    }

    [Fact]
    public void Parse_MultipleInlineImages_DetectsAll()
    {
        // Arrange - Content stream with two inline images
        var contentStream = @"q
50 0 0 50 100 100 cm
BI /W 5 /H 5 /BPC 8 /CS /G ID
01234567890123456789012345
EI
Q
q
50 0 0 50 200 200 cm
BI /W 5 /H 5 /BPC 8 /CS /G ID
01234567890123456789012345
EI
Q
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);

        // Act
        var operations = _parser.Parse(bytes, 792);

        // Assert
        var inlineImageOps = operations.OfType<InlineImageOperation>().ToList();
        _output.WriteLine($"Found {inlineImageOps.Count} inline images");

        inlineImageOps.Should().HaveCount(2);
    }

    [Fact]
    public void Build_InlineImageOperation_PreservesRawBytes()
    {
        // Arrange - Parse a content stream with inline image
        var originalContent = @"q
100 0 0 100 50 50 cm
BI
/W 10 /H 10 /BPC 8 /CS /G
ID
0123456789
EI
Q
";
        var originalBytes = Encoding.Latin1.GetBytes(originalContent);
        var operations = _parser.Parse(originalBytes, 792);

        // Act - Rebuild content stream
        var rebuiltBytes = _builder.Build(operations);
        var rebuiltString = Encoding.Latin1.GetString(rebuiltBytes);

        _output.WriteLine("Original:");
        _output.WriteLine(originalContent);
        _output.WriteLine("Rebuilt:");
        _output.WriteLine(rebuiltString);

        // Assert - The inline image sequence should be preserved
        rebuiltString.Should().Contain("BI");
        rebuiltString.Should().Contain("ID");
        rebuiltString.Should().Contain("EI");
        rebuiltString.Should().Contain("/W 10");
        rebuiltString.Should().Contain("/H 10");
    }

    [Fact]
    public void BuildWithRedactions_InlineImageIntersects_RemovesImage()
    {
        // Arrange - Parse a content stream with inline image
        var contentStream = @"q
50 0 0 50 100 200 cm
BI /W 10 /H 10 /BPC 8 /CS /G ID
0123456789
EI
Q
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);
        var operations = _parser.Parse(bytes, 792);

        // Redaction area that covers the image (at 100-150, 200-250)
        var redactionArea = new PdfRectangle(90, 190, 160, 260);

        // Act
        var rebuiltBytes = _builder.BuildWithRedactions(operations, new[] { redactionArea });
        var rebuiltString = Encoding.Latin1.GetString(rebuiltBytes);

        _output.WriteLine("Redaction area: " + redactionArea);
        _output.WriteLine("Rebuilt content:");
        _output.WriteLine(rebuiltString);

        // Assert - The inline image should be removed
        rebuiltString.Should().NotContain("BI");
        rebuiltString.Should().NotContain("ID");
        rebuiltString.Should().NotContain("EI");
    }

    [Fact]
    public void BuildWithRedactions_InlineImageDoesNotIntersect_PreservesImage()
    {
        // Arrange - Parse a content stream with inline image
        var contentStream = @"q
50 0 0 50 100 200 cm
BI /W 10 /H 10 /BPC 8 /CS /G ID
0123456789
EI
Q
";
        var bytes = Encoding.Latin1.GetBytes(contentStream);
        var operations = _parser.Parse(bytes, 792);

        // Redaction area that does NOT cover the image (far away)
        var redactionArea = new PdfRectangle(500, 500, 550, 550);

        // Act
        var rebuiltBytes = _builder.BuildWithRedactions(operations, new[] { redactionArea });
        var rebuiltString = Encoding.Latin1.GetString(rebuiltBytes);

        _output.WriteLine("Redaction area: " + redactionArea);
        _output.WriteLine("Rebuilt content:");
        _output.WriteLine(rebuiltString);

        // Assert - The inline image should be preserved
        rebuiltString.Should().Contain("BI");
        rebuiltString.Should().Contain("EI");
    }

    [Fact]
    public void InlineImageOperation_IntersectsWith_CorrectlyDetectsOverlap()
    {
        // Arrange - Create inline image at (100, 200) with size 50x50
        var rawBytes = Encoding.Latin1.GetBytes("BI /W 10 /H 10 ID data EI");
        var img = new InlineImageOperation
        {
            Operator = "BI",
            Operands = new List<object>(),
            RawBytes = rawBytes,
            BoundingBox = new PdfRectangle(100, 200, 150, 250),
            ImageWidth = 10,
            ImageHeight = 10,
            BitsPerComponent = 8
        };

        // Act & Assert - overlapping areas
        img.IntersectsWith(new PdfRectangle(120, 220, 130, 230)).Should().BeTrue("area inside image");
        img.IntersectsWith(new PdfRectangle(90, 190, 110, 210)).Should().BeTrue("area overlaps corner");
        img.IntersectsWith(new PdfRectangle(100, 200, 150, 250)).Should().BeTrue("exact same area");

        // Non-overlapping areas
        img.IntersectsWith(new PdfRectangle(0, 0, 50, 50)).Should().BeFalse("area far away");
        img.IntersectsWith(new PdfRectangle(200, 200, 250, 250)).Should().BeFalse("area to the right");
        img.IntersectsWith(new PdfRectangle(100, 300, 150, 350)).Should().BeFalse("area above");
    }
}
