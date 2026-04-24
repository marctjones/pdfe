using Avalonia;

namespace PdfEditor.Models;

/// <summary>
/// One piece of text found on the current page that is present in the
/// PDF content stream but visually hidden by an overlay. Coordinates
/// are in rendered-image pixels (top-left origin, Avalonia convention)
/// — the control can drop them straight into an overlay canvas.
/// </summary>
public sealed record HiddenTextHighlight(
    string Text,
    Rect ScreenBounds,
    string HiddenBy);
