using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PdfEditor.Redaction;

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
