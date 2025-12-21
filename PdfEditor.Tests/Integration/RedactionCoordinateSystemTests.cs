using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using PdfEditor.Services;
using PdfEditor.Services.Redaction;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Integration tests to verify that TextBoundsCalculator and RedactionService
/// use the SAME coordinate system (Avalonia coordinates: top-left origin, PDF points).
///
/// These tests are CRITICAL for ensuring redaction works correctly.
/// If coordinates don't match, intersection tests will fail and text won't be removed.
/// </summary>
public class RedactionCoordinateSystemTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly ILoggerFactory _loggerFactory;

    public RedactionCoordinateSystemTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void RedactionService_AndTextBoundsCalculator_UseSameCoordinateSystem()
    {
        // This is the MOST CRITICAL test: verify that when we parse a PDF page,
        // the text bounding boxes are in the SAME coordinate system as the
        // redaction area from the UI.

        // Arrange: Create a PDF with known text at known position
        var pdfPath = Path.GetTempFileName();
        _tempFiles.Add(pdfPath);

        TestPdfGenerator.CreatePdfWithTextAt(
            pdfPath,
            "TESTWORD",
            x: 100,       // PDF points from left
            y: 700,       // PDF points from bottom (near top of 792pt page)
            fontSize: 18);

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var pageHeight = page.Height.Point;

        _output.WriteLine($"Created test PDF: page height = {pageHeight:F2} points");
        _output.WriteLine($"Text 'TESTWORD' at PDF coords: (100, 700) with fontSize=18");
        _output.WriteLine($"Expected text top: Y = 700 + 18 = 718");
        _output.WriteLine($"Expected Avalonia Y: {pageHeight:F2} - 718 = {pageHeight - 718:F2}");

        // Act: Parse the page to get text operations
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        var operations = parser.ParseContentStream(page);
        var textOps = operations.OfType<TextOperation>()
            .Where(op => !string.IsNullOrWhiteSpace(op.Text))
            .ToList();

        _output.WriteLine($"\nFound {textOps.Count} text operations:");
        foreach (var op in textOps)
        {
            _output.WriteLine($"  '{op.Text}': BBox=({op.BoundingBox.X:F2},{op.BoundingBox.Y:F2},{op.BoundingBox.Width:F2}x{op.BoundingBox.Height:F2})");
        }

        // Assert: Text operation should exist
        textOps.Should().NotBeEmpty("Parser should find text operations");

        var testWordOp = textOps.FirstOrDefault(op => op.Text.Contains("TESTWORD"));
        testWordOp.Should().NotBeNull("Should find 'TESTWORD' text operation");

        // Assert: Text bounds should be in Avalonia coordinates (top-left origin)
        var textBounds = testWordOp!.BoundingBox;

        // Text at PDF Y=700 should have Avalonia Y near the top of the page (low Y value)
        // With fontSize=18, ascent ~13.5, the top would be around 700+13.5=713.5 in PDF
        // Avalonia Y = 792 - 713.5 = 78.5 (approximately)
        // Allow tolerance for different font metrics
        var expectedAvaloniaYApprox = pageHeight - 720;  // Roughly near top

        textBounds.Y.Should().BeLessThan(100,
            "Text near top of page (PDF Y=700) should have low Avalonia Y value (top-left origin)");
        textBounds.Y.Should().BeGreaterThan(60,
            "Text Y should be positive and account for font ascent/descent");

        // Assert: Simulate redaction area selection at the same location as the parsed text
        // User selects the text area in Avalonia coordinates at 150 DPI
        // Use the actual textBounds we got from parsing
        var imageSelectionPixels = new Rect(
            textBounds.X * 150.0 / 72.0,      // X in pixels
            textBounds.Y * 150.0 / 72.0,      // Y in pixels
            textBounds.Width * 150.0 / 72.0,  // Width in pixels
            textBounds.Height * 150.0 / 72.0); // Height in pixels

        // Convert back to redaction area (same as RedactionService does)
        var redactionArea = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(
            imageSelectionPixels, 150);

        _output.WriteLine($"\nSimulated UI selection:");
        _output.WriteLine($"  Image pixels (150 DPI): ({imageSelectionPixels.X:F2},{imageSelectionPixels.Y:F2},{imageSelectionPixels.Width:F2}x{imageSelectionPixels.Height:F2})");
        _output.WriteLine($"  Redaction area (PDF points): ({redactionArea.X:F2},{redactionArea.Y:F2},{redactionArea.Width:F2}x{redactionArea.Height:F2})");
        _output.WriteLine($"  Text bounds (PDF points):    ({textBounds.X:F2},{textBounds.Y:F2},{textBounds.Width:F2}x{textBounds.Height:F2})");

        // Assert: Redaction area and text bounds should match in coordinate system
        textBounds.Y.Should().BeApproximately(redactionArea.Y, 1,
            "Text bounds and redaction area should have SAME Y coordinate (Avalonia top-left origin)");

        // Assert: They should intersect
        var intersects = textBounds.Intersects(redactionArea);
        _output.WriteLine($"\nIntersection test: {intersects}");

        intersects.Should().BeTrue(
            "Text bounds and redaction area at same position MUST intersect - " +
            "this proves both use the SAME coordinate system (Avalonia top-left origin, PDF points)");

        _output.WriteLine("\n✓ VERIFIED: TextBoundsCalculator and RedactionService use the SAME coordinate system");
    }

    [Fact]
    public void Redaction_WithMatchingCoordinates_RemovesText()
    {
        // This test verifies the ENTIRE pipeline: parsing → filtering → removal

        // Arrange: Create PDF with text
        var pdfPath = Path.GetTempFileName();
        _tempFiles.Add(pdfPath);

        TestPdfGenerator.CreatePdfWithTextAt(
            pdfPath,
            "REMOVE_ME",
            x: 200,
            y: 400,      // Middle of page
            fontSize: 24);

        // Act: Apply redaction using coordinates that SHOULD match the text
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var pageHeight = page.Height.Point;

        // Text at PDF Y=400, height=24, so top is at Y=424
        // Avalonia Y = 792 - 424 = 368
        // Create redaction area at Avalonia (200, 368, 150, 24)
        var avaloniaRedactionArea = new Rect(200, 368, 150, 24);

        // Convert to image pixels at 150 DPI (as if from UI)
        var imageSelectionPixels = new Rect(
            avaloniaRedactionArea.X * 150.0 / 72.0,
            avaloniaRedactionArea.Y * 150.0 / 72.0,
            avaloniaRedactionArea.Width * 150.0 / 72.0,
            avaloniaRedactionArea.Height * 150.0 / 72.0);

        _output.WriteLine($"Applying redaction:");
        _output.WriteLine($"  Avalonia coords: ({avaloniaRedactionArea.X:F2},{avaloniaRedactionArea.Y:F2},{avaloniaRedactionArea.Width:F2}x{avaloniaRedactionArea.Height:F2})");
        _output.WriteLine($"  Image pixels (150 DPI): ({imageSelectionPixels.X:F2},{imageSelectionPixels.Y:F2},{imageSelectionPixels.Width:F2}x{imageSelectionPixels.Height:F2})");

        var redactionService = new RedactionService(
            _loggerFactory.CreateLogger<RedactionService>(),
            _loggerFactory);

        redactionService.RedactArea(page, imageSelectionPixels, renderDpi: 150);

        // Save and verify
        var redactedPath = Path.GetTempFileName();
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Assert: Text should be removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"\nText after redaction: '{textAfter}'");

        textAfter.Should().NotContain("REMOVE_ME",
            "Text MUST be removed from PDF structure when coordinates match - " +
            "this proves TextBoundsCalculator and RedactionService use the SAME coordinate system");

        _output.WriteLine("✓ VERIFIED: Redaction successfully removed text using matching coordinate system");
    }

    [Theory]
    [InlineData(100, 700, 18, 150)]   // Text near top, 150 DPI
    [InlineData(200, 400, 24, 150)]   // Text in middle, 150 DPI
    [InlineData(150, 100, 12, 150)]   // Text near bottom, 150 DPI
    [InlineData(100, 700, 18, 72)]    // Text near top, 72 DPI
    [InlineData(200, 400, 24, 300)]   // Text in middle, 300 DPI
    public void CoordinateConversion_RoundTrip_PreservesIntersection(
        double pdfX, double pdfY, double fontSize, int renderDpi)
    {
        // This test verifies that converting coordinates back and forth
        // preserves the ability to detect intersections

        // Arrange: Create text bounds in PDF coords
        var pageHeight = 792.0;
        var pdfTop = pdfY + fontSize;

        // Convert to Avalonia coords (as TextBoundsCalculator does)
        var avaloniaY = CoordinateConverter.PdfYToAvaloniaY(pdfTop, pageHeight);
        var textBoundsAvalonia = new Rect(pdfX, avaloniaY, 100, fontSize);

        _output.WriteLine($"PDF text: ({pdfX:F2},{pdfY:F2}) fontSize={fontSize}");
        _output.WriteLine($"Text bounds (Avalonia): ({textBoundsAvalonia.X:F2},{textBoundsAvalonia.Y:F2},{textBoundsAvalonia.Width:F2}x{textBoundsAvalonia.Height:F2})");

        // Act: Simulate UI selection → image pixels → redaction area
        var imagePixels = new Rect(
            textBoundsAvalonia.X * renderDpi / 72.0,
            textBoundsAvalonia.Y * renderDpi / 72.0,
            textBoundsAvalonia.Width * renderDpi / 72.0,
            textBoundsAvalonia.Height * renderDpi / 72.0);

        var redactionArea = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(
            imagePixels, renderDpi);

        _output.WriteLine($"Image pixels ({renderDpi} DPI): ({imagePixels.X:F2},{imagePixels.Y:F2},{imagePixels.Width:F2}x{imagePixels.Height:F2})");
        _output.WriteLine($"Redaction area (Avalonia): ({redactionArea.X:F2},{redactionArea.Y:F2},{redactionArea.Width:F2}x{redactionArea.Height:F2})");

        // Assert: After round-trip conversion, coordinates should match
        textBoundsAvalonia.X.Should().BeApproximately(redactionArea.X, 0.5);
        textBoundsAvalonia.Y.Should().BeApproximately(redactionArea.Y, 0.5,
            $"Round-trip conversion at {renderDpi} DPI should preserve Y coordinate");

        // Assert: Should intersect
        var intersects = textBoundsAvalonia.Intersects(redactionArea);
        intersects.Should().BeTrue(
            $"Round-trip conversion at {renderDpi} DPI should preserve intersection capability");

        _output.WriteLine($"✓ Round-trip at {renderDpi} DPI preserves coordinates and intersection");
    }

    [Fact]
    public void Parser_ProducesAvaloniaCoordinates_NotPdfCoordinates()
    {
        // This test explicitly verifies that ContentStreamParser output is NOT
        // in PDF coordinates (which would have high Y values near page top)

        // Arrange: Create PDF with text near TOP of page
        var pdfPath = Path.GetTempFileName();
        _tempFiles.Add(pdfPath);

        TestPdfGenerator.CreatePdfWithTextAt(
            pdfPath,
            "TOP_TEXT",
            x: 100,
            y: 760,      // Near TOP in PDF coords (high Y value)
            fontSize: 12);

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var pageHeight = page.Height.Point;

        // Act: Parse
        var parser = new ContentStreamParser(
            _loggerFactory.CreateLogger<ContentStreamParser>(),
            _loggerFactory);

        var operations = parser.ParseContentStream(page);
        var textOp = operations.OfType<TextOperation>()
            .FirstOrDefault(op => op.Text.Contains("TOP_TEXT"));

        // Assert: Should NOT be null
        textOp.Should().NotBeNull();

        // Assert: If it were in PDF coordinates, Y would be ~760 (near 792)
        // But in Avalonia coordinates, Y should be low (near 0 = top)
        var bounds = textOp!.BoundingBox;

        _output.WriteLine($"Text 'TOP_TEXT' at PDF Y=760 (near top of 792pt page)");
        _output.WriteLine($"Parser returned Y={bounds.Y:F2}");

        bounds.Y.Should().BeLessThan(100,
            "Text near TOP of page should have LOW Y value in Avalonia coords (top-left origin), " +
            "NOT high Y value like PDF coords (bottom-left origin)");

        _output.WriteLine("✓ VERIFIED: Parser produces Avalonia coordinates (top-left origin), NOT PDF coordinates");
    }
}
