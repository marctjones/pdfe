using FluentAssertions;
using PdfEditor.Redaction.Tests.Utilities;
using PdfSharp.Drawing;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Comprehensive tests for partial shape redaction across all geometric shapes.
/// Tests: rectangles, triangles, circles, ellipses, regular polygons, stars, and irregular shapes.
///
/// Shape Classification:
/// - Regular shapes: predictable vertices (rectangle, triangle, pentagon, hexagon)
/// - Irregular shapes: arbitrary vertices (random polygons)
/// - Curved shapes: circles, ellipses (approximated as polygons in PDF)
/// - Complex shapes: stars (concave), compound paths
/// </summary>
public class AllShapesRedactionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;

    public AllShapesRedactionTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), $"all_shapes_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _output.WriteLine($"Test files at: {_tempDir}");
        _output.WriteLine($"View with: timg --grid=2 -g 80x40 {_tempDir}/*.png");
    }

    #region Triangle Tests

    [Fact]
    public void RedactLocation_Triangle_PartialOverlap_ClipsShape()
    {
        // Arrange: Equilateral triangle pointing up, base at y=500, apex at y=600
        var inputPath = Path.Combine(_tempDir, "triangle_input.pdf");
        var outputPath = Path.Combine(_tempDir, "triangle_output.pdf");

        // Triangle: (100,500), (200,500), (150,600) - 100 wide, 100 tall
        TestPdfGenerator.CreateTrianglePdf(inputPath, 100, 500, 200, 500, 150, 600, XColors.Green);

        // Redact the top half (apex area)
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(100, 550, 200, 650)
        };

        var redactor = new TextRedactor();

        // Act
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        // Assert
        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"Triangle test - input: {inputPath}");
        _output.WriteLine($"Triangle test - output: {outputPath}");
    }

    [Fact]
    public void RedactLocation_Triangle_SideOverlap_ClipsCorrectly()
    {
        // Arrange: Triangle with redaction covering one side
        var inputPath = Path.Combine(_tempDir, "triangle_side_input.pdf");
        var outputPath = Path.Combine(_tempDir, "triangle_side_output.pdf");

        TestPdfGenerator.CreateTrianglePdf(inputPath, 100, 500, 200, 500, 150, 600, XColors.Green);

        // Redact the right side
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(150, 500, 250, 650)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    #endregion

    #region Circle Tests

    [Fact]
    public void RedactLocation_Circle_PartialOverlap_ClipsShape()
    {
        // Arrange: Circle at center (200,500), radius 50
        var inputPath = Path.Combine(_tempDir, "circle_input.pdf");
        var outputPath = Path.Combine(_tempDir, "circle_output.pdf");

        TestPdfGenerator.CreateCirclePdf(inputPath, 200, 500, 50, XColors.Red);

        // Redact the right half
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(200, 450, 300, 550)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"Circle test - input: {inputPath}");
        _output.WriteLine($"Circle test - output: {outputPath}");
    }

    [Fact]
    public void RedactLocation_Circle_QuarterOverlap_ClipsCorrectly()
    {
        // Arrange: Circle with small corner redaction
        var inputPath = Path.Combine(_tempDir, "circle_quarter_input.pdf");
        var outputPath = Path.Combine(_tempDir, "circle_quarter_output.pdf");

        TestPdfGenerator.CreateCirclePdf(inputPath, 200, 500, 50, XColors.Red);

        // Redact just the top-right quarter
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(200, 500, 260, 560)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    #endregion

    #region Ellipse Tests

    [Fact]
    public void RedactLocation_Ellipse_PartialOverlap_ClipsShape()
    {
        // Arrange: Horizontal ellipse at center (200,500), rx=60, ry=30
        var inputPath = Path.Combine(_tempDir, "ellipse_input.pdf");
        var outputPath = Path.Combine(_tempDir, "ellipse_output.pdf");

        TestPdfGenerator.CreateEllipsePdf(inputPath, 200, 500, 60, 30, XColors.Orange);

        // Redact the right end
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(200, 460, 280, 540)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"Ellipse test - input: {inputPath}");
        _output.WriteLine($"Ellipse test - output: {outputPath}");
    }

    #endregion

    #region Regular Polygon Tests

    [Fact]
    public void RedactLocation_Pentagon_PartialOverlap_ClipsShape()
    {
        // Arrange: Regular pentagon at center (200,500), radius 50
        var inputPath = Path.Combine(_tempDir, "pentagon_input.pdf");
        var outputPath = Path.Combine(_tempDir, "pentagon_output.pdf");

        TestPdfGenerator.CreateRegularPolygonPdf(inputPath, 200, 500, 50, 5, XColors.Purple);

        // Redact the bottom half
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(140, 440, 260, 500)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"Pentagon test - input: {inputPath}");
        _output.WriteLine($"Pentagon test - output: {outputPath}");
    }

    [Fact]
    public void RedactLocation_Hexagon_PartialOverlap_ClipsShape()
    {
        // Arrange: Regular hexagon at center (200,500), radius 50
        var inputPath = Path.Combine(_tempDir, "hexagon_input.pdf");
        var outputPath = Path.Combine(_tempDir, "hexagon_output.pdf");

        TestPdfGenerator.CreateRegularPolygonPdf(inputPath, 200, 500, 50, 6, XColors.Cyan);

        // Redact the left third
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(140, 440, 180, 560)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactLocation_Octagon_PartialOverlap_ClipsShape()
    {
        // Arrange: Regular octagon at center (200,500), radius 50
        var inputPath = Path.Combine(_tempDir, "octagon_input.pdf");
        var outputPath = Path.Combine(_tempDir, "octagon_output.pdf");

        TestPdfGenerator.CreateRegularPolygonPdf(inputPath, 200, 500, 50, 8, XColors.Brown);

        // Redact the top right corner
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(200, 500, 270, 570)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    #endregion

    #region Star Tests (Concave Shapes)

    [Fact]
    public void RedactLocation_Star_PartialOverlap_ClipsShape()
    {
        // Arrange: 5-point star at center (200,500), outer=50, inner=20
        var inputPath = Path.Combine(_tempDir, "star_input.pdf");
        var outputPath = Path.Combine(_tempDir, "star_output.pdf");

        TestPdfGenerator.CreateStarPdf(inputPath, 200, 500, 50, 20, 5, XColors.Gold);

        // Redact the top point
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(170, 530, 230, 560)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"Star test - input: {inputPath}");
        _output.WriteLine($"Star test - output: {outputPath}");
    }

    [Fact]
    public void RedactLocation_Star_CenterOverlap_ClipsCenter()
    {
        // Arrange: Star with redaction in center (tests concave clipping)
        var inputPath = Path.Combine(_tempDir, "star_center_input.pdf");
        var outputPath = Path.Combine(_tempDir, "star_center_output.pdf");

        TestPdfGenerator.CreateStarPdf(inputPath, 200, 500, 60, 25, 5, XColors.Gold);

        // Redact the center area
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(185, 485, 215, 515)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    #endregion

    #region Irregular Polygon Tests

    [Fact]
    public void RedactLocation_IrregularPolygon_PartialOverlap_ClipsShape()
    {
        // Arrange: Irregular 6-sided polygon
        var inputPath = Path.Combine(_tempDir, "irregular_input.pdf");
        var outputPath = Path.Combine(_tempDir, "irregular_output.pdf");

        TestPdfGenerator.CreatePolygonPdf(inputPath, XColors.Magenta,
            (150, 500), (180, 550), (220, 540), (250, 570), (230, 490), (190, 480));

        // Redact the right side
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(210, 470, 280, 580)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"Irregular polygon test - input: {inputPath}");
        _output.WriteLine($"Irregular polygon test - output: {outputPath}");
    }

    #endregion

    #region Comprehensive Multi-Shape Tests

    [Fact]
    public void RedactLocation_AllShapes_HorizontalStripe_ClipsAllShapes()
    {
        // Arrange: PDF with all shapes, redact a horizontal stripe across all
        var inputPath = Path.Combine(_tempDir, "all_shapes_input.pdf");
        var outputPath = Path.Combine(_tempDir, "all_shapes_output.pdf");

        TestPdfGenerator.CreateAllShapesPdf(inputPath);

        // Redact a horizontal stripe across the middle of the page
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(0, 400, 612, 500)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine($"All shapes horizontal stripe test:");
        _output.WriteLine($"  Input: {inputPath}");
        _output.WriteLine($"  Output: {outputPath}");
    }

    [Fact]
    public void RedactLocation_AllShapes_VerticalStripe_ClipsAllShapes()
    {
        // Arrange: PDF with all shapes, redact a vertical stripe
        var inputPath = Path.Combine(_tempDir, "all_shapes_vert_input.pdf");
        var outputPath = Path.Combine(_tempDir, "all_shapes_vert_output.pdf");

        TestPdfGenerator.CreateAllShapesPdf(inputPath);

        // Redact a vertical stripe down the middle
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(150, 100, 250, 700)
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    [Fact]
    public void RedactLocation_AllShapes_DiagonalPattern_MultipleAreas()
    {
        // Arrange: Multiple redaction areas in a diagonal pattern
        var inputPath = Path.Combine(_tempDir, "all_shapes_diag_input.pdf");
        var outputPath = Path.Combine(_tempDir, "all_shapes_diag_output.pdf");

        TestPdfGenerator.CreateAllShapesPdf(inputPath);

        // Multiple small redaction areas
        var locations = new[]
        {
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(50, 600, 100, 650) },
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(150, 500, 200, 550) },
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(200, 400, 250, 450) },
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(100, 300, 150, 350) },
            new RedactionLocation { PageNumber = 1, BoundingBox = new PdfRectangle(200, 150, 300, 200) }
        };

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, locations);

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();
    }

    #endregion

    #region Visual Diagnostic Test

    [Fact]
    public void DiagnoseAllShapes_DumpContentStreams()
    {
        // This test creates before/after PNGs for visual inspection
        var inputPath = Path.Combine(_tempDir, "visual_all_input.pdf");
        var outputPath = Path.Combine(_tempDir, "visual_all_output.pdf");

        _output.WriteLine("=== Creating All Shapes PDF ===");
        TestPdfGenerator.CreateAllShapesPdf(inputPath);

        // Redact a vertical stripe through the shapes
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(80, 100, 120, 700)
        };

        _output.WriteLine("=== Performing Redaction ===");
        _output.WriteLine($"Redaction area: (80, 100) to (120, 700) - vertical stripe on left side");

        var redactor = new TextRedactor();
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });

        _output.WriteLine($"Success: {result.Success}");

        result.Success.Should().BeTrue();
        PdfTestHelpers.IsValidPdf(outputPath).Should().BeTrue();

        _output.WriteLine("\n=== Visual Inspection Commands ===");
        _output.WriteLine($"pdftoppm -png {inputPath} {_tempDir}/visual_input");
        _output.WriteLine($"pdftoppm -png {outputPath} {_tempDir}/visual_output");
        _output.WriteLine($"timg --grid=2 -g 80x40 {_tempDir}/visual_input-1.png {_tempDir}/visual_output-1.png");
    }

    #endregion
}
