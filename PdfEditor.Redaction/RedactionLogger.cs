using Microsoft.Extensions.Logging;

namespace PdfEditor.Redaction;

/// <summary>
/// Logging utilities for the redaction library.
/// Provides detailed diagnostic output for debugging coordinate and matching issues.
/// </summary>
public static class RedactionLogger
{
    /// <summary>
    /// Log a parsed text operation with its bounding box.
    /// </summary>
    public static void LogTextOperation(ILogger logger, TextOperation op, int index)
    {
        logger.LogDebug(
            "TextOp[{Index}]: '{Text}' at ({Left:F2}, {Bottom:F2}, {Right:F2}, {Top:F2}) font={Font} size={Size:F1}",
            index,
            op.Text.Length > 20 ? op.Text[..20] + "..." : op.Text,
            op.BoundingBox.Left,
            op.BoundingBox.Bottom,
            op.BoundingBox.Right,
            op.BoundingBox.Top,
            op.FontName ?? "?",
            op.FontSize);
    }

    /// <summary>
    /// Log a redaction area.
    /// </summary>
    public static void LogRedactionArea(ILogger logger, PdfRectangle area, int page)
    {
        logger.LogDebug(
            "Redaction area on page {Page}: ({Left:F2}, {Bottom:F2}, {Right:F2}, {Top:F2})",
            page,
            area.Left,
            area.Bottom,
            area.Right,
            area.Top);
    }

    /// <summary>
    /// Log intersection check result.
    /// </summary>
    public static void LogIntersection(ILogger logger, TextOperation op, PdfRectangle area, bool intersects)
    {
        if (intersects)
        {
            logger.LogDebug(
                "INTERSECTS: '{Text}' [{OpLeft:F2}-{OpRight:F2}] x [{OpBot:F2}-{OpTop:F2}] with [{AreaLeft:F2}-{AreaRight:F2}] x [{AreaBot:F2}-{AreaTop:F2}]",
                op.Text.Length > 10 ? op.Text[..10] + "..." : op.Text,
                op.BoundingBox.Left, op.BoundingBox.Right,
                op.BoundingBox.Bottom, op.BoundingBox.Top,
                area.Left, area.Right,
                area.Bottom, area.Top);
        }
    }

    /// <summary>
    /// Log parsing start.
    /// </summary>
    public static void LogParseStart(ILogger logger, int contentLength, double pageHeight)
    {
        logger.LogDebug(
            "Parsing content stream: {Length} bytes, page height {Height:F2} points",
            contentLength,
            pageHeight);
    }

    /// <summary>
    /// Log parsing complete.
    /// </summary>
    public static void LogParseComplete(ILogger logger, int operationCount, int textOpCount)
    {
        logger.LogDebug(
            "Parsed {Total} operations ({TextOps} text operations)",
            operationCount,
            textOpCount);
    }

    /// <summary>
    /// Log redaction result.
    /// </summary>
    public static void LogRedactionResult(ILogger logger, RedactionResult result)
    {
        if (result.Success)
        {
            logger.LogInformation(
                "Redaction complete: {Count} items removed from pages {Pages}",
                result.RedactionCount,
                string.Join(", ", result.AffectedPages));
        }
        else
        {
            logger.LogError("Redaction failed: {Error}", result.ErrorMessage);
        }
    }
}

/// <summary>
/// Event IDs for structured logging.
/// </summary>
public static class RedactionEventIds
{
    public static readonly EventId ParseStart = new(1001, "ParseStart");
    public static readonly EventId ParseComplete = new(1002, "ParseComplete");
    public static readonly EventId TextOperationFound = new(1003, "TextOperationFound");
    public static readonly EventId IntersectionCheck = new(1004, "IntersectionCheck");
    public static readonly EventId RedactionApplied = new(1005, "RedactionApplied");
    public static readonly EventId RedactionComplete = new(1006, "RedactionComplete");
    public static readonly EventId RedactionFailed = new(1007, "RedactionFailed");
}
