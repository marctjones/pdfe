using System.Diagnostics;
using System.Text.Json;

namespace PdfEditor.Redaction;

/// <summary>
/// Wrapper for veraPDF validator for PDF/A compliance checking.
/// </summary>
public static class VeraPdfValidator
{
    private static string? _veraPdfPath;

    /// <summary>
    /// Check if veraPDF is available on the system.
    /// </summary>
    public static bool IsAvailable()
    {
        if (_veraPdfPath != null)
            return true;

        // Try common paths
        var paths = new[]
        {
            "verapdf",
            "/home/marc/verapdf/verapdf",
            "/usr/local/bin/verapdf",
            "/opt/verapdf/verapdf",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "verapdf", "verapdf")
        };

        foreach (var path in paths)
        {
            if (TryExecute(path, "--version", out _))
            {
                _veraPdfPath = path;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get the path to veraPDF executable.
    /// </summary>
    public static string? GetPath() => _veraPdfPath;

    /// <summary>
    /// Validate a PDF file against PDF/A standard.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file to validate.</param>
    /// <param name="flavour">PDF/A flavour (e.g., "1b", "2b", "3b", "4", or "0" for auto-detect).</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult Validate(string pdfPath, string flavour = "0")
    {
        if (!IsAvailable())
        {
            return new ValidationResult
            {
                IsCompliant = false,
                Flavour = flavour,
                Error = "veraPDF is not available on this system"
            };
        }

        if (!File.Exists(pdfPath))
        {
            return new ValidationResult
            {
                IsCompliant = false,
                Flavour = flavour,
                Error = $"File not found: {pdfPath}"
            };
        }

        // Run veraPDF with machine-readable output
        var args = $"--format mrr -f {flavour} \"{pdfPath}\"";
        if (!TryExecute(_veraPdfPath!, args, out var output))
        {
            return new ValidationResult
            {
                IsCompliant = false,
                Flavour = flavour,
                Error = "veraPDF execution failed"
            };
        }

        return ParseOutput(output, flavour);
    }

    /// <summary>
    /// Validate and get detailed report.
    /// </summary>
    public static ValidationResult ValidateWithDetails(string pdfPath, string flavour = "0")
    {
        if (!IsAvailable())
        {
            return new ValidationResult
            {
                IsCompliant = false,
                Flavour = flavour,
                Error = "veraPDF is not available on this system"
            };
        }

        if (!File.Exists(pdfPath))
        {
            return new ValidationResult
            {
                IsCompliant = false,
                Flavour = flavour,
                Error = $"File not found: {pdfPath}"
            };
        }

        // Run veraPDF with detailed output
        var args = $"--format xml -f {flavour} \"{pdfPath}\"";
        if (!TryExecute(_veraPdfPath!, args, out var output))
        {
            return new ValidationResult
            {
                IsCompliant = false,
                Flavour = flavour,
                Error = "veraPDF execution failed"
            };
        }

        return ParseXmlOutput(output, flavour);
    }

    private static bool TryExecute(string command, string args, out string output)
    {
        output = "";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000); // 30 second timeout

            // veraPDF may return non-zero for non-compliant files
            // We consider it a successful execution if we got output
            return !string.IsNullOrEmpty(output) || process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static ValidationResult ParseOutput(string output, string flavour)
    {
        // Simple check for compliance in MRR format
        // MRR format includes: compliant="true" or compliant="false"
        var isCompliant = output.Contains("compliant=\"true\"", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("\"compliant\":true", StringComparison.OrdinalIgnoreCase);

        var errors = new List<ValidationError>();

        // Extract failed checks from MRR format
        // Look for <check status="failed" or "status":"failed"
        if (output.Contains("status=\"failed\"", StringComparison.OrdinalIgnoreCase))
        {
            // Parse failed checks from XML-like MRR format
            var checkMatches = System.Text.RegularExpressions.Regex.Matches(
                output,
                @"clause=""([^""]+)"".*?testNumber=""(\d+)"".*?status=""failed""",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            foreach (System.Text.RegularExpressions.Match match in checkMatches)
            {
                errors.Add(new ValidationError
                {
                    Clause = match.Groups[1].Value,
                    TestNumber = int.TryParse(match.Groups[2].Value, out var tn) ? tn : 0,
                    Message = $"Clause {match.Groups[1].Value} test {match.Groups[2].Value} failed"
                });
            }
        }

        return new ValidationResult
        {
            IsCompliant = isCompliant,
            Flavour = flavour,
            Errors = errors,
            RawOutput = output
        };
    }

    private static ValidationResult ParseXmlOutput(string output, string flavour)
    {
        // For XML output, look for validation profile result
        var isCompliant = output.Contains("isCompliant=\"true\"", StringComparison.OrdinalIgnoreCase) ||
                          output.Contains("<isCompliant>true</isCompliant>", StringComparison.OrdinalIgnoreCase);

        var errors = new List<ValidationError>();

        // Parse failed rules
        var ruleMatches = System.Text.RegularExpressions.Regex.Matches(
            output,
            @"<rule[^>]*specification=""([^""]+)""[^>]*clause=""([^""]+)""[^>]*>[^<]*<status>failed</status>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in ruleMatches)
        {
            errors.Add(new ValidationError
            {
                Specification = match.Groups[1].Value,
                Clause = match.Groups[2].Value,
                Message = $"Rule violation: {match.Groups[1].Value} clause {match.Groups[2].Value}"
            });
        }

        return new ValidationResult
        {
            IsCompliant = isCompliant,
            Flavour = flavour,
            Errors = errors,
            RawOutput = output
        };
    }
}

/// <summary>
/// Result of veraPDF validation.
/// </summary>
public record ValidationResult
{
    public bool IsCompliant { get; init; }
    public string Flavour { get; init; } = "";
    public string? Error { get; init; }
    public List<ValidationError> Errors { get; init; } = new();
    public string? RawOutput { get; init; }
}

/// <summary>
/// A validation error from veraPDF.
/// </summary>
public record ValidationError
{
    public string? Specification { get; init; }
    public string? Clause { get; init; }
    public int TestNumber { get; init; }
    public string Message { get; init; } = "";
}
