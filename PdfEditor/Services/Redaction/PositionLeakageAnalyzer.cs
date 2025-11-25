using Avalonia;
using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Analyzes redacted PDFs for potential position information leakage.
/// Based on research from "Story Beyond the Eye: Glyph Positions Break PDF Text Redaction" (PETS 2023)
/// </summary>
public class PositionLeakageAnalyzer
{
    private readonly ILogger<PositionLeakageAnalyzer> _logger;

    public PositionLeakageAnalyzer(ILogger<PositionLeakageAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyze a redacted PDF for potential position information leakage
    /// </summary>
    public PositionLeakageReport Analyze(PdfDocument document, List<RedactionArea> redactedAreas)
    {
        var report = new PositionLeakageReport();

        try
        {
            foreach (var area in redactedAreas)
            {
                if (area.PageIndex >= 0 && area.PageIndex < document.PageCount)
                {
                    var page = document.Pages[area.PageIndex];
                    AnalyzePageForLeakage(page, area.Area, report);
                }
            }

            // Calculate overall risk score
            report.OverallRiskScore = CalculateOverallRisk(report);

            // Generate recommendations
            GenerateRecommendations(report);

            _logger.LogInformation(
                "Position leakage analysis complete. Risk score: {Score:F2}, Vulnerabilities: {Count}",
                report.OverallRiskScore, report.Vulnerabilities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during position leakage analysis");
            report.AnalysisErrors.Add($"Analysis failed: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Calculate entropy of character spacing near redaction boundaries
    /// Higher entropy suggests more information leakage
    /// </summary>
    public double CalculateSpacingEntropy(PdfPage page, Rect redactionArea)
    {
        try
        {
            var parser = new ContentStreamParser(
                _logger as ILogger<ContentStreamParser> ??
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentStreamParser>.Instance,
                new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());

            var operations = parser.ParseContentStream(page);
            var textOps = operations.OfType<TextOperation>().ToList();

            // Find text operations near the redaction boundary
            var nearbyOps = textOps
                .Where(op => IsNearRedactionBoundary(op.BoundingBox, redactionArea, threshold: 50))
                .ToList();

            if (nearbyOps.Count < 2)
                return 0.0;

            // Calculate spacing variations
            var spacings = new List<double>();
            for (int i = 1; i < nearbyOps.Count; i++)
            {
                var gap = nearbyOps[i].BoundingBox.X -
                         (nearbyOps[i - 1].BoundingBox.X + nearbyOps[i - 1].BoundingBox.Width);
                spacings.Add(gap);
            }

            // Calculate entropy (variation in spacing)
            if (spacings.Count == 0)
                return 0.0;

            var mean = spacings.Average();
            var variance = spacings.Sum(s => Math.Pow(s - mean, 2)) / spacings.Count;
            var stdDev = Math.Sqrt(variance);

            // Normalize to 0-1 range (higher = more variation = potential leakage)
            var normalizedEntropy = Math.Min(1.0, stdDev / 20.0);

            return normalizedEntropy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not calculate spacing entropy");
            return 0.5; // Return middle value on error
        }
    }

    /// <summary>
    /// Detect if relative positioning (Td) reveals redaction width
    /// </summary>
    public bool DetectsRelativePositionLeak(PdfPage page, Rect redactionArea)
    {
        try
        {
            var parser = new ContentStreamParser(
                _logger as ILogger<ContentStreamParser> ??
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentStreamParser>.Instance,
                new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());

            var operations = parser.ParseContentStream(page);

            // Find text state operations that use relative positioning
            var textStateOps = operations.OfType<TextStateOperation>()
                .Where(op => op.Type == TextStateOperationType.MoveText)
                .ToList();

            // Check if any relative moves correspond to redaction width
            foreach (var op in textStateOps)
            {
                if (op.OriginalObject is PdfSharp.Pdf.Content.Objects.COperator cOp)
                {
                    if (cOp.Operands.Count >= 1)
                    {
                        var xMove = GetOperandValue(cOp.Operands[0]);

                        // If the move distance is suspiciously close to the redaction width
                        if (Math.Abs(xMove - redactionArea.Width) < 5)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect relative position leak");
            return false;
        }
    }

    private void AnalyzePageForLeakage(PdfPage page, Rect redactionArea, PositionLeakageReport report)
    {
        // Check for spacing entropy
        var entropy = CalculateSpacingEntropy(page, redactionArea);
        if (entropy > 0.5)
        {
            report.Vulnerabilities.Add(new LeakageVulnerability
            {
                Type = LeakageType.SpacingVariation,
                Severity = entropy > 0.7 ? Severity.High : Severity.Medium,
                Description = $"High character spacing variation near redaction (entropy: {entropy:F2})",
                Location = redactionArea
            });
        }

        // Check for relative position leaks
        if (DetectsRelativePositionLeak(page, redactionArea))
        {
            report.Vulnerabilities.Add(new LeakageVulnerability
            {
                Type = LeakageType.RelativePositioning,
                Severity = Severity.High,
                Description = "Relative text positioning may reveal redacted content width",
                Location = redactionArea
            });
        }

        // Check for character spacing anomalies
        CheckCharacterSpacingAnomalies(page, redactionArea, report);
    }

    private void CheckCharacterSpacingAnomalies(PdfPage page, Rect redactionArea, PositionLeakageReport report)
    {
        try
        {
            var parser = new ContentStreamParser(
                _logger as ILogger<ContentStreamParser> ??
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<ContentStreamParser>.Instance,
                new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory());

            var operations = parser.ParseContentStream(page);

            // Find text operations with custom character spacing near redaction
            var textOps = operations.OfType<TextOperation>()
                .Where(op => IsNearRedactionBoundary(op.BoundingBox, redactionArea, threshold: 30))
                .ToList();

            foreach (var textOp in textOps)
            {
                if (textOp.TextState.CharacterSpacing != 0)
                {
                    report.Vulnerabilities.Add(new LeakageVulnerability
                    {
                        Type = LeakageType.CustomCharacterSpacing,
                        Severity = Severity.Medium,
                        Description = $"Custom character spacing ({textOp.TextState.CharacterSpacing:F2}) near redaction boundary",
                        Location = textOp.BoundingBox
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check character spacing anomalies");
        }
    }

    private bool IsNearRedactionBoundary(Rect itemBounds, Rect redactionArea, double threshold)
    {
        // Check if item is within threshold distance of redaction area
        var expandedArea = new Rect(
            redactionArea.X - threshold,
            redactionArea.Y - threshold,
            redactionArea.Width + 2 * threshold,
            redactionArea.Height + 2 * threshold);

        return expandedArea.Intersects(itemBounds) && !redactionArea.Contains(itemBounds);
    }

    private double CalculateOverallRisk(PositionLeakageReport report)
    {
        if (report.Vulnerabilities.Count == 0)
            return 0.0;

        var severityWeights = new Dictionary<Severity, double>
        {
            { Severity.Low, 0.2 },
            { Severity.Medium, 0.5 },
            { Severity.High, 0.8 },
            { Severity.Critical, 1.0 }
        };

        var totalWeight = report.Vulnerabilities
            .Sum(v => severityWeights.GetValueOrDefault(v.Severity, 0.5));

        // Normalize to 0-1, with diminishing returns for many vulnerabilities
        return Math.Min(1.0, totalWeight / 3.0);
    }

    private void GenerateRecommendations(PositionLeakageReport report)
    {
        if (report.Vulnerabilities.Any(v => v.Type == LeakageType.RelativePositioning))
        {
            report.Recommendations.Add("Convert relative text positioning (Td) to absolute positioning (Tm) near redaction boundaries");
        }

        if (report.Vulnerabilities.Any(v => v.Type == LeakageType.SpacingVariation))
        {
            report.Recommendations.Add("Normalize character spacing near redaction boundaries to prevent width inference");
        }

        if (report.Vulnerabilities.Any(v => v.Type == LeakageType.CustomCharacterSpacing))
        {
            report.Recommendations.Add("Reset character spacing to default (0) after redaction");
        }

        if (report.OverallRiskScore > 0.7)
        {
            report.Recommendations.Add("Consider using 'paranoid' mode to convert redacted areas to images");
        }
    }

    private double GetOperandValue(PdfSharp.Pdf.Content.Objects.CObject obj)
    {
        if (obj is PdfSharp.Pdf.Content.Objects.CInteger intVal)
            return intVal.Value;
        if (obj is PdfSharp.Pdf.Content.Objects.CReal realVal)
            return realVal.Value;
        return 0;
    }
}

/// <summary>
/// Report of position leakage analysis
/// </summary>
public class PositionLeakageReport
{
    public double OverallRiskScore { get; set; }
    public List<LeakageVulnerability> Vulnerabilities { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public List<string> AnalysisErrors { get; set; } = new();
}

/// <summary>
/// Individual vulnerability found during analysis
/// </summary>
public class LeakageVulnerability
{
    public LeakageType Type { get; set; }
    public Severity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public Rect Location { get; set; }
}

/// <summary>
/// Types of position leakage vulnerabilities
/// </summary>
public enum LeakageType
{
    SpacingVariation,
    RelativePositioning,
    CustomCharacterSpacing,
    WordSpacingAnomaly,
    GlyphWidthLeak
}

/// <summary>
/// Severity levels for vulnerabilities
/// </summary>
public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Represents an area that was redacted
/// </summary>
public class RedactionArea
{
    public int PageIndex { get; set; }
    public Rect Area { get; set; }
}
