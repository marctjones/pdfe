using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PdfEditor.Redaction;
using PdfSharp.Fonts;

namespace PdfEditor.Redaction.Cli;

/// <summary>
/// pdfer - TRUE glyph-level PDF redaction CLI tool.
/// Removes text from PDF structure, not just visual covering.
/// </summary>
class Program
{
    private static readonly string Version = "1.0.0";
    private static bool _quiet = false;
    private static bool _jsonOutput = false;

    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return PrintUsage();
        }

        var command = args[0].ToLowerInvariant();

        // Global options check
        if (command == "--version" || command == "-v")
        {
            Console.WriteLine($"pdfer {Version}");
            return 0;
        }

        if (command == "--help" || command == "-h")
        {
            return PrintUsage();
        }

        return command switch
        {
            "redact" => RunRedact(args.Skip(1).ToArray()),
            "verify" => RunVerify(args.Skip(1).ToArray()),
            "search" => RunSearch(args.Skip(1).ToArray()),
            "info" => RunInfo(args.Skip(1).ToArray()),
            "demo" => RunDemo(args.Skip(1).ToArray()),
            _ => PrintUsage($"Unknown command: {command}")
        };
    }

    static int PrintUsage(string? error = null)
    {
        if (error != null)
        {
            Console.Error.WriteLine($"Error: {error}");
            Console.Error.WriteLine();
        }

        Console.WriteLine($@"pdfer {Version} - TRUE glyph-level PDF redaction

USAGE:
    pdfer <command> [options]

COMMANDS:
    redact      Redact text from PDF (removes from structure, not just visual)
    verify      Verify text is NOT extractable from PDF
    search      Search for text in PDF and show locations
    info        Show PDF information and text content
    demo        Run true redaction verification demo

GLOBAL OPTIONS:
    -h, --help      Show this help message
    -v, --version   Show version number

EXAMPLES:
    pdfer redact input.pdf output.pdf ""123-45-6789""
    pdfer redact input.pdf output.pdf ""SSN"" ""DOB"" --case-insensitive
    pdfer redact input.pdf output.pdf --terms-file sensitive.txt
    pdfer redact input.pdf output.pdf --regex ""\d{{3}}-\d{{2}}-\d{{4}}""
    pdfer verify output.pdf ""123-45-6789""
    pdfer search input.pdf ""SSN"" --json

Run 'pdfer <command> --help' for more information on a command.");

        return error != null ? 1 : 0;
    }

    static int RunRedact(string[] args)
    {
        // Parse arguments
        string? inputPath = null;
        string? outputPath = null;
        var terms = new List<string>();
        var regexPatterns = new List<string>();
        string? termsFile = null;
        string? pagesSpec = null;
        var options = new RedactionOptions();
        bool verbose = false;
        bool dryRun = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                return PrintRedactHelp();
            }
            else if (arg == "--case-insensitive" || arg == "-i")
            {
                options.CaseSensitive = false;
            }
            else if (arg == "--no-marker")
            {
                options.DrawVisualMarker = false;
            }
            else if (arg == "--sanitize-metadata" || arg == "-m")
            {
                options.SanitizeMetadata = true;
            }
            else if (arg == "--verbose")
            {
                verbose = true;
            }
            else if (arg == "--quiet" || arg == "-q")
            {
                _quiet = true;
            }
            else if (arg == "--json")
            {
                _jsonOutput = true;
                _quiet = true; // JSON mode implies quiet
            }
            else if (arg == "--dry-run" || arg == "-n")
            {
                dryRun = true;
            }
            else if (arg == "--terms-file" || arg == "-f")
            {
                if (i + 1 >= args.Length)
                {
                    return PrintError("--terms-file requires a file path");
                }
                termsFile = args[++i];
            }
            else if (arg == "--regex" || arg == "-r")
            {
                if (i + 1 >= args.Length)
                {
                    return PrintError("--regex requires a pattern");
                }
                regexPatterns.Add(args[++i]);
            }
            else if (arg == "--pages" || arg == "-p")
            {
                if (i + 1 >= args.Length)
                {
                    return PrintError("--pages requires a page specification");
                }
                pagesSpec = args[++i];
            }
            else if (arg == "--preserve-partial-glyphs")
            {
                options.PreservePartialGlyphsAsImages = true;
            }
            else if (arg == "--partial-glyph-dpi")
            {
                if (i + 1 >= args.Length)
                {
                    return PrintError("--partial-glyph-dpi requires a number");
                }
                if (!int.TryParse(args[++i], out var dpi) || dpi < 72 || dpi > 1200)
                {
                    return PrintError("--partial-glyph-dpi must be between 72 and 1200");
                }
                options.PartialGlyphRasterizationDpi = dpi;
            }
            else if (arg == "--glyph-strategy")
            {
                if (i + 1 >= args.Length)
                {
                    return PrintError("--glyph-strategy requires a value (center, any, full)");
                }
                var strategy = args[++i].ToLowerInvariant();
                options.GlyphRemovalStrategy = strategy switch
                {
                    "center" => GlyphRemovalStrategy.CenterPoint,
                    "any" => GlyphRemovalStrategy.AnyOverlap,
                    "full" => GlyphRemovalStrategy.FullyContained,
                    _ => throw new ArgumentException($"Unknown glyph strategy: {strategy}")
                };
            }
            else if (!arg.StartsWith("-"))
            {
                // Positional argument
                if (inputPath == null)
                    inputPath = arg;
                else if (outputPath == null)
                    outputPath = arg;
                else
                    terms.Add(arg);
            }
            else
            {
                return PrintError($"Unknown option: {arg}");
            }
        }

        // Validate arguments
        if (inputPath == null)
        {
            return PrintError("Input file is required");
        }

        if (outputPath == null && !dryRun)
        {
            return PrintError("Output file is required (or use --dry-run)");
        }

        if (!File.Exists(inputPath))
        {
            return PrintError($"Input file not found: {inputPath}");
        }

        // Load terms from file if specified
        if (termsFile != null)
        {
            if (!File.Exists(termsFile))
            {
                return PrintError($"Terms file not found: {termsFile}");
            }
            var fileTerms = File.ReadAllLines(termsFile)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                .Select(line => line.Trim());
            terms.AddRange(fileTerms);
        }

        // Read from stdin if no terms specified and stdin has data
        if (terms.Count == 0 && regexPatterns.Count == 0 && Console.IsInputRedirected)
        {
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                {
                    terms.Add(line.Trim());
                }
            }
        }

        if (terms.Count == 0 && regexPatterns.Count == 0)
        {
            return PrintError("At least one search term or --regex pattern is required");
        }

        // Parse page specification
        HashSet<int>? targetPages = null;
        if (pagesSpec != null)
        {
            targetPages = ParsePageSpec(pagesSpec);
            if (targetPages == null)
            {
                return PrintError($"Invalid page specification: {pagesSpec}");
            }
        }

        try
        {
            // Expand regex patterns to actual matches
            if (regexPatterns.Count > 0)
            {
                var regexMatches = FindRegexMatches(inputPath, regexPatterns, options.CaseSensitive);
                terms.AddRange(regexMatches);
            }

            if (terms.Count == 0)
            {
                Log("No matches found for regex patterns");
                return OutputResult(new RedactionSummary { Success = true, TotalRedactions = 0 });
            }

            if (dryRun)
            {
                return RunDryRun(inputPath, terms, options);
            }

            // Perform redactions
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                if (verbose)
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Debug);
                }
            });

            var logger = loggerFactory.CreateLogger<TextRedactor>();
            var redactor = new TextRedactor(logger);

            var summary = new RedactionSummary();
            string currentInput = inputPath;
            int tempCounter = 0;

            var tempFiles = new List<string>();

            for (int i = 0; i < terms.Count; i++)
            {
                var term = terms[i];
                var isLast = i == terms.Count - 1;
                var currentOutput = isLast ? outputPath! : Path.Combine(Path.GetDirectoryName(outputPath)!, $".pdfer_temp_{tempCounter++}_{Path.GetFileName(outputPath)}");

                Log($"Redacting: {term}");

                var result = redactor.RedactText(currentInput, currentOutput, term, options);

                if (!result.Success)
                {
                    // Cleanup temp files on error
                    foreach (var tf in tempFiles)
                    {
                        if (File.Exists(tf)) File.Delete(tf);
                    }
                    return PrintError($"Redaction failed for '{term}': {result.ErrorMessage}");
                }

                summary.Terms.Add(new TermResult
                {
                    Term = term,
                    Occurrences = result.RedactionCount,
                    Pages = result.AffectedPages.ToList()
                });
                summary.TotalRedactions += result.RedactionCount;

                // Chain: only update input if we actually created output
                if (result.RedactionCount > 0)
                {
                    if (!isLast)
                    {
                        tempFiles.Add(currentOutput);
                        currentInput = currentOutput;
                    }
                }
                else if (isLast && currentInput != inputPath)
                {
                    // No match on last term, but we have a temp file to copy to final output
                    File.Copy(currentInput, outputPath!, overwrite: true);
                }
                else if (isLast)
                {
                    // No matches at all, just copy input to output
                    File.Copy(inputPath, outputPath!, overwrite: true);
                }
            }

            // Cleanup temp files
            foreach (var tf in tempFiles)
            {
                if (File.Exists(tf)) File.Delete(tf);
            }

            // Verify redaction
            summary.Success = true;
            summary.OutputFile = outputPath;

            foreach (var termResult in summary.Terms)
            {
                var stillPresent = VerifyTextPresent(outputPath!, termResult.Term, options.CaseSensitive);
                termResult.Verified = !stillPresent;
                if (stillPresent)
                {
                    summary.VerificationFailed = true;
                }
            }

            return OutputResult(summary);
        }
        catch (Exception ex)
        {
            return PrintError($"Redaction failed: {ex.Message}");
        }
    }

    static int RunDryRun(string inputPath, List<string> terms, RedactionOptions options)
    {
        Log("DRY RUN - No changes will be made");

        var summary = new RedactionSummary { Success = true, DryRun = true };

        using var document = UglyToad.PdfPig.PdfDocument.Open(inputPath);
        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var term in terms)
        {
            var termResult = new TermResult { Term = term };

            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                var page = document.GetPage(pageNum);
                var pageText = page.Text;
                int count = CountOccurrences(pageText, term, comparison);
                if (count > 0)
                {
                    termResult.Occurrences += count;
                    termResult.Pages.Add(pageNum);
                }
            }

            summary.Terms.Add(termResult);
            summary.TotalRedactions += termResult.Occurrences;
        }

        return OutputResult(summary);
    }

    static int PrintRedactHelp()
    {
        Console.WriteLine(@"pdfer redact - Redact text from PDF

USAGE:
    pdfer redact <input.pdf> <output.pdf> [terms...] [options]

ARGUMENTS:
    <input.pdf>     Input PDF file
    <output.pdf>    Output PDF file (redacted)
    [terms...]      Text strings to redact (multiple allowed)

OPTIONS:
    -i, --case-insensitive    Match text regardless of case
    -f, --terms-file <file>   Read terms from file (one per line)
    -r, --regex <pattern>     Redact text matching regex pattern
    -p, --pages <spec>        Only redact on specific pages (e.g., 1-3,5,7-9)
    -n, --dry-run             Show what would be redacted without modifying
    -m, --sanitize-metadata   Remove document metadata
        --no-marker           Don't draw visual black box over redacted area
    -q, --quiet               Suppress non-error output
        --json                Output results as JSON
        --verbose             Show detailed processing information
    -h, --help                Show this help

PARTIAL GLYPH OPTIONS (experimental):
        --preserve-partial-glyphs     Preserve visible portions of partially overlapping
                                      glyphs as rasterized images (default: off)
        --partial-glyph-dpi <dpi>     DPI for rasterizing partial glyphs (72-1200, default: 300)
        --glyph-strategy <strategy>   How to decide if glyph should be removed:
                                      - center: Remove if center point in area (default)
                                      - any: Remove if any part overlaps area
                                      - full: Remove only if fully inside area

EXAMPLES:
    # Redact a single term
    pdfer redact input.pdf output.pdf ""123-45-6789""

    # Redact multiple terms
    pdfer redact input.pdf output.pdf ""SSN"" ""John Doe"" ""555-1234""

    # Case-insensitive redaction
    pdfer redact input.pdf output.pdf ""confidential"" -i

    # Redact from a list file
    pdfer redact input.pdf output.pdf -f sensitive-terms.txt

    # Redact SSN pattern with regex
    pdfer redact input.pdf output.pdf -r ""\d{3}-\d{2}-\d{4}""

    # Dry run to preview
    pdfer redact input.pdf output.pdf ""SECRET"" --dry-run

    # Pipe terms from another command
    cat terms.txt | pdfer redact input.pdf output.pdf

    # JSON output for scripting
    pdfer redact input.pdf output.pdf ""SSN"" --json

EXIT CODES:
    0    Success
    1    Error (invalid arguments, file not found, etc.)
    2    Verification failed (text still extractable after redaction)");

        return 0;
    }

    static int RunVerify(string[] args)
    {
        string? pdfPath = null;
        var terms = new List<string>();
        bool caseSensitive = true;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                return PrintVerifyHelp();
            }
            else if (arg == "--case-insensitive" || arg == "-i")
            {
                caseSensitive = false;
            }
            else if (arg == "--quiet" || arg == "-q")
            {
                _quiet = true;
            }
            else if (arg == "--json")
            {
                _jsonOutput = true;
                _quiet = true;
            }
            else if (!arg.StartsWith("-"))
            {
                if (pdfPath == null)
                    pdfPath = arg;
                else
                    terms.Add(arg);
            }
            else
            {
                return PrintError($"Unknown option: {arg}");
            }
        }

        if (pdfPath == null)
        {
            return PrintError("PDF file is required");
        }

        if (!File.Exists(pdfPath))
        {
            return PrintError($"PDF file not found: {pdfPath}");
        }

        if (terms.Count == 0)
        {
            return PrintError("At least one search term is required");
        }

        try
        {
            var results = new List<VerifyResult>();
            bool allPassed = true;

            foreach (var term in terms)
            {
                var found = VerifyTextPresent(pdfPath, term, caseSensitive);
                results.Add(new VerifyResult
                {
                    Term = term,
                    Found = found,
                    Passed = !found
                });

                if (found)
                {
                    allPassed = false;
                }
            }

            if (_jsonOutput)
            {
                var output = new { success = allPassed, results };
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                foreach (var result in results)
                {
                    if (result.Passed)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"PASS: '{result.Term}' not found in PDF");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"FAIL: '{result.Term}' still extractable from PDF");
                    }
                    Console.ResetColor();
                }
            }

            return allPassed ? 0 : 2;
        }
        catch (Exception ex)
        {
            return PrintError($"Verification failed: {ex.Message}");
        }
    }

    static int PrintVerifyHelp()
    {
        Console.WriteLine(@"pdfer verify - Verify text is NOT extractable from PDF

USAGE:
    pdfer verify <file.pdf> <term> [terms...] [options]

ARGUMENTS:
    <file.pdf>      PDF file to verify
    <term>          Text that should NOT be present (multiple allowed)

OPTIONS:
    -i, --case-insensitive    Match text regardless of case
    -q, --quiet               Suppress output (exit code only)
        --json                Output results as JSON
    -h, --help                Show this help

EXIT CODES:
    0    All terms verified (none found in PDF)
    1    Error
    2    Verification failed (text still present)

EXAMPLES:
    pdfer verify redacted.pdf ""123-45-6789""
    pdfer verify redacted.pdf ""SSN"" ""DOB"" ""Address""
    pdfer verify redacted.pdf ""secret"" -i");

        return 0;
    }

    static int RunSearch(string[] args)
    {
        string? pdfPath = null;
        string? searchTerm = null;
        string? regexPattern = null;
        bool caseSensitive = true;
        bool showContext = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                return PrintSearchHelp();
            }
            else if (arg == "--case-insensitive" || arg == "-i")
            {
                caseSensitive = false;
            }
            else if (arg == "--context" || arg == "-c")
            {
                showContext = true;
            }
            else if (arg == "--regex" || arg == "-r")
            {
                if (i + 1 < args.Length)
                {
                    regexPattern = args[++i];
                }
                else
                {
                    return PrintError("--regex requires a pattern argument");
                }
            }
            else if (arg == "--quiet" || arg == "-q")
            {
                _quiet = true;
            }
            else if (arg == "--json")
            {
                _jsonOutput = true;
                _quiet = true;
            }
            else if (!arg.StartsWith("-"))
            {
                if (pdfPath == null)
                    pdfPath = arg;
                else if (searchTerm == null)
                    searchTerm = arg;
            }
            else
            {
                return PrintError($"Unknown option: {arg}");
            }
        }

        if (pdfPath == null)
        {
            return PrintError("PDF file is required");
        }

        if (!File.Exists(pdfPath))
        {
            return PrintError($"PDF file not found: {pdfPath}");
        }

        if (searchTerm == null && regexPattern == null)
        {
            return PrintError("Search term or --regex pattern is required");
        }

        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var results = new List<SearchResult>();
            int totalOccurrences = 0;
            string displayTerm = regexPattern != null ? $"/{regexPattern}/" : searchTerm!;

            // Compile regex if specified
            Regex? regex = null;
            if (regexPattern != null)
            {
                var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                try
                {
                    regex = new Regex(regexPattern, regexOptions);
                }
                catch (ArgumentException ex)
                {
                    return PrintError($"Invalid regex pattern: {ex.Message}");
                }
            }

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                var page = document.GetPage(pageNum);
                var letters = page.Letters.ToList();
                var fullText = string.Concat(letters.Select(l => l.Value));

                if (regex != null)
                {
                    // Regex search
                    var matches = regex.Matches(fullText);
                    foreach (Match match in matches)
                    {
                        totalOccurrences++;
                        var result = new SearchResult
                        {
                            Page = pageNum,
                            Index = match.Index,
                            MatchedText = match.Value
                        };

                        // Get position info
                        if (match.Index < letters.Count && match.Index + match.Length <= letters.Count)
                        {
                            var firstLetter = letters[match.Index];
                            var lastLetter = letters[Math.Min(match.Index + match.Length - 1, letters.Count - 1)];
                            result.X = firstLetter.GlyphRectangle.Left;
                            result.Y = firstLetter.GlyphRectangle.Bottom;
                            result.Width = lastLetter.GlyphRectangle.Right - firstLetter.GlyphRectangle.Left;
                            result.Height = Math.Max(firstLetter.GlyphRectangle.Height, lastLetter.GlyphRectangle.Height);
                        }

                        // Get context
                        if (showContext)
                        {
                            int contextStart = Math.Max(0, match.Index - 20);
                            int contextEnd = Math.Min(fullText.Length, match.Index + match.Length + 20);
                            result.Context = fullText.Substring(contextStart, contextEnd - contextStart);
                        }

                        results.Add(result);
                    }
                }
                else
                {
                    // Literal string search
                    int index = 0;
                    while ((index = fullText.IndexOf(searchTerm!, index, comparison)) >= 0)
                    {
                        totalOccurrences++;

                        var result = new SearchResult
                        {
                            Page = pageNum,
                            Index = index,
                            MatchedText = searchTerm
                        };

                        // Get position info
                        if (index < letters.Count && index + searchTerm!.Length <= letters.Count)
                        {
                            var firstLetter = letters[index];
                            var lastLetter = letters[Math.Min(index + searchTerm.Length - 1, letters.Count - 1)];
                            result.X = firstLetter.GlyphRectangle.Left;
                            result.Y = firstLetter.GlyphRectangle.Bottom;
                            result.Width = lastLetter.GlyphRectangle.Right - firstLetter.GlyphRectangle.Left;
                            result.Height = Math.Max(firstLetter.GlyphRectangle.Height, lastLetter.GlyphRectangle.Height);
                        }

                        // Get context
                        if (showContext)
                        {
                            int contextStart = Math.Max(0, index - 20);
                            int contextEnd = Math.Min(fullText.Length, index + searchTerm!.Length + 20);
                            result.Context = fullText.Substring(contextStart, contextEnd - contextStart);
                        }

                        results.Add(result);
                        index++;
                    }
                }
            }

            if (_jsonOutput)
            {
                var output = new
                {
                    file = pdfPath,
                    term = searchTerm,
                    regex = regexPattern,
                    total = totalOccurrences,
                    results
                };
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                if (totalOccurrences == 0)
                {
                    Console.WriteLine($"No occurrences of '{displayTerm}' found");
                }
                else
                {
                    Console.WriteLine($"Found {totalOccurrences} occurrence(s) of '{displayTerm}':");
                    foreach (var result in results)
                    {
                        if (regexPattern != null && result.MatchedText != null)
                        {
                            Console.WriteLine($"  Page {result.Page}: \"{result.MatchedText}\" at ({result.X:F1}, {result.Y:F1})");
                        }
                        else
                        {
                            Console.WriteLine($"  Page {result.Page}: position ({result.X:F1}, {result.Y:F1})");
                        }
                        if (showContext && result.Context != null)
                        {
                            Console.WriteLine($"    ...{result.Context}...");
                        }
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return PrintError($"Search failed: {ex.Message}");
        }
    }

    static int PrintSearchHelp()
    {
        Console.WriteLine(@"pdfer search - Search for text in PDF

USAGE:
    pdfer search <file.pdf> <term> [options]
    pdfer search <file.pdf> -r <regex> [options]

ARGUMENTS:
    <file.pdf>      PDF file to search
    <term>          Text to search for (literal match)

OPTIONS:
    -r, --regex <pattern>     Search using regex pattern instead of literal text
    -i, --case-insensitive    Match text regardless of case
    -c, --context             Show surrounding text context
        --json                Output results as JSON
    -h, --help                Show this help

EXAMPLES:
    # Literal text search
    pdfer search document.pdf ""SSN""
    pdfer search document.pdf ""confidential"" -i --context

    # Regex search - find SSN patterns
    pdfer search document.pdf -r ""\d{3}-\d{2}-\d{4}""

    # Regex search - find email addresses
    pdfer search document.pdf -r ""[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}""

    # Regex search - find phone numbers
    pdfer search document.pdf -r ""\(\d{3}\)\s*\d{3}-\d{4}""

    # JSON output for scripting
    pdfer search document.pdf -r ""\d{3}-\d{2}-\d{4}"" --json");

        return 0;
    }

    static int RunInfo(string[] args)
    {
        string? pdfPath = null;
        bool showText = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                Console.WriteLine(@"pdfer info - Show PDF information

USAGE:
    pdfer info <file.pdf> [options]

OPTIONS:
    --text      Extract and show all text content
    --json      Output as JSON
    -h, --help  Show this help");
                return 0;
            }
            else if (arg == "--text")
            {
                showText = true;
            }
            else if (arg == "--json")
            {
                _jsonOutput = true;
            }
            else if (!arg.StartsWith("-"))
            {
                pdfPath = arg;
            }
        }

        if (pdfPath == null)
        {
            return PrintError("PDF file is required");
        }

        if (!File.Exists(pdfPath))
        {
            return PrintError($"PDF file not found: {pdfPath}");
        }

        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var info = new
            {
                file = pdfPath,
                pages = document.NumberOfPages,
                version = document.Version.ToString(),
                text = showText ? document.GetPages().Select(p => new { page = p.Number, content = p.Text }).ToList() : null
            };

            if (_jsonOutput)
            {
                Console.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"File: {pdfPath}");
                Console.WriteLine($"Pages: {document.NumberOfPages}");
                Console.WriteLine($"PDF Version: {document.Version}");

                if (showText)
                {
                    Console.WriteLine("\nText Content:");
                    foreach (var page in document.GetPages())
                    {
                        Console.WriteLine($"\n--- Page {page.Number} ---");
                        Console.WriteLine(page.Text);
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return PrintError($"Failed to read PDF: {ex.Message}");
        }
    }

    static int RunDemo(string[] args)
    {
        string? inputPdf = null;
        string? outputDir = null;
        bool createTest = false;
        string? redactText = null;
        string? redactArea = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--help" || arg == "-h")
            {
                return PrintDemoHelp();
            }
            else if (arg == "--create-test" || arg == "-c")
            {
                createTest = true;
            }
            else if (arg == "--output-dir" || arg == "-o")
            {
                if (i + 1 < args.Length)
                    outputDir = args[++i];
                else
                    return PrintError("--output-dir requires a path");
            }
            else if (arg == "--redact-text" || arg == "-t")
            {
                if (i + 1 < args.Length)
                    redactText = args[++i];
                else
                    return PrintError("--redact-text requires a term");
            }
            else if (arg == "--redact-area" || arg == "-a")
            {
                if (i + 1 < args.Length)
                    redactArea = args[++i];
                else
                    return PrintError("--redact-area requires coordinates (x1,y1,x2,y2)");
            }
            else if (!arg.StartsWith("-"))
            {
                inputPdf = arg;
            }
        }

        // Set output directory
        if (outputDir == null)
        {
            outputDir = Path.Combine(Path.GetTempPath(), $"pdfer_demo_{DateTime.Now:yyyyMMdd_HHmmss}");
        }
        Directory.CreateDirectory(outputDir);

        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                TRUE REDACTION VERIFICATION DEMO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("This demo proves that pdfer performs TRUE redaction - removing content");
        Console.WriteLine("from the PDF structure, not just drawing black boxes over it.");
        Console.WriteLine();

        string beforePdf;
        string afterPdf = Path.Combine(outputDir, "after_redaction.pdf");
        var textsToRedact = new List<string>();
        PdfRectangle? areaToRedact = null;

        if (createTest || inputPdf == null)
        {
            // Create a test PDF with known content
            beforePdf = Path.Combine(outputDir, "before_redaction.pdf");
            Console.WriteLine("📄 Creating test PDF with known content...");
            CreateDemoTestPdf(beforePdf);
            textsToRedact.AddRange(new[] { "SECRET-TEXT-A", "PARTIAL-TEXT" });
            areaToRedact = new PdfRectangle(200, 100, 400, 750);
            Console.WriteLine($"   Created: {beforePdf}");
        }
        else
        {
            beforePdf = inputPdf;
            if (!File.Exists(beforePdf))
            {
                return PrintError($"Input file not found: {beforePdf}");
            }
            Console.WriteLine($"📄 Using input PDF: {beforePdf}");

            if (redactText != null)
            {
                textsToRedact.Add(redactText);
            }

            if (redactArea != null)
            {
                var parts = redactArea.Split(',');
                if (parts.Length == 4 &&
                    double.TryParse(parts[0], out var x1) &&
                    double.TryParse(parts[1], out var y1) &&
                    double.TryParse(parts[2], out var x2) &&
                    double.TryParse(parts[3], out var y2))
                {
                    areaToRedact = new PdfRectangle(x1, y1, x2, y2);
                }
                else
                {
                    return PrintError("Invalid --redact-area format. Use: x1,y1,x2,y2");
                }
            }

            if (textsToRedact.Count == 0 && areaToRedact == null)
            {
                return PrintError("Specify --redact-text or --redact-area for custom PDF");
            }
        }

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                         STEP 1: BEFORE STATE");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Extract text BEFORE
        Console.WriteLine("📝 Extracting text from BEFORE PDF using PdfPig...");
        var textBefore = ExtractAllText(beforePdf);
        Console.WriteLine();
        Console.WriteLine("Text content (first 500 chars):");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine(textBefore.Length > 500 ? textBefore.Substring(0, 500) + "..." : textBefore);
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        // Check for texts to redact
        Console.WriteLine();
        Console.WriteLine("🔍 Checking for text to be redacted:");
        foreach (var term in textsToRedact)
        {
            var found = textBefore.Contains(term);
            Console.WriteLine($"   '{term}': {(found ? "✓ FOUND" : "✗ NOT FOUND")}");
        }

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                       STEP 2: PERFORMING REDACTION");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Perform redaction
        var redactor = new TextRedactor();
        bool success = true;

        if (textsToRedact.Count > 0)
        {
            Console.WriteLine($"🔒 Redacting text terms: {string.Join(", ", textsToRedact.Select(t => $"'{t}'"))}");

            string currentInput = beforePdf;
            for (int i = 0; i < textsToRedact.Count; i++)
            {
                var term = textsToRedact[i];
                var currentOutput = i == textsToRedact.Count - 1 && areaToRedact == null
                    ? afterPdf
                    : Path.Combine(outputDir, $"temp_{i}.pdf");

                var result = redactor.RedactText(currentInput, currentOutput, term);
                Console.WriteLine($"   '{term}': {result.RedactionCount} occurrences redacted");

                if (!result.Success)
                {
                    Console.WriteLine($"   ERROR: {result.ErrorMessage}");
                    success = false;
                    break;
                }

                currentInput = currentOutput;
            }

            if (areaToRedact != null && success)
            {
                // Also do area redaction
                var location = new RedactionLocation
                {
                    PageNumber = 1,
                    BoundingBox = areaToRedact.Value
                };
                Console.WriteLine($"🔒 Redacting area: ({areaToRedact.Value.Left},{areaToRedact.Value.Bottom}) to ({areaToRedact.Value.Right},{areaToRedact.Value.Top})");
                var result = redactor.RedactLocations(currentInput, afterPdf, new[] { location });
                success = result.Success;
            }
        }
        else if (areaToRedact != null)
        {
            var location = new RedactionLocation
            {
                PageNumber = 1,
                BoundingBox = areaToRedact.Value
            };
            Console.WriteLine($"🔒 Redacting area: ({areaToRedact.Value.Left},{areaToRedact.Value.Bottom}) to ({areaToRedact.Value.Right},{areaToRedact.Value.Top})");
            var result = redactor.RedactLocations(beforePdf, afterPdf, new[] { location });
            success = result.Success;
        }

        if (!success)
        {
            return PrintError("Redaction failed");
        }

        Console.WriteLine($"   Output saved to: {afterPdf}");

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                         STEP 3: AFTER STATE");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Extract text AFTER
        Console.WriteLine("📝 Extracting text from AFTER PDF using PdfPig...");
        var textAfter = ExtractAllText(afterPdf);
        Console.WriteLine();
        Console.WriteLine("Text content (first 500 chars):");
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");
        Console.WriteLine(textAfter.Length > 500 ? textAfter.Substring(0, 500) + "..." : textAfter);
        Console.WriteLine("─────────────────────────────────────────────────────────────────────");

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                     STEP 4: VERIFICATION REPORT");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        bool allPassed = true;
        Console.WriteLine("📋 TEXT VERIFICATION (proves true removal, not just visual covering):");
        Console.WriteLine();

        foreach (var term in textsToRedact)
        {
            var stillPresent = textAfter.Contains(term);
            var passed = !stillPresent;
            allPassed &= passed;

            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"   ✓ PASS: '{term}' is NOT extractable");
                Console.WriteLine($"           → Text was REMOVED from PDF structure");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   ✗ FAIL: '{term}' is STILL extractable");
                Console.WriteLine($"           → Redaction may have failed");
            }
            Console.ResetColor();
        }

        // Also verify with pdftotext if available
        Console.WriteLine();
        Console.WriteLine("📋 CROSS-VERIFICATION with pdftotext:");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pdftotext",
                Arguments = $"\"{afterPdf}\" -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                var pdftotextOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                foreach (var term in textsToRedact)
                {
                    var found = pdftotextOutput.Contains(term);
                    if (!found)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"   ✓ pdftotext confirms: '{term}' not found");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"   ✗ pdftotext found: '{term}'");
                        allPassed = false;
                    }
                    Console.ResetColor();
                }
            }
        }
        catch
        {
            Console.WriteLine("   (pdftotext not available for cross-verification)");
        }

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("                          FINAL VERDICT");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════");
        Console.WriteLine();

        if (allPassed)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ████████████████████████████████████████████████████████████████");
            Console.WriteLine("   █                                                              █");
            Console.WriteLine("   █   ✓ TRUE REDACTION VERIFIED                                  █");
            Console.WriteLine("   █                                                              █");
            Console.WriteLine("   █   Content was REMOVED from PDF structure.                    █");
            Console.WriteLine("   █   This is SECURE redaction - not just visual covering.       █");
            Console.WriteLine("   █                                                              █");
            Console.WriteLine("   ████████████████████████████████████████████████████████████████");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("   ████████████████████████████████████████████████████████████████");
            Console.WriteLine("   █                                                              █");
            Console.WriteLine("   █   ✗ VERIFICATION FAILED                                      █");
            Console.WriteLine("   █                                                              █");
            Console.WriteLine("   █   Some content may still be extractable.                     █");
            Console.WriteLine("   █   Review the output and investigate.                         █");
            Console.WriteLine("   █                                                              █");
            Console.WriteLine("   ████████████████████████████████████████████████████████████████");
        }
        Console.ResetColor();

        Console.WriteLine();
        Console.WriteLine("📁 Files created:");
        Console.WriteLine($"   Before: {beforePdf}");
        Console.WriteLine($"   After:  {afterPdf}");
        Console.WriteLine();
        Console.WriteLine("🖼️  View with:");
        Console.WriteLine($"   pdftoppm -png \"{beforePdf}\" /tmp/before");
        Console.WriteLine($"   pdftoppm -png \"{afterPdf}\" /tmp/after");
        Console.WriteLine($"   timg --grid=2 -g 80x40 /tmp/before-1.png /tmp/after-1.png");
        Console.WriteLine();

        return allPassed ? 0 : 2;
    }

    static int PrintDemoHelp()
    {
        Console.WriteLine(@"pdfer demo - Run true redaction verification demo

USAGE:
    pdfer demo [options]              Create test PDF and demonstrate redaction
    pdfer demo <input.pdf> [options]  Use custom PDF for demo

OPTIONS:
    -c, --create-test              Force creation of test PDF (even if input provided)
    -o, --output-dir <path>        Output directory for demo files
    -t, --redact-text <term>       Text to redact (for custom PDF)
    -a, --redact-area <coords>     Area to redact: x1,y1,x2,y2 (for custom PDF)
    -h, --help                     Show this help

EXAMPLES:
    # Run demo with auto-generated test PDF
    pdfer demo

    # Run demo with custom PDF and text redaction
    pdfer demo document.pdf --redact-text ""SECRET""

    # Run demo with area-based redaction
    pdfer demo document.pdf --redact-area 100,400,300,600

    # Specify output directory
    pdfer demo --output-dir ./demo_output

WHAT THIS DEMO PROVES:
    The demo shows that pdfer performs TRUE glyph-level redaction:
    1. Text is REMOVED from the PDF content stream
    2. Text extraction tools (PdfPig, pdftotext) cannot find redacted text
    3. This is fundamentally different from just drawing black boxes

    Fake redaction (black box only) would still allow extraction of the
    underlying text. This demo proves that doesn't happen with pdfer.");

        return 0;
    }

    static void CreateDemoTestPdf(string outputPath)
    {
        // Initialize font resolver for cross-platform support
        if (GlobalFontSettings.FontResolver == null)
        {
            GlobalFontSettings.FontResolver = new CustomFontResolver();
        }

        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(612);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(792);

        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var titleFont = new PdfSharp.Drawing.XFont("Helvetica", 14, PdfSharp.Drawing.XFontStyleEx.Bold);
        var labelFont = new PdfSharp.Drawing.XFont("Helvetica", 8);
        var textFont = new PdfSharp.Drawing.XFont("Helvetica", 11);

        // Title
        gfx.DrawString("TRUE REDACTION VERIFICATION TEST PDF", titleFont,
            PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(150, 30));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 1: TEXT REDACTION TESTS (y=50-120)
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("TEXT TESTS:", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 55));

        // Text inside zone (should be removed)
        gfx.DrawString("Inside→", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(10, 75));
        gfx.DrawString("SECRET-TEXT-A", textFont, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(220, 75));

        // Text outside zone (should remain)
        gfx.DrawString("Outside→", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(10, 100));
        gfx.DrawString("KEEP-TEXT-B", textFont, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(60, 100));

        // Text straddling zone
        gfx.DrawString("Partial→", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(420, 75));
        gfx.DrawString("VISIBLE", textFont, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(470, 75));
        gfx.DrawString("PARTIAL-TEXT", textFont, PdfSharp.Drawing.XBrushes.Black,
            new PdfSharp.Drawing.XPoint(220, 100));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 1.5: ROTATED TEXT TESTS (y=115-130)
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("ROTATED TEXT:", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 115));

        // Rotated text INSIDE zone (should be removed) - 45° at x=250
        DrawRotatedText(gfx, "ROT45-SECRET", textFont, 250, 125, 45, PdfSharp.Drawing.XColors.DarkRed);

        // Rotated text OUTSIDE zone (should remain) - 90° at x=450 (right of zone)
        DrawRotatedText(gfx, "ROT90-KEEP", textFont, 480, 90, 90, PdfSharp.Drawing.XColors.DarkGreen);

        // Rotated text OUTSIDE zone (should remain) - 270° at x=150 (left of zone)
        DrawRotatedText(gfx, "ROT270-KEEP", textFont, 150, 120, 270, PdfSharp.Drawing.XColors.DarkBlue);

        // Rotated text partially in zone - 30° starting outside, extending into zone
        DrawRotatedText(gfx, "ROT30-PARTIAL", textFont, 180, 140, 30, PdfSharp.Drawing.XColors.DarkOrange);

        // Upside down text INSIDE zone (should be removed) - 180°
        DrawRotatedText(gfx, "ROT180-SECRET", textFont, 300, 140, 180, PdfSharp.Drawing.XColors.Purple);

        // ═══════════════════════════════════════════════════════════════════
        // ROW 2: BASIC SHAPES - INSIDE ZONE (y=150-230) - Should be REMOVED
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("SHAPES INSIDE (removed):", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 155));

        // Rectangle (inside)
        gfx.DrawRectangle(new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColors.Blue),
            220, 170, 50, 50);
        gfx.DrawString("Rect", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(230, 225));

        // Circle/Ellipse (inside)
        gfx.DrawEllipse(new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColors.Red),
            290, 170, 50, 50);
        gfx.DrawString("Circle", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(298, 225));

        // Triangle (inside)
        DrawTriangle(gfx, 380, 220, 25, PdfSharp.Drawing.XColors.Green);
        gfx.DrawString("Tri", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(372, 225));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 3: BASIC SHAPES - OUTSIDE ZONE (y=220-320) - Should REMAIN
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("SHAPES OUTSIDE (remain):", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 225));

        // Rectangle (outside)
        gfx.DrawRectangle(new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColors.Orange),
            30, 240, 50, 50);
        gfx.DrawString("Rect", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(40, 295));

        // Circle (outside)
        gfx.DrawEllipse(new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColors.Purple),
            100, 240, 50, 50);
        gfx.DrawString("Circle", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(108, 295));

        // Triangle (outside - right side)
        DrawTriangle(gfx, 500, 290, 25, PdfSharp.Drawing.XColors.Teal);
        gfx.DrawString("Tri", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(492, 295));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 4: COMPLEX SHAPES - INSIDE ZONE (y=310-410) - Should be REMOVED
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("COMPLEX INSIDE (removed):", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 315));

        // Pentagon (inside)
        DrawRegularPolygon(gfx, 245, 355, 25, 5, PdfSharp.Drawing.XColors.Magenta);
        gfx.DrawString("Pent", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(232, 390));

        // Hexagon (inside)
        DrawRegularPolygon(gfx, 315, 355, 25, 6, PdfSharp.Drawing.XColors.Cyan);
        gfx.DrawString("Hex", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(305, 390));

        // Star (inside)
        DrawStar(gfx, 385, 355, 25, 15, 5, PdfSharp.Drawing.XColors.Gold);
        gfx.DrawString("Star", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(375, 390));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 5: COMPLEX SHAPES - OUTSIDE ZONE (y=400-500) - Should REMAIN
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("COMPLEX OUTSIDE (remain):", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 405));

        // Pentagon (outside)
        DrawRegularPolygon(gfx, 55, 445, 25, 5, PdfSharp.Drawing.XColors.Coral);
        gfx.DrawString("Pent", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(42, 480));

        // Hexagon (outside)
        DrawRegularPolygon(gfx, 125, 445, 25, 6, PdfSharp.Drawing.XColors.LimeGreen);
        gfx.DrawString("Hex", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(115, 480));

        // Star (outside - right side)
        DrawStar(gfx, 500, 445, 25, 15, 5, PdfSharp.Drawing.XColors.DeepPink);
        gfx.DrawString("Star", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(490, 480));

        // Octagon (outside)
        DrawRegularPolygon(gfx, 560, 355, 20, 8, PdfSharp.Drawing.XColors.SlateBlue);
        gfx.DrawString("Oct", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(552, 385));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 6: PARTIAL OVERLAP SHAPES (y=500-600) - Should be CLIPPED
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("PARTIAL OVERLAP (clipped):", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 505));

        // Wide rectangle straddling zone boundary (left half remains)
        gfx.DrawRectangle(new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColors.ForestGreen),
            100, 520, 200, 50);
        gfx.DrawString("Rect (left half remains)", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(100, 575));

        // Ellipse straddling zone (right portion removed)
        gfx.DrawEllipse(new PdfSharp.Drawing.XSolidBrush(PdfSharp.Drawing.XColors.Crimson),
            150, 590, 100, 60);
        gfx.DrawString("Ellipse (right clipped)", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(150, 655));

        // Triangle straddling zone
        DrawTriangle(gfx, 250, 680, 40, PdfSharp.Drawing.XColors.DarkOrange);
        gfx.DrawString("Tri (partial)", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(210, 690));

        // Star straddling zone
        DrawStar(gfx, 350, 620, 30, 18, 5, PdfSharp.Drawing.XColors.DodgerBlue);
        gfx.DrawString("Star (partial)", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(410, 630));

        // ═══════════════════════════════════════════════════════════════════
        // ROW 7: IRREGULAR SHAPES (y=700-770)
        // ═══════════════════════════════════════════════════════════════════
        gfx.DrawString("IRREGULAR SHAPES:", labelFont, PdfSharp.Drawing.XBrushes.DarkBlue,
            new PdfSharp.Drawing.XPoint(10, 705));

        // Arrow shape (outside)
        DrawArrow(gfx, 30, 720, 60, 30, PdfSharp.Drawing.XColors.Navy);
        gfx.DrawString("Arrow", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(40, 760));

        // Diamond (inside - should be removed)
        DrawDiamond(gfx, 280, 740, 30, 40, PdfSharp.Drawing.XColors.Maroon);
        gfx.DrawString("Diamond", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(260, 765));

        // Cross/Plus (outside)
        DrawCross(gfx, 500, 740, 25, 8, PdfSharp.Drawing.XColors.DarkGreen);
        gfx.DrawString("Cross", labelFont, PdfSharp.Drawing.XBrushes.Gray,
            new PdfSharp.Drawing.XPoint(490, 765));

        // ═══════════════════════════════════════════════════════════════════
        // REDACTION ZONE INDICATOR
        // ═══════════════════════════════════════════════════════════════════
        var dashedPen = new PdfSharp.Drawing.XPen(PdfSharp.Drawing.XColors.Red, 2)
        {
            DashStyle = PdfSharp.Drawing.XDashStyle.Dash
        };
        gfx.DrawRectangle(dashedPen, 200, 50, 200, 730);

        // Zone labels
        gfx.DrawString("REDACTION", labelFont, PdfSharp.Drawing.XBrushes.Red,
            new PdfSharp.Drawing.XPoint(265, 45));
        gfx.DrawString("ZONE", labelFont, PdfSharp.Drawing.XBrushes.Red,
            new PdfSharp.Drawing.XPoint(280, 785));
        gfx.DrawString("x=200-400", labelFont, PdfSharp.Drawing.XBrushes.Red,
            new PdfSharp.Drawing.XPoint(268, 795));

        document.Save(outputPath);
    }

    // Helper: Draw rotated text
    static void DrawRotatedText(PdfSharp.Drawing.XGraphics gfx, string text, PdfSharp.Drawing.XFont font, double x, double y, double angleDegrees, PdfSharp.Drawing.XColor color)
    {
        // Save current graphics state
        var state = gfx.Save();

        // Move to text position
        gfx.TranslateTransform(x, y);

        // Rotate around that point
        gfx.RotateTransform(angleDegrees);

        // Draw text at origin (now rotated)
        gfx.DrawString(text, font, new PdfSharp.Drawing.XSolidBrush(color), 0, 0);

        // Restore graphics state
        gfx.Restore(state);
    }

    // Helper: Draw a triangle
    static void DrawTriangle(PdfSharp.Drawing.XGraphics gfx, double centerX, double centerY, double radius, PdfSharp.Drawing.XColor color)
    {
        var points = new PdfSharp.Drawing.XPoint[3];
        for (int i = 0; i < 3; i++)
        {
            double angle = (i * 2 * Math.PI / 3) - Math.PI / 2; // Start from top
            points[i] = new PdfSharp.Drawing.XPoint(
                centerX + radius * Math.Cos(angle),
                centerY + radius * Math.Sin(angle));
        }
        gfx.DrawPolygon(new PdfSharp.Drawing.XSolidBrush(color), points, PdfSharp.Drawing.XFillMode.Winding);
    }

    // Helper: Draw a regular polygon (pentagon, hexagon, octagon, etc.)
    static void DrawRegularPolygon(PdfSharp.Drawing.XGraphics gfx, double centerX, double centerY, double radius, int sides, PdfSharp.Drawing.XColor color)
    {
        var points = new PdfSharp.Drawing.XPoint[sides];
        for (int i = 0; i < sides; i++)
        {
            double angle = (i * 2 * Math.PI / sides) - Math.PI / 2;
            points[i] = new PdfSharp.Drawing.XPoint(
                centerX + radius * Math.Cos(angle),
                centerY + radius * Math.Sin(angle));
        }
        gfx.DrawPolygon(new PdfSharp.Drawing.XSolidBrush(color), points, PdfSharp.Drawing.XFillMode.Winding);
    }

    // Helper: Draw a star
    static void DrawStar(PdfSharp.Drawing.XGraphics gfx, double centerX, double centerY, double outerRadius, double innerRadius, int points, PdfSharp.Drawing.XColor color)
    {
        var starPoints = new PdfSharp.Drawing.XPoint[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            double radius = (i % 2 == 0) ? outerRadius : innerRadius;
            double angle = (i * Math.PI / points) - Math.PI / 2;
            starPoints[i] = new PdfSharp.Drawing.XPoint(
                centerX + radius * Math.Cos(angle),
                centerY + radius * Math.Sin(angle));
        }
        gfx.DrawPolygon(new PdfSharp.Drawing.XSolidBrush(color), starPoints, PdfSharp.Drawing.XFillMode.Winding);
    }

    // Helper: Draw an arrow pointing right
    static void DrawArrow(PdfSharp.Drawing.XGraphics gfx, double x, double y, double width, double height, PdfSharp.Drawing.XColor color)
    {
        var points = new PdfSharp.Drawing.XPoint[]
        {
            new PdfSharp.Drawing.XPoint(x, y + height * 0.25),
            new PdfSharp.Drawing.XPoint(x + width * 0.6, y + height * 0.25),
            new PdfSharp.Drawing.XPoint(x + width * 0.6, y),
            new PdfSharp.Drawing.XPoint(x + width, y + height * 0.5),
            new PdfSharp.Drawing.XPoint(x + width * 0.6, y + height),
            new PdfSharp.Drawing.XPoint(x + width * 0.6, y + height * 0.75),
            new PdfSharp.Drawing.XPoint(x, y + height * 0.75),
        };
        gfx.DrawPolygon(new PdfSharp.Drawing.XSolidBrush(color), points, PdfSharp.Drawing.XFillMode.Winding);
    }

    // Helper: Draw a diamond
    static void DrawDiamond(PdfSharp.Drawing.XGraphics gfx, double centerX, double centerY, double halfWidth, double halfHeight, PdfSharp.Drawing.XColor color)
    {
        var points = new PdfSharp.Drawing.XPoint[]
        {
            new PdfSharp.Drawing.XPoint(centerX, centerY - halfHeight),
            new PdfSharp.Drawing.XPoint(centerX + halfWidth, centerY),
            new PdfSharp.Drawing.XPoint(centerX, centerY + halfHeight),
            new PdfSharp.Drawing.XPoint(centerX - halfWidth, centerY),
        };
        gfx.DrawPolygon(new PdfSharp.Drawing.XSolidBrush(color), points, PdfSharp.Drawing.XFillMode.Winding);
    }

    // Helper: Draw a cross/plus sign
    static void DrawCross(PdfSharp.Drawing.XGraphics gfx, double centerX, double centerY, double size, double thickness, PdfSharp.Drawing.XColor color)
    {
        var points = new PdfSharp.Drawing.XPoint[]
        {
            new PdfSharp.Drawing.XPoint(centerX - thickness/2, centerY - size),
            new PdfSharp.Drawing.XPoint(centerX + thickness/2, centerY - size),
            new PdfSharp.Drawing.XPoint(centerX + thickness/2, centerY - thickness/2),
            new PdfSharp.Drawing.XPoint(centerX + size, centerY - thickness/2),
            new PdfSharp.Drawing.XPoint(centerX + size, centerY + thickness/2),
            new PdfSharp.Drawing.XPoint(centerX + thickness/2, centerY + thickness/2),
            new PdfSharp.Drawing.XPoint(centerX + thickness/2, centerY + size),
            new PdfSharp.Drawing.XPoint(centerX - thickness/2, centerY + size),
            new PdfSharp.Drawing.XPoint(centerX - thickness/2, centerY + thickness/2),
            new PdfSharp.Drawing.XPoint(centerX - size, centerY + thickness/2),
            new PdfSharp.Drawing.XPoint(centerX - size, centerY - thickness/2),
            new PdfSharp.Drawing.XPoint(centerX - thickness/2, centerY - thickness/2),
        };
        gfx.DrawPolygon(new PdfSharp.Drawing.XSolidBrush(color), points, PdfSharp.Drawing.XFillMode.Winding);
    }

    static string ExtractAllText(string pdfPath)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
            var sb = new System.Text.StringBuilder();
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Error extracting text: {ex.Message}]";
        }
    }

    // Helper methods

    static void Log(string message)
    {
        if (!_quiet)
        {
            Console.WriteLine(message);
        }
    }

    static int PrintError(string message)
    {
        if (_jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { success = false, error = message }));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }
        return 1;
    }

    static int OutputResult(RedactionSummary summary)
    {
        if (_jsonOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        else if (!_quiet)
        {
            if (summary.DryRun)
            {
                Console.WriteLine("DRY RUN RESULTS:");
            }

            foreach (var term in summary.Terms)
            {
                Console.WriteLine($"  '{term.Term}': {term.Occurrences} occurrence(s) on pages [{string.Join(", ", term.Pages)}]");
            }

            Console.WriteLine($"\nTotal: {summary.TotalRedactions} redaction(s)");

            if (!summary.DryRun && summary.OutputFile != null)
            {
                Console.WriteLine($"Output: {summary.OutputFile}");

                if (summary.VerificationFailed)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WARNING: Some text may still be extractable");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("VERIFIED: All redacted text removed from PDF structure");
                    Console.ResetColor();
                }
            }
        }

        return summary.VerificationFailed ? 2 : 0;
    }

    static bool VerifyTextPresent(string pdfPath, string searchText, bool caseSensitive)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var page in document.GetPages())
        {
            if (page.Text.Contains(searchText, comparison))
            {
                return true;
            }
        }

        return false;
    }

    static List<string> FindRegexMatches(string pdfPath, List<string> patterns, bool caseSensitive)
    {
        var matches = new HashSet<string>();
        var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath);

        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            foreach (var pattern in patterns)
            {
                try
                {
                    var regex = new Regex(pattern, regexOptions);
                    foreach (Match match in regex.Matches(pageText))
                    {
                        matches.Add(match.Value);
                    }
                }
                catch (RegexParseException ex)
                {
                    Console.Error.WriteLine($"Warning: Invalid regex '{pattern}': {ex.Message}");
                }
            }
        }

        return matches.ToList();
    }

    static HashSet<int>? ParsePageSpec(string spec)
    {
        var pages = new HashSet<int>();

        foreach (var part in spec.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var range = trimmed.Split('-');
                if (range.Length == 2 &&
                    int.TryParse(range[0], out int start) &&
                    int.TryParse(range[1], out int end))
                {
                    for (int i = start; i <= end; i++)
                    {
                        pages.Add(i);
                    }
                }
                else
                {
                    return null;
                }
            }
            else if (int.TryParse(trimmed, out int page))
            {
                pages.Add(page);
            }
            else
            {
                return null;
            }
        }

        return pages;
    }

    static int CountOccurrences(string text, string search, StringComparison comparison)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(search, index, comparison)) != -1)
        {
            count++;
            index++;
        }
        return count;
    }
}

// Data classes for JSON output

class RedactionSummary
{
    public bool Success { get; set; }
    public bool DryRun { get; set; }
    public int TotalRedactions { get; set; }
    public string? OutputFile { get; set; }
    public bool VerificationFailed { get; set; }
    public List<TermResult> Terms { get; set; } = new();
}

class TermResult
{
    public string Term { get; set; } = "";
    public int Occurrences { get; set; }
    public List<int> Pages { get; set; } = new();
    public bool Verified { get; set; }
}

class VerifyResult
{
    public string Term { get; set; } = "";
    public bool Found { get; set; }
    public bool Passed { get; set; }
}

class SearchResult
{
    public int Page { get; set; }
    public int Index { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? Context { get; set; }
    public string? MatchedText { get; set; }
}
