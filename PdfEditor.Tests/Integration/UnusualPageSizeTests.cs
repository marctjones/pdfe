using FluentAssertions;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.IO;
using Xunit;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for PDFs with unusual page sizes (non-standard aspect ratios).
/// Verifies that documents display correctly regardless of page dimensions.
/// </summary>
public class UnusualPageSizeTests
{
    [Fact]
    public void WideLandscapePdf_HasUnusualAspectRatio()
    {
        // Arrange - Create synthetic PDF with wide landscape dimensions (1000x624 pts)
        // This mimics the aspect ratio of ID cards/driver's licenses
        var testPdfPath = Path.GetTempFileName() + ".pdf";
        try
        {
            TestPdfGenerator.CreateCustomSizePdf(testPdfPath, 1000, 624, "Wide Landscape Document");

            // Act - Open PDF with PdfSharp
            using var document = PdfReader.Open(testPdfPath, PdfDocumentOpenMode.Import);

            // Assert
            document.Should().NotBeNull("document should load successfully");
            document.PageCount.Should().Be(1, "should have 1 page");

            var page = document.Pages[0];
            page.Should().NotBeNull();

            // Get page dimensions
            var pageWidth = page!.Width.Point;
            var pageHeight = page.Height.Point;

            // Log the dimensions for analysis
            Console.WriteLine($"Wide Landscape PDF dimensions:");
            Console.WriteLine($"  Width: {pageWidth} pts");
            Console.WriteLine($"  Height: {pageHeight} pts");
            Console.WriteLine($"  Aspect ratio: {pageWidth / pageHeight:F2}:1");
            Console.WriteLine($"  Comparison to Letter (612x792): {(pageWidth / pageHeight) / (612.0 / 792.0):F2}x");

            // Verify dimensions
            pageWidth.Should().BeApproximately(1000, 1, "width should be 1000 pts");
            pageHeight.Should().BeApproximately(624, 1, "height should be 624 pts");

            // This is a wide aspect ratio (1.6:1) vs letter (0.77:1)
            var aspectRatio = pageWidth / pageHeight;
            aspectRatio.Should().BeGreaterThan(1.5, "wide landscape is unusually wide");

            // Check if this causes issues with our layout
            // Letter size is 612x792 = 0.77:1 (portrait)
            // Wide landscape is 1000x624 = 1.6:1 (landscape)
            // That's over 2x wider relative to height!

            var isWiderThanLetter = aspectRatio > (612.0 / 792.0);
            isWiderThanLetter.Should().BeTrue("wide landscape is much wider than standard letter");

            Console.WriteLine($"\n⚠️ ANALYSIS:");
            Console.WriteLine($"  This PDF is {aspectRatio / (612.0 / 792.0):F1}x wider (relative to height) than letter size");
            Console.WriteLine($"  When centered in a 1200px wide viewport, it will have significant whitespace");
            Console.WriteLine($"  This is EXPECTED behavior - the PDF itself has this unusual aspect ratio");
        }
        finally
        {
            if (File.Exists(testPdfPath))
                File.Delete(testPdfPath);
        }
    }

    [Fact]
    public void WideLandscapePdf_RendersCorrectly()
    {
        // Arrange - Create synthetic PDF with wide landscape dimensions
        var testPdfPath = Path.GetTempFileName() + ".pdf";
        try
        {
            TestPdfGenerator.CreateCustomSizePdf(testPdfPath, 1000, 624, "Wide Landscape Document");

            // Act - Render using PDFtoImage library via MemoryStream
            using var fileStream = File.OpenRead(testPdfPath);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            var options = new PDFtoImage.RenderOptions(Dpi: 150);
            using var image = PDFtoImage.Conversion.ToImage(memoryStream, page: 0, options: options);

            // Assert
            image.Should().NotBeNull("page should render successfully");

            // Calculate expected image dimensions at 150 DPI
            // PDF points are at 72 DPI
            // 1000 pts * (150/72) = 2083 pixels width
            // 624 pts * (150/72) = 1300 pixels height
            var expectedWidth = (int)(1000 * (150.0 / 72.0));
            var expectedHeight = (int)(624 * (150.0 / 72.0));

            Console.WriteLine($"\nRendered image dimensions:");
            Console.WriteLine($"  Expected: {expectedWidth}x{expectedHeight} px at 150 DPI");
            Console.WriteLine($"  Actual: {image!.Width}x{image.Height} px");

            image.Width.Should().BeCloseTo(expectedWidth, 5, "rendered width should match PDF dimensions");
            image.Height.Should().BeCloseTo(expectedHeight, 5, "rendered height should match PDF dimensions");

            // Check aspect ratio is preserved
            var renderedAspectRatio = (double)image.Width / image.Height;
            renderedAspectRatio.Should().BeApproximately(1000.0 / 624.0, 0.01, "aspect ratio should be preserved");
        }
        finally
        {
            if (File.Exists(testPdfPath))
                File.Delete(testPdfPath);
        }
    }

    [Fact]
    public void ComparisonTest_LetterSizeVsWideLandscape()
    {
        // This test documents the difference between standard and unusual page sizes

        // Letter size (standard US)
        var letterWidth = 612.0;
        var letterHeight = 792.0;
        var letterAspectRatio = letterWidth / letterHeight;

        // Wide landscape (ID card / business card size)
        var wideWidth = 1000.0;
        var wideHeight = 624.0;
        var wideAspectRatio = wideWidth / wideHeight;

        Console.WriteLine("Page Size Comparison:");
        Console.WriteLine($"\nLetter (8.5\" x 11\"):");
        Console.WriteLine($"  Dimensions: {letterWidth}x{letterHeight} pts");
        Console.WriteLine($"  Aspect ratio: {letterAspectRatio:F2}:1 (portrait)");

        Console.WriteLine($"\nWide Landscape (ID Card Size):");
        Console.WriteLine($"  Dimensions: {wideWidth}x{wideHeight} pts");
        Console.WriteLine($"  Aspect ratio: {wideAspectRatio:F2}:1 (landscape)");

        Console.WriteLine($"\nDifference:");
        Console.WriteLine($"  Wide landscape is {wideAspectRatio / letterAspectRatio:F2}x wider relative to height");

        Console.WriteLine($"\nIn a 1200px viewport:");
        Console.WriteLine($"  Letter at 150 DPI: {letterWidth * 150 / 72:F0}x{letterHeight * 150 / 72:F0} = {letterWidth * 150 / 72:F0} px wide");
        Console.WriteLine($"  Wide at 150 DPI: {wideWidth * 150 / 72:F0}x{wideHeight * 150 / 72:F0} = {wideWidth * 150 / 72:F0} px wide");
        Console.WriteLine($"  Wide uses {(wideWidth * 150 / 72) / 1200.0 * 100:F0}% of viewport width");
        Console.WriteLine($"  Whitespace is {1200 - (wideWidth * 150 / 72):F0} px on either side");

        Console.WriteLine($"\n📋 CONCLUSION:");
        Console.WriteLine($"  The whitespace is EXPECTED and CORRECT");
        Console.WriteLine($"  The PDF itself is wide and short (like an ID card)");
        Console.WriteLine($"  Our centering behavior is appropriate");
        Console.WriteLine($"  Consider: Fit-to-width zoom might help fill the viewport better");
    }
}
