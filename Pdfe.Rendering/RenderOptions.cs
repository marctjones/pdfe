using SkiaSharp;

namespace Pdfe.Rendering;

/// <summary>
/// Options for PDF page rendering.
/// </summary>
public record RenderOptions
{
    /// <summary>
    /// Resolution in dots per inch. Default is 150.
    /// </summary>
    public int Dpi { get; init; } = 150;

    /// <summary>
    /// Background color. Default is white.
    /// </summary>
    public SKColor BackgroundColor { get; init; } = SKColors.White;

    /// <summary>
    /// Whether to use anti-aliasing. Default is true.
    /// </summary>
    public bool AntiAlias { get; init; } = true;

    /// <summary>
    /// Optional clip rectangle (in page points).
    /// </summary>
    public SKRect? ClipRect { get; init; }
}
