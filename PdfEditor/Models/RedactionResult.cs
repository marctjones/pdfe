namespace PdfEditor.Models;

/// <summary>
/// Result of a redaction operation, indicating the mode and what was removed.
/// Used for logging and verification.
/// </summary>
public class RedactionResult
{
    /// <summary>
    /// Mode of redaction performed
    /// </summary>
    public RedactionMode Mode { get; set; }

    /// <summary>
    /// Whether content was actually removed from PDF structure
    /// </summary>
    public bool ContentRemoved { get; set; }

    /// <summary>
    /// Number of text operations removed
    /// </summary>
    public int TextOperationsRemoved { get; set; }

    /// <summary>
    /// Number of image operations removed
    /// </summary>
    public int ImageOperationsRemoved { get; set; }

    /// <summary>
    /// Number of graphics/path operations removed
    /// </summary>
    public int GraphicsOperationsRemoved { get; set; }

    /// <summary>
    /// Total operations removed
    /// </summary>
    public int TotalOperationsRemoved => TextOperationsRemoved + ImageOperationsRemoved + GraphicsOperationsRemoved;

    /// <summary>
    /// Whether visual black box was drawn
    /// </summary>
    public bool VisualCoverageDrawn { get; set; }
}

/// <summary>
/// Mode of redaction operation
/// </summary>
public enum RedactionMode
{
    /// <summary>
    /// TRUE content-level redaction - content removed from PDF structure
    /// This is the secure mode - text is not extractable after redaction.
    /// </summary>
    TrueRedaction,

    /// <summary>
    /// Visual-only redaction - only black box drawn, content still in PDF
    /// This is UNSAFE for sensitive data - text is still extractable.
    /// Only occurs when redaction area contains no content.
    /// </summary>
    VisualOnly,

    /// <summary>
    /// Redaction completely failed
    /// </summary>
    Failed
}
