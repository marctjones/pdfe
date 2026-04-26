using Pdfe.Core.Primitives;

namespace Pdfe.Core.Document;

/// <summary>
/// Parses the /Annots array of a page dictionary into <see cref="PdfAnnotation"/> objects.
/// ISO 32000-2:2020 §12.5.
/// </summary>
internal static class PdfAnnotationParser
{
    /// <summary>
    /// Parse all annotations from <paramref name="pageDict"/>.
    /// </summary>
    public static IReadOnlyList<PdfAnnotation> Parse(
        PdfDocument doc,
        PdfDictionary pageDict,
        System.Collections.Generic.Dictionary<(int, int), int> pageRefToNumber,
        System.Collections.Generic.Dictionary<string, PdfObject>? namedDests)
    {
        var annotsObj = pageDict.GetOptional("Annots");
        if (annotsObj == null) return Array.Empty<PdfAnnotation>();
        if (doc.Resolve(annotsObj) is not PdfArray annots) return Array.Empty<PdfAnnotation>();

        var result = new List<PdfAnnotation>();
        foreach (var entry in annots)
        {
            if (doc.Resolve(entry) is not PdfDictionary annot) continue;

            var parsed = ParseOne(doc, annot, pageRefToNumber, namedDests);
            if (parsed != null)
                result.Add(parsed);
        }
        return result;
    }

    private static PdfAnnotation? ParseOne(
        PdfDocument doc,
        PdfDictionary annot,
        System.Collections.Generic.Dictionary<(int, int), int> pageRefToNumber,
        System.Collections.Generic.Dictionary<string, PdfObject>? namedDests)
    {
        // Rect is mandatory for all annotations
        var rectArr = doc.Resolve(annot.GetOptional("Rect") ?? (PdfObject)PdfNull.Instance) as PdfArray;
        if (rectArr == null || rectArr.Count < 4) return null;

        var rect = new PdfRectangle(
            rectArr.GetNumber(0), rectArr.GetNumber(1),
            rectArr.GetNumber(2), rectArr.GetNumber(3));

        var subtype = ParseSubtype(annot.GetNameOrNull("Subtype"));

        var contents     = annot.GetStringOrNull("Contents");
        var author       = annot.GetStringOrNull("T");
        var name         = annot.GetStringOrNull("NM");
        var iconName     = annot.GetNameOrNull("Name");
        var flags        = (PdfAnnotationFlags)annot.GetInt("F", 0);
        var isOpen       = annot.GetBool("Open", false);
        var modDate      = ParseDate(annot.GetStringOrNull("M"));
        var creationDate = ParseDate(annot.GetStringOrNull("CreationDate"));
        var color        = ParseColor(doc, annot);
        var quadPoints   = ParseQuadPoints(doc, annot);

        // Link-specific: destination page + URI
        int? destPage = null;
        string? uri = null;
        if (subtype == PdfAnnotationSubtype.Link)
            (destPage, uri) = ResolveLink(doc, annot, pageRefToNumber, namedDests);

        return new PdfAnnotation(
            subtype, rect, contents, author,
            modDate, creationDate, color, flags, name,
            quadPoints, destPage, uri, isOpen, iconName,
            annot);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PdfAnnotationSubtype ParseSubtype(string? name) => name switch
    {
        "Text"           => PdfAnnotationSubtype.Text,
        "Link"           => PdfAnnotationSubtype.Link,
        "FreeText"       => PdfAnnotationSubtype.FreeText,
        "Line"           => PdfAnnotationSubtype.Line,
        "Square"         => PdfAnnotationSubtype.Square,
        "Circle"         => PdfAnnotationSubtype.Circle,
        "Polygon"        => PdfAnnotationSubtype.Polygon,
        "PolyLine"       => PdfAnnotationSubtype.PolyLine,
        "Highlight"      => PdfAnnotationSubtype.Highlight,
        "Underline"      => PdfAnnotationSubtype.Underline,
        "Squiggly"       => PdfAnnotationSubtype.Squiggly,
        "StrikeOut"      => PdfAnnotationSubtype.StrikeOut,
        "Stamp"          => PdfAnnotationSubtype.Stamp,
        "Caret"          => PdfAnnotationSubtype.Caret,
        "Ink"            => PdfAnnotationSubtype.Ink,
        "Popup"          => PdfAnnotationSubtype.Popup,
        "FileAttachment" => PdfAnnotationSubtype.FileAttachment,
        "Sound"          => PdfAnnotationSubtype.Sound,
        "Movie"          => PdfAnnotationSubtype.Movie,
        "Widget"         => PdfAnnotationSubtype.Widget,
        "Screen"         => PdfAnnotationSubtype.Screen,
        "Watermark"      => PdfAnnotationSubtype.Watermark,
        "Redact"         => PdfAnnotationSubtype.Redact,
        _                => PdfAnnotationSubtype.Unknown
    };

    private static (double R, double G, double B)? ParseColor(PdfDocument doc, PdfDictionary annot)
    {
        var cObj = annot.GetOptional("C");
        if (cObj == null) return null;
        if (doc.Resolve(cObj) is not PdfArray arr) return null;

        return arr.Count switch
        {
            1 => (arr.GetNumber(0), arr.GetNumber(0), arr.GetNumber(0)), // Gray
            3 => (arr.GetNumber(0), arr.GetNumber(1), arr.GetNumber(2)), // RGB
            4 => CmykToRgb(arr.GetNumber(0), arr.GetNumber(1), arr.GetNumber(2), arr.GetNumber(3)),
            _ => null
        };
    }

    private static (double R, double G, double B) CmykToRgb(double c, double m, double y, double k)
    {
        var r = (1 - c) * (1 - k);
        var g = (1 - m) * (1 - k);
        var b = (1 - y) * (1 - k);
        return (r, g, b);
    }

    private static IReadOnlyList<PdfRectangle>? ParseQuadPoints(PdfDocument doc, PdfDictionary annot)
    {
        var qpObj = annot.GetOptional("QuadPoints");
        if (qpObj == null) return null;
        if (doc.Resolve(qpObj) is not PdfArray arr) return null;
        if (arr.Count < 8 || arr.Count % 8 != 0) return null;

        var rects = new List<PdfRectangle>(arr.Count / 8);
        for (int i = 0; i < arr.Count; i += 8)
        {
            // Each group is x1y1 x2y2 x3y3 x4y4 (four corners, order per spec)
            // Build an axis-aligned bounding box from the four points
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int j = 0; j < 8; j += 2)
            {
                var px = arr.GetNumber(i + j);
                var py = arr.GetNumber(i + j + 1);
                if (px < minX) minX = px;
                if (py < minY) minY = py;
                if (px > maxX) maxX = px;
                if (py > maxY) maxY = py;
            }
            rects.Add(new PdfRectangle(minX, minY, maxX, maxY));
        }
        return rects;
    }

    private static (int? destPage, string? uri) ResolveLink(
        PdfDocument doc,
        PdfDictionary annot,
        System.Collections.Generic.Dictionary<(int, int), int> pageRefToNumber,
        System.Collections.Generic.Dictionary<string, PdfObject>? namedDests)
    {
        // Check for GoToR / URI action first
        var actionObj = annot.GetOptional("A");
        if (actionObj != null && doc.Resolve(actionObj) is PdfDictionary action)
        {
            var actionType = action.GetNameOrNull("S");
            if (actionType == "URI")
            {
                var u = action.GetStringOrNull("URI");
                return (null, u);
            }
            if (actionType == "GoTo")
            {
                var d = action.GetOptional("D");
                var page = ResolveDestPage(doc, d, pageRefToNumber, namedDests);
                return (page, null);
            }
            return (null, null);
        }

        var dest = annot.GetOptional("Dest");
        var destPage2 = ResolveDestPage(doc, dest, pageRefToNumber, namedDests);
        return (destPage2, null);
    }

    private static int? ResolveDestPage(
        PdfDocument doc,
        PdfObject? dest,
        System.Collections.Generic.Dictionary<(int, int), int> pageRefToNumber,
        System.Collections.Generic.Dictionary<string, PdfObject>? namedDests)
    {
        if (dest == null) return null;

        if (dest is PdfName n && namedDests != null && namedDests.TryGetValue(n.Value, out var nd))
            dest = nd;
        else if (dest is PdfString s && namedDests != null && namedDests.TryGetValue(s.Value, out var sd))
            dest = sd;
        else
            dest = doc.Resolve(dest);

        if (dest is PdfArray arr && arr.Count > 0 &&
            arr[0] is PdfReference pageRef &&
            pageRefToNumber.TryGetValue((pageRef.ObjectNum, pageRef.Generation), out var pageNum))
        {
            return pageNum;
        }
        return null;
    }

    /// <summary>Parse a PDF date string (D:YYYYMMDDHHmmSSOHH'mm') into DateTimeOffset.</summary>
    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (raw == null) return null;
        // Strip leading "D:" if present
        var s = raw.StartsWith("D:") ? raw[2..] : raw;
        if (s.Length < 4) return null;

        try
        {
            int year   = Parse4(s, 0);
            int month  = s.Length >= 6  ? Parse2(s, 4)  : 1;
            int day    = s.Length >= 8  ? Parse2(s, 6)  : 1;
            int hour   = s.Length >= 10 ? Parse2(s, 8)  : 0;
            int minute = s.Length >= 12 ? Parse2(s, 10) : 0;
            int second = s.Length >= 14 ? Parse2(s, 12) : 0;

            TimeSpan offset = TimeSpan.Zero;
            if (s.Length >= 15)
            {
                char sign = s[14];
                if ((sign == '+' || sign == '-') && s.Length >= 20)
                {
                    int oh = Parse2(s, 15);
                    int om = s.Length >= 20 && s[17] == '\'' ? Parse2(s, 18) : 0;
                    offset = new TimeSpan(oh, om, 0);
                    if (sign == '-') offset = -offset;
                }
                else if (sign == 'Z')
                {
                    offset = TimeSpan.Zero;
                }
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }
        catch
        {
            return null;
        }
    }

    private static int Parse4(string s, int i) =>
        (s[i] - '0') * 1000 + (s[i+1] - '0') * 100 + (s[i+2] - '0') * 10 + (s[i+3] - '0');

    private static int Parse2(string s, int i) =>
        (s[i] - '0') * 10 + (s[i+1] - '0');
}
