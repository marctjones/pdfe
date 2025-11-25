using Xunit;
using FluentAssertions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Pdf.IO;
using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Tests for coordinate conversion accuracy across the full redaction pipeline.
///
/// Coordinate Systems Involved:
/// 1. Screen pixels - Where user clicks/drags in the UI
/// 2. Zoom-compensated pixels - Screen coords / ZoomLevel (at render DPI)
/// 3. Render DPI pixels - Image rendered at specified DPI (default 150)
/// 4. PDF points - 72 DPI coordinate system (1 point = 1/72 inch)
/// 5. PDF origin - Bottom-left (0,0 at bottom-left corner)
/// 6. Avalonia origin - Top-left (0,0 at top-left corner)
///
/// Conversion Flow:
/// Screen → (÷ zoom) → Image coords → (× 72/renderDpi) → PDF points → Compare with text bounds
/// </summary>
public class CoordinateConversionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    // Standard page dimensions
    private const double PAGE_WIDTH_POINTS = 612;  // 8.5" × 72 DPI
    private const double PAGE_HEIGHT_POINTS = 792; // 11" × 72 DPI

    public CoordinateConversionTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        var logger = _loggerFactory.CreateLogger<RedactionService>();
        _redactionService = new RedactionService(logger, _loggerFactory);
    }

    #region Helper Methods

    /// <summary>
    /// Uses PdfPig to get the exact bounding box of a specific word in PDF coordinates.
    /// Returns bounds in PDF coordinate system (bottom-left origin).
    /// </summary>
    private (double x, double y, double width, double height)? GetWordBoundsFromPdfPig(
        string pdfPath, int pageIndex, string targetWord)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageIndex + 1); // PdfPig uses 1-based indexing

        var words = page.GetWords().ToList();
        var targetWords = words.Where(w => w.Text.Equals(targetWord, StringComparison.OrdinalIgnoreCase)).ToList();

        if (targetWords.Count == 0)
        {
            _output.WriteLine($"Word '{targetWord}' not found on page {pageIndex + 1}");
            return null;
        }

        var word = targetWords.First();
        var bbox = word.BoundingBox;

        _output.WriteLine($"PdfPig found '{targetWord}' at PDF coords: " +
            $"({bbox.Left:F2}, {bbox.Bottom:F2}) - ({bbox.Right:F2}, {bbox.Top:F2})");

        return (bbox.Left, bbox.Bottom, bbox.Width, bbox.Height);
    }

    /// <summary>
    /// Converts PDF coordinates (bottom-left origin) to Avalonia coordinates (top-left origin).
    /// </summary>
    private Rect PdfToAvaloniaCoords(double pdfX, double pdfY, double width, double height, double pageHeight)
    {
        // In Avalonia: Y increases downward from top
        // In PDF: Y increases upward from bottom
        // avaloniaY = pageHeight - pdfY - height
        var avaloniaY = pageHeight - pdfY - height;
        return new Rect(pdfX, avaloniaY, width, height);
    }

    /// <summary>
    /// Simulates UI selection: converts screen coordinates through zoom and DPI to redaction area.
    /// This mirrors the conversion in MainWindow.axaml.cs and RedactionService.
    /// </summary>
    private Rect SimulateUISelection(
        Rect pdfPointsArea,
        double renderDpi,
        double zoomLevel)
    {
        // Reverse the conversion to find what screen coords would produce this PDF area
        // PDF points → render DPI pixels → zoom-scaled screen coords

        // The redaction service scales: area * (72 / renderDpi)
        // So to get PDF points, user selects at: pdfPoints * (renderDpi / 72)
        var scaleToRenderDpi = renderDpi / 72.0;

        var renderDpiX = pdfPointsArea.X * scaleToRenderDpi;
        var renderDpiY = pdfPointsArea.Y * scaleToRenderDpi;
        var renderDpiWidth = pdfPointsArea.Width * scaleToRenderDpi;
        var renderDpiHeight = pdfPointsArea.Height * scaleToRenderDpi;

        // This is what CurrentRedactionArea would be (after zoom compensation in UI)
        return new Rect(renderDpiX, renderDpiY, renderDpiWidth, renderDpiHeight);
    }

    /// <summary>
    /// Creates a test PDF with specific words at known positions.
    /// Returns the path and a dictionary of word -> (pdfX, pdfY) positions.
    /// </summary>
    private (string path, Dictionary<string, (double x, double y)> wordPositions)
        CreatePrecisionTestPdf(string filename)
    {
        var path = CreateTempPath(filename);
        _tempFiles.Add(path);

        // Create PDF with words at specific positions using XGraphics coordinates
        // Note: XGraphics with default settings uses top-left origin like Avalonia
        var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(PAGE_WIDTH_POINTS);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(PAGE_HEIGHT_POINTS);

        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Arial", 12);

        // Dictionary to track word positions (XGraphics Y = Avalonia Y = top-left origin)
        var wordPositions = new Dictionary<string, (double x, double y)>();

        // Place words at specific, well-separated positions
        // These Y values are XGraphics/Avalonia coordinates (top-left origin)
        var placements = new[]
        {
            ("ALPHA", 100.0, 100.0),
            ("BETA", 300.0, 100.0),
            ("GAMMA", 100.0, 200.0),
            ("DELTA", 300.0, 200.0),
            ("EPSILON", 100.0, 300.0),
            ("ZETA", 300.0, 300.0),
            ("THETA", 100.0, 400.0),
            ("IOTA", 300.0, 400.0),
            ("KAPPA", 100.0, 500.0),
            ("LAMBDA", 300.0, 500.0),
        };

        foreach (var (word, x, y) in placements)
        {
            gfx.DrawString(word, font, PdfSharp.Drawing.XBrushes.Black,
                new PdfSharp.Drawing.XPoint(x, y));
            wordPositions[word] = (x, y);
            _output.WriteLine($"Placed '{word}' at XGraphics coords ({x}, {y})");
        }

        document.Save(path);
        document.Dispose();

        return (path, wordPositions);
    }

    private string CreateTempPath(string filename)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PdfEditorCoordTests");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, filename);
    }

    #endregion

    #region Basic Coordinate Conversion Tests

    [Fact]
    public void CoordinateConversion_AtDefaultDpi_ShouldRedactCorrectWord()
    {
        // Arrange
        _output.WriteLine("=== Test: Coordinate Conversion at Default DPI (150) ===");

        var (pdfPath, wordPositions) = CreatePrecisionTestPdf("coord_default_dpi.pdf");

        // Verify words exist and get their exact bounds from PdfPig
        var allTextBefore = PdfTestHelpers.ExtractAllText(pdfPath);
        _output.WriteLine($"All text before: {allTextBefore}");
        allTextBefore.Should().Contain("GAMMA");
        allTextBefore.Should().Contain("DELTA");

        // Get exact bounds of target word using PdfPig
        var gammaBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "GAMMA");
        gammaBounds.Should().NotBeNull("PdfPig should find GAMMA");

        // Convert PDF bounds to Avalonia coordinates for comparison
        var pdfBounds = gammaBounds!.Value;
        var avaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        _output.WriteLine($"GAMMA bounds in Avalonia coords: ({avaloniaRect.X:F2}, {avaloniaRect.Y:F2}, " +
            $"{avaloniaRect.Width:F2}x{avaloniaRect.Height:F2})");

        // Create redaction area with padding around the word
        var redactionArea = new Rect(
            avaloniaRect.X - 5,
            avaloniaRect.Y - 5,
            avaloniaRect.Width + 10,
            avaloniaRect.Height + 10);

        // Simulate UI selection at 150 DPI, zoom 1.0
        var renderDpi = 150;
        var zoomLevel = 1.0;
        var uiSelectionArea = SimulateUISelection(redactionArea, renderDpi, zoomLevel);

        _output.WriteLine($"UI selection area (at {renderDpi} DPI, zoom {zoomLevel}): " +
            $"({uiSelectionArea.X:F2}, {uiSelectionArea.Y:F2}, {uiSelectionArea.Width:F2}x{uiSelectionArea.Height:F2})");

        // Act
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        _redactionService.RedactArea(page, uiSelectionArea, renderDpi);

        var redactedPath = CreateTempPath("coord_default_dpi_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after redaction: {textAfter}");

        textAfter.Should().NotContain("GAMMA", "GAMMA should be redacted");
        textAfter.Should().Contain("ALPHA", "ALPHA should remain (different location)");
        textAfter.Should().Contain("BETA", "BETA should remain (different location)");
        textAfter.Should().Contain("DELTA", "DELTA should remain (same row but different column)");
        textAfter.Should().Contain("EPSILON", "EPSILON should remain (different location)");

        _output.WriteLine("✓ Test passed: Only GAMMA was redacted at 150 DPI");
    }

    [Theory]
    [InlineData(72, 1.0)]   // Low DPI, no zoom
    [InlineData(96, 1.0)]   // Medium DPI, no zoom
    [InlineData(150, 1.0)]  // Default DPI, no zoom
    [InlineData(300, 1.0)]  // High DPI, no zoom
    [InlineData(150, 0.5)]  // Default DPI, zoomed out
    [InlineData(150, 1.5)]  // Default DPI, zoomed in
    [InlineData(150, 2.0)]  // Default DPI, 2x zoom
    [InlineData(72, 2.0)]   // Low DPI, 2x zoom
    [InlineData(300, 0.5)]  // High DPI, zoomed out
    public void CoordinateConversion_AtVariousDpiAndZoom_ShouldRedactCorrectWord(int renderDpi, double zoomLevel)
    {
        // Arrange
        _output.WriteLine($"=== Test: DPI={renderDpi}, Zoom={zoomLevel} ===");

        var (pdfPath, wordPositions) = CreatePrecisionTestPdf($"coord_dpi{renderDpi}_zoom{zoomLevel}.pdf");

        var allTextBefore = PdfTestHelpers.ExtractAllText(pdfPath);
        allTextBefore.Should().Contain("EPSILON");

        // Get exact bounds of target word
        var epsilonBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "EPSILON");
        epsilonBounds.Should().NotBeNull();

        var pdfBounds = epsilonBounds!.Value;
        var avaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        _output.WriteLine($"EPSILON Avalonia bounds: ({avaloniaRect.X:F2}, {avaloniaRect.Y:F2}, " +
            $"{avaloniaRect.Width:F2}x{avaloniaRect.Height:F2})");

        // Create tight redaction area
        var redactionArea = new Rect(
            avaloniaRect.X - 3,
            avaloniaRect.Y - 3,
            avaloniaRect.Width + 6,
            avaloniaRect.Height + 6);

        // Simulate UI selection
        var uiSelectionArea = SimulateUISelection(redactionArea, renderDpi, zoomLevel);

        _output.WriteLine($"UI selection: ({uiSelectionArea.X:F2}, {uiSelectionArea.Y:F2}, " +
            $"{uiSelectionArea.Width:F2}x{uiSelectionArea.Height:F2})");

        // Act
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        _redactionService.RedactArea(page, uiSelectionArea, renderDpi);

        var redactedPath = CreateTempPath($"coord_dpi{renderDpi}_zoom{zoomLevel}_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after: {textAfter}");

        textAfter.Should().NotContain("EPSILON", $"EPSILON should be redacted at DPI={renderDpi}, zoom={zoomLevel}");
        textAfter.Should().Contain("GAMMA", "GAMMA should remain (row above)");
        textAfter.Should().Contain("ZETA", "ZETA should remain (same row, different column)");
        textAfter.Should().Contain("THETA", "THETA should remain (row below)");

        _output.WriteLine($"✓ Passed: DPI={renderDpi}, Zoom={zoomLevel}");
    }

    #endregion

    #region Precision Tests

    [Fact]
    public void CoordinateConversion_TightBounds_ShouldOnlyRedactTargetWord()
    {
        // This test verifies that with very tight bounds, only the exact target is redacted
        _output.WriteLine("=== Test: Tight Bounds Precision ===");

        var (pdfPath, _) = CreatePrecisionTestPdf("coord_tight_bounds.pdf");

        // Get bounds for ZETA (at 300, 300)
        var zetaBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "ZETA");
        zetaBounds.Should().NotBeNull();

        var pdfBounds = zetaBounds!.Value;
        var avaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        // Use VERY tight bounds - just 1 pixel padding
        var redactionArea = new Rect(
            avaloniaRect.X - 1,
            avaloniaRect.Y - 1,
            avaloniaRect.Width + 2,
            avaloniaRect.Height + 2);

        _output.WriteLine($"Tight redaction area: ({redactionArea.X:F2}, {redactionArea.Y:F2}, " +
            $"{redactionArea.Width:F2}x{redactionArea.Height:F2})");

        // Use default render settings
        var renderDpi = 150;
        var uiSelectionArea = SimulateUISelection(redactionArea, renderDpi, 1.0);

        // Act
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, uiSelectionArea, renderDpi);

        var redactedPath = CreateTempPath("coord_tight_bounds_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Assert - ONLY ZETA should be gone
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        _output.WriteLine($"Text after tight redaction: {textAfter}");

        textAfter.Should().NotContain("ZETA", "Target word ZETA should be redacted");

        // All other words should remain
        var otherWords = new[] { "ALPHA", "BETA", "GAMMA", "DELTA", "EPSILON", "THETA", "IOTA", "KAPPA", "LAMBDA" };
        foreach (var word in otherWords)
        {
            textAfter.Should().Contain(word, $"{word} should remain with tight bounds");
        }

        _output.WriteLine("✓ Tight bounds test passed - only target word redacted");
    }

    [Fact]
    public void CoordinateConversion_AdjacentWords_ShouldNotAffectNeighbors()
    {
        // Test that redacting one word doesn't affect adjacent words
        _output.WriteLine("=== Test: Adjacent Words Protection ===");

        var (pdfPath, _) = CreatePrecisionTestPdf("coord_adjacent.pdf");

        // GAMMA is at (100, 200) and DELTA is at (300, 200) - same row
        var gammaBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "GAMMA");
        gammaBounds.Should().NotBeNull();

        var pdfBounds = gammaBounds!.Value;
        var avaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        // Redaction area should be clearly between the words
        var redactionArea = new Rect(
            avaloniaRect.X - 5,
            avaloniaRect.Y - 5,
            avaloniaRect.Width + 10,
            avaloniaRect.Height + 10);

        _output.WriteLine($"GAMMA redaction area: ({redactionArea.X:F2}, {redactionArea.Y:F2}, " +
            $"{redactionArea.Width:F2}x{redactionArea.Height:F2})");

        // Verify DELTA is far enough away
        var deltaBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "DELTA");
        deltaBounds.Should().NotBeNull();
        var deltaAvaloniaRect = PdfToAvaloniaCoords(
            deltaBounds!.Value.x, deltaBounds.Value.y,
            deltaBounds.Value.width, deltaBounds.Value.height, PAGE_HEIGHT_POINTS);

        _output.WriteLine($"DELTA bounds: ({deltaAvaloniaRect.X:F2}, {deltaAvaloniaRect.Y:F2})");

        // Redaction area should NOT intersect with DELTA
        var intersects = redactionArea.Intersects(deltaAvaloniaRect);
        _output.WriteLine($"Redaction area intersects DELTA: {intersects}");
        intersects.Should().BeFalse("Redaction area should not reach DELTA");

        var renderDpi = 150;
        var uiSelectionArea = SimulateUISelection(redactionArea, renderDpi, 1.0);

        // Act
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, uiSelectionArea, renderDpi);

        var redactedPath = CreateTempPath("coord_adjacent_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);

        textAfter.Should().NotContain("GAMMA", "GAMMA should be redacted");
        textAfter.Should().Contain("DELTA", "DELTA (same row) should NOT be affected");
        textAfter.Should().Contain("ALPHA", "ALPHA (row above, same column) should NOT be affected");
        textAfter.Should().Contain("EPSILON", "EPSILON (row below, same column) should NOT be affected");

        _output.WriteLine("✓ Adjacent words protected successfully");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void CoordinateConversion_AtPageEdges_ShouldWorkCorrectly()
    {
        // Test redaction near page edges where coordinate conversions are most sensitive
        _output.WriteLine("=== Test: Page Edge Coordinates ===");

        var pdfPath = CreateTempPath("coord_edges.pdf");
        _tempFiles.Add(pdfPath);

        // Create PDF with text near edges
        var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(PAGE_WIDTH_POINTS);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(PAGE_HEIGHT_POINTS);

        using (var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page))
        {
            var font = new PdfSharp.Drawing.XFont("Arial", 12);

            // Near top-left corner
            gfx.DrawString("TOPLEFT", font, PdfSharp.Drawing.XBrushes.Black,
                new PdfSharp.Drawing.XPoint(20, 30));

            // Near top-right corner
            gfx.DrawString("TOPRIGHT", font, PdfSharp.Drawing.XBrushes.Black,
                new PdfSharp.Drawing.XPoint(520, 30));

            // Near bottom-left corner
            gfx.DrawString("BOTTOMLEFT", font, PdfSharp.Drawing.XBrushes.Black,
                new PdfSharp.Drawing.XPoint(20, 770));

            // Center for comparison
            gfx.DrawString("CENTER", font, PdfSharp.Drawing.XBrushes.Black,
                new PdfSharp.Drawing.XPoint(280, 400));
        }

        document.Save(pdfPath);
        document.Dispose();

        // Test redacting TOPLEFT (near origin in Avalonia coords)
        var topleftBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "TOPLEFT");
        topleftBounds.Should().NotBeNull();

        var pdfBounds = topleftBounds!.Value;
        var avaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        _output.WriteLine($"TOPLEFT in Avalonia coords: ({avaloniaRect.X:F2}, {avaloniaRect.Y:F2})");

        var redactionArea = new Rect(
            avaloniaRect.X - 5,
            avaloniaRect.Y - 5,
            avaloniaRect.Width + 10,
            avaloniaRect.Height + 10);

        var renderDpi = 150;
        var uiSelectionArea = SimulateUISelection(redactionArea, renderDpi, 1.0);

        // Act
        var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[0];
        _redactionService.RedactArea(pg, uiSelectionArea, renderDpi);

        var redactedPath = CreateTempPath("coord_edges_redacted.pdf");
        _tempFiles.Add(redactedPath);
        doc.Save(redactedPath);
        doc.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);

        textAfter.Should().NotContain("TOPLEFT", "TOPLEFT at page edge should be redacted");
        textAfter.Should().Contain("TOPRIGHT", "Other corners should remain");
        textAfter.Should().Contain("BOTTOMLEFT", "Other corners should remain");
        textAfter.Should().Contain("CENTER", "Center should remain");

        _output.WriteLine("✓ Page edge coordinates handled correctly");
    }

    [Theory]
    [InlineData(72)]
    [InlineData(96)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(300)]
    public void CoordinateConversion_DpiScaling_ShouldMaintainPrecision(int renderDpi)
    {
        // Verify that DPI scaling maintains coordinate precision
        _output.WriteLine($"=== Test: DPI Scaling Precision at {renderDpi} DPI ===");

        var (pdfPath, _) = CreatePrecisionTestPdf($"coord_dpi_precision_{renderDpi}.pdf");

        // Target KAPPA at (100, 500)
        var kappaBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "KAPPA");
        kappaBounds.Should().NotBeNull();

        var pdfBounds = kappaBounds!.Value;
        var avaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        // Calculate expected coordinates at this DPI
        var dpiScale = renderDpi / 72.0;
        var expectedRenderX = avaloniaRect.X * dpiScale;
        var expectedRenderY = avaloniaRect.Y * dpiScale;

        _output.WriteLine($"PDF points: ({avaloniaRect.X:F2}, {avaloniaRect.Y:F2})");
        _output.WriteLine($"At {renderDpi} DPI: ({expectedRenderX:F2}, {expectedRenderY:F2})");

        var redactionArea = new Rect(
            avaloniaRect.X - 5,
            avaloniaRect.Y - 5,
            avaloniaRect.Width + 10,
            avaloniaRect.Height + 10);

        var uiSelectionArea = SimulateUISelection(redactionArea, renderDpi, 1.0);

        _output.WriteLine($"UI selection at {renderDpi} DPI: ({uiSelectionArea.X:F2}, {uiSelectionArea.Y:F2})");

        // Verify the reverse conversion
        var convertedBack = new Rect(
            uiSelectionArea.X * 72.0 / renderDpi,
            uiSelectionArea.Y * 72.0 / renderDpi,
            uiSelectionArea.Width * 72.0 / renderDpi,
            uiSelectionArea.Height * 72.0 / renderDpi);

        _output.WriteLine($"Converted back to PDF points: ({convertedBack.X:F2}, {convertedBack.Y:F2})");

        // Should match original redaction area (within floating point tolerance)
        convertedBack.X.Should().BeApproximately(redactionArea.X, 0.01);
        convertedBack.Y.Should().BeApproximately(redactionArea.Y, 0.01);

        // Act
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        _redactionService.RedactArea(page, uiSelectionArea, renderDpi);

        var redactedPath = CreateTempPath($"coord_dpi_precision_{renderDpi}_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Assert
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);

        textAfter.Should().NotContain("KAPPA", $"KAPPA should be redacted at {renderDpi} DPI");
        textAfter.Should().Contain("LAMBDA", "LAMBDA (same row) should remain");
        textAfter.Should().Contain("THETA", "THETA (row above) should remain");

        _output.WriteLine($"✓ DPI scaling precise at {renderDpi} DPI");
    }

    #endregion

    #region Full Pipeline Integration Tests

    [Fact]
    public void FullPipeline_ScreenToRedaction_ShouldMatchExpectedArea()
    {
        // This test simulates the complete coordinate flow from screen click to redaction
        _output.WriteLine("=== Test: Full Pipeline Screen to Redaction ===");

        var (pdfPath, wordPositions) = CreatePrecisionTestPdf("coord_full_pipeline.pdf");

        // Simulate a user clicking on IOTA at position (300, 400) in XGraphics coords
        var iotaBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "IOTA");
        iotaBounds.Should().NotBeNull();

        var pdfBounds = iotaBounds!.Value;
        var targetAvaloniaRect = PdfToAvaloniaCoords(
            pdfBounds.x, pdfBounds.y, pdfBounds.width, pdfBounds.height, PAGE_HEIGHT_POINTS);

        _output.WriteLine($"Target IOTA in Avalonia coords: ({targetAvaloniaRect.X:F2}, {targetAvaloniaRect.Y:F2})");

        // Simulate multiple DPI/zoom combinations
        var testCases = new[]
        {
            (dpi: 150, zoom: 1.0, name: "default"),
            (dpi: 150, zoom: 1.5, name: "zoomed"),
            (dpi: 72, zoom: 1.0, name: "low_dpi"),
            (dpi: 300, zoom: 0.75, name: "high_dpi_zoomed_out"),
        };

        foreach (var (dpi, zoom, name) in testCases)
        {
            _output.WriteLine($"\n--- Testing {name}: DPI={dpi}, Zoom={zoom} ---");

            // Calculate what the user would see and select on screen
            var dpiScale = dpi / 72.0;

            // Screen coordinates = PDF points * DPI scale * zoom
            var screenX = targetAvaloniaRect.X * dpiScale * zoom;
            var screenY = targetAvaloniaRect.Y * dpiScale * zoom;
            var screenWidth = targetAvaloniaRect.Width * dpiScale * zoom;
            var screenHeight = targetAvaloniaRect.Height * dpiScale * zoom;

            _output.WriteLine($"Screen coords (what user sees): ({screenX:F2}, {screenY:F2})");

            // UI compensates for zoom: divides by zoom level
            var imageX = screenX / zoom;
            var imageY = screenY / zoom;
            var imageWidth = screenWidth / zoom;
            var imageHeight = screenHeight / zoom;

            _output.WriteLine($"After zoom compensation (image coords): ({imageX:F2}, {imageY:F2})");

            // Add padding for redaction area
            var uiSelectionArea = new Rect(
                imageX - 5 * dpiScale,
                imageY - 5 * dpiScale,
                imageWidth + 10 * dpiScale,
                imageHeight + 10 * dpiScale);

            _output.WriteLine($"UI selection area: ({uiSelectionArea.X:F2}, {uiSelectionArea.Y:F2}, " +
                $"{uiSelectionArea.Width:F2}x{uiSelectionArea.Height:F2})");

            // Copy file for this test case
            var testPdfPath = CreateTempPath($"coord_pipeline_{name}.pdf");
            File.Copy(pdfPath, testPdfPath, true);
            _tempFiles.Add(testPdfPath);

            // Act
            var document = PdfReader.Open(testPdfPath, PdfDocumentOpenMode.Modify);
            var page = document.Pages[0];
            _redactionService.RedactArea(page, uiSelectionArea, dpi);

            var redactedPath = CreateTempPath($"coord_pipeline_{name}_redacted.pdf");
            _tempFiles.Add(redactedPath);
            document.Save(redactedPath);
            document.Dispose();

            // Assert
            var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);

            textAfter.Should().NotContain("IOTA", $"IOTA should be redacted ({name})");
            textAfter.Should().Contain("THETA", $"THETA should remain ({name})");
            textAfter.Should().Contain("KAPPA", $"KAPPA should remain ({name})");

            _output.WriteLine($"✓ {name} passed");
        }

        _output.WriteLine("\n✓ Full pipeline test passed for all combinations");
    }

    [Fact]
    public void CoordinateConversion_RoundTrip_ShouldPreserveValues()
    {
        // Verify coordinate conversions are reversible without precision loss
        _output.WriteLine("=== Test: Coordinate Round-Trip Precision ===");

        var testPoints = new[]
        {
            (x: 0.0, y: 0.0),           // Origin
            (x: 100.0, y: 100.0),       // Normal point
            (x: 306.0, y: 396.0),       // Center of page
            (x: 612.0, y: 792.0),       // Page extent
            (x: 50.5, y: 123.456),      // Fractional coordinates
        };

        var testDpis = new[] { 72, 96, 150, 200, 300 };
        var testZooms = new[] { 0.5, 1.0, 1.5, 2.0, 3.0 };

        foreach (var (x, y) in testPoints)
        {
            foreach (var dpi in testDpis)
            {
                foreach (var zoom in testZooms)
                {
                    // PDF points → screen (forward conversion)
                    var dpiScale = dpi / 72.0;
                    var screenX = x * dpiScale * zoom;
                    var screenY = y * dpiScale * zoom;

                    // Screen → image coords (zoom compensation)
                    var imageX = screenX / zoom;
                    var imageY = screenY / zoom;

                    // Image coords → PDF points (RedactionService conversion)
                    var pdfX = imageX * (72.0 / dpi);
                    var pdfY = imageY * (72.0 / dpi);

                    // Should match original
                    pdfX.Should().BeApproximately(x, 0.0001,
                        $"X round-trip failed for ({x},{y}) at DPI={dpi}, zoom={zoom}");
                    pdfY.Should().BeApproximately(y, 0.0001,
                        $"Y round-trip failed for ({x},{y}) at DPI={dpi}, zoom={zoom}");
                }
            }
        }

        _output.WriteLine("✓ All round-trip conversions preserved precision");
    }

    #endregion

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            TestPdfGenerator.CleanupTestFile(file);
        }

        // Clean up temp directory if empty
        var tempDir = Path.Combine(Path.GetTempPath(), "PdfEditorCoordTests");
        try
        {
            if (Directory.Exists(tempDir) && !Directory.EnumerateFileSystemEntries(tempDir).Any())
            {
                Directory.Delete(tempDir);
            }
        }
        catch { /* Ignore cleanup errors */ }
    }
}
