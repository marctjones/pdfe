namespace PdfEditor.Redaction;

/// <summary>
/// Rebuilds PDF content streams from parsed operations.
/// Used to create the modified content stream after filtering out redacted operations.
/// </summary>
public interface IContentStreamBuilder
{
    /// <summary>
    /// Build a content stream from a list of operations.
    /// </summary>
    /// <param name="operations">Operations to include in the rebuilt stream.</param>
    /// <returns>Content stream bytes.</returns>
    byte[] Build(IEnumerable<PdfOperation> operations);

    /// <summary>
    /// Build a content stream, excluding operations that intersect with redaction areas.
    /// </summary>
    /// <param name="operations">All operations from original content stream.</param>
    /// <param name="redactionAreas">Areas to redact (operations intersecting these are excluded).</param>
    /// <returns>Content stream bytes with redacted operations removed.</returns>
    byte[] BuildWithRedactions(IEnumerable<PdfOperation> operations, IEnumerable<PdfRectangle> redactionAreas);
}
