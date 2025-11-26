using Avalonia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfEditor.Services;
using PdfEditor.Tests.Utilities;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

// Use the centralized coordinate converter

namespace PdfEditor.Tests.Integration;

/// <summary>
/// Visual coordinate verification tests that render PDFs to images and verify
/// pixel-level accuracy of coordinate conversions.
///
/// These tests verify:
/// 1. Black boxes appear at the exact visual position expected
/// 2. Text removal aligns with black box position
/// 3. Before/after image differences are localized to redaction area
/// 4. Cross-DPI coordinate consistency
/// </summary>
[Collection("Sequential")]
public class VisualCoordinateVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly RedactionService _redactionService;
    private readonly ILoggerFactory _loggerFactory;

    private const double PAGE_WIDTH = 612;   // Letter width in points
    private const double PAGE_HEIGHT = 792;  // Letter height in points

    public VisualCoordinateVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = NullLoggerFactory.Instance;
        var logger = NullLogger<RedactionService>.Instance;
        _redactionService = new RedactionService(logger, _loggerFactory);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    #region Visual Alignment Tests

    /// <summary>
    /// Verify that a black box drawn at specific coordinates appears at the correct
    /// pixel position when rendered.
    /// </summary>
    [SkippableFact]
    public void BlackBox_AtKnownPosition_AppearsAtCorrectPixelLocation()
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine("=== Test: Black Box Visual Position Verification ===");

        // Create PDF with a black box at known position
        var pdfPath = CreateTempPath("visual_blackbox_position.pdf");

        // Position the black box at (100, 100) in PDF points (Avalonia/top-left coords)
        var blackBoxRect = new Rect(100, 100, 200, 50);
        CreatePdfWithBlackBox(pdfPath, blackBoxRect);

        // Render at 150 DPI
        const int renderDpi = 150;
        using var bitmap = RenderPdfPage(pdfPath, 0, renderDpi);

        // Calculate expected pixel position
        var scale = renderDpi / 72.0;
        var expectedPixelX = (int)(blackBoxRect.X * scale);
        var expectedPixelY = (int)(blackBoxRect.Y * scale);
        var expectedPixelWidth = (int)(blackBoxRect.Width * scale);
        var expectedPixelHeight = (int)(blackBoxRect.Height * scale);

        _output.WriteLine($"PDF position (Avalonia coords): ({blackBoxRect.X}, {blackBoxRect.Y}) {blackBoxRect.Width}x{blackBoxRect.Height}");
        _output.WriteLine($"Expected pixels at {renderDpi} DPI: ({expectedPixelX}, {expectedPixelY}) {expectedPixelWidth}x{expectedPixelHeight}");
        _output.WriteLine($"Image size: {bitmap.Width}x{bitmap.Height}");

        // First, find where the dark pixels actually are
        var darkBounds = FindDarkPixelBounds(bitmap);
        if (darkBounds != null)
        {
            var (minX, minY, maxX, maxY) = darkBounds.Value;
            _output.WriteLine($"Actual dark pixels found at: ({minX}, {minY}) to ({maxX}, {maxY})");
            _output.WriteLine($"Actual size: {maxX - minX + 1}x{maxY - minY + 1}");

            // Verify the black box is at approximately the expected position (within tolerance)
            const int tolerance = 10; // Allow 10 pixel tolerance for rounding/anti-aliasing

            minX.Should().BeInRange(expectedPixelX - tolerance, expectedPixelX + tolerance,
                $"black box left edge should be near expected X={expectedPixelX}");
            minY.Should().BeInRange(expectedPixelY - tolerance, expectedPixelY + tolerance,
                $"black box top edge should be near expected Y={expectedPixelY}");

            var actualWidth = maxX - minX + 1;
            var actualHeight = maxY - minY + 1;
            actualWidth.Should().BeInRange(expectedPixelWidth - tolerance, expectedPixelWidth + tolerance,
                "black box width should match expected");
            actualHeight.Should().BeInRange(expectedPixelHeight - tolerance, expectedPixelHeight + tolerance,
                "black box height should match expected");
        }
        else
        {
            // No dark pixels found - fail the test
            false.Should().BeTrue("Expected to find dark pixels in rendered image, but found none");
        }

        // Verify center is black using actual bounds
        if (darkBounds != null)
        {
            var (minX, minY, maxX, maxY) = darkBounds.Value;
            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;

            var centerPixel = bitmap.GetPixel(centerX, centerY);
            _output.WriteLine($"Center pixel at ({centerX}, {centerY}): RGB({centerPixel.Red},{centerPixel.Green},{centerPixel.Blue})");

            centerPixel.Red.Should().BeLessThan(50, "center should be black");
            centerPixel.Green.Should().BeLessThan(50, "center should be black");
            centerPixel.Blue.Should().BeLessThan(50, "center should be black");
        }

        _output.WriteLine("✓ Black box appears at correct visual position");
    }

    /// <summary>
    /// Verify black box position at different DPI values to ensure coordinate scaling is consistent.
    /// </summary>
    [SkippableTheory]
    [InlineData(72)]
    [InlineData(96)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(300)]
    public void BlackBox_AtVariousDpi_MaintainsCorrectRelativePosition(int renderDpi)
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine($"=== Test: Black Box Position at {renderDpi} DPI ===");

        var pdfPath = CreateTempPath($"visual_dpi_{renderDpi}.pdf");

        // Create black box at center-ish of page
        var blackBoxRect = new Rect(200, 300, 150, 80);
        CreatePdfWithBlackBox(pdfPath, blackBoxRect);

        using var bitmap = RenderPdfPage(pdfPath, 0, renderDpi);

        var scale = renderDpi / 72.0;
        var expectedX = (int)(blackBoxRect.X * scale);
        var expectedY = (int)(blackBoxRect.Y * scale);
        var centerX = expectedX + (int)(blackBoxRect.Width * scale / 2);
        var centerY = expectedY + (int)(blackBoxRect.Height * scale / 2);

        _output.WriteLine($"At {renderDpi} DPI, center pixel should be at ({centerX}, {centerY})");

        var centerPixel = bitmap.GetPixel(centerX, centerY);

        // Center should be black
        var isBlack = centerPixel.Red < 30 && centerPixel.Green < 30 && centerPixel.Blue < 30;
        isBlack.Should().BeTrue($"center pixel at {renderDpi} DPI should be black, got RGB({centerPixel.Red},{centerPixel.Green},{centerPixel.Blue})");

        // Calculate relative position (should be constant across DPI)
        var relativeX = centerX / (double)bitmap.Width;
        var relativeY = centerY / (double)bitmap.Height;

        _output.WriteLine($"Relative position: ({relativeX:F4}, {relativeY:F4})");

        // Relative position should be approximately the same regardless of DPI
        var expectedRelativeX = (blackBoxRect.X + blackBoxRect.Width / 2) / PAGE_WIDTH;
        var expectedRelativeY = (blackBoxRect.Y + blackBoxRect.Height / 2) / PAGE_HEIGHT;

        relativeX.Should().BeApproximately(expectedRelativeX, 0.01, $"relative X position at {renderDpi} DPI");
        relativeY.Should().BeApproximately(expectedRelativeY, 0.01, $"relative Y position at {renderDpi} DPI");

        _output.WriteLine($"✓ Position verified at {renderDpi} DPI");
    }

    /// <summary>
    /// Verify that redaction service places black box at the same position as text removal.
    /// This is the critical alignment test.
    /// </summary>
    [SkippableFact]
    public void Redaction_BlackBoxAndTextRemoval_AreVisuallyAligned()
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine("=== Test: Black Box and Text Removal Alignment ===");

        // Create PDF with text at known position
        var pdfPath = CreateTempPath("visual_alignment_test.pdf");
        const double textX = 150;
        const double textY = 200;  // Avalonia Y (from top)
        CreatePdfWithTextAt(pdfPath, "REDACT_ME", textX, textY);

        // Verify text exists
        var textBefore = PdfTestHelpers.ExtractAllText(pdfPath);
        textBefore.Should().Contain("REDACT_ME");

        // Render before redaction
        const int renderDpi = 150;
        using var bitmapBefore = RenderPdfPage(pdfPath, 0, renderDpi);

        // Get word bounds using PdfPig for reference
        var wordBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "REDACT_ME");
        wordBounds.Should().NotBeNull("word should be found");

        _output.WriteLine($"PdfPig found word at PDF coords (bottom-left): " +
            $"({wordBounds!.Value.left:F2}, {wordBounds.Value.bottom:F2}) - ({wordBounds.Value.right:F2}, {wordBounds.Value.top:F2})");

        // Convert to Avalonia coordinates
        var avaloniaY = PAGE_HEIGHT - wordBounds.Value.top;
        var avaloniaRect = new Rect(
            wordBounds.Value.left - 5,
            avaloniaY - 5,
            wordBounds.Value.right - wordBounds.Value.left + 10,
            wordBounds.Value.top - wordBounds.Value.bottom + 10);

        _output.WriteLine($"Redaction area in Avalonia coords: ({avaloniaRect.X:F2}, {avaloniaRect.Y:F2}) {avaloniaRect.Width:F2}x{avaloniaRect.Height:F2}");

        // Apply redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        // Convert to render DPI coordinates for RedactionService
        var scale = renderDpi / 72.0;
        var renderRect = new Rect(
            avaloniaRect.X * scale,
            avaloniaRect.Y * scale,
            avaloniaRect.Width * scale,
            avaloniaRect.Height * scale);

        _redactionService.RedactArea(page, renderRect, renderDpi);

        var redactedPath = CreateTempPath("visual_alignment_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Verify text is removed
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        textAfter.Should().NotContain("REDACT_ME", "text should be removed from PDF structure");

        // Render after redaction
        using var bitmapAfter = RenderPdfPage(redactedPath, 0, renderDpi);

        // Check that black pixels appear in the expected redaction area
        var pixelX = (int)(avaloniaRect.X * scale);
        var pixelY = (int)(avaloniaRect.Y * scale);
        var centerX = pixelX + (int)(avaloniaRect.Width * scale / 2);
        var centerY = pixelY + (int)(avaloniaRect.Height * scale / 2);

        var centerPixelAfter = bitmapAfter.GetPixel(centerX, centerY);
        _output.WriteLine($"Center pixel after redaction at ({centerX}, {centerY}): RGB({centerPixelAfter.Red},{centerPixelAfter.Green},{centerPixelAfter.Blue})");

        // Should be black (the redaction box)
        var isBlack = centerPixelAfter.Red < 30 && centerPixelAfter.Green < 30 && centerPixelAfter.Blue < 30;
        isBlack.Should().BeTrue("center of redacted area should have black box");

        // Area outside redaction should remain white
        var outsideX = pixelX - 20;
        var outsideY = pixelY;
        if (outsideX > 0)
        {
            var outsidePixel = bitmapAfter.GetPixel(outsideX, outsideY);
            outsidePixel.Red.Should().BeGreaterThan(200, "area outside redaction should remain white");
        }

        _output.WriteLine("✓ Black box and text removal are visually aligned");
    }

    #endregion

    #region Before/After Image Diff Tests

    /// <summary>
    /// Compare before and after images to verify changes are localized to redaction area.
    /// </summary>
    [SkippableFact]
    public void Redaction_ImageDiff_ChangesLocalizedToRedactionArea()
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine("=== Test: Image Diff - Changes Localized ===");

        var pdfPath = CreateTempPath("visual_diff_test.pdf");

        // Create PDF with text at multiple positions
        CreatePdfWithMultipleTexts(pdfPath);

        var textBefore = PdfTestHelpers.ExtractAllText(pdfPath);
        textBefore.Should().Contain("TARGET");
        textBefore.Should().Contain("PRESERVE1");
        textBefore.Should().Contain("PRESERVE2");

        const int renderDpi = 150;
        using var bitmapBefore = RenderPdfPage(pdfPath, 0, renderDpi);

        // Find and redact only TARGET
        var targetBounds = GetWordBoundsFromPdfPig(pdfPath, 0, "TARGET");
        targetBounds.Should().NotBeNull();

        var avaloniaY = PAGE_HEIGHT - targetBounds!.Value.top;
        var redactionArea = new Rect(
            targetBounds.Value.left - 5,
            avaloniaY - 5,
            targetBounds.Value.right - targetBounds.Value.left + 10,
            targetBounds.Value.top - targetBounds.Value.bottom + 10);

        // Apply redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var scale = renderDpi / 72.0;
        var renderRect = new Rect(
            redactionArea.X * scale,
            redactionArea.Y * scale,
            redactionArea.Width * scale,
            redactionArea.Height * scale);

        _redactionService.RedactArea(page, renderRect, renderDpi);

        var redactedPath = CreateTempPath("visual_diff_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        using var bitmapAfter = RenderPdfPage(redactedPath, 0, renderDpi);

        // Calculate pixel regions
        var redactionPixelX = (int)(redactionArea.X * scale);
        var redactionPixelY = (int)(redactionArea.Y * scale);
        var redactionPixelW = (int)(redactionArea.Width * scale);
        var redactionPixelH = (int)(redactionArea.Height * scale);

        // Count changed pixels inside and outside redaction area
        var (changedInside, changedOutside, totalInside, totalOutside) =
            CompareImages(bitmapBefore, bitmapAfter,
                redactionPixelX, redactionPixelY, redactionPixelW, redactionPixelH,
                margin: 20); // Allow some margin for anti-aliasing

        _output.WriteLine($"Pixels inside redaction area: {changedInside} changed out of {totalInside} ({100.0 * changedInside / totalInside:F1}%)");
        _output.WriteLine($"Pixels outside redaction area: {changedOutside} changed out of {totalOutside} ({100.0 * changedOutside / totalOutside:F1}%)");

        // Most changes should be inside the redaction area
        changedInside.Should().BeGreaterThan(0, "redaction area should have changes");

        // Very few changes outside (allow small percentage for anti-aliasing/compression artifacts)
        var outsideChangePercent = 100.0 * changedOutside / totalOutside;
        outsideChangePercent.Should().BeLessThan(1.0,
            "changes outside redaction area should be minimal (< 1%)");

        // Verify preserved text is still there
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        textAfter.Should().Contain("PRESERVE1", "text outside redaction should be preserved");
        textAfter.Should().Contain("PRESERVE2", "text outside redaction should be preserved");
        textAfter.Should().NotContain("TARGET", "target text should be removed");

        _output.WriteLine("✓ Changes are localized to redaction area");
    }

    /// <summary>
    /// Verify that multiple redactions only affect their respective areas.
    /// </summary>
    [SkippableFact]
    public void MultipleRedactions_ImageDiff_EachAreaIndependent()
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine("=== Test: Multiple Redactions Independence ===");

        var pdfPath = CreateTempPath("visual_multi_redact.pdf");
        CreatePdfWithGridText(pdfPath);

        const int renderDpi = 150;
        using var bitmapOriginal = RenderPdfPage(pdfPath, 0, renderDpi);

        // Redact specific cells
        var cellsToRedact = new[] { "A1", "B2", "C3" };
        var cellPositions = new Dictionary<string, (double x, double y)>
        {
            ["A1"] = (100, 100),
            ["A2"] = (100, 200),
            ["A3"] = (100, 300),
            ["B1"] = (250, 100),
            ["B2"] = (250, 200),
            ["B3"] = (250, 300),
            ["C1"] = (400, 100),
            ["C2"] = (400, 200),
            ["C3"] = (400, 300),
        };

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var scale = renderDpi / 72.0;
        foreach (var cell in cellsToRedact)
        {
            var (x, y) = cellPositions[cell];
            var renderRect = new Rect(
                (x - 5) * scale,
                (y - 5) * scale,
                80 * scale,
                30 * scale);

            _redactionService.RedactArea(page, renderRect, renderDpi);
            _output.WriteLine($"Redacted {cell} at ({x}, {y})");
        }

        var redactedPath = CreateTempPath("visual_multi_redact_done.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        using var bitmapAfter = RenderPdfPage(redactedPath, 0, renderDpi);

        // Verify redacted cells have black boxes
        foreach (var cell in cellsToRedact)
        {
            var (x, y) = cellPositions[cell];
            var pixelX = (int)((x + 30) * scale);
            var pixelY = (int)((y + 5) * scale);

            var pixel = bitmapAfter.GetPixel(pixelX, pixelY);
            var isBlack = pixel.Red < 50 && pixel.Green < 50 && pixel.Blue < 50;
            isBlack.Should().BeTrue($"redacted cell {cell} should have black box at ({pixelX}, {pixelY})");
        }

        // Verify non-redacted cells are NOT black
        var cellsToPreserve = cellPositions.Keys.Except(cellsToRedact);
        foreach (var cell in cellsToPreserve)
        {
            var (x, y) = cellPositions[cell];
            var pixelX = (int)((x + 30) * scale);
            var pixelY = (int)((y + 5) * scale);

            var pixel = bitmapAfter.GetPixel(pixelX, pixelY);
            var isBlack = pixel.Red < 50 && pixel.Green < 50 && pixel.Blue < 50;
            isBlack.Should().BeFalse($"non-redacted cell {cell} should NOT have black box");
        }

        // Verify text extraction
        var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
        foreach (var cell in cellsToRedact)
        {
            textAfter.Should().NotContain(cell, $"redacted cell {cell} text should be removed");
        }
        foreach (var cell in cellsToPreserve)
        {
            textAfter.Should().Contain(cell, $"preserved cell {cell} text should remain");
        }

        _output.WriteLine("✓ Multiple redactions are independent");
    }

    #endregion

    #region Cross-DPI Consistency Tests

    /// <summary>
    /// Verify that the same redaction at different render DPI values
    /// removes the same text and produces visually similar results.
    /// </summary>
    [SkippableFact]
    public void Redaction_AcrossDpiValues_ProducesConsistentResults()
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine("=== Test: Cross-DPI Consistency ===");

        var testDpis = new[] { 72, 96, 150, 200, 300 };
        var redactionResults = new Dictionary<int, string>();

        foreach (var dpi in testDpis)
        {
            var pdfPath = CreateTempPath($"visual_cross_dpi_{dpi}_original.pdf");
            CreatePdfWithTextAt(pdfPath, "REMOVE_THIS", 200, 250);

            // Find text bounds
            var bounds = GetWordBoundsFromPdfPig(pdfPath, 0, "REMOVE_THIS");
            bounds.Should().NotBeNull();

            var avaloniaY = PAGE_HEIGHT - bounds!.Value.top;
            var pdfPointsRect = new Rect(
                bounds.Value.left - 5,
                avaloniaY - 5,
                bounds.Value.right - bounds.Value.left + 10,
                bounds.Value.top - bounds.Value.bottom + 10);

            // Apply redaction at this DPI
            var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            var page = document.Pages[0];

            var scale = dpi / 72.0;
            var renderRect = new Rect(
                pdfPointsRect.X * scale,
                pdfPointsRect.Y * scale,
                pdfPointsRect.Width * scale,
                pdfPointsRect.Height * scale);

            _redactionService.RedactArea(page, renderRect, dpi);

            var redactedPath = CreateTempPath($"visual_cross_dpi_{dpi}_redacted.pdf");
            _tempFiles.Add(redactedPath);
            document.Save(redactedPath);
            document.Dispose();

            // Extract remaining text
            var textAfter = PdfTestHelpers.ExtractAllText(redactedPath);
            redactionResults[dpi] = textAfter;

            _output.WriteLine($"DPI {dpi}: Text after redaction = '{textAfter.Trim()}'");
        }

        // All DPI values should produce the same result (text removed)
        foreach (var dpi in testDpis)
        {
            redactionResults[dpi].Should().NotContain("REMOVE_THIS",
                $"text should be removed at {dpi} DPI");
        }

        _output.WriteLine("✓ Cross-DPI consistency verified");
    }

    #endregion

    #region Edge Position Tests

    /// <summary>
    /// Test redaction at page corners to verify coordinate conversion at extremes.
    /// </summary>
    [SkippableTheory]
    [InlineData("top-left", 20, 20)]
    [InlineData("top-right", 500, 20)]
    [InlineData("bottom-left", 20, 700)]
    [InlineData("bottom-right", 500, 700)]
    [InlineData("center", 250, 350)]
    public void Redaction_AtPagePositions_BlackBoxAppearsCorrectly(string position, double x, double y)
    {
        Skip.IfNot(PdfTestHelpers.IsRenderingAvailable(), PdfTestHelpers.GetRenderingUnavailableMessage());

        _output.WriteLine($"=== Test: Redaction at {position} ({x}, {y}) ===");

        var pdfPath = CreateTempPath($"visual_edge_{position.Replace("-", "_")}.pdf");

        // Create PDF with text at specified position
        CreatePdfWithTextAt(pdfPath, position.ToUpper().Replace("-", ""), x, y);

        const int renderDpi = 150;

        // Apply redaction
        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];

        var scale = renderDpi / 72.0;
        var renderRect = new Rect(
            (x - 10) * scale,
            (y - 10) * scale,
            120 * scale,
            40 * scale);

        _redactionService.RedactArea(page, renderRect, renderDpi);

        var redactedPath = CreateTempPath($"visual_edge_{position.Replace("-", "_")}_redacted.pdf");
        _tempFiles.Add(redactedPath);
        document.Save(redactedPath);
        document.Dispose();

        // Render and verify black box position
        using var bitmap = RenderPdfPage(redactedPath, 0, renderDpi);

        var centerX = (int)((x + 50) * scale);
        var centerY = (int)((y + 5) * scale);

        // Ensure we're within bitmap bounds
        centerX = Math.Min(centerX, bitmap.Width - 1);
        centerY = Math.Min(centerY, bitmap.Height - 1);
        centerX = Math.Max(centerX, 0);
        centerY = Math.Max(centerY, 0);

        var pixel = bitmap.GetPixel(centerX, centerY);
        _output.WriteLine($"Pixel at ({centerX}, {centerY}): RGB({pixel.Red},{pixel.Green},{pixel.Blue})");

        var isBlack = pixel.Red < 50 && pixel.Green < 50 && pixel.Blue < 50;
        isBlack.Should().BeTrue($"redaction at {position} should produce black box");

        _output.WriteLine($"✓ Redaction at {position} verified");
    }

    #endregion

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdfe_visual_test_{Guid.NewGuid()}_{filename}");
        _tempFiles.Add(path);
        return path;
    }

    private void CreatePdfWithBlackBox(string path, Rect boxRect)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PAGE_WIDTH);
        page.Height = XUnit.FromPoint(PAGE_HEIGHT);

        using var gfx = XGraphics.FromPdfPage(page);
        var pageHeight = page.Height.Point;

        // Use CoordinateConverter for proper XGraphics conversion
        var (x, y, width, height) = CoordinateConverter.ForXGraphics(boxRect, pageHeight);

        gfx.DrawRectangle(XBrushes.Black, x, y, width, height);

        document.Save(path);
        document.Dispose();
    }

    /// <summary>
    /// Find the bounding box of dark pixels in a rendered image.
    /// Useful for verifying where content actually appears.
    /// </summary>
    private (int minX, int minY, int maxX, int maxY)? FindDarkPixelBounds(SKBitmap bitmap, int threshold = 100)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        bool foundAny = false;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < threshold && pixel.Green < threshold && pixel.Blue < threshold)
                {
                    foundAny = true;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return foundAny ? (minX, minY, maxX, maxY) : null;
    }

    private void CreatePdfWithTextAt(string path, string text, double x, double y)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PAGE_WIDTH);
        page.Height = XUnit.FromPoint(PAGE_HEIGHT);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        // XGraphics uses top-left origin (same as Avalonia), so use coordinates directly
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y));

        document.Save(path);
        document.Dispose();
    }

    private void CreatePdfWithMultipleTexts(string path)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PAGE_WIDTH);
        page.Height = XUnit.FromPoint(PAGE_HEIGHT);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 14);

        // XGraphics uses top-left origin (same as Avalonia), so use coordinates directly
        var texts = new[]
        {
            ("PRESERVE1", 100.0, 100.0),
            ("TARGET", 100.0, 300.0),
            ("PRESERVE2", 100.0, 500.0),
        };

        foreach (var (text, x, y) in texts)
        {
            // XGraphics uses top-left origin, so use coordinates directly
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y));
        }

        document.Save(path);
        document.Dispose();
    }

    private void CreatePdfWithGridText(string path)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PAGE_WIDTH);
        page.Height = XUnit.FromPoint(PAGE_HEIGHT);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        // XGraphics uses top-left origin (same as Avalonia), so use coordinates directly
        var cells = new[]
        {
            ("A1", 100.0, 100.0), ("B1", 250.0, 100.0), ("C1", 400.0, 100.0),
            ("A2", 100.0, 200.0), ("B2", 250.0, 200.0), ("C2", 400.0, 200.0),
            ("A3", 100.0, 300.0), ("B3", 250.0, 300.0), ("C3", 400.0, 300.0),
        };

        foreach (var (text, x, y) in cells)
        {
            // XGraphics uses top-left origin, so use coordinates directly
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, y));
        }

        document.Save(path);
        document.Dispose();
    }

    private SKBitmap RenderPdfPage(string pdfPath, int pageIndex, int dpi)
    {
        using var fileStream = File.OpenRead(pdfPath);
        using var memoryStream = new MemoryStream();
        fileStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        var options = new PDFtoImage.RenderOptions(Dpi: dpi);
        return PDFtoImage.Conversion.ToImage(memoryStream, page: pageIndex, options: options);
    }

    private void VerifyPixelIsBlack(SKBitmap bitmap, int x, int y, string location)
    {
        if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height)
        {
            _output.WriteLine($"Warning: {location} pixel ({x}, {y}) is outside bitmap bounds");
            return;
        }

        var pixel = bitmap.GetPixel(x, y);
        var isBlack = pixel.Red < 50 && pixel.Green < 50 && pixel.Blue < 50;
        isBlack.Should().BeTrue($"{location} pixel at ({x}, {y}) should be black, got RGB({pixel.Red},{pixel.Green},{pixel.Blue})");
    }

    private (double left, double bottom, double right, double top)? GetWordBoundsFromPdfPig(
        string pdfPath, int pageIndex, string word)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var page = document.GetPage(pageIndex + 1);

        var foundWord = page.GetWords()
            .FirstOrDefault(w => w.Text.Equals(word, StringComparison.OrdinalIgnoreCase));

        if (foundWord == null)
            return null;

        var bbox = foundWord.BoundingBox;
        return (bbox.Left, bbox.Bottom, bbox.Right, bbox.Top);
    }

    private (int changedInside, int changedOutside, int totalInside, int totalOutside) CompareImages(
        SKBitmap before, SKBitmap after,
        int redactX, int redactY, int redactW, int redactH,
        int margin = 0)
    {
        var changedInside = 0;
        var changedOutside = 0;
        var totalInside = 0;
        var totalOutside = 0;

        // Expand redaction area by margin
        var expandedX = Math.Max(0, redactX - margin);
        var expandedY = Math.Max(0, redactY - margin);
        var expandedRight = Math.Min(before.Width, redactX + redactW + margin);
        var expandedBottom = Math.Min(before.Height, redactY + redactH + margin);

        for (int y = 0; y < before.Height && y < after.Height; y++)
        {
            for (int x = 0; x < before.Width && x < after.Width; x++)
            {
                var pixelBefore = before.GetPixel(x, y);
                var pixelAfter = after.GetPixel(x, y);

                var changed = Math.Abs(pixelBefore.Red - pixelAfter.Red) > 10 ||
                              Math.Abs(pixelBefore.Green - pixelAfter.Green) > 10 ||
                              Math.Abs(pixelBefore.Blue - pixelAfter.Blue) > 10;

                var isInsideExpanded = x >= expandedX && x < expandedRight &&
                                       y >= expandedY && y < expandedBottom;

                if (isInsideExpanded)
                {
                    totalInside++;
                    if (changed) changedInside++;
                }
                else
                {
                    totalOutside++;
                    if (changed) changedOutside++;
                }
            }
        }

        return (changedInside, changedOutside, totalInside, totalOutside);
    }

    #endregion
}
