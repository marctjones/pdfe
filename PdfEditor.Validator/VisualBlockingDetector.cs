using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using PdfSharp.Pdf.IO;
using System.Text;

namespace PdfEditor.Validator;

/// <summary>
/// Detects content that is visually blocked by black rectangles or other shapes
/// This analyzes the z-order (drawing order) to determine what's on top
/// </summary>
public class VisualBlockingDetector
{
    public class ContentItem
    {
        public string Type { get; set; } = "";
        public string Content { get; set; } = "";
        public PdfRectangle BoundingBox { get; set; }
        public int DrawingOrder { get; set; } // Lower = drawn first = underneath
        public bool IsBlackRectangle { get; set; }
    }

    public class BlockingResult
    {
        public List<ContentItem> BlockedItems { get; set; } = new();
        public List<ContentItem> BlackBoxes { get; set; } = new();
        public bool HasBlockedContent => BlockedItems.Any();
    }

    /// <summary>
    /// Analyze PDF to find content that is visually blocked by black rectangles
    /// </summary>
    public static BlockingResult AnalyzeVisualBlocking(string pdfPath)
    {
        var result = new BlockingResult();

        using var document = PdfDocument.Open(pdfPath);

        foreach (var page in document.GetPages())
        {
            // Get all text with positions
            var words = page.GetWords().ToList();

            // Note: PdfPig doesn't easily give us z-order or identify black rectangles
            // from the rendering. We need to parse the content stream to get drawing order.

            // For now, we can detect overlapping text (which might indicate blocking)
            for (int i = 0; i < words.Count; i++)
            {
                for (int j = i + 1; j < words.Count; j++)
                {
                    if (BoundingBoxesOverlap(words[i].BoundingBox, words[j].BoundingBox))
                    {
                        // These words overlap - one might be blocking the other
                        // The one drawn later (higher j) is on top
                        result.BlockedItems.Add(new ContentItem
                        {
                            Type = "Text",
                            Content = words[i].Text,
                            BoundingBox = words[i].BoundingBox,
                            DrawingOrder = i
                        });
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Analyzes PDF content stream to detect black rectangles and what they cover
    /// This requires parsing the raw PDF operators
    /// </summary>
    public static BlockingResult AnalyzeContentStream(string pdfPath, int pageIndex = 0)
    {
        var result = new BlockingResult();

        var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        var page = document.Pages[pageIndex];

        // Get content stream
        var contentStream = page.Contents.Elements.FirstOrDefault();
        if (contentStream is PdfSharp.Pdf.PdfDictionary dict && dict.Stream != null)
        {
            var bytes = dict.Stream.Value;
            var content = Encoding.ASCII.GetString(bytes);

            // Parse for drawing operations
            // This is simplified - a full implementation would use ContentStreamParser
            var lines = content.Split('\n');

            var operations = new List<(string op, string data, int order)>();
            int order = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Contains(" re")) // Rectangle operator
                {
                    operations.Add(("rectangle", line, order++));
                }
                else if (line.Contains(" Tj") || line.Contains(" TJ")) // Text operators
                {
                    operations.Add(("text", line, order++));
                }
                else if (line.Contains(" f") || line.Contains(" F")) // Fill operator
                {
                    operations.Add(("fill", line, order++));
                }
                else if (line.Contains("RG") || line.Contains("rg")) // Color operators
                {
                    operations.Add(("color", line, order++));
                }
            }

            // Look for black rectangles (0 0 0 rg or RG followed by rectangle)
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];

                // Check if this is a black color setting
                if (op.op == "color" && (op.data.Contains("0 0 0 rg") || op.data.Contains("0 0 0 RG")))
                {
                    // Look for rectangle after this color setting
                    for (int j = i + 1; j < Math.Min(i + 5, operations.Count); j++)
                    {
                        if (operations[j].op == "rectangle")
                        {
                            result.BlackBoxes.Add(new ContentItem
                            {
                                Type = "BlackRectangle",
                                Content = operations[j].data,
                                DrawingOrder = operations[j].order
                            });

                            // Now check if any text operations came BEFORE this
                            // (meaning they would be underneath)
                            for (int k = 0; k < operations.Count; k++)
                            {
                                if (operations[k].op == "text" && operations[k].order < operations[j].order)
                                {
                                    result.BlockedItems.Add(new ContentItem
                                    {
                                        Type = "BlockedText",
                                        Content = operations[k].data,
                                        DrawingOrder = operations[k].order
                                    });
                                }
                            }

                            break;
                        }
                    }
                }
            }
        }

        return result;
    }

    private static bool BoundingBoxesOverlap(PdfRectangle box1, PdfRectangle box2)
    {
        return box1.Left < box2.Right &&
               box1.Right > box2.Left &&
               box1.Bottom < box2.Top &&
               box1.Top > box2.Bottom;
    }

    /// <summary>
    /// Render PDF to image and analyze pixels to detect black boxes
    /// This is the most accurate but requires image processing
    /// </summary>
    public static void AnalyzeRenderedPdf(string pdfPath)
    {
        Console.WriteLine("NOTE: Pixel-level analysis requires image rendering libraries");
        Console.WriteLine("Consider using:");
        Console.WriteLine("  - ImageMagick: convert PDF to PNG and analyze pixels");
        Console.WriteLine("  - PDFtoImage: Render PDF and check for black regions");
        Console.WriteLine("  - Tesseract OCR: Compare OCR results with text extraction");
    }
}

/// <summary>
/// Command to analyze visual blocking
/// </summary>
public static class VisualBlockingCommands
{
    public static void DetectBlocking(string pdfPath)
    {
        Console.WriteLine($"=== Analyzing Visual Blocking in: {Path.GetFileName(pdfPath)} ===\n");

        // Method 1: Content stream analysis
        Console.WriteLine("Method 1: Content Stream Analysis");
        Console.WriteLine("Parsing PDF operators to find black rectangles and underlying content...\n");

        try
        {
            var result = VisualBlockingDetector.AnalyzeContentStream(pdfPath);

            if (result.BlackBoxes.Any())
            {
                Console.WriteLine($"Found {result.BlackBoxes.Count} black rectangle(s):");
                foreach (var box in result.BlackBoxes)
                {
                    Console.WriteLine($"  Black box at drawing order {box.DrawingOrder}");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("No black rectangles detected.\n");
            }

            if (result.BlockedItems.Any())
            {
                Console.WriteLine($"⚠ Found {result.BlockedItems.Count} potentially blocked item(s):");
                foreach (var item in result.BlockedItems.Take(20))
                {
                    Console.WriteLine($"  {item.Type}: {item.Content.Substring(0, Math.Min(50, item.Content.Length))}");
                    Console.WriteLine($"    Drawing order: {item.DrawingOrder} (drawn before black box)");
                }
                if (result.BlockedItems.Count > 20)
                {
                    Console.WriteLine($"  ... and {result.BlockedItems.Count - 20} more");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("✓ No content detected underneath black boxes.\n");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error analyzing content stream: {ex.Message}\n");
        }

        // Method 2: Overlap detection
        Console.WriteLine("Method 2: Spatial Overlap Detection");
        Console.WriteLine("Checking for overlapping text elements...\n");

        try
        {
            var overlapResult = VisualBlockingDetector.AnalyzeVisualBlocking(pdfPath);

            if (overlapResult.BlockedItems.Any())
            {
                Console.WriteLine($"⚠ Found {overlapResult.BlockedItems.Count} overlapping text elements");
                foreach (var item in overlapResult.BlockedItems.Take(10))
                {
                    Console.WriteLine($"  \"{item.Content}\" at ({item.BoundingBox.Left:F1}, {item.BoundingBox.Bottom:F1})");
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("✓ No overlapping text detected.\n");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error analyzing overlaps: {ex.Message}\n");
        }

        // Recommendations
        Console.WriteLine("=== Recommendations ===");
        Console.WriteLine("For complete visual blocking analysis:");
        Console.WriteLine("1. Use ImageMagick to render PDF to image");
        Console.WriteLine("2. Analyze pixels to find black regions");
        Console.WriteLine("3. Compare with text positions from PdfPig");
        Console.WriteLine("4. Or use our redaction validation approach:");
        Console.WriteLine("   - Extract text before redaction");
        Console.WriteLine("   - Extract text after redaction");
        Console.WriteLine("   - Compare: removed text = content that was under black boxes");
    }
}
