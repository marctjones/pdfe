using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfEditor.Redaction.Tests.Utilities;

/// <summary>
/// Generates test PDFs with known content for verifying redaction.
/// </summary>
public static class TestPdfGenerator
{
    /// <summary>
    /// Create a simple PDF with a single line of text using Tj operator.
    /// </summary>
    public static void CreateSimpleTextPdf(string outputPath, string text)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        // Draw at known position (100, 700 in PDF coords = 100, 92 in graphics coords)
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 92));

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with multiple lines of text.
    /// </summary>
    public static void CreateMultiLineTextPdf(string outputPath, params string[] lines)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        double y = 92;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(100, y));
            y += 20;
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with text at specific positions.
    /// </summary>
    public static void CreateTextAtPositions(string outputPath, params (string text, double x, double y)[] items)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        foreach (var (text, x, y) in items)
        {
            // Convert PDF Y to graphics Y (PDF is bottom-left, graphics is top-left)
            var graphicsY = 792 - y;
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, graphicsY));
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with sensitive data patterns (for redaction testing).
    /// </summary>
    public static void CreateSensitiveDataPdf(string outputPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        var lines = new[]
        {
            "Name: John Doe",
            "SSN: 123-45-6789",
            "Date of Birth: 01/15/1990",
            "Address: 123 Main Street",
            "Phone: (555) 123-4567",
            "Email: john.doe@example.com"
        };

        double y = 92;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(100, y));
            y += 20;
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF that uses TJ array with kerning (more complex text storage).
    /// This requires manually constructing PDF content, which PDFsharp may not produce directly.
    /// For now, this creates a simple PDF - TJ testing requires sample PDFs.
    /// </summary>
    public static void CreateKernedTextPdf(string outputPath, string text)
    {
        // PDFsharp typically generates Tj operators
        // For TJ testing, we'll need actual PDF samples
        CreateSimpleTextPdf(outputPath, text);
    }

    /// <summary>
    /// Create a PDF with multiple pages, each containing text.
    /// </summary>
    public static void CreateMultiPagePdf(string outputPath, string[] pageTexts)
    {
        using var document = new PdfDocument();

        foreach (var text in pageTexts)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(612);  // US Letter
            page.Height = XUnit.FromPoint(792);

            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Helvetica", 12);

            // Draw at known position
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 92));
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create an empty PDF with one blank page (no content).
    /// </summary>
    public static void CreateEmptyPdf(string outputPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        // Don't draw anything - just save empty page
        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with custom page dimensions (for testing unusual page sizes).
    /// </summary>
    /// <param name="outputPath">Output file path</param>
    /// <param name="widthPoints">Width in PDF points (72 DPI)</param>
    /// <param name="heightPoints">Height in PDF points (72 DPI)</param>
    /// <param name="text">Optional text to include on the page</param>
    public static void CreateCustomSizePdf(string outputPath, double widthPoints, double heightPoints, string text = "Test Document")
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(widthPoints);
        page.Height = XUnit.FromPoint(heightPoints);

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        // Draw text at center of page
        var textSize = gfx.MeasureString(text, font);
        var x = (widthPoints - textSize.Width) / 2;
        var y = (heightPoints - textSize.Height) / 2;

        // Convert PDF Y to graphics Y
        var graphicsY = heightPoints - y;
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, graphicsY));

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a rotated page.
    /// The text is drawn normally but the page has a /Rotate entry.
    /// </summary>
    /// <param name="outputPath">Output file path</param>
    /// <param name="text">Text to include on the page</param>
    /// <param name="rotationDegrees">Rotation in degrees (0, 90, 180, 270)</param>
    public static void CreateRotatedPdf(string outputPath, string text, int rotationDegrees)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        // Set the page rotation
        page.Rotate = rotationDegrees;

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        // Draw text at a known position in page content stream coordinates
        // The position is in the unrotated coordinate system
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(100, 92));

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with multiple lines of text on a rotated page.
    /// </summary>
    /// <param name="outputPath">Output file path</param>
    /// <param name="rotationDegrees">Rotation in degrees (0, 90, 180, 270)</param>
    /// <param name="lines">Lines of text to include</param>
    public static void CreateRotatedMultiLinePdf(string outputPath, int rotationDegrees, params string[] lines)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        // Set the page rotation
        page.Rotate = rotationDegrees;

        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Helvetica", 12);

        double y = 92;
        foreach (var line in lines)
        {
            gfx.DrawString(line, font, XBrushes.Black, new XPoint(100, y));
            y += 20;
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with filled rectangles at specified positions.
    /// Used for testing partial shape redaction (issue #197).
    /// </summary>
    /// <param name="outputPath">Output file path</param>
    /// <param name="rectangles">Array of (x, y, width, height, color) tuples in PDF coordinates</param>
    public static void CreateRectanglesPdf(string outputPath, params (double x, double y, double width, double height, XColor color)[] rectangles)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        foreach (var (x, y, width, height, color) in rectangles)
        {
            // Convert PDF Y to graphics Y (PDF is bottom-left, graphics is top-left)
            var graphicsY = 792 - y - height;
            gfx.DrawRectangle(new XSolidBrush(color), x, graphicsY, width, height);
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a single filled rectangle.
    /// Convenience method for testing partial shape redaction.
    /// </summary>
    public static void CreateSingleRectanglePdf(string outputPath, double x, double y, double width, double height)
    {
        CreateRectanglesPdf(outputPath, (x, y, width, height, XColors.Blue));
    }

    /// <summary>
    /// Create a PDF with text and shapes combined.
    /// Used for testing mixed content redaction.
    /// </summary>
    /// <param name="outputPath">Output file path</param>
    /// <param name="text">Text to include</param>
    /// <param name="textX">Text X position in PDF coords</param>
    /// <param name="textY">Text Y position in PDF coords</param>
    /// <param name="rectangles">Rectangles to draw</param>
    public static void CreateTextAndShapesPdf(string outputPath, string text, double textX, double textY,
        params (double x, double y, double width, double height, XColor color)[] rectangles)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        // Draw rectangles first (background)
        foreach (var (x, y, width, height, color) in rectangles)
        {
            var graphicsY = 792 - y - height;
            gfx.DrawRectangle(new XSolidBrush(color), x, graphicsY, width, height);
        }

        // Draw text on top
        var font = new XFont("Helvetica", 12);
        var graphicsTextY = 792 - textY;
        gfx.DrawString(text, font, XBrushes.Black, new XPoint(textX, graphicsTextY));

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a stroked (outlined) rectangle.
    /// Tests stroke vs fill path handling in redaction.
    /// </summary>
    public static void CreateStrokedRectanglePdf(string outputPath, double x, double y, double width, double height, double lineWidth = 2.0)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        var graphicsY = 792 - y - height;
        var pen = new XPen(XColors.Red, lineWidth);
        gfx.DrawRectangle(pen, x, graphicsY, width, height);

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with multiple non-overlapping rectangles for testing
    /// that partial redaction only affects shapes in the redaction area.
    /// </summary>
    public static void CreateGridOfRectanglesPdf(string outputPath, int rows, int cols, double size, double spacing)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        var startX = 50.0;
        var startY = 700.0;  // PDF coordinates (near top)

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                var x = startX + col * (size + spacing);
                var y = startY - row * (size + spacing);

                // Convert to graphics Y
                var graphicsY = 792 - y - size;

                // Alternate colors for visibility
                var color = (row + col) % 2 == 0 ? XColors.Blue : XColors.Green;
                gfx.DrawRectangle(new XSolidBrush(color), x, graphicsY, size, size);
            }
        }

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a filled triangle.
    /// Triangle is defined by 3 vertices in PDF coordinates.
    /// </summary>
    public static void CreateTrianglePdf(string outputPath,
        double x1, double y1, double x2, double y2, double x3, double y3,
        XColor color)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        // Convert PDF Y to graphics Y
        var gy1 = 792 - y1;
        var gy2 = 792 - y2;
        var gy3 = 792 - y3;

        var path = new XGraphicsPath();
        path.AddLine(x1, gy1, x2, gy2);
        path.AddLine(x2, gy2, x3, gy3);
        path.CloseFigure();

        gfx.DrawPath(new XSolidBrush(color), path);

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a filled circle.
    /// Circle is defined by center point and radius in PDF coordinates.
    /// </summary>
    public static void CreateCirclePdf(string outputPath,
        double centerX, double centerY, double radius,
        XColor color)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        // Convert PDF Y to graphics Y (center point)
        var graphicsY = 792 - centerY;

        gfx.DrawEllipse(new XSolidBrush(color),
            centerX - radius, graphicsY - radius,
            radius * 2, radius * 2);

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a filled ellipse.
    /// Ellipse is defined by center point and semi-axes in PDF coordinates.
    /// </summary>
    public static void CreateEllipsePdf(string outputPath,
        double centerX, double centerY, double radiusX, double radiusY,
        XColor color)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        // Convert PDF Y to graphics Y
        var graphicsY = 792 - centerY;

        gfx.DrawEllipse(new XSolidBrush(color),
            centerX - radiusX, graphicsY - radiusY,
            radiusX * 2, radiusY * 2);

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with an irregular polygon (arbitrary number of vertices).
    /// Points are in PDF coordinates.
    /// </summary>
    public static void CreatePolygonPdf(string outputPath,
        XColor color,
        params (double x, double y)[] points)
    {
        if (points.Length < 3)
            throw new ArgumentException("Polygon requires at least 3 points");

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        // Convert to XPoint array with Y coordinate conversion
        var xpoints = points.Select(p => new XPoint(p.x, 792 - p.y)).ToArray();

        var path = new XGraphicsPath();
        path.AddPolygon(xpoints);

        gfx.DrawPath(new XSolidBrush(color), path);

        document.Save(outputPath);
    }

    /// <summary>
    /// Create a PDF with a regular polygon (equal sides).
    /// </summary>
    public static void CreateRegularPolygonPdf(string outputPath,
        double centerX, double centerY, double radius, int sides,
        XColor color)
    {
        if (sides < 3)
            throw new ArgumentException("Polygon requires at least 3 sides");

        var points = new (double x, double y)[sides];
        for (int i = 0; i < sides; i++)
        {
            var angle = 2 * Math.PI * i / sides - Math.PI / 2; // Start from top
            points[i] = (
                centerX + radius * Math.Cos(angle),
                centerY + radius * Math.Sin(angle)
            );
        }

        CreatePolygonPdf(outputPath, color, points);
    }

    /// <summary>
    /// Create a PDF with a star shape.
    /// Tests complex polygons with concave regions.
    /// </summary>
    public static void CreateStarPdf(string outputPath,
        double centerX, double centerY, double outerRadius, double innerRadius, int points,
        XColor color)
    {
        if (points < 3)
            throw new ArgumentException("Star requires at least 3 points");

        var vertices = new (double x, double y)[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            var angle = Math.PI * i / points - Math.PI / 2;
            var radius = (i % 2 == 0) ? outerRadius : innerRadius;
            vertices[i] = (
                centerX + radius * Math.Cos(angle),
                centerY + radius * Math.Sin(angle)
            );
        }

        CreatePolygonPdf(outputPath, color, vertices);
    }

    /// <summary>
    /// Create a PDF with multiple shapes for comprehensive testing.
    /// Includes rectangle, triangle, circle, ellipse, pentagon, and star.
    /// </summary>
    public static void CreateAllShapesPdf(string outputPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);

        // Row 1: Rectangle and Triangle
        // Rectangle at (50, 650) - blue
        gfx.DrawRectangle(new XSolidBrush(XColors.Blue), 50, 792 - 650 - 80, 100, 80);

        // Triangle at (200, 570)-(250, 650)-(300, 570) - green
        var trianglePath = new XGraphicsPath();
        trianglePath.AddPolygon(new XPoint[]
        {
            new XPoint(200, 792 - 650),  // top
            new XPoint(150, 792 - 570),  // bottom-left
            new XPoint(250, 792 - 570)   // bottom-right
        });
        gfx.DrawPath(new XSolidBrush(XColors.Green), trianglePath);

        // Row 2: Circle and Ellipse
        // Circle at center (100, 480), radius 50 - red
        gfx.DrawEllipse(new XSolidBrush(XColors.Red), 50, 792 - 530, 100, 100);

        // Ellipse at center (250, 480), rx=60, ry=40 - orange
        gfx.DrawEllipse(new XSolidBrush(XColors.Orange), 190, 792 - 520, 120, 80);

        // Row 3: Pentagon and Hexagon
        // Pentagon at center (100, 330), radius 50 - purple
        var pentagonPoints = GenerateRegularPolygonPoints(100, 330, 50, 5);
        var pentagonPath = new XGraphicsPath();
        pentagonPath.AddPolygon(pentagonPoints.Select(p => new XPoint(p.x, 792 - p.y)).ToArray());
        gfx.DrawPath(new XSolidBrush(XColors.Purple), pentagonPath);

        // Hexagon at center (250, 330), radius 50 - cyan
        var hexagonPoints = GenerateRegularPolygonPoints(250, 330, 50, 6);
        var hexagonPath = new XGraphicsPath();
        hexagonPath.AddPolygon(hexagonPoints.Select(p => new XPoint(p.x, 792 - p.y)).ToArray());
        gfx.DrawPath(new XSolidBrush(XColors.Cyan), hexagonPath);

        // Row 4: Star and Irregular polygon
        // 5-point star at center (100, 180), outer=50, inner=20 - gold
        var starPoints = GenerateStarPoints(100, 180, 50, 20, 5);
        var starPath = new XGraphicsPath();
        starPath.AddPolygon(starPoints.Select(p => new XPoint(p.x, 792 - p.y)).ToArray());
        gfx.DrawPath(new XSolidBrush(XColors.Gold), starPath);

        // Irregular polygon - magenta
        var irregularPath = new XGraphicsPath();
        irregularPath.AddPolygon(new XPoint[]
        {
            new XPoint(200, 792 - 220),
            new XPoint(230, 792 - 180),
            new XPoint(280, 792 - 190),
            new XPoint(300, 792 - 150),
            new XPoint(270, 792 - 130),
            new XPoint(220, 792 - 140)
        });
        gfx.DrawPath(new XSolidBrush(XColors.Magenta), irregularPath);

        document.Save(outputPath);
    }

    private static (double x, double y)[] GenerateRegularPolygonPoints(double cx, double cy, double r, int sides)
    {
        var points = new (double x, double y)[sides];
        for (int i = 0; i < sides; i++)
        {
            var angle = 2 * Math.PI * i / sides - Math.PI / 2;
            points[i] = (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
        }
        return points;
    }

    private static (double x, double y)[] GenerateStarPoints(double cx, double cy, double outer, double inner, int points)
    {
        var vertices = new (double x, double y)[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            var angle = Math.PI * i / points - Math.PI / 2;
            var radius = (i % 2 == 0) ? outer : inner;
            vertices[i] = (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));
        }
        return vertices;
    }

    #region True Redaction Verification Test PDFs

    /// <summary>
    /// Creates a comprehensive test PDF with labeled zones for verifying true redaction.
    /// Each zone contains text and/or shapes with labels explaining what should happen.
    ///
    /// Layout (PDF coordinates, origin bottom-left):
    /// - Zone A (y=680-720): Text "SECRET-TEXT-A" - should be fully redacted
    /// - Zone B (y=600-640): Text "KEEP-TEXT-B" - should remain (outside redaction)
    /// - Zone C (y=480-560): Blue square - should be fully removed
    /// - Zone D (y=360-440): Green rectangle spanning x=100-300 - right half should be clipped
    /// - Zone E (y=240-320): Red circle - should remain unchanged
    /// - Zone F (y=120-200): Text "PARTIAL-TEXT-F" with partial redaction
    ///
    /// Redaction area: x=200-400, y=100-750 (vertical stripe on right side)
    /// </summary>
    public static void CreateTrueRedactionTestPdf(string outputPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);  // US Letter
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var labelFont = new XFont("Helvetica", 10);
        var textFont = new XFont("Helvetica", 14);

        // Zone A: Text to be fully redacted (inside redaction area)
        DrawLabel(gfx, labelFont, 50, 730, "ZONE A: Text should be REMOVED");
        gfx.DrawString("SECRET-TEXT-A", textFont, XBrushes.Black, new XPoint(220, 792 - 700));

        // Zone B: Text to remain (outside redaction area)
        DrawLabel(gfx, labelFont, 50, 650, "ZONE B: Text should REMAIN");
        gfx.DrawString("KEEP-TEXT-B", textFont, XBrushes.Black, new XPoint(50, 792 - 620));

        // Zone C: Shape to be fully removed (inside redaction area)
        DrawLabel(gfx, labelFont, 50, 570, "ZONE C: Shape should be REMOVED");
        gfx.DrawRectangle(new XSolidBrush(XColors.Blue), 220, 792 - 560, 80, 80);

        // Zone D: Shape to be partially clipped
        DrawLabel(gfx, labelFont, 50, 450, "ZONE D: Shape RIGHT HALF should be clipped");
        gfx.DrawRectangle(new XSolidBrush(XColors.Green), 100, 792 - 440, 200, 80);  // x=100 to x=300

        // Zone E: Shape to remain (outside redaction area)
        DrawLabel(gfx, labelFont, 50, 330, "ZONE E: Shape should REMAIN");
        gfx.DrawEllipse(new XSolidBrush(XColors.Red), 80, 792 - 320, 80, 80);

        // Zone F: Text with partial word redaction
        DrawLabel(gfx, labelFont, 50, 210, "ZONE F: 'PARTIAL' removed, 'VISIBLE' remains");
        gfx.DrawString("VISIBLE-TEXT", textFont, XBrushes.Black, new XPoint(50, 792 - 180));
        gfx.DrawString("PARTIAL-TEXT", textFont, XBrushes.Black, new XPoint(220, 792 - 180));

        // Draw redaction zone indicator (dashed outline)
        var dashedPen = new XPen(XColors.Gray, 1) { DashStyle = XDashStyle.Dash };
        gfx.DrawRectangle(dashedPen, 200, 792 - 750, 200, 650);
        gfx.DrawString("← REDACTION ZONE →", labelFont, XBrushes.Gray, new XPoint(230, 792 - 760));

        document.Save(outputPath);
    }

    /// <summary>
    /// Returns the redaction area for the TrueRedactionTestPdf.
    /// </summary>
    public static PdfRectangle GetTrueRedactionTestArea()
    {
        return new PdfRectangle(200, 100, 400, 750);
    }

    /// <summary>
    /// Returns expected verification results for TrueRedactionTestPdf.
    /// </summary>
    public static TrueRedactionExpectations GetTrueRedactionExpectations()
    {
        return new TrueRedactionExpectations
        {
            TextShouldBeRemoved = new[] { "SECRET-TEXT-A", "PARTIAL-TEXT" },
            TextShouldRemain = new[] { "KEEP-TEXT-B", "VISIBLE-TEXT" },
            ShapeZones = new[]
            {
                new ShapeExpectation { Zone = "C", Description = "Blue square", ShouldBeFullyRemoved = true },
                new ShapeExpectation { Zone = "D", Description = "Green rectangle", ShouldBePartiallyClipped = true,
                    OriginalXMax = 300, ClippedXMax = 200 },
                new ShapeExpectation { Zone = "E", Description = "Red circle", ShouldRemainUnchanged = true }
            }
        };
    }

    /// <summary>
    /// Creates a labeled test PDF with shapes and text for visual verification.
    /// Each shape has a text label nearby indicating what it is.
    /// </summary>
    public static void CreateLabeledShapesAndTextPdf(string outputPath)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);

        using var gfx = XGraphics.FromPdfPage(page);
        var labelFont = new XFont("Helvetica", 9);
        var textFont = new XFont("Helvetica", 12);

        double y = 750;
        double rowHeight = 100;

        // Row 1: Rectangle with label
        DrawLabel(gfx, labelFont, 50, y, "RECTANGLE");
        gfx.DrawRectangle(new XSolidBrush(XColors.Blue), 50, 792 - (y - 20), 80, 60);
        gfx.DrawString("RECT-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));
        y -= rowHeight;

        // Row 2: Triangle with label
        DrawLabel(gfx, labelFont, 50, y, "TRIANGLE");
        var trianglePath = new XGraphicsPath();
        trianglePath.AddPolygon(new XPoint[]
        {
            new XPoint(90, 792 - (y - 20)),
            new XPoint(50, 792 - (y - 70)),
            new XPoint(130, 792 - (y - 70))
        });
        gfx.DrawPath(new XSolidBrush(XColors.Green), trianglePath);
        gfx.DrawString("TRI-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));
        y -= rowHeight;

        // Row 3: Circle with label
        DrawLabel(gfx, labelFont, 50, y, "CIRCLE");
        gfx.DrawEllipse(new XSolidBrush(XColors.Red), 50, 792 - (y - 10), 70, 70);
        gfx.DrawString("CIRCLE-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));
        y -= rowHeight;

        // Row 4: Ellipse with label
        DrawLabel(gfx, labelFont, 50, y, "ELLIPSE");
        gfx.DrawEllipse(new XSolidBrush(XColors.Orange), 50, 792 - (y - 20), 90, 50);
        gfx.DrawString("ELLIPSE-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));
        y -= rowHeight;

        // Row 5: Pentagon with label
        DrawLabel(gfx, labelFont, 50, y, "PENTAGON");
        var pentagonPoints = GenerateRegularPolygonPoints(90, y - 45, 40, 5);
        var pentagonPath = new XGraphicsPath();
        pentagonPath.AddPolygon(pentagonPoints.Select(p => new XPoint(p.x, 792 - p.y)).ToArray());
        gfx.DrawPath(new XSolidBrush(XColors.Purple), pentagonPath);
        gfx.DrawString("PENTA-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));
        y -= rowHeight;

        // Row 6: Star with label
        DrawLabel(gfx, labelFont, 50, y, "STAR");
        var starPoints = GenerateStarPoints(90, y - 45, 40, 18, 5);
        var starPath = new XGraphicsPath();
        starPath.AddPolygon(starPoints.Select(p => new XPoint(p.x, 792 - p.y)).ToArray());
        gfx.DrawPath(new XSolidBrush(XColors.Gold), starPath);
        gfx.DrawString("STAR-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));
        y -= rowHeight;

        // Row 7: Irregular polygon with label
        DrawLabel(gfx, labelFont, 50, y, "IRREGULAR");
        var irregularPath = new XGraphicsPath();
        irregularPath.AddPolygon(new XPoint[]
        {
            new XPoint(50, 792 - (y - 30)),
            new XPoint(70, 792 - (y - 10)),
            new XPoint(110, 792 - (y - 20)),
            new XPoint(130, 792 - (y - 50)),
            new XPoint(100, 792 - (y - 70)),
            new XPoint(60, 792 - (y - 60))
        });
        gfx.DrawPath(new XSolidBrush(XColors.Magenta), irregularPath);
        gfx.DrawString("IRREG-TEXT", textFont, XBrushes.Black, new XPoint(150, 792 - (y - 40)));

        document.Save(outputPath);
    }

    private static void DrawLabel(XGraphics gfx, XFont font, double x, double y, string text)
    {
        // y is in PDF coordinates, convert to graphics
        gfx.DrawString(text, font, XBrushes.DarkGray, new XPoint(x, 792 - y));
    }

    #endregion
}

/// <summary>
/// Expected results for true redaction verification tests.
/// </summary>
public class TrueRedactionExpectations
{
    public string[] TextShouldBeRemoved { get; set; } = Array.Empty<string>();
    public string[] TextShouldRemain { get; set; } = Array.Empty<string>();
    public ShapeExpectation[] ShapeZones { get; set; } = Array.Empty<ShapeExpectation>();
}

/// <summary>
/// Expectation for a single shape zone in the test PDF.
/// </summary>
public class ShapeExpectation
{
    public string Zone { get; set; } = "";
    public string Description { get; set; } = "";
    public bool ShouldBeFullyRemoved { get; set; }
    public bool ShouldBePartiallyClipped { get; set; }
    public bool ShouldRemainUnchanged { get; set; }
    public double OriginalXMax { get; set; }
    public double ClippedXMax { get; set; }
}
