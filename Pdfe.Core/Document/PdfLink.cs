using Pdfe.Core.Primitives;
using System.Collections.Generic;

namespace Pdfe.Core.Document;

/// <summary>
/// One link annotation extracted from a PDF page's /Annots array
/// (PDF spec §12.5.6.5). For now we only surface internal-document
/// destinations — the kind that turn up in clickable tables of
/// contents and back-of-book indexes. External URI links are
/// dropped so callers don't have to disambiguate the click target.
/// </summary>
public sealed class PdfLink
{
    /// <summary>Click rectangle in PDF points (Y-up, bottom-left origin).</summary>
    public PdfRectangle Rect { get; }
    /// <summary>1-based page number of the link's destination.</summary>
    public int DestinationPage { get; }

    public PdfLink(PdfRectangle rect, int destinationPage)
    {
        Rect = rect;
        DestinationPage = destinationPage;
    }
}

public static class PdfLinkParser
{
    /// <summary>
    /// Extract internal-document link annotations from <paramref name="pageDict"/>.
    /// </summary>
    /// <remarks>
    /// We share PdfOutlineParser's page-ref → page-number map and named-dest
    /// resolution because both ToC entries and link annotations use the
    /// same /Dest mechanism under the hood.
    /// </remarks>
    public static IReadOnlyList<PdfLink> Parse(PdfDocument doc, PdfDictionary pageDict,
        System.Collections.Generic.Dictionary<(int, int), int> pageRefToNumber,
        System.Collections.Generic.Dictionary<string, PdfObject>? namedDests)
    {
        var annotsObj = pageDict.GetOptional("Annots");
        if (annotsObj == null) return System.Array.Empty<PdfLink>();
        if (doc.Resolve(annotsObj) is not PdfArray annots) return System.Array.Empty<PdfLink>();

        var links = new List<PdfLink>();
        foreach (var entry in annots)
        {
            if (doc.Resolve(entry) is not PdfDictionary annot) continue;
            if (annot.GetNameOrNull("Subtype") != "Link") continue;

            var rectArr = doc.Resolve(annot.GetOptional("Rect") ?? (PdfObject)PdfNull.Instance) as PdfArray;
            if (rectArr == null || rectArr.Count < 4) continue;
            var rect = new PdfRectangle(
                (double)rectArr.GetNumber(0),
                (double)rectArr.GetNumber(1),
                (double)rectArr.GetNumber(2),
                (double)rectArr.GetNumber(3));

            var destPage = ResolveLinkDestination(doc, annot, pageRefToNumber, namedDests);
            if (destPage == null) continue;

            links.Add(new PdfLink(rect, destPage.Value));
        }
        return links;
    }

    private static int? ResolveLinkDestination(PdfDocument doc, PdfDictionary annot,
        System.Collections.Generic.Dictionary<(int, int), int> pageRefToNumber,
        System.Collections.Generic.Dictionary<string, PdfObject>? namedDests)
    {
        var dest = annot.GetOptional("Dest");
        if (dest == null)
        {
            var action = doc.Resolve(annot.GetOptional("A") ?? (PdfObject)PdfNull.Instance) as PdfDictionary;
            if (action == null) return null;
            // Only GoTo actions resolve to internal pages — drop URI etc.
            if (action.GetNameOrNull("S") != "GoTo") return null;
            dest = action.GetOptional("D");
        }
        if (dest == null) return null;

        // Resolve named destinations to their array form (same code path
        // outline items use; both go through the catalog's name tree).
        if (dest is PdfName n &&
            namedDests != null && namedDests.TryGetValue(n.Value, out var nd))
        {
            dest = nd;
        }
        else if (dest is PdfString s &&
            namedDests != null && namedDests.TryGetValue(s.Value, out var sd))
        {
            dest = sd;
        }
        else
        {
            dest = doc.Resolve(dest);
        }

        if (dest is PdfArray arr && arr.Count > 0 &&
            arr[0] is PdfReference pageRef &&
            pageRefToNumber.TryGetValue((pageRef.ObjectNum, pageRef.Generation), out var pageNum))
        {
            return pageNum;
        }
        return null;
    }
}
