using PdfSharp.Pdf;
using PdfSharp.Drawing;
using System.IO;

namespace PdfEditor.Tests.Utilities;

/// <summary>
/// Generates simple test PDFs with known content for testing
/// </summary>
public static class TestPdfGenerator
{
    /// <summary>
    /// Creates a simple single-page PDF with text at known positions
    /// </summary>
    public static string CreateSimpleTextPdf(string outputPath, string text = "Test Content")
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Draw text at known position (100, 100)
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 100));
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a simple multi-page PDF with text at known positions
    /// Overload with pageCount parameter for backwards compatibility
    /// </summary>
    public static string CreateSimpleTextPdf(string outputPath, int pageCount)
    {
        return CreateMultiPagePdf(outputPath, pageCount);
    }

    /// <summary>
    /// Creates a PDF with ONLY text, with custom content
    /// Overload with text parameter for backwards compatibility
    /// </summary>
    public static string CreateTextOnlyPdf(string outputPath, string text)
    {
        return CreateSimpleTextPdf(outputPath, text);
    }

    /// <summary>
    /// Creates a PDF with multiple lines of text
    /// Overload with string array for backwards compatibility
    /// </summary>
    public static string CreateTextOnlyPdf(string outputPath, string[] lines)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        double y = 50;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(100, y));
            y += 30;
        }

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with multiple text blocks at known positions
    /// </summary>
    public static string CreateMultiTextPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Text blocks at different positions
        gfx.DrawString("CONFIDENTIAL", font, XBrushes.Black, new XPoint(100, 100));
        gfx.DrawString("Public Information", font, XBrushes.Black, new XPoint(100, 200));
        gfx.DrawString("Secret Data", font, XBrushes.Black, new XPoint(100, 300));
        gfx.DrawString("Normal Text", font, XBrushes.Black, new XPoint(100, 400));
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with text and graphics (rectangles)
    /// </summary>
    public static string CreateTextWithGraphicsPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Draw rectangles
        gfx.DrawRectangle(XBrushes.LightBlue, new XRect(50, 50, 200, 50));
        gfx.DrawRectangle(XBrushes.LightGreen, new XRect(50, 150, 200, 50));
        
        // Draw text over rectangles
        gfx.DrawString("Text in Blue Box", font, XBrushes.Black, new XPoint(60, 75));
        gfx.DrawString("Text in Green Box", font, XBrushes.Black, new XPoint(60, 175));
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with transformed (rotated/scaled) text
    /// </summary>
    public static string CreateTransformedTextPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);
        
        // Normal text
        gfx.DrawString("Normal Text", font, XBrushes.Black, new XPoint(100, 100));
        
        // Rotated text
        gfx.Save();
        gfx.RotateAtTransform(45, new XPoint(200, 200));
        gfx.DrawString("Rotated Text", font, XBrushes.Black, new XPoint(200, 200));
        gfx.Restore();
        
        // Scaled text
        gfx.Save();
        gfx.ScaleTransform(1.5, 1.5);
        gfx.DrawString("Scaled Text", font, XBrushes.Black, new XPoint(100, 200));
        gfx.Restore();
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a multi-page PDF
    /// </summary>
    public static string CreateMultiPagePdf(string outputPath, int pageCount = 3)
    {
        var document = new PdfDocument();
        
        for (int i = 0; i < pageCount; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            
            gfx.DrawString($"Page {i + 1} Content", font, XBrushes.Black, new XPoint(100, 100));
            gfx.DrawString($"Secret on Page {i + 1}", font, XBrushes.Black, new XPoint(100, 200));
        }
        
        document.Save(outputPath);
        document.Dispose();
        
        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with content at known grid positions for precise testing
    /// </summary>
    public static string CreateGridContentPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);
        var smallFont = new XFont("Arial", 8);

        // Create a grid of text elements at known positions
        // Format: "Cell(X,Y)" at position (X, Y)
        for (int x = 100; x <= 500; x += 100)
        {
            for (int y = 100; y <= 700; y += 100)
            {
                gfx.DrawString($"Cell({x},{y})", font, XBrushes.Black, new XPoint(x, y));
            }
        }

        // Add some graphics elements (rectangles) at known positions
        gfx.DrawRectangle(XPens.Blue, XBrushes.LightBlue, new XRect(150, 150, 80, 40));
        gfx.DrawRectangle(XPens.Green, XBrushes.LightGreen, new XRect(350, 350, 80, 40));
        gfx.DrawRectangle(XPens.Red, XBrushes.LightPink, new XRect(250, 550, 80, 40));

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with specific content items at known exact positions
    /// Returns a dictionary mapping content to positions for verification
    /// </summary>
    public static (string path, Dictionary<string, (double x, double y, double width, double height)> contentMap)
        CreateMappedContentPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        var contentMap = new Dictionary<string, (double, double, double, double)>();

        // Text item 1: "CONFIDENTIAL" at top
        var text1 = "CONFIDENTIAL";
        contentMap[text1] = (100, 100, 120, 20);
        gfx.DrawString(text1, font, XBrushes.Black, new XPoint(100, 100));

        // Text item 2: "PUBLIC" at middle left
        var text2 = "PUBLIC";
        contentMap[text2] = (100, 300, 80, 20);
        gfx.DrawString(text2, font, XBrushes.Black, new XPoint(100, 300));

        // Text item 3: "SECRET" at middle right
        var text3 = "SECRET";
        contentMap[text3] = (400, 300, 80, 20);
        gfx.DrawString(text3, font, XBrushes.Black, new XPoint(400, 300));

        // Text item 4: "PRIVATE" at bottom
        var text4 = "PRIVATE";
        contentMap[text4] = (100, 500, 80, 20);
        gfx.DrawString(text4, font, XBrushes.Black, new XPoint(100, 500));

        // Text item 5: "INTERNAL" at bottom right
        var text5 = "INTERNAL";
        contentMap[text5] = (400, 500, 90, 20);
        gfx.DrawString(text5, font, XBrushes.Black, new XPoint(400, 500));

        // Graphics: Blue rectangle
        contentMap["BLUE_BOX"] = (50, 150, 100, 50);
        gfx.DrawRectangle(XBrushes.LightBlue, new XRect(50, 150, 100, 50));

        // Graphics: Green rectangle
        contentMap["GREEN_BOX"] = (350, 350, 100, 50);
        gfx.DrawRectangle(XBrushes.LightGreen, new XRect(350, 350, 100, 50));

        document.Save(outputPath);
        document.Dispose();

        return (outputPath, contentMap);
    }

    /// <summary>
    /// Creates a complex PDF with mixed content types
    /// </summary>
    public static string CreateComplexContentPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var normalFont = new XFont("Arial", 12);
        var smallFont = new XFont("Arial", 8);

        // Title
        gfx.DrawString("Confidential Document", titleFont, XBrushes.Black, new XPoint(50, 50));

        // Header section with background
        gfx.DrawRectangle(XBrushes.LightGray, new XRect(50, 70, 500, 40));
        gfx.DrawString("Classification: TOP SECRET", normalFont, XBrushes.Red, new XPoint(60, 95));

        // Body text
        gfx.DrawString("Subject: Redaction Testing", normalFont, XBrushes.Black, new XPoint(50, 150));
        gfx.DrawString("This document contains sensitive information.", normalFont, XBrushes.Black, new XPoint(50, 180));
        gfx.DrawString("Some parts must be redacted before release.", normalFont, XBrushes.Black, new XPoint(50, 210));

        // Sensitive data section
        gfx.DrawRectangle(XPens.Red, new XRect(50, 240, 500, 120));
        gfx.DrawString("SENSITIVE DATA:", normalFont, XBrushes.Red, new XPoint(60, 265));
        gfx.DrawString("Account: 1234-5678-9012-3456", normalFont, XBrushes.Black, new XPoint(60, 295));
        gfx.DrawString("SSN: 123-45-6789", normalFont, XBrushes.Black, new XPoint(60, 325));
        gfx.DrawString("Password: SuperSecret123!", normalFont, XBrushes.Black, new XPoint(60, 355));

        // Public information section
        gfx.DrawRectangle(XPens.Green, new XRect(50, 380, 500, 80));
        gfx.DrawString("PUBLIC INFORMATION:", normalFont, XBrushes.Green, new XPoint(60, 405));
        gfx.DrawString("Company: ACME Corporation", normalFont, XBrushes.Black, new XPoint(60, 435));

        // Footer
        gfx.DrawString("Page 1 of 1", smallFont, XBrushes.Gray, new XPoint(50, 750));
        gfx.DrawString("Document ID: DOC-2025-001", smallFont, XBrushes.Gray, new XPoint(400, 750));

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with ONLY text (no shapes or graphics)
    /// </summary>
    public static string CreateTextOnlyPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 12);

        // Multiple text items at various positions - NO SHAPES
        gfx.DrawString("Header Text", font, XBrushes.Black, new XPoint(100, 50));
        gfx.DrawString("CONFIDENTIAL SECTION", font, XBrushes.Red, new XPoint(100, 100));
        gfx.DrawString("This is line 1 of confidential data", font, XBrushes.Black, new XPoint(100, 130));
        gfx.DrawString("This is line 2 of confidential data", font, XBrushes.Black, new XPoint(100, 160));
        gfx.DrawString("PUBLIC SECTION", font, XBrushes.Green, new XPoint(100, 250));
        gfx.DrawString("This is public information line 1", font, XBrushes.Black, new XPoint(100, 280));
        gfx.DrawString("This is public information line 2", font, XBrushes.Black, new XPoint(100, 310));
        gfx.DrawString("ANOTHER CONFIDENTIAL BLOCK", font, XBrushes.Red, new XPoint(100, 400));
        gfx.DrawString("Secret data here", font, XBrushes.Black, new XPoint(100, 430));
        gfx.DrawString("More secret data", font, XBrushes.Black, new XPoint(100, 460));
        gfx.DrawString("Footer - Public", font, XBrushes.Black, new XPoint(100, 700));

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with ONLY shapes/graphics (no text at all)
    /// </summary>
    public static string CreateShapesOnlyPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);

        // NO TEXT - Only shapes and graphics

        // Large blue rectangle
        gfx.DrawRectangle(XPens.Blue, XBrushes.LightBlue, new XRect(50, 50, 200, 100));

        // Green circle (using ellipse)
        gfx.DrawEllipse(XPens.Green, XBrushes.LightGreen, new XRect(300, 50, 150, 150));

        // Red rectangle that will be partially covered
        gfx.DrawRectangle(XPens.Red, XBrushes.LightPink, new XRect(100, 250, 300, 100));

        // Yellow rectangle
        gfx.DrawRectangle(XPens.Yellow, XBrushes.LightYellow, new XRect(50, 400, 150, 80));

        // Purple rectangle
        gfx.DrawRectangle(XPens.Purple, XBrushes.Lavender, new XRect(350, 400, 150, 80));

        // Orange triangle (using path)
        gfx.DrawPolygon(XPens.Orange, XBrushes.Orange,
            new XPoint[] {
                new XPoint(100, 600),
                new XPoint(200, 700),
                new XPoint(0, 700)
            },
            XFillMode.Winding);

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with layered/overlapping shapes
    /// Multiple shapes drawn on top of each other
    /// </summary>
    public static string CreateLayeredShapesPdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);

        // Layer 1: Large background rectangle
        gfx.DrawRectangle(XBrushes.LightGray, new XRect(100, 100, 400, 300));

        // Layer 2: Blue rectangle on top
        gfx.DrawRectangle(XPens.Blue, XBrushes.LightBlue, new XRect(150, 150, 200, 100));

        // Layer 3: Green rectangle overlapping blue
        gfx.DrawRectangle(XPens.Green, XBrushes.LightGreen, new XRect(200, 200, 200, 100));

        // Layer 4: Red circle overlapping both
        gfx.DrawEllipse(XPens.Red, XBrushes.LightPink, new XRect(250, 180, 120, 120));

        // Add text labels to identify layers
        gfx.DrawString("Layer 1 (gray)", font, XBrushes.Black, new XPoint(110, 120));
        gfx.DrawString("Layer 2 (blue)", font, XBrushes.DarkBlue, new XPoint(160, 170));
        gfx.DrawString("Layer 3 (green)", font, XBrushes.DarkGreen, new XPoint(210, 220));
        gfx.DrawString("Layer 4 (red)", font, XBrushes.DarkRed, new XPoint(270, 240));

        // Additional separate shapes not in the layered area
        gfx.DrawRectangle(XPens.Purple, XBrushes.Lavender, new XRect(100, 500, 150, 100));
        gfx.DrawString("Separate shape", font, XBrushes.Black, new XPoint(110, 520));

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Creates a PDF with shapes that will be partially covered by redaction
    /// </summary>
    public static string CreatePartialCoveragePdf(string outputPath)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(600);
        page.Height = XUnit.FromPoint(800);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 10);

        // Large rectangle that will be partially redacted
        gfx.DrawRectangle(XPens.Blue, XBrushes.LightBlue, new XRect(50, 100, 400, 150));
        gfx.DrawString("This large blue rectangle", font, XBrushes.DarkBlue, new XPoint(60, 120));
        gfx.DrawString("will be partially covered", font, XBrushes.DarkBlue, new XPoint(60, 140));
        gfx.DrawString("by a black box", font, XBrushes.DarkBlue, new XPoint(60, 160));

        // Circle that will be partially redacted
        gfx.DrawEllipse(XPens.Green, XBrushes.LightGreen, new XRect(100, 350, 200, 200));
        gfx.DrawString("Partial circle", font, XBrushes.DarkGreen, new XPoint(150, 450));

        // Text that will be partially redacted
        gfx.DrawString("This text spans a long area and will be partially redacted in the middle portion only",
            font, XBrushes.Black, new XPoint(50, 650));

        document.Save(outputPath);
        document.Dispose();

        return outputPath;
    }

    /// <summary>
    /// Cleans up test PDF file
    /// </summary>
    public static void CleanupTestFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
