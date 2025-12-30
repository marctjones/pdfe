using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;

namespace PdfEditor.Redaction;

/// <summary>
/// Handles redaction of PDF annotations (comments, highlights, stamps, form fields, etc.).
/// Annotations are stored separately from page content and must be handled distinctly.
/// </summary>
public class AnnotationRedactor
{
    private readonly ILogger<AnnotationRedactor> _logger;

    public AnnotationRedactor() : this(NullLogger<AnnotationRedactor>.Instance) { }

    public AnnotationRedactor(ILogger<AnnotationRedactor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Remove annotations that intersect with the specified redaction areas.
    /// </summary>
    /// <param name="page">The PDF page to redact annotations from.</param>
    /// <param name="areas">Redaction areas in PDF coordinates (bottom-left origin).</param>
    /// <returns>Number of annotations removed.</returns>
    public int RedactAnnotations(PdfPage page, IEnumerable<PdfRectangle> areas)
    {
        var areaList = areas.ToList();
        if (areaList.Count == 0)
            return 0;

        var annotations = page.Annotations;
        if (annotations == null || annotations.Count == 0)
        {
            _logger.LogDebug("Page has no annotations");
            return 0;
        }

        _logger.LogDebug("Found {Count} annotations on page", annotations.Count);

        // Collect annotations to remove (can't modify while iterating)
        var toRemove = new List<PdfAnnotation>();

        for (int i = 0; i < annotations.Count; i++)
        {
            var annotation = annotations[i];
            var annotRect = GetAnnotationRect(annotation);

            if (annotRect.HasValue)
            {
                foreach (var area in areaList)
                {
                    if (annotRect.Value.IntersectsWith(area))
                    {
                        _logger.LogDebug("Annotation {Index} ({Type}) intersects redaction area",
                            i, GetAnnotationType(annotation));
                        toRemove.Add(annotation);
                        break;
                    }
                }
            }
        }

        // Remove the collected annotations
        foreach (var annotation in toRemove)
        {
            try
            {
                annotations.Remove(annotation);
                _logger.LogDebug("Removed annotation: {Type}", GetAnnotationType(annotation));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove annotation");
            }
        }

        return toRemove.Count;
    }

    /// <summary>
    /// Remove all annotations from a page.
    /// </summary>
    /// <param name="page">The PDF page.</param>
    /// <returns>Number of annotations removed.</returns>
    public int RemoveAllAnnotations(PdfPage page)
    {
        var annotations = page.Annotations;
        if (annotations == null || annotations.Count == 0)
            return 0;

        var count = annotations.Count;
        _logger.LogDebug("Removing all {Count} annotations from page", count);

        // Remove from end to avoid index shifting issues
        for (int i = count - 1; i >= 0; i--)
        {
            try
            {
                var annotation = annotations[i];
                annotations.Remove(annotation);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove annotation at index {Index}", i);
            }
        }

        return count;
    }

    /// <summary>
    /// Get the bounding rectangle of an annotation.
    /// </summary>
    private PdfRectangle? GetAnnotationRect(PdfAnnotation annotation)
    {
        try
        {
            // Try to get the /Rect entry
            var rectObj = annotation.Elements.GetArray("/Rect");
            if (rectObj != null && rectObj.Elements.Count >= 4)
            {
                var left = rectObj.Elements.GetReal(0);
                var bottom = rectObj.Elements.GetReal(1);
                var right = rectObj.Elements.GetReal(2);
                var top = rectObj.Elements.GetReal(3);

                return new PdfRectangle(left, bottom, right, top);
            }

            // Fall back to Rectangle property if available
            var rect = annotation.Rectangle;
            if (rect.Width > 0 || rect.Height > 0)
            {
                return new PdfRectangle(rect.X1, rect.Y1, rect.X2, rect.Y2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get annotation rectangle");
        }

        return null;
    }

    /// <summary>
    /// Get the type of an annotation.
    /// </summary>
    private string GetAnnotationType(PdfAnnotation annotation)
    {
        try
        {
            var subtype = annotation.Elements.GetName("/Subtype");
            return subtype ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Get information about annotations on a page.
    /// </summary>
    public List<AnnotationInfo> GetAnnotations(PdfPage page)
    {
        var result = new List<AnnotationInfo>();
        var annotations = page.Annotations;

        if (annotations == null || annotations.Count == 0)
            return result;

        for (int i = 0; i < annotations.Count; i++)
        {
            var annotation = annotations[i];
            var rect = GetAnnotationRect(annotation);

            result.Add(new AnnotationInfo
            {
                Index = i,
                Type = GetAnnotationType(annotation),
                Rectangle = rect
            });
        }

        return result;
    }
}

/// <summary>
/// Information about a PDF annotation.
/// </summary>
public record AnnotationInfo
{
    public int Index { get; init; }
    public string Type { get; init; } = "";
    public PdfRectangle? Rectangle { get; init; }
}
