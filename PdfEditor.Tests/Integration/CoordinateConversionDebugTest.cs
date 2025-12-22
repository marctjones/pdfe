using Xunit;
using Xunit.Abstractions;
using Avalonia;
using PdfEditor.Services;
using System;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Debug test to diagnose coordinate conversion mismatch between
/// PdfTextExtractionService and RedactionService
/// </summary>
public class CoordinateConversionDebugTest
{
    private readonly ITestOutputHelper _output;

    public CoordinateConversionDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompareCoordinateConversions_FromUserReport()
    {
        // From user's second redaction attempt:
        // Image pixels: (479.50,864.00,163.00x35.00) at 150 DPI
        var imageSelection = new Rect(479.50, 864.00, 163.00, 35.00);
        var renderDpi = 150;
        var pageHeightPoints = 792.0; // Letter size page

        _output.WriteLine("=== Coordinate Conversion Comparison ===");
        _output.WriteLine($"Input (image pixels): ({imageSelection.X:F2},{imageSelection.Y:F2},{imageSelection.Width:F2}x{imageSelection.Height:F2})");
        _output.WriteLine($"Render DPI: {renderDpi}");
        _output.WriteLine($"Page height: {pageHeightPoints:F2} points");
        _output.WriteLine("");

        // What RedactionService does:
        var redactionArea = CoordinateConverter.ImageSelectionToPdfPointsTopLeft(imageSelection, renderDpi);
        _output.WriteLine("RedactionService conversion (top-left origin):");
        _output.WriteLine($"  Method: ImageSelectionToPdfPointsTopLeft()");
        _output.WriteLine($"  Output: ({redactionArea.X:F2},{redactionArea.Y:F2},{redactionArea.Width:F2}x{redactionArea.Height:F2})");
        _output.WriteLine($"  Y range: {redactionArea.Y:F2} to {redactionArea.Bottom:F2}");
        _output.WriteLine("");

        // What PdfTextExtractionService does:
        var (left, bottom, right, top) = CoordinateConverter.ImageSelectionToPdfCoords(
            imageSelection, pageHeightPoints, renderDpi);
        _output.WriteLine("PdfTextExtractionService conversion (bottom-left origin):");
        _output.WriteLine($"  Method: ImageSelectionToPdfCoords()");
        _output.WriteLine($"  Output: PDF ({left:F2},{bottom:F2}) to ({right:F2},{top:F2})");
        _output.WriteLine("");

        // Convert PdfTextExtraction coords to Avalonia for comparison:
        var textExtractionAsAvalonia = CoordinateConverter.PdfRectToAvaloniaRect(
            left, bottom, right, top, pageHeightPoints);
        _output.WriteLine("PdfTextExtraction converted to Avalonia (top-left origin):");
        _output.WriteLine($"  Output: ({textExtractionAsAvalonia.X:F2},{textExtractionAsAvalonia.Y:F2},{textExtractionAsAvalonia.Width:F2}x{textExtractionAsAvalonia.Height:F2})");
        _output.WriteLine($"  Y range: {textExtractionAsAvalonia.Y:F2} to {textExtractionAsAvalonia.Bottom:F2}");
        _output.WriteLine("");

        // Compare:
        _output.WriteLine("Difference between RedactionService and PdfTextExtraction:");
        _output.WriteLine($"  ΔX: {Math.Abs(redactionArea.X - textExtractionAsAvalonia.X):F2} points");
        _output.WriteLine($"  ΔY: {Math.Abs(redactionArea.Y - textExtractionAsAvalonia.Y):F2} points");
        _output.WriteLine($"  ΔWidth: {Math.Abs(redactionArea.Width - textExtractionAsAvalonia.Width):F2} points");
        _output.WriteLine($"  ΔHeight: {Math.Abs(redactionArea.Height - textExtractionAsAvalonia.Height):F2} points");
        _output.WriteLine("");

        // From user's log: text operation bounding box
        var textOpBounds = new Rect(100.07, 387.55, 313.02, 10.02);
        _output.WriteLine($"Text operation bounding box (from ContentStreamParser):");
        _output.WriteLine($"  ({textOpBounds.X:F2},{textOpBounds.Y:F2},{textOpBounds.Width:F2}x{textOpBounds.Height:F2})");
        _output.WriteLine($"  Y range: {textOpBounds.Y:F2} to {textOpBounds.Bottom:F2}");
        _output.WriteLine("");

        // Check intersection
        _output.WriteLine("Intersection checks:");
        _output.WriteLine($"  RedactionArea ∩ TextOpBounds: {redactionArea.Intersects(textOpBounds)}");
        _output.WriteLine($"  TextExtraction ∩ TextOpBounds: {textExtractionAsAvalonia.Intersects(textOpBounds)}");
        _output.WriteLine("");

        // Analysis
        if (Math.Abs(redactionArea.Y - textExtractionAsAvalonia.Y) > 1.0)
        {
            _output.WriteLine("❌ MISMATCH DETECTED!");
            _output.WriteLine($"   The two conversion methods produce different Y coordinates.");
            _output.WriteLine($"   This explains why text extraction finds text but redaction doesn't.");
        }
        else
        {
            _output.WriteLine("✓ Coordinate conversions match");
        }
    }
}
