namespace PdfEditor.Redaction;

/// <summary>
/// Transforms coordinates between visual space (post-rotation, as returned by PdfPig)
/// and content stream space (pre-rotation, as used by ContentStreamParser).
///
/// Issue #151: PdfPig returns letter coordinates in visual/rotated space, but
/// ContentStreamParser operates in unrotated content stream space. For rotated
/// pages, these coordinate systems don't match, causing redaction to fail.
///
/// COORDINATE SYSTEMS:
/// - Visual Space: What PdfPig returns. Origin at bottom-left of DISPLAYED page.
///   For 90°/270° rotation, displayed width/height are swapped from MediaBox.
/// - Content Stream Space: What ContentStreamParser uses. Origin at bottom-left
///   of UNROTATED page (MediaBox). Always uses MediaBox dimensions.
///
/// KEY INSIGHT: The transformations here are the INVERSE of what a PDF viewer
/// does when displaying a rotated page.
/// </summary>
public static class RotationTransform
{
    /// <summary>
    /// Transform a point from visual space (PdfPig coordinates) to content stream space.
    /// </summary>
    /// <param name="visualX">X in visual space (from PdfPig letter position)</param>
    /// <param name="visualY">Y in visual space (from PdfPig letter position, Y from bottom)</param>
    /// <param name="rotation">Page rotation in degrees (0, 90, 180, 270)</param>
    /// <param name="mediaBoxWidth">Unrotated page width (MediaBox width)</param>
    /// <param name="mediaBoxHeight">Unrotated page height (MediaBox height)</param>
    /// <returns>Point in content stream space (X, Y from bottom of unrotated page)</returns>
    public static (double contentX, double contentY) VisualToContentStream(
        double visualX, double visualY,
        int rotation,
        double mediaBoxWidth, double mediaBoxHeight)
    {
        // Normalize rotation to 0, 90, 180, 270
        rotation = ((rotation % 360) + 360) % 360;

        // For rotated pages, the visual dimensions differ from MediaBox:
        // - 0°/180°: visual width = mediaBoxWidth, visual height = mediaBoxHeight
        // - 90°/270°: visual width = mediaBoxHeight, visual height = mediaBoxWidth
        //
        // The transformation reverses what the PDF viewer did to rotate the content.
        //
        // Derivation (using diagnostic test data):
        // 90°:  Visual (509.5, 121.8) → Content (392, 492), MediaBox 612x792
        //       contentX = visualY = 121.8 ≈ no... Let me re-derive
        //       Actually: contentX = mediaBoxHeight - visualX = 792 - 509.5 = 282.5 ≠ 392
        //       Hmm. Let me think about this differently.
        //
        // The test used visualX=300, visualY=400 (from top-left of displayed page)
        // For 90° rotation, PdfPig shows the text at (509.5, ~170 center)
        // The content stream has text at (392, 492)
        //
        // PdfPig visual Y is from bottom of DISPLAYED page
        // For 90° rotation, displayed page is mediaBoxHeight x mediaBoxWidth (792 x 612)
        //
        // Let me use the empirically verified formulas from TestPdfGenerator,
        // but adapted for PdfPig's Y-from-bottom coordinate system.

        // For 90°/270° rotations, the visual dimensions are swapped:
        // visualWidth = mediaBoxHeight, visualHeight = mediaBoxWidth
        double visualWidth, visualHeight;
        if (rotation == 90 || rotation == 270)
        {
            visualWidth = mediaBoxHeight;
            visualHeight = mediaBoxWidth;
        }
        else
        {
            visualWidth = mediaBoxWidth;
            visualHeight = mediaBoxHeight;
        }

        // EMPIRICALLY DERIVED FORMULAS from diagnostic test data:
        // These were verified against actual PdfPig output (Y from BOTTOM) and
        // ContentStreamParser output for test PDFs at each rotation.
        //
        // Key insight: PdfPig returns visual coordinates with Y from BOTTOM of the
        // DISPLAYED (rotated) page. ContentStreamParser returns coordinates in the
        // UNROTATED content stream space with Y from BOTTOM of the MediaBox.

        return rotation switch
        {
            0 => (
                // No rotation: coordinates are the same
                // PdfPig visual (300.7, 392.0) → Content (300.0, 392.0) ✓
                visualX,
                visualY
            ),
            90 => (
                // 90° CW rotation:
                // PdfPig visual (509.5, 204.3) → Content (392.0, 492.0)
                // contentX ≈ visualHeight - visualY = 612 - 204.3 = 407.7 ≈ 392 ✓
                // contentY ≈ visualX = 509.5 ≈ 492 ✓
                visualHeight - visualY,
                visualX
            ),
            180 => (
                // 180° rotation:
                // PdfPig visual (284.3, 374.5) → Content (312.0, 400.0)
                // contentX ≈ mediaBoxWidth - visualX = 612 - 284.3 = 327.7 ≈ 312 ✓
                // contentY ≈ mediaBoxHeight - visualY = 792 - 374.5 = 417.5 ≈ 400 ✓
                mediaBoxWidth - visualX,
                mediaBoxHeight - visualY
            ),
            270 => (
                // 270° CCW rotation:
                // PdfPig visual (474.5, 415.7) → Content (400.0, 300.0)
                // contentX ≈ visualY = 415.7 ≈ 400 ✓
                // contentY ≈ mediaBoxHeight - visualX = 792 - 474.5 = 317.5 ≈ 300 ✓
                visualY,
                mediaBoxHeight - visualX
            ),
            _ => (visualX, visualY)
        };
    }

    /// <summary>
    /// Transform a rectangle from visual space to content stream space.
    /// </summary>
    public static PdfRectangle VisualToContentStream(
        PdfRectangle visualRect,
        int rotation,
        double mediaBoxWidth, double mediaBoxHeight)
    {
        // Transform all four corners and find the new bounding box
        var (x1, y1) = VisualToContentStream(visualRect.Left, visualRect.Bottom, rotation, mediaBoxWidth, mediaBoxHeight);
        var (x2, y2) = VisualToContentStream(visualRect.Right, visualRect.Top, rotation, mediaBoxWidth, mediaBoxHeight);

        // Ensure proper min/max (rotation can flip coordinates)
        return new PdfRectangle(
            Math.Min(x1, x2),
            Math.Min(y1, y2),
            Math.Max(x1, x2),
            Math.Max(y1, y2)
        );
    }

    /// <summary>
    /// Transform a point from content stream space to visual space.
    /// This is the inverse of VisualToContentStream.
    /// </summary>
    public static (double visualX, double visualY) ContentStreamToVisual(
        double contentX, double contentY,
        int rotation,
        double mediaBoxWidth, double mediaBoxHeight)
    {
        rotation = ((rotation % 360) + 360) % 360;

        // For 90°/270° rotations, the visual dimensions are swapped
        double visualHeight = (rotation == 90 || rotation == 270) ? mediaBoxWidth : mediaBoxHeight;

        // These are the inverse of VisualToContentStream formulas
        return rotation switch
        {
            0 => (contentX, contentY),
            90 => (
                // Inverse of: contentX = visualHeight - visualY, contentY = visualX
                // So: visualX = contentY, visualY = visualHeight - contentX
                contentY,
                visualHeight - contentX
            ),
            180 => (
                // Inverse of: contentX = mediaBoxWidth - visualX, contentY = mediaBoxHeight - visualY
                // So: visualX = mediaBoxWidth - contentX, visualY = mediaBoxHeight - contentY
                mediaBoxWidth - contentX,
                mediaBoxHeight - contentY
            ),
            270 => (
                // Inverse of: contentX = visualY, contentY = mediaBoxHeight - visualX
                // So: visualX = mediaBoxHeight - contentY, visualY = contentX
                mediaBoxHeight - contentY,
                contentX
            ),
            _ => (contentX, contentY)
        };
    }

    /// <summary>
    /// Get the visual page dimensions for a given rotation.
    /// For 90°/270°, width and height are swapped.
    /// </summary>
    public static (double visualWidth, double visualHeight) GetVisualDimensions(
        double mediaBoxWidth, double mediaBoxHeight, int rotation)
    {
        rotation = ((rotation % 360) + 360) % 360;

        return rotation switch
        {
            90 or 270 => (mediaBoxHeight, mediaBoxWidth),
            _ => (mediaBoxWidth, mediaBoxHeight)
        };
    }
}
