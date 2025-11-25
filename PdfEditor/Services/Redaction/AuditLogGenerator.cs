using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Generates audit logs for redaction operations
/// </summary>
public class AuditLogGenerator
{
    private readonly ILogger<AuditLogGenerator> _logger;

    public AuditLogGenerator(ILogger<AuditLogGenerator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create an audit log for batch processing results
    /// </summary>
    public void CreateAuditLog(BatchProcessingResult result, string outputPath)
    {
        try
        {
            var extension = Path.GetExtension(outputPath).ToLowerInvariant();

            if (extension == ".json")
            {
                CreateJsonAuditLog(result, outputPath);
            }
            else
            {
                CreateCsvAuditLog(result, outputPath);
            }

            _logger.LogInformation("Audit log created at {Path}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit log at {Path}", outputPath);
        }
    }

    /// <summary>
    /// Append a single entry to an existing audit log
    /// </summary>
    public void AppendEntry(string logPath, FileProcessingResult fileResult)
    {
        try
        {
            var line = FormatCsvLine(fileResult);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append to audit log at {Path}", logPath);
        }
    }

    private void CreateCsvAuditLog(BatchProcessingResult result, string outputPath)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("Timestamp,SourceFile,OutputFile,Success,RedactionCount,PIITypesFound,ProcessingTimeMs,ErrorMessage");

        // File entries
        foreach (var fileResult in result.FileResults)
        {
            sb.AppendLine(FormatCsvLine(fileResult));
        }

        // Summary section
        sb.AppendLine();
        sb.AppendLine("# Summary");
        sb.AppendLine($"# Total Files: {result.TotalFiles}");
        sb.AppendLine($"# Successful: {result.SuccessfulFiles}");
        sb.AppendLine($"# Failed: {result.FailedFiles}");
        sb.AppendLine($"# Total Redactions: {result.TotalRedactions}");
        sb.AppendLine($"# Total Duration: {result.TotalDuration.TotalSeconds:F2} seconds");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:O}");

        File.WriteAllText(outputPath, sb.ToString());
    }

    private void CreateJsonAuditLog(BatchProcessingResult result, string outputPath)
    {
        var auditData = new
        {
            GeneratedAt = DateTime.UtcNow,
            Summary = new
            {
                result.TotalFiles,
                result.SuccessfulFiles,
                result.FailedFiles,
                result.TotalRedactions,
                TotalDurationSeconds = result.TotalDuration.TotalSeconds
            },
            Files = result.FileResults.Select(f => new
            {
                f.SourcePath,
                f.OutputPath,
                f.Success,
                f.RedactionCount,
                PIITypesFound = f.PIITypesFound.Select(p => p.ToString()).ToList(),
                ProcessingTimeMs = f.ProcessingTime.TotalMilliseconds,
                f.ErrorMessage
            }).ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(auditData, options);
        File.WriteAllText(outputPath, json);
    }

    private string FormatCsvLine(FileProcessingResult result)
    {
        var timestamp = DateTime.UtcNow.ToString("O");
        var piiTypes = string.Join(";", result.PIITypesFound.Select(p => p.ToString()));
        var errorMessage = EscapeCsvField(result.ErrorMessage ?? "");

        return $"{timestamp},{EscapeCsvField(result.SourcePath)},{EscapeCsvField(result.OutputPath)}," +
               $"{result.Success},{result.RedactionCount},{piiTypes},{result.ProcessingTime.TotalMilliseconds:F0},{errorMessage}";
    }

    private string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
