using System.CommandLine;
using Pdfe.Core.Document;
using Pdfe.Core.Graphics;
using Pdfe.Rendering;
using SkiaSharp;

namespace Pdfe.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("pdfe - PDF toolkit powered by Pdfe.Core")
        {
            CreateInfoCommand(),
            CreateTextCommand(),
            CreateLettersCommand(),
            CreateRenderCommand(),
            CreateDrawCommand(),
            CreateDemoCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// pdfe info <file> - Show PDF document information
    /// </summary>
    static Command CreateInfoCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file to analyze");
        var command = new Command("info", "Show PDF document information")
        {
            fileArg
        };

        command.SetHandler((FileInfo file) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            try
            {
                using var doc = PdfDocument.Open(file.FullName);

                Console.WriteLine($"File: {file.Name}");
                Console.WriteLine($"Size: {file.Length:N0} bytes");
                Console.WriteLine();
                Console.WriteLine("=== Document Info ===");
                Console.WriteLine($"PDF Version: {doc.Version}");
                Console.WriteLine($"Page Count: {doc.PageCount}");
                Console.WriteLine($"Encrypted: {doc.IsEncrypted}");
                Console.WriteLine();

                if (doc.Title != null) Console.WriteLine($"Title: {doc.Title}");
                if (doc.Author != null) Console.WriteLine($"Author: {doc.Author}");
                if (doc.Subject != null) Console.WriteLine($"Subject: {doc.Subject}");
                if (doc.Creator != null) Console.WriteLine($"Creator: {doc.Creator}");
                if (doc.Producer != null) Console.WriteLine($"Producer: {doc.Producer}");

                Console.WriteLine();
                Console.WriteLine("=== Pages ===");
                for (int i = 1; i <= Math.Min(doc.PageCount, 10); i++)
                {
                    var page = doc.GetPage(i);
                    Console.WriteLine($"  Page {i}: {page.Width:F0} x {page.Height:F0} pts ({page.Width / 72:F1}\" x {page.Height / 72:F1}\")");
                }
                if (doc.PageCount > 10)
                    Console.WriteLine($"  ... and {doc.PageCount - 10} more pages");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }, fileArg);

        return command;
    }

    /// <summary>
    /// pdfe text <file> [--page N] - Extract text from PDF
    /// </summary>
    static Command CreateTextCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file");
        var pageOption = new Option<int?>("--page", "Specific page number (1-based)");
        pageOption.AddAlias("-p");

        var command = new Command("text", "Extract text from PDF")
        {
            fileArg,
            pageOption
        };

        command.SetHandler((FileInfo file, int? page) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            try
            {
                using var doc = PdfDocument.Open(file.FullName);

                if (page.HasValue)
                {
                    if (page.Value < 1 || page.Value > doc.PageCount)
                    {
                        Console.Error.WriteLine($"Invalid page number. Document has {doc.PageCount} pages.");
                        return;
                    }
                    var p = doc.GetPage(page.Value);
                    Console.WriteLine($"=== Page {page.Value} ===");
                    Console.WriteLine(p.Text);
                }
                else
                {
                    for (int i = 1; i <= doc.PageCount; i++)
                    {
                        var p = doc.GetPage(i);
                        if (doc.PageCount > 1)
                            Console.WriteLine($"=== Page {i} ===");
                        Console.WriteLine(p.Text);
                        if (i < doc.PageCount)
                            Console.WriteLine();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }, fileArg, pageOption);

        return command;
    }

    /// <summary>
    /// pdfe letters <file> --page N - Show letters with positions
    /// </summary>
    static Command CreateLettersCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file");
        var pageOption = new Option<int>("--page", () => 1, "Page number (1-based)");
        pageOption.AddAlias("-p");
        var limitOption = new Option<int>("--limit", () => 50, "Maximum letters to show");
        limitOption.AddAlias("-n");

        var command = new Command("letters", "Show letters with position information")
        {
            fileArg,
            pageOption,
            limitOption
        };

        command.SetHandler((FileInfo file, int page, int limit) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            try
            {
                using var doc = PdfDocument.Open(file.FullName);

                if (page < 1 || page > doc.PageCount)
                {
                    Console.Error.WriteLine($"Invalid page number. Document has {doc.PageCount} pages.");
                    return;
                }

                var p = doc.GetPage(page);
                var letters = p.Letters;

                Console.WriteLine($"Page {page}: {letters.Count} letters");
                Console.WriteLine();
                Console.WriteLine("Char  X       Y       Width   Font");
                Console.WriteLine("----  ------  ------  ------  ----");

                foreach (var letter in letters.Take(limit))
                {
                    var ch = letter.Value.Length == 1 && char.IsControl(letter.Value[0])
                        ? $"\\x{(int)letter.Value[0]:X2}"
                        : letter.Value;
                    Console.WriteLine($"{ch,-4}  {letter.StartX,6:F1}  {letter.StartY,6:F1}  {letter.Width,6:F1}  {letter.FontName}");
                }

                if (letters.Count > limit)
                    Console.WriteLine($"... and {letters.Count - limit} more letters");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }, fileArg, pageOption, limitOption);

        return command;
    }

    /// <summary>
    /// pdfe render <file> -o <output.png> [--page N] [--dpi N]
    /// </summary>
    static Command CreateRenderCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file");
        var outputOption = new Option<FileInfo>("--output", "Output image file (PNG)") { IsRequired = true };
        outputOption.AddAlias("-o");
        var pageOption = new Option<int>("--page", () => 1, "Page number (1-based)");
        pageOption.AddAlias("-p");
        var dpiOption = new Option<int>("--dpi", () => 150, "Resolution in DPI");

        var command = new Command("render", "Render PDF page to image")
        {
            fileArg,
            outputOption,
            pageOption,
            dpiOption
        };

        command.SetHandler((FileInfo file, FileInfo output, int page, int dpi) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            try
            {
                using var doc = PdfDocument.Open(file.FullName);

                if (page < 1 || page > doc.PageCount)
                {
                    Console.Error.WriteLine($"Invalid page number. Document has {doc.PageCount} pages.");
                    return;
                }

                var renderer = new SkiaRenderer();
                var options = new RenderOptions { Dpi = dpi };

                Console.WriteLine($"Rendering page {page} at {dpi} DPI...");
                using var bitmap = renderer.RenderPage(doc.GetPage(page), options);

                Console.WriteLine($"Output size: {bitmap.Width} x {bitmap.Height} pixels");

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.OpenWrite(output.FullName);
                data.SaveTo(stream);

                Console.WriteLine($"Saved to: {output.FullName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }, fileArg, outputOption, pageOption, dpiOption);

        return command;
    }

    /// <summary>
    /// pdfe draw <file> -o <output.pdf> - Add shapes to PDF (demo)
    /// </summary>
    static Command CreateDrawCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file");
        var outputOption = new Option<FileInfo>("--output", "Output PDF file") { IsRequired = true };
        outputOption.AddAlias("-o");
        var rectOption = new Option<string?>("--rect", "Add rectangle: x,y,w,h (in points)");
        var colorOption = new Option<string>("--color", () => "black", "Fill color: black, red, green, blue");

        var command = new Command("draw", "Add shapes to PDF (demo of graphics API)")
        {
            fileArg,
            outputOption,
            rectOption,
            colorOption
        };

        command.SetHandler((FileInfo file, FileInfo output, string? rect, string color) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                return;
            }

            try
            {
                using var doc = PdfDocument.Open(file.FullName);
                var page = doc.GetPage(1);

                using var graphics = page.GetGraphics();

                var brush = color.ToLower() switch
                {
                    "red" => PdfBrush.Red,
                    "green" => PdfBrush.Green,
                    "blue" => PdfBrush.Blue,
                    "white" => PdfBrush.White,
                    _ => PdfBrush.Black
                };

                if (rect != null)
                {
                    var parts = rect.Split(',');
                    if (parts.Length == 4 &&
                        double.TryParse(parts[0], out var x) &&
                        double.TryParse(parts[1], out var y) &&
                        double.TryParse(parts[2], out var w) &&
                        double.TryParse(parts[3], out var h))
                    {
                        Console.WriteLine($"Drawing rectangle at ({x}, {y}) size {w}x{h} with {color} fill");
                        graphics.DrawRectangle(x, y, w, h, brush);
                    }
                    else
                    {
                        Console.Error.WriteLine("Invalid rectangle format. Use: x,y,w,h");
                        return;
                    }
                }
                else
                {
                    // Default: draw a sample rectangle
                    Console.WriteLine("Drawing sample rectangle at (100, 100) size 200x100");
                    graphics.DrawRectangle(100, 100, 200, 100, brush);
                }

                graphics.Flush();
                doc.Save(output.FullName);
                Console.WriteLine($"Saved to: {output.FullName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }, fileArg, outputOption, rectOption, colorOption);

        return command;
    }

    /// <summary>
    /// pdfe demo - Run interactive demos
    /// </summary>
    static Command CreateDemoCommand()
    {
        var command = new Command("demo", "Run interactive demos of Pdfe.Core capabilities");

        command.SetHandler(() =>
        {
            Console.WriteLine("=== Pdfe.Core Demo ===");
            Console.WriteLine();

            // Demo 1: Create PDF in memory
            Console.WriteLine("1. Creating PDF in memory...");
            var pdfBytes = CreateSamplePdf();
            Console.WriteLine($"   Created {pdfBytes.Length} byte PDF");

            // Demo 2: Parse and extract info
            Console.WriteLine();
            Console.WriteLine("2. Parsing PDF structure...");
            using var doc = PdfDocument.Open(pdfBytes);
            Console.WriteLine($"   Version: {doc.Version}");
            Console.WriteLine($"   Pages: {doc.PageCount}");

            // Demo 3: Extract text
            Console.WriteLine();
            Console.WriteLine("3. Extracting text...");
            var text = doc.GetPage(1).Text;
            Console.WriteLine($"   Text: \"{text.Trim()}\"");

            // Demo 4: Get letter positions
            Console.WriteLine();
            Console.WriteLine("4. Letter positions (first 5)...");
            var letters = doc.GetPage(1).Letters;
            foreach (var letter in letters.Take(5))
            {
                Console.WriteLine($"   '{letter.Value}' at ({letter.StartX:F1}, {letter.StartY:F1})");
            }

            // Demo 5: Add graphics
            Console.WriteLine();
            Console.WriteLine("5. Adding graphics...");
            var page = doc.GetPage(1);
            using var g = page.GetGraphics();
            g.DrawRectangle(50, 50, 100, 50, PdfBrush.Red);
            g.DrawLine(50, 50, 150, 100, PdfPen.Black);
            g.Flush();
            Console.WriteLine("   Added red rectangle and black line");

            // Demo 6: Save modified PDF
            Console.WriteLine();
            Console.WriteLine("6. Saving modified PDF...");
            var modifiedBytes = doc.SaveToBytes();
            Console.WriteLine($"   Modified PDF: {modifiedBytes.Length} bytes");

            // Demo 7: Render to image
            Console.WriteLine();
            Console.WriteLine("7. Rendering to image...");
            var renderer = new SkiaRenderer();
            using var bitmap = renderer.RenderPage(doc.GetPage(1), new RenderOptions { Dpi = 72 });
            Console.WriteLine($"   Bitmap: {bitmap.Width} x {bitmap.Height} pixels");

            Console.WriteLine();
            Console.WriteLine("=== Demo Complete ===");
            Console.WriteLine();
            Console.WriteLine("Try these commands:");
            Console.WriteLine("  pdfe info <file.pdf>              - Show PDF info");
            Console.WriteLine("  pdfe text <file.pdf>              - Extract text");
            Console.WriteLine("  pdfe letters <file.pdf> -p 1      - Show letter positions");
            Console.WriteLine("  pdfe render <file.pdf> -o out.png - Render to image");
            Console.WriteLine("  pdfe draw <file.pdf> -o out.pdf   - Add shapes");
        });

        return command;
    }

    static byte[] CreateSamplePdf()
    {
        var content = "BT /F1 24 Tf 100 700 Td (Hello from Pdfe.Core!) Tj ET";

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.NewLine = "\n";

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new long[6];

        offsets[1] = ms.Position;
        writer.WriteLine("1 0 obj");
        writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[2] = ms.Position;
        writer.WriteLine("2 0 obj");
        writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[3] = ms.Position;
        writer.WriteLine("3 0 obj");
        writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[4] = ms.Position;
        writer.WriteLine("4 0 obj");
        writer.WriteLine($"<< /Length {content.Length} >>");
        writer.WriteLine("stream");
        writer.Write(content);
        writer.WriteLine();
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");
        writer.Flush();

        offsets[5] = ms.Position;
        writer.WriteLine("5 0 obj");
        writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        writer.WriteLine("endobj");
        writer.Flush();

        long xrefPos = ms.Position;
        writer.WriteLine("xref");
        writer.WriteLine("0 6");
        writer.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= 5; i++)
            writer.WriteLine($"{offsets[i]:D10} 00000 n ");
        writer.Flush();

        writer.WriteLine("trailer");
        writer.WriteLine("<< /Root 1 0 R /Size 6 >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefPos.ToString());
        writer.WriteLine("%%EOF");
        writer.Flush();

        return ms.ToArray();
    }
}
