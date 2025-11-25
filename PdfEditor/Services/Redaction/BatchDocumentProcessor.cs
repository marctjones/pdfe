using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Processes multiple PDF documents with the same redaction rules
/// </summary>
public class BatchDocumentProcessor
{
    private readonly ILogger<BatchDocumentProcessor> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public BatchDocumentProcessor(ILogger<BatchDocumentProcessor> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Process multiple files with same redaction rules
    /// </summary>
    public async Task<BatchProcessingResult> ProcessFilesAsync(
        IEnumerable<string> filePaths,
        RedactionRuleSet rules,
        BatchOptions options,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchProcessingResult();
        var sw = Stopwatch.StartNew();
        var files = filePaths.ToList();
        result.TotalFiles = files.Count;

        _logger.LogInformation("Starting batch processing of {Count} files", files.Count);

        // Ensure output directory exists
        if (!string.IsNullOrEmpty(options.OutputDirectory))
        {
            Directory.CreateDirectory(options.OutputDirectory);
        }

        var semaphore = new SemaphoreSlim(options.MaxParallelism);
        var tasks = new List<Task<FileProcessingResult>>();

        for (int i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = files[i];
            var fileIndex = i;

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    progress?.Report(new BatchProgress
                    {
                        CurrentFile = fileIndex + 1,
                        TotalFiles = files.Count,
                        CurrentFileName = Path.GetFileName(filePath),
                        Status = "Processing"
                    });

                    return ProcessSingleFile(filePath, rules, options);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        try
        {
            var fileResults = await Task.WhenAll(tasks);
            result.FileResults = fileResults.ToList();
            result.SuccessfulFiles = fileResults.Count(r => r.Success);
            result.FailedFiles = fileResults.Count(r => !r.Success);
            result.TotalRedactions = fileResults.Sum(r => r.RedactionCount);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Batch processing was cancelled");
            throw;
        }

        sw.Stop();
        result.TotalDuration = sw.Elapsed;

        // Generate audit log if requested
        if (options.CreateAuditLog && !string.IsNullOrEmpty(options.OutputDirectory))
        {
            var auditLogPath = Path.Combine(options.OutputDirectory, "audit_log.csv");
            var auditGenerator = new AuditLogGenerator(_loggerFactory.CreateLogger<AuditLogGenerator>());
            auditGenerator.CreateAuditLog(result, auditLogPath);
            result.AuditLogPath = auditLogPath;
        }

        progress?.Report(new BatchProgress
        {
            CurrentFile = files.Count,
            TotalFiles = files.Count,
            CurrentFileName = "Complete",
            Status = "Finished"
        });

        _logger.LogInformation(
            "Batch processing complete. Success: {Success}/{Total}, Redactions: {Redactions}, Duration: {Duration}",
            result.SuccessfulFiles, result.TotalFiles, result.TotalRedactions, result.TotalDuration);

        return result;
    }

    /// <summary>
    /// Process all PDFs in a directory
    /// </summary>
    public async Task<BatchProcessingResult> ProcessDirectoryAsync(
        string directoryPath,
        RedactionRuleSet rules,
        BatchOptions options,
        bool recursive = false,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*.pdf", searchOption);

        _logger.LogInformation("Found {Count} PDF files in {Directory} (recursive: {Recursive})",
            files.Length, directoryPath, recursive);

        return await ProcessFilesAsync(files, rules, options, progress, cancellationToken);
    }

    private FileProcessingResult ProcessSingleFile(string filePath, RedactionRuleSet rules, BatchOptions options)
    {
        var result = new FileProcessingResult
        {
            SourcePath = filePath,
            FileName = Path.GetFileName(filePath)
        };

        var sw = Stopwatch.StartNew();

        try
        {
            // Generate output path
            result.OutputPath = GenerateOutputPath(filePath, options);

            // Check if output exists and we shouldn't overwrite
            if (File.Exists(result.OutputPath) && !options.OverwriteExisting)
            {
                result.Success = false;
                result.ErrorMessage = "Output file already exists";
                return result;
            }

            var searchService = new TextSearchService(
                _loggerFactory.CreateLogger<TextSearchService>());

            // First pass: Search for all matches using PdfPig directly on the file
            // This avoids the issue of marking the PdfSharp document as "saved"
            var allMatches = new List<TextMatch>();
            var piiTypesFound = new HashSet<PIIType>();

            // Search for PII
            if (rules.PIITypesToRedact.Count > 0)
            {
                foreach (var piiType in rules.PIITypesToRedact)
                {
                    var matches = searchService.FindPIIInFile(filePath, piiType);
                    if (matches.Count > 0)
                    {
                        allMatches.AddRange(matches);
                        piiTypesFound.Add(piiType);
                    }
                }
            }

            // Search for text patterns
            foreach (var pattern in rules.TextPatternsToRedact)
            {
                var matches = searchService.FindTextInFile(filePath, pattern, new SearchOptions { UseRegex = false });
                allMatches.AddRange(matches);
            }

            // Search for regex patterns
            foreach (var pattern in rules.RegexPatternsToRedact)
            {
                var matches = searchService.FindTextInFile(filePath, pattern, new SearchOptions { UseRegex = true });
                allMatches.AddRange(matches);
            }

            // Second pass: Open document and redact all found matches
            using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.Modify);

            var batchRedactService = new BatchRedactService(
                _loggerFactory.CreateLogger<BatchRedactService>(),
                _loggerFactory);

            var redactionResult = batchRedactService.RedactMatches(document, allMatches, rules.Options);

            // Save the document
            document.Save(result.OutputPath);

            result.Success = true;
            result.RedactionCount = redactionResult.RedactedCount;
            result.PIITypesFound = piiTypesFound.ToList();

            _logger.LogDebug("Processed {File}: {Count} redactions", result.FileName, redactionResult.RedactedCount);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to process {File}", filePath);

            if (!options.ContinueOnError)
                throw;
        }

        sw.Stop();
        result.ProcessingTime = sw.Elapsed;

        return result;
    }

    private string GenerateOutputPath(string inputPath, BatchOptions options)
    {
        var directory = options.OutputDirectory ?? Path.GetDirectoryName(inputPath) ?? ".";
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);

        var outputFileName = options.OutputFilePattern
            .Replace("{filename}", fileName)
            .Replace("{extension}", extension);

        return Path.Combine(directory, outputFileName);
    }
}

/// <summary>
/// Set of redaction rules to apply
/// </summary>
public class RedactionRuleSet
{
    public List<PIIType> PIITypesToRedact { get; set; } = new();
    public List<string> TextPatternsToRedact { get; set; } = new();
    public List<string> RegexPatternsToRedact { get; set; } = new();
    public RedactionOptions Options { get; set; } = new();
}

/// <summary>
/// Options for batch processing
/// </summary>
public class BatchOptions
{
    public int MaxParallelism { get; set; } = 4;
    public string? OutputDirectory { get; set; }
    public string OutputFilePattern { get; set; } = "{filename}_redacted{extension}";
    public bool OverwriteExisting { get; set; } = false;
    public bool ContinueOnError { get; set; } = true;
    public bool CreateAuditLog { get; set; } = true;
}

/// <summary>
/// Result of batch processing
/// </summary>
public class BatchProcessingResult
{
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public int TotalRedactions { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<FileProcessingResult> FileResults { get; set; } = new();
    public string? AuditLogPath { get; set; }
}

/// <summary>
/// Result of processing a single file
/// </summary>
public class FileProcessingResult
{
    public string SourcePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int RedactionCount { get; set; }
    public List<PIIType> PIITypesFound { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Progress information for batch processing
/// </summary>
public class BatchProgress
{
    public int CurrentFile { get; set; }
    public int TotalFiles { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double PercentComplete => TotalFiles > 0 ? (double)CurrentFile / TotalFiles * 100 : 0;
}
