using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Validates reconstructed content stream operations for PDF spec compliance.
/// Catches errors before saving to prevent corrupt PDFs.
/// </summary>
/// <remarks>
/// Implements validation for issue #126:
/// - BT/ET balance and nesting
/// - Font references in page resources
/// - Tf before Tj in text blocks
/// - q/Q balance
/// </remarks>
public class ContentStreamValidator
{
    private readonly ILogger<ContentStreamValidator> _logger;

    public ContentStreamValidator() : this(NullLogger<ContentStreamValidator>.Instance)
    {
    }

    public ContentStreamValidator(ILogger<ContentStreamValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Result of content stream validation.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the content stream is valid.
        /// </summary>
        public bool IsValid { get; init; }

        /// <summary>
        /// List of validation errors (empty if valid).
        /// </summary>
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        /// <summary>
        /// List of validation warnings (non-fatal issues).
        /// </summary>
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Validate reconstructed content stream operations.
    /// </summary>
    /// <param name="operations">List of PDF operations to validate.</param>
    /// <param name="page">Optional PDF page for resource validation.</param>
    /// <returns>Validation result with any errors.</returns>
    public ValidationResult Validate(IReadOnlyList<PdfOperation> operations, PdfPage? page = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Check BT/ET balance
        ValidateBtEtBalance(operations, errors);

        // 2. Check BT/ET nesting (can't nest per PDF spec)
        ValidateBtEtNesting(operations, errors);

        // 3. Check q/Q (save/restore) balance
        ValidateGraphicsStateBalance(operations, errors, warnings);

        // 4. Check Tf appears before Tj in each text block
        ValidateFontBeforeText(operations, errors);

        // 5. Check font references exist in page resources (if page provided)
        if (page != null)
        {
            ValidateFontResources(operations, page, errors, warnings);
        }

        var isValid = errors.Count == 0;

        if (!isValid)
        {
            _logger.LogWarning("Content stream validation failed with {ErrorCount} errors", errors.Count);
            foreach (var error in errors)
            {
                _logger.LogWarning("  Validation error: {Error}", error);
            }
        }

        return new ValidationResult
        {
            IsValid = isValid,
            Errors = errors,
            Warnings = warnings
        };
    }

    private void ValidateBtEtBalance(IReadOnlyList<PdfOperation> operations, List<string> errors)
    {
        int btCount = operations.Count(op => op.Operator == "BT");
        int etCount = operations.Count(op => op.Operator == "ET");

        if (btCount != etCount)
        {
            errors.Add($"Unbalanced text objects: {btCount} BT, {etCount} ET");
        }
    }

    private void ValidateBtEtNesting(IReadOnlyList<PdfOperation> operations, List<string> errors)
    {
        int btStack = 0;

        foreach (var op in operations)
        {
            switch (op.Operator)
            {
                case "BT":
                    btStack++;
                    if (btStack > 1)
                    {
                        errors.Add($"Nested BT operators not allowed (at stream position {op.StreamPosition})");
                    }
                    break;
                case "ET":
                    btStack--;
                    if (btStack < 0)
                    {
                        errors.Add($"ET without matching BT (at stream position {op.StreamPosition})");
                        btStack = 0; // Reset to continue checking
                    }
                    break;
            }
        }
    }

    private void ValidateGraphicsStateBalance(IReadOnlyList<PdfOperation> operations, List<string> errors, List<string> warnings)
    {
        int qStack = 0;

        foreach (var op in operations)
        {
            switch (op.Operator)
            {
                case "q":
                    qStack++;
                    if (qStack > 28) // PDF spec recommends max 28 nesting
                    {
                        warnings.Add($"Deep graphics state nesting ({qStack} levels) may cause issues");
                    }
                    break;
                case "Q":
                    qStack--;
                    if (qStack < 0)
                    {
                        errors.Add($"Q (restore) without matching q (save) at stream position {op.StreamPosition}");
                        qStack = 0;
                    }
                    break;
            }
        }

        if (qStack > 0)
        {
            errors.Add($"Unbalanced graphics state: {qStack} q operators without matching Q");
        }
    }

    private void ValidateFontBeforeText(IReadOnlyList<PdfOperation> operations, List<string> errors)
    {
        bool inTextBlock = false;
        bool fontSet = false;

        foreach (var op in operations)
        {
            switch (op.Operator)
            {
                case "BT":
                    inTextBlock = true;
                    fontSet = false; // Reset font for new text block
                    break;
                case "Tf":
                    if (inTextBlock)
                    {
                        fontSet = true;
                    }
                    break;
                case "Tj":
                case "TJ":
                case "'":
                case "\"":
                    if (inTextBlock && !fontSet)
                    {
                        errors.Add($"Text-showing operator '{op.Operator}' without preceding Tf at stream position {op.StreamPosition}");
                    }
                    break;
                case "ET":
                    inTextBlock = false;
                    break;
            }
        }
    }

    private void ValidateFontResources(IReadOnlyList<PdfOperation> operations, PdfPage page, List<string> errors, List<string> warnings)
    {
        // Get font names used in content stream
        var usedFonts = operations
            .OfType<TextStateOperation>()
            .Where(op => op.Operator == "Tf" && op.Operands.Count >= 1)
            .Select(op => op.Operands[0] as string ?? op.Operands[0]?.ToString())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToList();

        if (usedFonts.Count == 0)
        {
            return; // No fonts to validate
        }

        // Get available fonts from page resources
        var availableFonts = GetPageFontResources(page);

        foreach (var fontName in usedFonts)
        {
            if (fontName == null) continue;

            // Remove leading / if present for comparison
            var cleanName = fontName.StartsWith("/") ? fontName.Substring(1) : fontName;

            bool found = availableFonts.Contains(fontName) ||
                         availableFonts.Contains("/" + cleanName) ||
                         availableFonts.Contains(cleanName);

            if (!found)
            {
                // For default fonts like /F1, we issue a warning not an error
                // because they might be inherited or added later
                if (fontName == "/F1" || fontName == "F1")
                {
                    warnings.Add($"Font '{fontName}' not in page resources (may be inherited)");
                }
                else
                {
                    warnings.Add($"Font '{fontName}' not found in page resources");
                }
            }
        }
    }

    private HashSet<string> GetPageFontResources(PdfPage page)
    {
        var fonts = new HashSet<string>();

        try
        {
            // Check /Resources/Font dictionary
            if (page.Resources != null &&
                page.Resources.Elements.ContainsKey("/Font"))
            {
                var fontDict = page.Resources.Elements.GetDictionary("/Font");
                if (fontDict != null)
                {
                    foreach (var key in fontDict.Elements.Keys)
                    {
                        fonts.Add(key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read page font resources");
        }

        return fonts;
    }
}
