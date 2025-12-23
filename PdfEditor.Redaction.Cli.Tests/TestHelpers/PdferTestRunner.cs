using System.Diagnostics;

namespace PdfEditor.Redaction.Cli.Tests.TestHelpers;

/// <summary>
/// Helper class for running pdfer CLI commands in tests.
/// Captures stdout, stderr, and exit codes for verification.
/// </summary>
public class PdferTestRunner
{
    private static readonly string PdferPath = GetPdferPath();

    private static string GetPdferPath()
    {
        // Find the pdfer executable
        var currentDir = Directory.GetCurrentDirectory();
        var searchPaths = new[]
        {
            Path.Combine(currentDir, "..", "..", "..", "..", "PdfEditor.Redaction.Cli", "bin", "Release", "net8.0", "pdfer"),
            Path.Combine(currentDir, "..", "..", "..", "..", "PdfEditor.Redaction.Cli", "bin", "Debug", "net8.0", "pdfer"),
            Path.Combine(currentDir, "PdfEditor.Redaction.Cli", "bin", "Release", "net8.0", "pdfer"),
            Path.Combine(currentDir, "PdfEditor.Redaction.Cli", "bin", "Debug", "net8.0", "pdfer"),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Fallback: use dotnet run
        return "dotnet";
    }

    public static PdferResult Run(params string[] args)
    {
        return Run(args, null);
    }

    public static PdferResult Run(string[] args, string? stdinInput)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinInput != null,
        };

        if (PdferPath == "dotnet")
        {
            startInfo.FileName = "dotnet";
            var projectPath = FindProjectPath();
            startInfo.Arguments = $"run --project \"{projectPath}\" --no-build -- {string.Join(" ", args.Select(EscapeArg))}";
        }
        else
        {
            startInfo.FileName = PdferPath;
            startInfo.Arguments = string.Join(" ", args.Select(EscapeArg));
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        if (stdinInput != null)
        {
            process.StandardInput.Write(stdinInput);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitFor(TimeSpan.FromSeconds(60));

        return new PdferResult
        {
            ExitCode = process.ExitCode,
            Stdout = stdout,
            Stderr = stderr
        };
    }

    private static string FindProjectPath()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var searchPaths = new[]
        {
            Path.Combine(currentDir, "..", "..", "..", "..", "PdfEditor.Redaction.Cli", "PdfEditor.Redaction.Cli.csproj"),
            Path.Combine(currentDir, "PdfEditor.Redaction.Cli", "PdfEditor.Redaction.Cli.csproj"),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        throw new FileNotFoundException("Could not find PdfEditor.Redaction.Cli.csproj");
    }

    private static string EscapeArg(string arg)
    {
        if (arg.Contains(' ') || arg.Contains('"'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        return arg;
    }
}

public class PdferResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";

    public bool Success => ExitCode == 0;
    public bool VerificationFailed => ExitCode == 2;
}

public static class ProcessExtensions
{
    public static bool WaitFor(this Process process, TimeSpan timeout)
    {
        return process.WaitForExit((int)timeout.TotalMilliseconds);
    }
}
