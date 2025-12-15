using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Microsoft.Extensions.Logging;
using PdfEditor.Services.Redaction;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace PdfEditor.Services.Verification;

/// <summary>
/// In-process verification helper to detect text that still overlaps redaction boxes (black rectangles).
/// Avoids spawning external tools for quick leakage checks.
/// </summary>
public class RedactionVerifier
{
    private readonly ILogger<RedactionVerifier> _logger;
    private readonly ContentStreamParser _parser;

    public RedactionVerifier(ILogger<RedactionVerifier> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _parser = new ContentStreamParser(loggerFactory.CreateLogger<ContentStreamParser>(), loggerFactory);
    }

    public VerificationResult Verify(PdfDocument document)
    {
        var result = new VerificationResult();

        for (int i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            var blackBoxes = DetectBlackRectangles(page);
            if (blackBoxes.Count == 0)
                continue;

            var textOps = _parser.ParseContentStream(page).OfType<TextOperation>().ToList();
            foreach (var textOp in textOps)
            {
                foreach (var box in blackBoxes)
                {
                    if (RectanglesOverlap(box, textOp.BoundingBox))
                    {
                        result.Leaks.Add(new VerificationLeak
                        {
                            PageIndex = i,
                            Text = textOp.Text,
                            BoundingBox = textOp.BoundingBox
                        });
                        break;
                    }
                }
            }
        }

        return result;
    }

    private List<Rect> DetectBlackRectangles(PdfPage page)
    {
        var rects = new List<Rect>();

        if (page.Contents.Elements.Count == 0)
            return rects;

        for (int idx = 0; idx < page.Contents.Elements.Count; idx++)
        {
            var content = page.Contents.Elements.GetObject(idx);
            if (content is not PdfDictionary dict || dict.Stream?.Value == null)
                continue;

            var text = System.Text.Encoding.ASCII.GetString(dict.Stream.Value);
            var lines = text.Split('\n');
            double? pendingX = null, pendingY = null, pendingW = null, pendingH = null;
            bool inBlackFill = false;
            var pageHeight = page.Height.Point;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (line == "0 g" || line == "0 0 0 rg" || line == "0 G" || line == "0 0 0 RG")
                    inBlackFill = true;
                else if (line.EndsWith(" g") || line.EndsWith(" rg") || line.EndsWith(" G") || line.EndsWith(" RG"))
                    inBlackFill = false;

                if (line.EndsWith(" re"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 &&
                        double.TryParse(parts[0], out var x) &&
                        double.TryParse(parts[1], out var y) &&
                        double.TryParse(parts[2], out var w) &&
                        double.TryParse(parts[3], out var h))
                    {
                        pendingX = x;
                        pendingY = y;
                        pendingW = Math.Abs(w);
                        pendingH = Math.Abs(h);
                        if (w < 0) pendingX = x + w;
                        if (h < 0) pendingY = y + h;
                    }
                }

                if ((line == "f" || line == "F" || line == "f*") &&
                    pendingX.HasValue && pendingY.HasValue &&
                    pendingW.HasValue && pendingH.HasValue &&
                    inBlackFill)
                {
                    rects.Add(new Rect(
                        pendingX.Value,
                        pageHeight - pendingY.Value - pendingH.Value,
                        pendingW.Value,
                        pendingH.Value));
                    pendingX = pendingY = pendingW = pendingH = null;
                }

                if (line == "n" || line == "S" || line == "s" || line == "B" || line == "b")
                {
                    pendingX = pendingY = pendingW = pendingH = null;
                }
            }
        }

        return rects;
    }

    private bool RectanglesOverlap(Rect a, Rect b)
    {
        return a.X < b.X + b.Width &&
               a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }
}

public class VerificationResult
{
    public List<VerificationLeak> Leaks { get; set; } = new();
    public bool Passed => Leaks.Count == 0;
}

public class VerificationLeak
{
    public int PageIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public Rect BoundingBox { get; set; }
}
