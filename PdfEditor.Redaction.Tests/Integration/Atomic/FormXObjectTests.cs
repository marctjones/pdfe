using FluentAssertions;
using PdfEditor.Redaction.ContentStream.Parsing;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration.Atomic;

/// <summary>
/// Atomic test suite for Form XObject (nested content stream) redaction.
/// Tests the parsing and redaction of Form XObjects which contain reusable graphics/text.
///
/// See Issue #153: Add support for Form XObject redaction
/// </summary>
[Collection("AtomicTests")]
public class FormXObjectTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TextRedactor _redactor;
    private readonly ContentStreamParser _parser;
    private readonly string _tempDir;

    public FormXObjectTests(ITestOutputHelper output)
    {
        _output = output;
        _redactor = new TextRedactor();
        _parser = new ContentStreamParser();
        _tempDir = Path.Combine(Path.GetTempPath(), $"formxobject_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // =========================================================================
    // Form XObject Detection Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "FormXObjectDetection")]
    public void ParseWithResources_SimplePdf_NoFormXObjects()
    {
        // Arrange - Create a simple PDF without Form XObjects
        var inputPath = Path.Combine(_tempDir, "simple.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Hello World");

        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];
        var resources = page.Elements.GetDictionary("/Resources");

        // Get content bytes
        var contentBytes = GetContentBytes(page);
        if (contentBytes == null)
        {
            _output.WriteLine("No content stream in test PDF");
            return;
        }

        // Act
        var operations = _parser.ParseWithResources(contentBytes, page.Height.Point, resources);

        // Assert
        operations.Should().NotBeEmpty("Should parse operations from content stream");
        operations.OfType<FormXObjectOperation>().Should().BeEmpty(
            "Simple PDF should have no Form XObjects");

        _output.WriteLine($"✓ Parsed {operations.Count} operations, 0 Form XObjects");
    }

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "FormXObjectDetection")]
    public void ParseWithResources_NullResources_ReturnsOperations()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "simple.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Test");

        using var doc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];

        var contentBytes = GetContentBytes(page);
        if (contentBytes == null)
        {
            _output.WriteLine("No content stream in test PDF");
            return;
        }

        // Act - Pass null resources
        var operations = _parser.ParseWithResources(contentBytes, page.Height.Point, null);

        // Assert
        operations.Should().NotBeEmpty("Should still parse operations without resources");

        _output.WriteLine($"✓ Parsed {operations.Count} operations with null resources");
    }

    // =========================================================================
    // FormXObjectOperation Class Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "FormXObjectClass")]
    public void FormXObjectOperation_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var op = new FormXObjectOperation
        {
            Operator = "Do",
            Operands = new List<object> { "/Fm1" },
            StreamPosition = 0,
            XObjectName = "/Fm1"
        };

        // Assert
        op.XObjectName.Should().Be("/Fm1");
        op.NestedOperations.Should().BeEmpty("Default nested operations should be empty list");
        op.FormBBox.Should().NotBeNull();
        op.FormMatrix.Should().HaveCount(6, "Form matrix should have 6 elements");
        op.FormMatrix.Should().BeEquivalentTo(new double[] { 1, 0, 0, 1, 0, 0 },
            "Default matrix should be identity");
        op.ContentStreamBytes.Should().BeNull("Default content bytes should be null");

        _output.WriteLine("✓ FormXObjectOperation default values are correct");
    }

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "FormXObjectClass")]
    public void FormXObjectOperation_WithNestedOperations_TracksCorrectly()
    {
        // Arrange
        var nestedTextOp = new TextOperation
        {
            Operator = "Tj",
            Operands = new List<object> { "Nested Text" },
            StreamPosition = 0,
            Text = "Nested Text",
            Glyphs = Array.Empty<GlyphPosition>(),
            BoundingBox = new PdfRectangle(10, 10, 100, 30)
        };

        // Act
        var formOp = new FormXObjectOperation
        {
            Operator = "Do",
            Operands = new List<object> { "/Fm1" },
            StreamPosition = 5,
            XObjectName = "/Fm1",
            NestedOperations = new List<PdfOperation> { nestedTextOp },
            FormBBox = new PdfRectangle(0, 0, 200, 100),
            FormMatrix = new double[] { 2, 0, 0, 2, 50, 50 }
        };

        // Assert
        formOp.NestedOperations.Should().HaveCount(1);
        formOp.NestedOperations[0].Should().BeOfType<TextOperation>();
        ((TextOperation)formOp.NestedOperations[0]).Text.Should().Be("Nested Text");

        _output.WriteLine("✓ FormXObjectOperation tracks nested operations correctly");
    }

    // =========================================================================
    // Redaction Integration Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "RedactionIntegration")]
    public void RedactLocations_SimplePdf_Works()
    {
        // Arrange
        var inputPath = Path.Combine(_tempDir, "input.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateSimpleTextPdf(inputPath, "Redact this text please");

        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(50, 700, 200, 750)
        };

        // Act
        var result = _redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();

        _output.WriteLine($"✓ Redaction completed: {result.RedactionCount} items redacted");
    }

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "RedactionIntegration")]
    public void RedactLocations_WithFormXObjectSupport_DoesNotCrash()
    {
        // Arrange - Create a PDF with graphics (which might have XObjects)
        var inputPath = Path.Combine(_tempDir, "graphics.pdf");
        var outputPath = Path.Combine(_tempDir, "output.pdf");
        TestPdfGenerator.CreateMultiLineTextPdf(inputPath, "Line 1", "Line 2", "Line 3");

        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(100, 500, 300, 600)
        };

        var options = new RedactionOptions
        {
            DrawVisualMarker = true,
            UseGlyphLevelRedaction = false
        };

        // Act
        var result = _redactor.RedactLocations(inputPath, outputPath, new[] { location }, options);

        // Assert
        result.Success.Should().BeTrue("Redaction should succeed");
        File.Exists(outputPath).Should().BeTrue();

        // Verify output is readable
        using var doc = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(1);

        _output.WriteLine("✓ Redaction with Form XObject support completed without crash");
    }

    // =========================================================================
    // Parser Integration Tests
    // =========================================================================

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "ParserIntegration")]
    public void Parse_ContentWithDoOperator_CreatesImageOperation()
    {
        // Arrange - Content stream with Do operator
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "q\n" +
            "100 0 0 100 50 700 cm\n" +
            "/Im1 Do\n" +
            "Q\n");

        // Act
        var operations = _parser.Parse(contentBytes, 842);

        // Assert
        var imageOps = operations.OfType<ImageOperation>().ToList();
        imageOps.Should().HaveCount(1, "Should detect Do operator as ImageOperation");
        imageOps[0].XObjectName.Should().Be("/Im1");

        _output.WriteLine($"✓ Do operator parsed as ImageOperation: {imageOps[0].XObjectName}");
    }

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "ImageRedaction")]
    public void Parse_ImageWithCTM_CalculatesBoundingBox()
    {
        // Arrange - Content stream: 100x50 image at position (50, 700)
        // CTM: [100 0 0 50 50 700] means scale 100x50 and translate to (50, 700)
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "q\n" +
            "100 0 0 50 50 700 cm\n" +
            "/Im1 Do\n" +
            "Q\n");

        // Act
        var operations = _parser.Parse(contentBytes, 842);

        // Assert
        var imageOps = operations.OfType<ImageOperation>().ToList();
        imageOps.Should().HaveCount(1);

        var bbox = imageOps[0].BoundingBox;
        // Unit square [0,0,1,1] transformed by [100 0 0 50 50 700]
        // (0,0) -> (50, 700)
        // (1,1) -> (150, 750)
        bbox.Left.Should().BeApproximately(50, 0.1, "Left should be 50");
        bbox.Bottom.Should().BeApproximately(700, 0.1, "Bottom should be 700");
        bbox.Right.Should().BeApproximately(150, 0.1, "Right should be 150");
        bbox.Top.Should().BeApproximately(750, 0.1, "Top should be 750");

        _output.WriteLine($"✓ Image bounding box: ({bbox.Left:F1}, {bbox.Bottom:F1}) to ({bbox.Right:F1}, {bbox.Top:F1})");
    }

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "ImageRedaction")]
    public void ImageOperation_IntersectsWith_RedactionArea()
    {
        // Arrange - Image at position (50, 700) with size 100x50
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "q\n" +
            "100 0 0 50 50 700 cm\n" +
            "/Im1 Do\n" +
            "Q\n");

        var operations = _parser.Parse(contentBytes, 842);
        var imageOp = operations.OfType<ImageOperation>().First();

        // Act & Assert - Redaction area that overlaps
        var overlappingArea = new PdfRectangle(60, 710, 140, 740);
        imageOp.IntersectsWith(overlappingArea).Should().BeTrue("Image should intersect with overlapping area");

        // Non-overlapping area
        var nonOverlappingArea = new PdfRectangle(200, 800, 300, 850);
        imageOp.IntersectsWith(nonOverlappingArea).Should().BeFalse("Image should not intersect with distant area");

        _output.WriteLine("✓ Image intersection detection works correctly");
    }

    [Fact]
    [Trait("Category", "Atomic")]
    [Trait("Type", "RectangleRedaction")]
    public void Parse_RectangleOperator_CalculatesBoundingBox()
    {
        // Arrange - Rectangle: x=100, y=200, w=50, h=30
        var contentBytes = System.Text.Encoding.ASCII.GetBytes(
            "100 200 50 30 re\n" +
            "f\n");

        // Act
        var operations = _parser.Parse(contentBytes, 842);

        // Assert
        var pathOps = operations.OfType<PathOperation>().Where(p => p.Type == PathType.Rectangle).ToList();
        pathOps.Should().HaveCount(1);

        var bbox = pathOps[0].BoundingBox;
        bbox.Left.Should().BeApproximately(100, 0.1);
        bbox.Bottom.Should().BeApproximately(200, 0.1);
        bbox.Right.Should().BeApproximately(150, 0.1);  // 100 + 50
        bbox.Top.Should().BeApproximately(230, 0.1);    // 200 + 30

        _output.WriteLine($"✓ Rectangle bounding box: ({bbox.Left:F1}, {bbox.Bottom:F1}) to ({bbox.Right:F1}, {bbox.Top:F1})");
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private byte[]? GetContentBytes(PdfSharp.Pdf.PdfPage page)
    {
        var contents = page.Contents;
        if (contents == null || contents.Elements.Count == 0)
            return null;

        using var ms = new MemoryStream();
        for (int i = 0; i < contents.Elements.Count; i++)
        {
            var item = contents.Elements[i];
            PdfSharp.Pdf.PdfDictionary? dict = null;

            if (item is PdfSharp.Pdf.Advanced.PdfReference pdfRef)
            {
                dict = pdfRef.Value as PdfSharp.Pdf.PdfDictionary;
            }
            else if (item is PdfSharp.Pdf.PdfDictionary d)
            {
                dict = d;
            }

            if (dict?.Stream?.Value is byte[] bytes)
            {
                ms.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.Length > 0 ? ms.ToArray() : null;
    }
}
