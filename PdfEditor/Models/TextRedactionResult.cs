namespace PdfEditor.Models;

/// <summary>
/// Outcome of a scripted text-redaction run (file-to-file).
/// </summary>
/// <remarks>
/// Returned by <see cref="Services.RedactionService"/>'s text-search
/// redaction API. The area-click path on the GUI side uses the richer
/// <see cref="RedactionResult"/> instead — that one tracks per-operation
/// counts, this one is just pass/fail plus a match count for the
/// scripting surface.
/// </remarks>
public sealed record TextRedactionResult(
    bool Success,
    int RedactionCount,
    string? ErrorMessage = null)
{
    public static TextRedactionResult Succeeded(int count) => new(true, count);
    public static TextRedactionResult Failed(string error) => new(false, 0, error);
}
