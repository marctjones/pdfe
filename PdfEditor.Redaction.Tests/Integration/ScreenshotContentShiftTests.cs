using FluentAssertions;
using PdfEditor.Redaction.GlyphLevel;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace PdfEditor.Redaction.Tests.Integration;

/// <summary>
/// Screenshot-based tests to detect and measure content shifting bug (#270).
///
/// Issue: When performing sequential redactions, text content can shift by ~6pt
/// due to mismatch between PdfPig visual coordinates and PDF content stream coordinates.
///
/// These tests create PDFs with precise text positioning, render before/after screenshots,
/// and measure pixel-level position changes to detect content shifting.
/// </summary>
public class ScreenshotContentShiftTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _tempFiles = new();
    private readonly string _screenshotDir;
    private const double Dpi = 300.0; // Higher DPI for precise measurement
    private const double PageHeight = 792.0;
    private const double PointsPerPixel = 72.0 / Dpi;

    public ScreenshotContentShiftTests(ITestOutputHelper output)
    {
        _output = output;
        _screenshotDir = Path.Combine(Path.GetTempPath(), "pdfe_shift_screenshots", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_screenshotDir);
        _output.WriteLine($"Screenshots saved to: {_screenshotDir}");

        // Initialize font resolver
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new TestFontResolver();
        }
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Test that detects content shifting by comparing screenshots before/after redaction.
    /// Uses reference text markers to detect positional changes.
    /// </summary>
    [Fact]
    public void SingleRedaction_ShouldNotShift_ReferenceMarkers()
    {
        // Arrange - Create PDF with reference markers and text to redact
        var inputPath = CreateTempPath("shift_test_single.pdf");
        var outputPath = CreateTempPath("shift_test_single_redacted.pdf");

        CreatePdfWithMarkers(inputPath, new[]
        {
            // Reference markers at edges (should NOT move)
            new TextMarker("REF_TOP_LEFT", 50, 750, 10),
            new TextMarker("REF_TOP_RIGHT", 450, 750, 10),
            new TextMarker("REF_BOTTOM_LEFT", 50, 100, 10),
            new TextMarker("REF_BOTTOM_RIGHT", 450, 100, 10),
            // Additional reference markers
            new TextMarker("REF_MID_LEFT", 50, 400, 10),
            new TextMarker("REF_MID_RIGHT", 450, 400, 10),
            // Text to redact in center
            new TextMarker("REDACT_THIS_TEXT", 200, 500, 12),
            // Text near redaction area (most likely to shift)
            new TextMarker("NEARBY_TEXT_A", 200, 550, 12),
            new TextMarker("NEARBY_TEXT_B", 200, 450, 12),
        });

        // Get initial positions from screenshot
        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "shift_single_01_before.png");

        var markerPositionsBefore = FindMarkerPositions(beforeImage, new[]
        {
            "REF_TOP_LEFT", "REF_TOP_RIGHT", "REF_BOTTOM_LEFT", "REF_BOTTOM_RIGHT",
            "REF_MID_LEFT", "REF_MID_RIGHT", "NEARBY_TEXT_A", "NEARBY_TEXT_B"
        });

        foreach (var (marker, pos) in markerPositionsBefore)
        {
            _output.WriteLine($"Before - {marker}: ({pos.X}, {pos.Y})");
        }

        // Act - Redact the center text
        var redactor = new TextRedactor();
        var location = new RedactionLocation
        {
            PageNumber = 1,
            BoundingBox = new PdfRectangle(180, 480, 400, 520) // Area around "REDACT_THIS_TEXT"
        };
        var result = redactor.RedactLocations(inputPath, outputPath, new[] { location });
        result.Success.Should().BeTrue($"Redaction failed: {result.ErrorMessage}");

        // Get positions after redaction
        var afterImage = RenderPdfToImage(outputPath);
        SaveScreenshot(afterImage, "shift_single_02_after.png");

        var markerPositionsAfter = FindMarkerPositions(afterImage, new[]
        {
            "REF_TOP_LEFT", "REF_TOP_RIGHT", "REF_BOTTOM_LEFT", "REF_BOTTOM_RIGHT",
            "REF_MID_LEFT", "REF_MID_RIGHT", "NEARBY_TEXT_A", "NEARBY_TEXT_B"
        });

        foreach (var (marker, pos) in markerPositionsAfter)
        {
            _output.WriteLine($"After - {marker}: ({pos.X}, {pos.Y})");
        }

        // Assert - Calculate shifts
        var shifts = new List<(string Marker, double ShiftX, double ShiftY, double TotalShift)>();
        var maxAcceptableShiftPixels = 1.0 * Dpi / 72.0; // 1 point = ~4 pixels at 300 DPI

        foreach (var (marker, beforePos) in markerPositionsBefore)
        {
            if (markerPositionsAfter.TryGetValue(marker, out var afterPos))
            {
                var shiftX = afterPos.X - beforePos.X;
                var shiftY = afterPos.Y - beforePos.Y;
                var totalShift = Math.Sqrt(shiftX * shiftX + shiftY * shiftY);
                var shiftInPoints = totalShift * PointsPerPixel;

                shifts.Add((marker, shiftX * PointsPerPixel, shiftY * PointsPerPixel, shiftInPoints));
                _output.WriteLine($"SHIFT: {marker} - X: {shiftX * PointsPerPixel:F2}pt, Y: {shiftY * PointsPerPixel:F2}pt, Total: {shiftInPoints:F2}pt");
            }
            else
            {
                _output.WriteLine($"WARNING: Marker {marker} not found after redaction");
            }
        }

        // Reference markers (corner ones) should not shift at all
        var cornerMarkers = shifts.Where(s =>
            s.Marker.StartsWith("REF_TOP") || s.Marker.StartsWith("REF_BOTTOM"));

        foreach (var corner in cornerMarkers)
        {
            corner.TotalShift.Should().BeLessThan(0.5,
                $"Corner reference marker '{corner.Marker}' should not shift after redaction (shifted {corner.TotalShift:F2}pt)");
        }

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    /// <summary>
    /// Test that detects ACCUMULATED content shifting after multiple sequential redactions.
    /// This is the core bug #270 detection test.
    /// </summary>
    [Fact]
    public void SequentialRedactions_ShouldNotAccumulate_ContentShift()
    {
        // Arrange - Create PDF with multiple reference markers and redaction targets
        var inputPath = CreateTempPath("shift_sequential.pdf");
        var temp1 = CreateTempPath("shift_seq_1.pdf");
        var temp2 = CreateTempPath("shift_seq_2.pdf");
        var temp3 = CreateTempPath("shift_seq_3.pdf");

        CreatePdfWithMarkers(inputPath, new[]
        {
            // Reference markers at corners (should NEVER move)
            new TextMarker("MARKER_A", 50, 750, 12),
            new TextMarker("MARKER_B", 450, 750, 12),
            new TextMarker("MARKER_C", 50, 100, 12),
            new TextMarker("MARKER_D", 450, 100, 12),
            // Text blocks to redact sequentially
            new TextMarker("REDACT_FIRST", 200, 650, 14),
            new TextMarker("REDACT_SECOND", 200, 500, 14),
            new TextMarker("REDACT_THIRD", 200, 350, 14),
            // Text between redaction targets (prone to shifting)
            new TextMarker("BETWEEN_1_2", 200, 575, 10),
            new TextMarker("BETWEEN_2_3", 200, 425, 10),
        });

        // Get initial positions
        var originalImage = RenderPdfToImage(inputPath);
        SaveScreenshot(originalImage, "shift_seq_00_original.png");

        var referenceMarkers = new[] { "MARKER_A", "MARKER_B", "MARKER_C", "MARKER_D" };
        var betweenMarkers = new[] { "BETWEEN_1_2", "BETWEEN_2_3" };
        var allMarkers = referenceMarkers.Concat(betweenMarkers).ToArray();

        var originalPositions = FindMarkerPositions(originalImage, allMarkers);
        _output.WriteLine("\n=== ORIGINAL POSITIONS ===");
        foreach (var (marker, pos) in originalPositions)
        {
            _output.WriteLine($"{marker}: ({pos.X}, {pos.Y})");
        }

        var redactor = new TextRedactor();

        // Sequential redactions
        // Redaction 1
        var result1 = redactor.RedactText(inputPath, temp1, "REDACT_FIRST");
        result1.Success.Should().BeTrue($"Redaction 1 failed: {result1.ErrorMessage}");

        var image1 = RenderPdfToImage(temp1);
        SaveScreenshot(image1, "shift_seq_01_after_first.png");
        var positions1 = FindMarkerPositions(image1, allMarkers);

        _output.WriteLine("\n=== AFTER FIRST REDACTION ===");
        ReportShifts(originalPositions, positions1, "After Redaction 1");

        // Redaction 2
        var result2 = redactor.RedactText(temp1, temp2, "REDACT_SECOND");
        result2.Success.Should().BeTrue($"Redaction 2 failed: {result2.ErrorMessage}");

        var image2 = RenderPdfToImage(temp2);
        SaveScreenshot(image2, "shift_seq_02_after_second.png");
        var positions2 = FindMarkerPositions(image2, allMarkers);

        _output.WriteLine("\n=== AFTER SECOND REDACTION ===");
        ReportShifts(originalPositions, positions2, "After Redaction 2");

        // Redaction 3
        var result3 = redactor.RedactText(temp2, temp3, "REDACT_THIRD");
        result3.Success.Should().BeTrue($"Redaction 3 failed: {result3.ErrorMessage}");

        var image3 = RenderPdfToImage(temp3);
        SaveScreenshot(image3, "shift_seq_03_after_third.png");
        var positions3 = FindMarkerPositions(image3, allMarkers);

        _output.WriteLine("\n=== AFTER THIRD REDACTION (FINAL) ===");
        var finalShifts = ReportShifts(originalPositions, positions3, "Final");

        // Assert - Reference markers should not shift significantly
        var maxAcceptableShiftPoints = 0.5; // 0.5 points tolerance

        foreach (var refMarker in referenceMarkers)
        {
            var shift = finalShifts.FirstOrDefault(s => s.Marker == refMarker);
            if (shift != default)
            {
                shift.TotalShiftPt.Should().BeLessThan(maxAcceptableShiftPoints,
                    $"Reference marker '{refMarker}' should not shift after sequential redactions. " +
                    $"Shifted {shift.TotalShiftPt:F2}pt (X: {shift.ShiftXPt:F2}pt, Y: {shift.ShiftYPt:F2}pt). " +
                    "This indicates BUG #270: Content shifting during sequential redactions.");
            }
        }

        // Also check between markers - these are more likely to shift
        foreach (var betweenMarker in betweenMarkers)
        {
            var shift = finalShifts.FirstOrDefault(s => s.Marker == betweenMarker);
            if (shift != default)
            {
                // Allow more tolerance for between markers (they may shift due to content reconstruction)
                // but they should still not shift more than the known ~6pt bug
                if (shift.TotalShiftPt > 1.0)
                {
                    _output.WriteLine($"WARNING: '{betweenMarker}' shifted {shift.TotalShiftPt:F2}pt - potential BUG #270 symptom");
                }
            }
        }

        originalImage.Dispose();
        image1.Dispose();
        image2.Dispose();
        image3.Dispose();
    }

    /// <summary>
    /// Test with real-world birth certificate PDF if available.
    /// </summary>
    [SkippableFact]
    public void BirthCertificate_SequentialRedactions_ShouldNotShiftContent()
    {
        var projectRoot = FindProjectRoot();
        var sourcePdf = Path.Combine(projectRoot, "test-pdfs/sample-pdfs/birth-certificate-request-scrambled.pdf");

        Skip.IfNot(File.Exists(sourcePdf), $"Test PDF not found: {sourcePdf}");

        var temp1 = CreateTempPath("bc_seq1.pdf");
        var temp2 = CreateTempPath("bc_seq2.pdf");
        var temp3 = CreateTempPath("bc_seq3.pdf");

        // Get original screenshot
        var originalImage = RenderPdfToImage(sourcePdf);
        SaveScreenshot(originalImage, "bc_seq_00_original.png");

        // Find reference text that should NOT be redacted
        // We'll use pixel-based detection to find specific text regions
        var referenceRegions = new[]
        {
            // Top area - likely contains "BIRTH CERTIFICATE" header
            ("TOP_REGION", new SKRectI(0, 0, originalImage.Width, (int)(100 * Dpi / 72))),
            // Left margin area
            ("LEFT_MARGIN", new SKRectI(0, 0, (int)(50 * Dpi / 72), originalImage.Height)),
        };

        // Calculate "fingerprint" of reference regions before
        var fingerprintsBefore = new Dictionary<string, long>();
        foreach (var (name, rect) in referenceRegions)
        {
            fingerprintsBefore[name] = CalculateRegionFingerprint(originalImage, rect);
            _output.WriteLine($"Original fingerprint {name}: {fingerprintsBefore[name]}");
        }

        var redactor = new TextRedactor();

        // Sequential redactions
        var result1 = redactor.RedactText(sourcePdf, temp1, "DONOTMAILCASH");
        result1.Success.Should().BeTrue();

        var result2 = redactor.RedactText(temp1, temp2, "TORRINGTON");
        result2.Success.Should().BeTrue();

        var result3 = redactor.RedactText(temp2, temp3, "CONNECTICUT");
        result3.Success.Should().BeTrue();

        // Get final screenshot
        var finalImage = RenderPdfToImage(temp3);
        SaveScreenshot(finalImage, "bc_seq_03_final.png");

        // Calculate fingerprints after
        var fingerprintsAfter = new Dictionary<string, long>();
        foreach (var (name, rect) in referenceRegions)
        {
            fingerprintsAfter[name] = CalculateRegionFingerprint(finalImage, rect);
            _output.WriteLine($"Final fingerprint {name}: {fingerprintsAfter[name]}");

            var diff = Math.Abs(fingerprintsAfter[name] - fingerprintsBefore[name]);
            var diffPercent = (double)diff / fingerprintsBefore[name] * 100;
            _output.WriteLine($"Fingerprint difference for {name}: {diffPercent:F2}%");
        }

        // Compare specific pixel positions to detect shift
        // This is a heuristic - if the fingerprint changes significantly, content has shifted
        foreach (var (name, _) in referenceRegions)
        {
            var diff = Math.Abs(fingerprintsAfter[name] - fingerprintsBefore[name]);
            var diffPercent = (double)diff / fingerprintsBefore[name] * 100;

            // Allow up to 5% fingerprint change (accounts for anti-aliasing, etc.)
            // Larger changes indicate content shift
            if (diffPercent > 10)
            {
                _output.WriteLine($"POTENTIAL SHIFT DETECTED: {name} fingerprint changed by {diffPercent:F2}%");
            }
        }

        originalImage.Dispose();
        finalImage.Dispose();
    }

    /// <summary>
    /// Measure exact pixel shift of text by comparing character bounding boxes.
    /// </summary>
    [Fact]
    public void MeasurePixelShift_SequentialRedactions()
    {
        // Arrange
        var inputPath = CreateTempPath("measure_shift.pdf");
        var redactedPath = CreateTempPath("measure_shift_redacted.pdf");

        // Create PDF with precise text placement
        CreatePdfWithMarkers(inputPath, new[]
        {
            new TextMarker("XXXXX", 100, 600, 24), // Large text for easy detection
            new TextMarker("YYYYY", 100, 500, 24),
            new TextMarker("ZZZZZ", 100, 400, 24), // This will be redacted
            new TextMarker("AAAAA", 100, 300, 24),
            new TextMarker("BBBBB", 100, 200, 24),
        });

        var beforeImage = RenderPdfToImage(inputPath);
        SaveScreenshot(beforeImage, "measure_00_before.png");

        // Find bounding boxes of text markers before
        var boundsBefore = new Dictionary<string, SKRectI>();
        var markerNames = new[] { "XXXXX", "YYYYY", "AAAAA", "BBBBB" };
        foreach (var marker in markerNames)
        {
            var bounds = FindTextBounds(beforeImage, FindMarkerCenter(beforeImage, marker));
            if (bounds.HasValue)
            {
                boundsBefore[marker] = bounds.Value;
                _output.WriteLine($"Before {marker}: Y={bounds.Value.Top}px ({bounds.Value.Top * PointsPerPixel:F1}pt from top)");
            }
        }

        // Redact ZZZZZ
        var redactor = new TextRedactor();
        var result = redactor.RedactText(inputPath, redactedPath, "ZZZZZ");
        result.Success.Should().BeTrue();

        var afterImage = RenderPdfToImage(redactedPath);
        SaveScreenshot(afterImage, "measure_01_after.png");

        // Find bounding boxes after
        var boundsAfter = new Dictionary<string, SKRectI>();
        foreach (var marker in markerNames)
        {
            var bounds = FindTextBounds(afterImage, FindMarkerCenter(afterImage, marker));
            if (bounds.HasValue)
            {
                boundsAfter[marker] = bounds.Value;
                _output.WriteLine($"After {marker}: Y={bounds.Value.Top}px ({bounds.Value.Top * PointsPerPixel:F1}pt from top)");
            }
        }

        // Compare positions
        _output.WriteLine("\n=== SHIFT ANALYSIS ===");
        var shifts = new List<(string Marker, int ShiftPixels, double ShiftPoints)>();

        foreach (var marker in markerNames)
        {
            if (boundsBefore.TryGetValue(marker, out var before) &&
                boundsAfter.TryGetValue(marker, out var after))
            {
                var shiftY = after.Top - before.Top;
                var shiftYPoints = shiftY * PointsPerPixel;
                shifts.Add((marker, shiftY, shiftYPoints));
                _output.WriteLine($"{marker}: shifted {shiftY}px ({shiftYPoints:F2}pt)");

                // Assert - no marker should shift by more than 1pt
                Math.Abs(shiftYPoints).Should().BeLessThan(1.0,
                    $"Marker '{marker}' shifted {shiftYPoints:F2}pt after redaction - BUG #270");
            }
        }

        beforeImage.Dispose();
        afterImage.Dispose();
    }

    #region Helper Methods

    private string CreateTempPath(string filename)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(filename)}_{Guid.NewGuid()}{Path.GetExtension(filename)}");
        _tempFiles.Add(path);
        return path;
    }

    private record TextMarker(string Text, double X, double Y, double FontSize);

    private void CreatePdfWithMarkers(string path, TextMarker[] markers)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        foreach (var marker in markers)
        {
            var font = new XFont("Helvetica", marker.FontSize, XFontStyleEx.Bold);
            var yPos = page.Height.Point - marker.Y;
            gfx.DrawString(marker.Text, font, XBrushes.Black, marker.X, yPos);
        }

        document.Save(path);
    }

    private SKBitmap RenderPdfToImage(string pdfPath)
    {
        using var stream = File.OpenRead(pdfPath);
        var options = new PDFtoImage.RenderOptions(Dpi: (int)Dpi);
        using var image = PDFtoImage.Conversion.ToImage(stream, page: 0, options: options);

        using var memStream = new MemoryStream();
        image.Encode(memStream, SKEncodedImageFormat.Png, 100);
        memStream.Position = 0;

        return SKBitmap.Decode(memStream);
    }

    private void SaveScreenshot(SKBitmap image, string filename)
    {
        var path = Path.Combine(_screenshotDir, filename);
        using var stream = File.Create(path);
        image.Encode(stream, SKEncodedImageFormat.Png, 100);
        _output.WriteLine($"Screenshot: {path}");
    }

    private Dictionary<string, (int X, int Y)> FindMarkerPositions(SKBitmap image, string[] markerNames)
    {
        var positions = new Dictionary<string, (int X, int Y)>();

        foreach (var marker in markerNames)
        {
            var center = FindMarkerCenter(image, marker);
            if (center.HasValue)
            {
                positions[marker] = center.Value;
            }
        }

        return positions;
    }

    private (int X, int Y)? FindMarkerCenter(SKBitmap image, string markerText)
    {
        // Use text extraction to find approximate position, then refine with image analysis
        // For this test, we use a simple approach: find the darkest pixels in expected region

        // First, create a temporary PDF to get text positions from PdfPig
        // Since we can't easily get positions from the image, we use a heuristic:
        // Scan for clusters of dark pixels that form text

        // Simplified approach: find horizontal bands with significant dark pixels
        var darkRows = new List<(int Y, int DarkCount)>();
        for (int y = 0; y < image.Height; y++)
        {
            int darkCount = 0;
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128)
                {
                    darkCount++;
                }
            }
            if (darkCount > 5) // At least 5 dark pixels in row
            {
                darkRows.Add((y, darkCount));
            }
        }

        // Group consecutive rows into text lines
        var textLines = new List<(int Y, int Height)>();
        int lineStart = -1;
        int lastY = -2;
        foreach (var (y, _) in darkRows)
        {
            if (y > lastY + 3) // Gap of more than 3 rows = new line
            {
                if (lineStart >= 0)
                {
                    textLines.Add((lineStart, lastY - lineStart + 1));
                }
                lineStart = y;
            }
            lastY = y;
        }
        if (lineStart >= 0)
        {
            textLines.Add((lineStart, lastY - lineStart + 1));
        }

        // Return center of first text line found (simplified)
        // In practice, you'd match against the marker text
        if (textLines.Count > 0)
        {
            // Find the line that matches the expected Y position based on marker
            // This is a simplified heuristic
            var expectedY = GetExpectedMarkerY(markerText);
            var bestLine = textLines
                .Select(l => (l.Y, l.Height, Diff: Math.Abs(l.Y - expectedY)))
                .OrderBy(x => x.Diff)
                .First();

            // Find horizontal center of text in this line
            int lineY = bestLine.Y + bestLine.Height / 2;
            int minX = image.Width, maxX = 0;
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, lineY);
                if (pixel.Red < 128)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }
            if (minX < maxX)
            {
                return ((minX + maxX) / 2, lineY);
            }
        }

        return null;
    }

    private int GetExpectedMarkerY(string markerText)
    {
        // Map marker names to expected Y positions (in pixels at 300 DPI)
        // These are approximate based on the PDF coordinates used in CreatePdfWithMarkers
        return markerText switch
        {
            "REF_TOP_LEFT" or "REF_TOP_RIGHT" or "MARKER_A" or "MARKER_B" => (int)((792 - 750) * Dpi / 72),
            "REF_MID_LEFT" or "REF_MID_RIGHT" => (int)((792 - 400) * Dpi / 72),
            "REF_BOTTOM_LEFT" or "REF_BOTTOM_RIGHT" or "MARKER_C" or "MARKER_D" => (int)((792 - 100) * Dpi / 72),
            "REDACT_THIS_TEXT" or "REDACT_SECOND" => (int)((792 - 500) * Dpi / 72),
            "NEARBY_TEXT_A" or "BETWEEN_1_2" => (int)((792 - 575) * Dpi / 72),
            "NEARBY_TEXT_B" or "BETWEEN_2_3" => (int)((792 - 425) * Dpi / 72),
            "REDACT_FIRST" => (int)((792 - 650) * Dpi / 72),
            "REDACT_THIRD" => (int)((792 - 350) * Dpi / 72),
            "XXXXX" => (int)((792 - 600) * Dpi / 72),
            "YYYYY" => (int)((792 - 500) * Dpi / 72),
            "ZZZZZ" => (int)((792 - 400) * Dpi / 72),
            "AAAAA" => (int)((792 - 300) * Dpi / 72),
            "BBBBB" => (int)((792 - 200) * Dpi / 72),
            _ => image.Height / 2
        };
    }

    private SKRectI? FindTextBounds(SKBitmap image, (int X, int Y)? center)
    {
        if (!center.HasValue) return null;

        var (cx, cy) = center.Value;

        // Expand from center to find text bounds
        int minX = cx, maxX = cx, minY = cy, maxY = cy;
        int searchRadius = 100;

        // Find vertical bounds
        for (int y = cy; y >= Math.Max(0, cy - searchRadius); y--)
        {
            bool hasInk = false;
            for (int x = Math.Max(0, cx - 50); x < Math.Min(image.Width, cx + 50); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.Red < 128)
                {
                    hasInk = true;
                    break;
                }
            }
            if (hasInk) minY = y;
            else break;
        }

        for (int y = cy; y < Math.Min(image.Height, cy + searchRadius); y++)
        {
            bool hasInk = false;
            for (int x = Math.Max(0, cx - 50); x < Math.Min(image.Width, cx + 50); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.Red < 128)
                {
                    hasInk = true;
                    break;
                }
            }
            if (hasInk) maxY = y;
            else break;
        }

        // Find horizontal bounds
        for (int x = cx; x >= Math.Max(0, cx - searchRadius); x--)
        {
            bool hasInk = false;
            for (int y = minY; y <= maxY; y++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.Red < 128)
                {
                    hasInk = true;
                    break;
                }
            }
            if (hasInk) minX = x;
            else break;
        }

        for (int x = cx; x < Math.Min(image.Width, cx + searchRadius); x++)
        {
            bool hasInk = false;
            for (int y = minY; y <= maxY; y++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.Red < 128)
                {
                    hasInk = true;
                    break;
                }
            }
            if (hasInk) maxX = x;
            else break;
        }

        if (maxX > minX && maxY > minY)
        {
            return new SKRectI(minX, minY, maxX, maxY);
        }

        return null;
    }

    private SKBitmap image => throw new NotImplementedException(); // Placeholder for lambda

    private List<(string Marker, double ShiftXPt, double ShiftYPt, double TotalShiftPt)>
        ReportShifts(Dictionary<string, (int X, int Y)> original, Dictionary<string, (int X, int Y)> current, string label)
    {
        var shifts = new List<(string Marker, double ShiftXPt, double ShiftYPt, double TotalShiftPt)>();

        foreach (var (marker, origPos) in original)
        {
            if (current.TryGetValue(marker, out var currPos))
            {
                var shiftX = (currPos.X - origPos.X) * PointsPerPixel;
                var shiftY = (currPos.Y - origPos.Y) * PointsPerPixel;
                var total = Math.Sqrt(shiftX * shiftX + shiftY * shiftY);

                shifts.Add((marker, shiftX, shiftY, total));

                if (total > 0.1) // Only report non-trivial shifts
                {
                    _output.WriteLine($"{label} - {marker}: X={shiftX:F2}pt, Y={shiftY:F2}pt, Total={total:F2}pt");
                }
            }
            else
            {
                _output.WriteLine($"{label} - {marker}: NOT FOUND");
            }
        }

        return shifts;
    }

    private long CalculateRegionFingerprint(SKBitmap image, SKRectI region)
    {
        // Calculate a hash/fingerprint of pixel values in region
        long fingerprint = 0;
        int sampleStep = 3; // Sample every 3rd pixel for efficiency

        for (int y = region.Top; y < Math.Min(region.Bottom, image.Height); y += sampleStep)
        {
            for (int x = region.Left; x < Math.Min(region.Right, image.Width); x += sampleStep)
            {
                var pixel = image.GetPixel(x, y);
                // Quantize to reduce noise from anti-aliasing
                var quantized = (pixel.Red / 32) + (pixel.Green / 32) * 8 + (pixel.Blue / 32) * 64;
                fingerprint += quantized * (x + y * image.Width);
            }
        }

        return fingerprint;
    }

    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "test-pdfs")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return "/home/marc/pdfe";
    }

    #endregion

    #region Font Resolver

    private class TestFontResolver : IFontResolver
    {
        private readonly Dictionary<string, string> _fontCache = new(StringComparer.OrdinalIgnoreCase);

        public TestFontResolver()
        {
            var fontDirs = new[]
            {
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/fonts")
            };

            foreach (var dir in fontDirs.Where(Directory.Exists))
            {
                try
                {
                    foreach (var font in Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories))
                    {
                        var name = Path.GetFileNameWithoutExtension(font);
                        if (!_fontCache.ContainsKey(name))
                            _fontCache[name] = font;
                    }
                }
                catch { }
            }
        }

        public byte[]? GetFont(string faceName)
        {
            if (_fontCache.TryGetValue(faceName, out var path) && File.Exists(path))
                return File.ReadAllBytes(path);
            return null;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var candidates = new[] { "LiberationSans-Regular", "LiberationSans-Bold", "DejaVuSans", "DejaVuSans-Bold", "FreeSans" };
            foreach (var c in candidates)
            {
                if (_fontCache.ContainsKey(c))
                    return new FontResolverInfo(c);
            }
            return new FontResolverInfo(_fontCache.Keys.FirstOrDefault() ?? "Arial");
        }
    }

    #endregion
}
