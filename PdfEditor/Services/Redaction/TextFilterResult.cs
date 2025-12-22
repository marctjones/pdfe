using System.Collections.Generic;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Result of character-level text filtering operation.
/// Contains the filtered operations and metadata about what was removed.
/// </summary>
public class TextFilterResult
{
    /// <summary>Operations to include in output (may be original, partial, or empty)</summary>
    public List<PdfOperation> Operations { get; } = new();

    /// <summary>Text that was removed (for clipboard history)</summary>
    public string RemovedText { get; set; } = string.Empty;

    /// <summary>
    /// If true, character matching failed and operation-level check should be used as fallback.
    /// This ensures we maintain security even if character-level filtering fails.
    /// </summary>
    public bool FallbackToOperationLevel { get; set; }
}
