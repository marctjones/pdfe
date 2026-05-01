using System.CommandLine;
using Pdfe.Core.Document;
using Pdfe.Core.Graphics;
using Pdfe.Core.Text.Segmentation;
using Pdfe.Ocr;
using Pdfe.Rendering;
using SkiaSharp;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Pdfe.Cli.Tests")]

namespace Pdfe.Cli;

class Program
{
    static Task<int> Main(string[] args) => RunAsync(args);

    /// <summary>
    /// Build and invoke the root command. Exposed for tests so they can
    /// exercise the CLI parsing + handler pipeline without spawning a
    /// subprocess.
    /// </summary>
    internal static Task<int> RunAsync(string[] args)
    {
        var rootCommand = new RootCommand("pdfe - PDF toolkit powered by Pdfe.Core")
        {
            CreateInfoCommand(),
            CreateTextCommand(),
            CreateLettersCommand(),
            CreateRenderCommand(),
            CreateDrawCommand(),
            CreateRedactCommand(),
            CreateFillFormCommand(),
            CreateAddFieldCommand(),
            CreateAutodetectFieldsCommand(),
            CreateAuditCommand(),
            CreateOcrCommand(),
            CreateDemoCommand(),
            CreateCorpusScanCommand(),
        };

        return rootCommand.InvokeAsync(args);
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
    /// pdfe redact &lt;input&gt; &lt;output&gt; &lt;text&gt; [--case-sensitive]
    /// Remove every occurrence of a text string from a PDF at the
    /// content-stream level (glyph removal, not visual overlay), then
    /// save the result to a new file.
    /// </summary>
    static Command CreateRedactCommand()
    {
        var inputArg = new Argument<FileInfo>("input", "Input PDF file");
        var outputArg = new Argument<FileInfo>("output", "Output PDF path");
        var textArg = new Argument<string>("text", "Text to remove (all occurrences)");
        var caseSensitiveOption = new Option<bool>(
            "--case-sensitive",
            () => false,
            "Match case exactly (default: case-insensitive)");

        var command = new Command(
            "redact",
            "Remove text from a PDF (glyph-level removal; text extraction will not find it)")
        {
            inputArg, outputArg, textArg, caseSensitiveOption
        };

        command.SetHandler((FileInfo input, FileInfo output, string text, bool caseSensitive) =>
        {
            if (!input.Exists)
            {
                Console.Error.WriteLine($"File not found: {input.FullName}");
                Environment.ExitCode = 1;
                return;
            }
            if (string.IsNullOrEmpty(text))
            {
                Console.Error.WriteLine("Redaction text must not be empty.");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                int count = RunRedact(input.FullName, output.FullName, text, caseSensitive);
                Console.WriteLine($"Redacted {count} occurrence(s) of '{text}'");
                Console.WriteLine($"Output: {output.FullName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, inputArg, outputArg, textArg, caseSensitiveOption);

        return command;
    }

    /// <summary>
    /// Core redact-a-file operation — open, call Pdfe.Core's text
    /// redaction, save. Exposed internally for tests.
    /// </summary>
    internal static int RunRedact(string inputPath, string outputPath, string text, bool caseSensitive)
    {
        var bytes = File.ReadAllBytes(inputPath);
        using var doc = PdfDocument.Open(bytes);
        var count = doc.RedactText(text, caseSensitive);
        doc.Save(outputPath);
        return count;
    }

    /// <summary>
    /// pdfe fill-form &lt;input&gt; &lt;output&gt; --field name=value [--field name=value]... [--flatten]
    /// Set AcroForm field values on a PDF and save. With --flatten, the
    /// values are baked into the page content streams and the form is
    /// removed (no longer interactive).
    /// </summary>
    static Command CreateFillFormCommand()
    {
        var inputArg = new Argument<FileInfo>("input", "Input PDF file");
        var outputArg = new Argument<FileInfo>("output", "Output PDF path");
        var fieldOption = new Option<string[]>(
            "--field",
            "Field assignment in the form 'FullName=Value'. May be repeated for multiple fields.")
        { AllowMultipleArgumentsPerToken = false };
        fieldOption.AddAlias("-f");
        var flattenOption = new Option<bool>(
            "--flatten",
            () => false,
            "Bake values into page content and remove the form (non-interactive output)");

        var command = new Command(
            "fill-form",
            "Set AcroForm field values and save (optionally flatten to baked content)")
        {
            inputArg, outputArg, fieldOption, flattenOption
        };

        command.SetHandler((FileInfo input, FileInfo output, string[] fields, bool flatten) =>
        {
            if (!input.Exists)
            {
                Console.Error.WriteLine($"File not found: {input.FullName}");
                Environment.ExitCode = 1;
                return;
            }

            if (fields == null || fields.Length == 0)
            {
                Console.Error.WriteLine("At least one --field name=value assignment is required.");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                int set = RunFillForm(input.FullName, output.FullName, fields, flatten);
                Console.WriteLine($"Set {set} field value(s){(flatten ? " (flattened)" : "")}");
                Console.WriteLine($"Output: {output.FullName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, inputArg, outputArg, fieldOption, flattenOption);

        return command;
    }

    /// <summary>
    /// Core fill-form operation. Returns the number of fields that were
    /// successfully assigned. Throws InvalidOperationException if the
    /// document has no AcroForm or any --field token is malformed; throws
    /// KeyNotFoundException for an unknown field name.
    /// </summary>
    internal static int RunFillForm(string inputPath, string outputPath, string[] fields, bool flatten)
    {
        var bytes = File.ReadAllBytes(inputPath);
        using var doc = PdfDocument.Open(bytes);

        var form = doc.GetAcroForm()
            ?? throw new InvalidOperationException("Document has no /AcroForm — nothing to fill.");

        int set = 0;
        foreach (var raw in fields)
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0)
                throw new InvalidOperationException(
                    $"Malformed --field '{raw}'. Expected 'FullName=Value'.");
            var name = raw.Substring(0, eq);
            var value = raw.Substring(eq + 1);

            var field = form.FindField(name)
                ?? throw new KeyNotFoundException($"Field '{name}' not found in document.");
            field.SetValue(value);
            set++;
        }

        if (flatten)
            doc.FlattenAcroForm();

        doc.Save(outputPath);
        return set;
    }

    /// <summary>
    /// pdfe add-field input output --type T --name N --page P --rect "x,y,w,h" [--value v] [--option o]...
    /// Add a new AcroForm field to an existing PDF.
    /// </summary>
    static Command CreateAddFieldCommand()
    {
        var inputArg = new Argument<FileInfo>("input", "Input PDF file");
        var outputArg = new Argument<FileInfo>("output", "Output PDF path");
        var typeOption = new Option<string>("--type", () => "Text",
            "Field type: Text, Checkbox, Choice, Signature");
        var nameOption = new Option<string>("--name", "Full field name") { IsRequired = true };
        var pageOption = new Option<int>("--page", () => 1, "1-based page number");
        var rectOption = new Option<string>("--rect",
            "Rect in PDF points as 'left,bottom,right,top' (bottom-left origin)") { IsRequired = true };
        var valueOption = new Option<string?>("--value", "Default value (Text/Choice) or 'Yes'/'Off' (Checkbox)");
        var optionsOption = new Option<string[]>("--option",
            "Choice option (repeatable). At least one required for --type Choice.")
        { AllowMultipleArgumentsPerToken = false };

        var command = new Command("add-field",
            "Add a new AcroForm field (Text/Checkbox/Choice/Signature) to a PDF")
        {
            inputArg, outputArg, typeOption, nameOption, pageOption, rectOption, valueOption, optionsOption
        };

        command.SetHandler(ctx =>
        {
            var input = ctx.ParseResult.GetValueForArgument(inputArg);
            var output = ctx.ParseResult.GetValueForArgument(outputArg);
            var type = ctx.ParseResult.GetValueForOption(typeOption)!;
            var name = ctx.ParseResult.GetValueForOption(nameOption)!;
            var page = ctx.ParseResult.GetValueForOption(pageOption);
            var rectStr = ctx.ParseResult.GetValueForOption(rectOption)!;
            var value = ctx.ParseResult.GetValueForOption(valueOption);
            var options = ctx.ParseResult.GetValueForOption(optionsOption) ?? Array.Empty<string>();

            if (!input.Exists)
            {
                Console.Error.WriteLine($"File not found: {input.FullName}");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                RunAddField(input.FullName, output.FullName, type, name, page, rectStr, value, options);
                Console.WriteLine($"Added {type} field '{name}' to page {page}");
                Console.WriteLine($"Output: {output.FullName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }

    internal static void RunAddField(string inputPath, string outputPath,
        string type, string name, int page, string rectStr,
        string? value, string[] options)
    {
        var rect = ParseRect(rectStr);
        var bytes = File.ReadAllBytes(inputPath);
        using var doc = PdfDocument.Open(bytes);

        switch (type.ToLowerInvariant())
        {
            case "text":
                var t = doc.AddTextField(page, rect, name, defaultValue: value);
                break;
            case "checkbox":
            case "btn":
            case "button":
                doc.AddCheckBox(page, rect, name,
                    defaultChecked: string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase));
                break;
            case "choice":
            case "combo":
            case "dropdown":
                if (options.Length == 0)
                    throw new ArgumentException("--option is required at least once for --type Choice.");
                doc.AddChoiceField(page, rect, name, options, defaultValue: value);
                break;
            case "signature":
            case "sig":
                doc.AddSignatureField(page, rect, name);
                break;
            default:
                throw new ArgumentException(
                    $"Unknown field type '{type}'. Use Text, Checkbox, Choice, or Signature.");
        }

        doc.Save(outputPath);
    }

    private static Pdfe.Core.Document.PdfRectangle ParseRect(string s)
    {
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            throw new ArgumentException($"Expected --rect 'left,bottom,right,top'; got '{s}'.");
        var nums = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out nums[i]))
                throw new ArgumentException($"Bad number in --rect: '{parts[i]}'.");
        }
        return new Pdfe.Core.Document.PdfRectangle(nums[0], nums[1], nums[2], nums[3]);
    }

    /// <summary>
    /// pdfe autodetect-fields input [output] [--apply] [--json]
    /// Run heuristic auto-detection. Prints suggestions; with --apply,
    /// also materialises them into output.
    /// </summary>
    static Command CreateAutodetectFieldsCommand()
    {
        var inputArg = new Argument<FileInfo>("input", "Input PDF file");
        var outputArg = new Argument<FileInfo?>("output",
            () => null, "Output PDF (required with --apply)");
        var applyOption = new Option<bool>("--apply", () => false,
            "Add the detected fields to the PDF and save to <output>");

        var command = new Command("autodetect-fields",
            "Heuristically detect likely form-field locations on each page")
        {
            inputArg, outputArg, applyOption
        };

        command.SetHandler((FileInfo input, FileInfo? output, bool apply) =>
        {
            if (!input.Exists)
            {
                Console.Error.WriteLine($"File not found: {input.FullName}");
                Environment.ExitCode = 1;
                return;
            }
            if (apply && output == null)
            {
                Console.Error.WriteLine("--apply requires an <output> PDF path.");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(input.FullName);
                using var doc = PdfDocument.Open(bytes);
                var sugg = PdfFormAutoDetector.Scan(doc);

                Console.WriteLine($"Detected {sugg.Count} field candidate(s):");
                foreach (var s in sugg)
                    Console.WriteLine(
                        $"  page {s.PageNumber}  {s.FieldType,-9}  " +
                        $"[{s.Rect.Left:0.#},{s.Rect.Bottom:0.#}-{s.Rect.Right:0.#},{s.Rect.Top:0.#}]  " +
                        $"{s.SuggestedName}  ({s.Reason})");

                if (apply)
                {
                    var n = PdfFormAutoDetector.Apply(doc, sugg);
                    doc.Save(output!.FullName);
                    Console.WriteLine($"Applied {n} field(s); wrote {output.FullName}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, inputArg, outputArg, applyOption);

        return command;
    }

    /// <summary>
    /// pdfe audit &lt;file&gt; [--json]
    /// Report text that is present in the PDF content stream but
    /// visually occluded by a later-drawn opaque object ("redaction
    /// by black box" style). Exits with a non-zero status when leaks
    /// are found, so CI can gate on a clean audit.
    /// </summary>
    static Command CreateAuditCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file to audit");
        var jsonOption = new Option<bool>(
            "--json",
            () => false,
            "Emit machine-readable JSON instead of the human-readable report");
        var deepOption = new Option<bool>(
            "--deep",
            () => false,
            "Also run differential OCR: render the page twice (with and " +
            "without overlays stripped), OCR both, and report words " +
            "recoverable from the underlying image but hidden in the " +
            "displayed render. Catches rasterized-leak cases the " +
            "structural detector can't see. Requires `tesseract` on PATH.");

        var command = new Command(
            "audit",
            "Detect text hidden behind opaque overlays (black-box redaction audit)")
        {
            fileArg, jsonOption, deepOption,
        };

        command.SetHandler((FileInfo file, bool json, bool deep) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var bytes = File.ReadAllBytes(file.FullName);
                using var doc = PdfDocument.Open(bytes);
                var structuralHits = HiddenTextDetector.Scan(doc);

                IReadOnlyList<DifferentialOcrHit> ocrHits = Array.Empty<DifferentialOcrHit>();
                if (deep)
                {
                    var ocr = new PdfOcrService();
                    if (!ocr.IsAvailable())
                    {
                        Console.Error.WriteLine(
                            "--deep requires tesseract on PATH. Install with " +
                            "`apt install tesseract-ocr`.");
                        Environment.ExitCode = 1;
                        return;
                    }
                    var auditor = new DifferentialOcrAuditor(ocr);
                    ocrHits = auditor.Scan(bytes);
                }

                if (json)
                {
                    PrintAuditJson(structuralHits, ocrHits);
                }
                else
                {
                    PrintAuditHuman(structuralHits, ocrHits, deep);
                }

                // Exit non-zero on any hits.
                Environment.ExitCode = (structuralHits.Count + ocrHits.Count) == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, fileArg, jsonOption, deepOption);

        return command;
    }

    private static void PrintAuditHuman(
        IReadOnlyList<HiddenTextRecord> structural,
        IReadOnlyList<DifferentialOcrHit> ocr,
        bool deepRun)
    {
        if (structural.Count == 0 && ocr.Count == 0)
        {
            Console.WriteLine(deepRun
                ? "✓ No hidden text detected (structural + differential OCR clean)."
                : "✓ No hidden text detected.");
            return;
        }

        if (structural.Count > 0)
        {
            Console.WriteLine($"✗ {structural.Count} structural hidden-text leak(s):");
            foreach (var h in structural)
            {
                Console.WriteLine(
                    $"  Page {h.PageNumber} at ({h.BoundingBox.Left:F1}, {h.BoundingBox.Bottom:F1}): " +
                    $"\"{h.Text}\" covered by {h.HiddenBy}");
            }
        }
        if (ocr.Count > 0)
        {
            Console.WriteLine($"✗ {ocr.Count} differential-OCR leak(s) " +
                $"(text in raster, hidden by overlay):");
            foreach (var h in ocr)
            {
                Console.WriteLine(
                    $"  Page {h.PageNumber} at ({h.BoundingBox.Left:F1}, {h.BoundingBox.Bottom:F1}) " +
                    $"[conf {h.Confidence:F2}]: \"{h.Text}\"");
            }
        }
    }

    private static void PrintAuditJson(
        IReadOnlyList<HiddenTextRecord> structural,
        IReadOnlyList<DifferentialOcrHit> ocr)
    {
        Console.WriteLine("{");
        Console.WriteLine("  \"structural\": [");
        for (int i = 0; i < structural.Count; i++)
        {
            var h = structural[i];
            var sep = i + 1 < structural.Count ? "," : "";
            Console.WriteLine(
                $"    {{ \"page\": {h.PageNumber}, " +
                $"\"text\": \"{Esc(h.Text)}\", " +
                $"\"bbox\": [{h.BoundingBox.Left:F2}, {h.BoundingBox.Bottom:F2}, " +
                $"{h.BoundingBox.Right:F2}, {h.BoundingBox.Top:F2}], " +
                $"\"hidden_by\": \"{Esc(h.HiddenBy)}\" }}{sep}");
        }
        Console.WriteLine("  ],");
        Console.WriteLine("  \"differential_ocr\": [");
        for (int i = 0; i < ocr.Count; i++)
        {
            var h = ocr[i];
            var sep = i + 1 < ocr.Count ? "," : "";
            Console.WriteLine(
                $"    {{ \"page\": {h.PageNumber}, " +
                $"\"text\": \"{Esc(h.Text)}\", " +
                $"\"bbox\": [{h.BoundingBox.Left:F2}, {h.BoundingBox.Bottom:F2}, " +
                $"{h.BoundingBox.Right:F2}, {h.BoundingBox.Top:F2}], " +
                $"\"confidence\": {h.Confidence:F3} }}{sep}");
        }
        Console.WriteLine("  ]");
        Console.WriteLine("}");
    }

    private static string Esc(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\n", "\\n").Replace("\r", "\\r");


    /// <summary>
    /// pdfe ocr &lt;file&gt; [--page N] [--dpi 300] [--lang eng]
    /// OCR a PDF page (or all pages) using the system tesseract CLI.
    /// </summary>
    static Command CreateOcrCommand()
    {
        var fileArg = new Argument<FileInfo>("file", "PDF file to OCR");
        var pageOption = new Option<int?>("--page", "Page to OCR (1-based). Omit for all pages.");
        pageOption.AddAlias("-p");
        var dpiOption = new Option<int>("--dpi", () => 300, "Render DPI for OCR (higher = slower, more accurate)");
        var langOption = new Option<string>("--lang", () => "eng", "Tesseract language code (e.g. eng, deu, eng+spa)");
        var tessdataOption = new Option<string?>("--tessdata", "Path to a directory containing <lang>.traineddata. Defaults to TESSDATA_PREFIX.");

        var command = new Command("ocr", "Render and OCR a PDF page via tesseract")
        {
            fileArg, pageOption, dpiOption, langOption, tessdataOption,
        };

        command.SetHandler((FileInfo file, int? page, int dpi, string lang, string? tessdata) =>
        {
            if (!file.Exists)
            {
                Console.Error.WriteLine($"File not found: {file.FullName}");
                Environment.ExitCode = 1;
                return;
            }

            var ocr = new PdfOcrService(language: lang, dpi: dpi, tessdataPrefix: tessdata);
            if (!ocr.IsAvailable())
            {
                Console.Error.WriteLine(
                    "tesseract CLI not found on PATH. Install with `apt install tesseract-ocr` " +
                    "(or your platform's equivalent).");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                using var doc = PdfDocument.Open(File.ReadAllBytes(file.FullName));
                int from = page.GetValueOrDefault(1);
                int to   = page.HasValue ? page.Value : doc.PageCount;

                if (from < 1 || from > doc.PageCount || to < from || to > doc.PageCount)
                {
                    Console.Error.WriteLine($"Page out of range (document has {doc.PageCount} pages).");
                    Environment.ExitCode = 1;
                    return;
                }

                for (int p = from; p <= to; p++)
                {
                    if (doc.PageCount > 1)
                        Console.WriteLine($"=== Page {p} ===");
                    var result = ocr.RecognizePage(doc.GetPage(p));
                    Console.WriteLine(result.Text);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, fileArg, pageOption, dpiOption, langOption, tessdataOption);

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

    /// <summary>
    /// pdfe corpus-scan &lt;corpus-dir&gt; --output out.json
    ///                  [--chunk N] [--total M] [--dpi 150]
    ///
    /// Internal-use subcommand that powers the chunked exploratory
    /// differential harness without the overhead of `dotnet test`.
    /// Replaces the per-chunk dotnet test invocation
    /// (~3 min startup × 14 chunks = 40+ min wasted) with a published
    /// pdfe binary call (~500 ms startup × 14 = 7 sec).
    ///
    /// Renders page 1 of each PDF in the corpus's chunk-slice with
    /// pdfe's SkiaRenderer and with `mutool draw`, computes diff
    /// metrics, and writes a per-chunk JSON. The shell driver
    /// scripts/run-exploratory-corpus.sh merges the slices.
    ///
    /// Memory budget: chunk index `i` mod total `M` selects the
    /// chunk's PDFs; one process per chunk keeps SkiaSharp's native
    /// allocations bounded by process exit.
    /// </summary>
    static Command CreateCorpusScanCommand()
    {
        var corpusArg = new Argument<DirectoryInfo>("corpus", "Directory of PDFs to scan");
        var outputOption = new Option<FileInfo>("--output", "Output JSON path") { IsRequired = true };
        var chunkOption = new Option<int>("--chunk", () => 0, "0-based chunk index");
        var totalOption = new Option<int>("--total", () => 1, "Total number of chunks");
        var dpiOption   = new Option<int>("--dpi", () => 150, "Render DPI");
        var diffPctOption = new Option<double>("--max-diff-fraction", () => 0.10,
            "Pass-fail threshold for differing-pixel fraction");
        var maxMaeOption = new Option<double>("--max-mae", () => 32.0,
            "Pass-fail threshold for mean-absolute-error per channel");
        var parallelOption = new Option<int>("--parallel", () => 0,
            "Concurrent PDFs within this chunk. 0 = auto (ProcessorCount/2).");
        var perPdfTimeoutOption = new Option<int>("--pdf-timeout-ms", () => 15_000,
            "Mutool timeout per PDF render. Lower = skip slow PDFs faster.");

        var command = new Command("corpus-scan",
            "Render each PDF with pdfe + mutool, compute pixel-diff, write JSON report")
        {
            corpusArg, outputOption, chunkOption, totalOption,
            dpiOption, diffPctOption, maxMaeOption, parallelOption, perPdfTimeoutOption,
        };

        command.SetHandler(ctx =>
        {
            var corpus = ctx.ParseResult.GetValueForArgument(corpusArg);
            var output = ctx.ParseResult.GetValueForOption(outputOption)!;
            var chunk  = ctx.ParseResult.GetValueForOption(chunkOption);
            var total  = ctx.ParseResult.GetValueForOption(totalOption);
            var dpi    = ctx.ParseResult.GetValueForOption(dpiOption);
            var diffPct = ctx.ParseResult.GetValueForOption(diffPctOption);
            var maxMae  = ctx.ParseResult.GetValueForOption(maxMaeOption);
            var parallel = ctx.ParseResult.GetValueForOption(parallelOption);
            var pdfTimeoutMs = ctx.ParseResult.GetValueForOption(perPdfTimeoutOption);

            if (parallel <= 0) parallel = Math.Max(1, Environment.ProcessorCount / 2);

            if (!corpus.Exists)
            {
                Console.Error.WriteLine($"Corpus not found: {corpus.FullName}");
                Environment.ExitCode = 1; return;
            }
            if (total < 1 || chunk < 0 || chunk >= total)
            {
                Console.Error.WriteLine($"Bad chunk: --chunk {chunk} --total {total}");
                Environment.ExitCode = 1; return;
            }
            if (!Pdfe.Rendering.Differential.MutoolReferenceRenderer.IsAvailable)
            {
                Console.Error.WriteLine("mutool not on PATH — install mupdf-tools");
                Environment.ExitCode = 2; return;
            }

            try
            {
                var ok = RunCorpusScan(corpus.FullName, output.FullName,
                    chunk, total, dpi, diffPct, maxMae, parallel, pdfTimeoutMs);
                Environment.ExitCode = ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Walk the corpus's chunk-slice, render each PDF with pdfe and
    /// mutool, diff, and write a single JSON file. Mirrors
    /// ExploratoryDifferentialTests.GenerateCorpusReportChunk minus
    /// xunit overhead.
    /// </summary>
    internal static bool RunCorpusScan(
        string corpusDir, string outputPath,
        int chunkIndex, int chunkTotal,
        int dpi, double maxDiffFraction, double maxMae,
        int parallel, int pdfTimeoutMs)
    {
        var pdfs = Directory.EnumerateFiles(corpusDir, "*.pdf")
            .OrderBy(p => p)
            .Select((path, idx) => (path, idx))
            .Where(t => t.idx % chunkTotal == chunkIndex)
            .Select(t => t.path)
            .ToList();

        Console.Out.WriteLine(
            $"chunk {chunkIndex + 1}/{chunkTotal}: scanning {pdfs.Count} PDFs in {corpusDir} " +
            $"({parallel}-way parallel, {pdfTimeoutMs}ms mutool timeout)");

        // Use a thread-safe collector. Order in the final JSON is
        // restored by sort-on-write since Parallel.ForEach completion
        // order is non-deterministic.
        var entries = new System.Collections.Concurrent.ConcurrentBag<CorpusScanEntry>();
        long peakBytes = 0;
        int processed = 0;

        var po = new ParallelOptions { MaxDegreeOfParallelism = parallel };
        Parallel.ForEach(pdfs, po, pdf =>
        {
            var rel = Path.GetFileName(pdf);
            // Wall-clock budget covers pdfe's render (no internal
            // timeout) AND mutool's render together. Some pdf.js
            // fixtures have pathological pdfe-side rendering that
            // would otherwise grind chunks for minutes per PDF.
            // Budget is 2× the mutool timeout — generous for normal
            // PDFs, hard cap on the bad ones.
            var wallBudgetMs = pdfTimeoutMs * 2;
            CorpusScanEntry entry;
            var task = Task.Run(() => ScanOne(rel, pdf, dpi, maxDiffFraction, maxMae, pdfTimeoutMs));
            if (task.Wait(wallBudgetMs))
            {
                entry = task.Result;
            }
            else
            {
                entry = new CorpusScanEntry
                {
                    path = rel,
                    status = "TIMEOUT",
                    errorType = "WallClockTimeout",
                    errorMessage = $"Per-PDF budget {wallBudgetMs}ms exceeded — pdfe or mutool got stuck.",
                };
                // Note: the underlying ScanOne task may keep running
                // until the process exits. That's acceptable here:
                // we're not sharing state, and process exit at chunk
                // end reaps the orphan.
            }
            entries.Add(entry);
            int n = System.Threading.Interlocked.Increment(ref processed);

            // Cheap RSS sample.
            var rss = Environment.WorkingSet;
            if (rss > System.Threading.Interlocked.Read(ref peakBytes))
                System.Threading.Interlocked.Exchange(ref peakBytes, rss);

            // Periodic GC to keep SkiaSharp's native memory bounded.
            // Less aggressive than the serial version because parallel
            // workers naturally interleave finalization.
            if (n % 20 == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        });

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var counts = new Dictionary<string, int>();
        foreach (var e in entries)
        {
            counts.TryGetValue(e.status, out var c);
            counts[e.status] = c + 1;
        }

        Console.Out.WriteLine();
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            Console.Out.WriteLine($"  {kv.Value,4}  {kv.Key}");
        Console.Out.WriteLine($"  total processed: {entries.Count}");
        Console.Out.WriteLine($"  peak RSS: {peakBytes / 1024 / 1024} MB");

        // Sort entries by path so parallel completion order doesn't make
        // diffs noisy across runs.
        var sortedEntries = entries.OrderBy(e => e.path).ToList();
        var report = new
        {
            generatedUtc = DateTime.UtcNow.ToString("o"),
            corpus = corpusDir,
            chunkIndex,
            chunkTotal,
            counts,
            total = entries.Count,
            peakRssBytes = peakBytes,
            entries = sortedEntries,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
        Console.Out.WriteLine($"  wrote {outputPath}");
        return true;
    }

    private static CorpusScanEntry ScanOne(
        string relPath, string pdfPath, int dpi,
        double maxDiffFraction, double maxMae, int mutoolTimeoutMs = 30_000)
    {
        var entry = new CorpusScanEntry { path = relPath };

        SkiaSharp.SKBitmap? pdfeBmp = null;
        SkiaSharp.SKBitmap? mutoolBmp = null;
        try
        {
            try
            {
                using var doc = PdfDocument.Open(pdfPath);
                entry.pageCount = doc.PageCount;
                if (doc.PageCount == 0) { entry.status = "EMPTY_DOC"; return entry; }
                var renderer = new SkiaRenderer();
                pdfeBmp = renderer.RenderPage(doc.GetPage(1), new RenderOptions { Dpi = dpi });
            }
            catch (Exception ex)
            {
                entry.status = "PARSE_ERROR";
                entry.errorType = ex.GetType().Name;
                entry.errorMessage = Trunc(ex.Message, 200);
                return entry;
            }

            if (pdfeBmp == null) { entry.status = "RENDER_NULL"; return entry; }

            mutoolBmp = Pdfe.Rendering.Differential.MutoolReferenceRenderer.RenderPage(
                pdfPath, 1, dpi, mutoolTimeoutMs);
            if (mutoolBmp == null) { entry.status = "MUTOOL_REFUSED"; return entry; }

            if (pdfeBmp.Width != mutoolBmp.Width || pdfeBmp.Height != mutoolBmp.Height)
            {
                using var resized = Pdfe.Rendering.Differential.DifferentialMetrics
                    .ResizeMatch(pdfeBmp, mutoolBmp.Width, mutoolBmp.Height);
                pdfeBmp.Dispose();
                pdfeBmp = resized.Copy();
            }

            var report = Pdfe.Rendering.Differential.DifferentialMetrics.Compare(pdfeBmp, mutoolBmp);
            entry.diffFraction = report.DifferingPixelFraction;
            entry.mae = report.MeanAbsoluteError;
            entry.status = (report.DifferingPixelFraction <= maxDiffFraction
                         && report.MeanAbsoluteError <= maxMae)
                ? "PASS" : "DIFF";
        }
        catch (Exception ex)
        {
            entry.status = "COMPARE_ERROR";
            entry.errorType = ex.GetType().Name;
            entry.errorMessage = Trunc(ex.Message, 200);
        }
        finally
        {
            pdfeBmp?.Dispose();
            mutoolBmp?.Dispose();
        }
        return entry;
    }

    private static string Trunc(string s, int n) =>
        s.Length <= n ? s : s.Substring(0, n) + "…";

    internal sealed class CorpusScanEntry
    {
        public string path { get; set; } = "";
        public string status { get; set; } = "UNKNOWN";
        public int pageCount { get; set; }
        public double diffFraction { get; set; }
        public double mae { get; set; }
        public string? errorType { get; set; }
        public string? errorMessage { get; set; }
    }
}
