using Avalonia;

namespace PdfEditor.Models;

/// <summary>
/// How a piece of hidden text was discovered. Drives the overlay
/// color in the GUI: <see cref="Structural"/> = yellow (we know the
/// exact characters from the content stream); <see cref="DifferentialOcr"/>
/// = orange (recovered from the underlying raster — confidence is
/// OCR-typical, not exact).
/// </summary>
public enum HiddenTextSource
{
    Structural,
    DifferentialOcr,
}

/// <summary>
/// One piece of text found on the current page that is present in the
/// PDF content stream but visually hidden by an overlay. Coordinates
/// are in rendered-image pixels (top-left origin, Avalonia convention)
/// — the control can drop them straight into an overlay canvas.
/// </summary>
public sealed record HiddenTextHighlight(
    string Text,
    Rect ScreenBounds,
    string HiddenBy,
    HiddenTextSource Source = HiddenTextSource.Structural);
