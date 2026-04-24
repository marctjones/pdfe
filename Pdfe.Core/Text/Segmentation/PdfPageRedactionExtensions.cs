using System.Collections.Generic;
using Pdfe.Core.Content;
using Pdfe.Core.Document;

namespace Pdfe.Core.Text.Segmentation;

/// <summary>
/// Public entry point for glyph-level redaction on a <see cref="PdfPage"/>.
/// Removes the characters whose glyph bounding boxes fall inside the given
/// area from the page's content stream — text-extraction tools reading the
/// resulting PDF will see no trace of the removed glyphs.
/// </summary>
public static class PdfPageRedactionExtensions
{
    /// <summary>
    /// Redact all glyphs overlapping <paramref name="area"/>.
    /// </summary>
    /// <param name="page">The page to mutate.</param>
    /// <param name="area">Area in content-stream coordinates. For rotated
    /// pages, callers should pre-transform visual coordinates into
    /// content-stream space before invoking.</param>
    /// <param name="strategy">How to decide whether a given glyph counts as
    /// inside the redaction area. Defaults to the most conservative option
    /// (any-overlap) — appropriate for privacy work where a partial hit
    /// still leaks information.</param>
    /// <remarks>
    /// Side-effect: the page's <c>/Contents</c> stream is rewritten.
    /// Subsequent calls to <see cref="PdfPage.Letters"/> will re-extract
    /// against the new content. Call <see cref="PdfDocument.Save(string)"/>
    /// on the owning document to persist.
    /// </remarks>
    public static void RedactArea(
        this PdfPage page,
        PdfRectangle area,
        GlyphRemovalStrategy strategy = GlyphRemovalStrategy.AnyOverlap)
    {
        if (page == null) throw new System.ArgumentNullException(nameof(page));

        var content = page.GetContentStream();

        // Short-circuit on empty pages — no ops means no work, and building
        // an empty content stream would overwrite any (legal-but-empty)
        // stream that was there.
        if (content.Operators.Count == 0) return;

        IReadOnlyList<ContentOperator> working = content.Operators;

        // Pass 1: text glyph removal (if there's any text on the page).
        var letters = page.Letters;
        if (letters.Count > 0)
        {
            var remover = new GlyphRemover();
            working = remover.ProcessOperations(working, letters, area, strategy);
        }

        // Pass 2: image XObject removal (#279). Walks the operator list
        // tracking CTM and drops image Do invocations whose transformed
        // unit-square AABB overlaps the redaction area.
        working = ImageRedactor.ProcessOperations(working, page, area, strategy, out _);

        page.SetContentStream(new ContentStream(working));
    }

    /// <summary>
    /// Redact multiple areas in a single pass. Each area is applied
    /// sequentially, so overlapping areas behave correctly.
    /// </summary>
    public static void RedactAreas(
        this PdfPage page,
        System.Collections.Generic.IEnumerable<PdfRectangle> areas,
        GlyphRemovalStrategy strategy = GlyphRemovalStrategy.AnyOverlap)
    {
        foreach (var area in areas)
            page.RedactArea(area, strategy);
    }
}
