using Pdfe.Core.Primitives;

namespace Pdfe.Core.Document;

/// <summary>
/// Internal parser for PDF interactive forms (AcroForm).
/// Walks the field tree (/Fields array) and constructs PdfField and PdfAcroForm objects.
/// PDF spec §12.7.
/// </summary>
internal static class PdfAcroFormParser
{
    /// <summary>
    /// Parse an AcroForm dictionary and return a structured PdfAcroForm object.
    /// </summary>
    public static PdfAcroForm Parse(PdfDocument doc, PdfDictionary acroFormDict)
    {
        // Read NeedsAppearances flag (default false)
        bool needsAppearances = acroFormDict.GetBool("NeedsAppearances", defaultValue: false);

        // Build a page-ref → page-number map for fast lookup
        var pageRefToNumber = PdfOutlineParser.BuildPageRefMap(doc);

        // Parse the /Fields array (top-level fields)
        var fields = new List<PdfField>();
        var fieldsObj = acroFormDict.GetOptional("Fields");
        if (fieldsObj != null && doc.Resolve(fieldsObj) is PdfArray fieldsArray)
        {
            foreach (var fieldRef in fieldsArray)
            {
                ParseFieldTree(doc, fieldRef, parentName: "", fields, pageRefToNumber);
            }
        }

        return new PdfAcroForm(fields.AsReadOnly(), needsAppearances);
    }

    /// <summary>
    /// Recursively walk a field tree, collecting all fields (both branches and leaves).
    /// A "leaf" field is one with no /Kids array, or whose /Kids are Widget annotations.
    /// </summary>
    private static void ParseFieldTree(
        PdfDocument doc,
        PdfObject? fieldObj,
        string parentName,
        List<PdfField> outputFields,
        Dictionary<(int, int), int> pageRefToNumber)
    {
        // Resolve indirect reference
        if (fieldObj == null || doc.Resolve(fieldObj) is not PdfDictionary fieldDict)
            return;

        // Get the field's partial name (/T)
        string? partialName = fieldDict.GetStringOrNull("T");
        if (partialName == null)
            return; // Skip fields without a name

        // Build the full name by appending to parent chain
        string fullName = string.IsNullOrEmpty(parentName) ? partialName : $"{parentName}.{partialName}";

        // Check if this field has kids (subsidiary fields)
        var kidsObj = fieldDict.GetOptional("Kids");
        bool hasKids = kidsObj != null && doc.Resolve(kidsObj) is PdfArray;

        if (hasKids && kidsObj != null)
        {
            // This is a non-terminal field (parent field). Recurse into kids.
            if (doc.Resolve(kidsObj) is PdfArray kidsArray)
            {
                foreach (var kidRef in kidsArray)
                {
                    ParseFieldTree(doc, kidRef, fullName, outputFields, pageRefToNumber);
                }
            }
        }
        else
        {
            // This is a terminal field (leaf). Extract its properties and create a PdfField.
            var field = ExtractField(doc, fieldDict, fullName, partialName, pageRefToNumber);
            if (field != null)
                outputFields.Add(field);
        }
    }

    /// <summary>
    /// Extract a single terminal field from its dictionary and create a PdfField object.
    /// </summary>
    private static PdfField? ExtractField(
        PdfDocument doc,
        PdfDictionary fieldDict,
        string fullName,
        string partialName,
        Dictionary<(int, int), int> pageRefToNumber)
    {
        // Get field type (/FT: /Btn, /Tx, /Ch, /Sig)
        var fieldTypeStr = fieldDict.GetNameOrNull("FT");
        var fieldType = fieldTypeStr switch
        {
            "Btn" => PdfFieldType.Button,
            "Tx" => PdfFieldType.Text,
            "Ch" => PdfFieldType.Choice,
            "Sig" => PdfFieldType.Signature,
            _ => PdfFieldType.Unknown,
        };

        // Get value (/V) — try to extract as string or name
        string? value = ExtractString(doc, fieldDict.GetOptional("V"));

        // Get default value (/DV)
        string? defaultValue = ExtractString(doc, fieldDict.GetOptional("DV"));

        // Get options (/Opt) for choice fields
        IReadOnlyList<string>? options = null;
        if (fieldType == PdfFieldType.Choice)
        {
            options = ExtractOptions(doc, fieldDict.GetOptional("Opt"));
        }

        // Get flags (/Ff) — bit flags for field properties
        int flags = fieldDict.GetInt("Ff", defaultValue: 0);
        bool isReadOnly = (flags & 0x1) != 0;     // Bit 0
        bool isRequired = (flags & 0x2) != 0;     // Bit 1
        bool isMultiline = (flags & 0x1000) != 0; // Bit 12

        // Get rectangle and page number from Widget annotation.
        // A field can be a widget itself, or have widget kids.
        PdfRectangle? rect = null;
        int? pageNumber = null;

        // Check if the field itself is a widget annotation
        var subtype = fieldDict.GetNameOrNull("Subtype");
        if (subtype == "Widget")
        {
            (rect, pageNumber) = ExtractWidgetInfo(doc, fieldDict, pageRefToNumber);
        }
        else
        {
            // Otherwise, look for widget kids
            var widgetKids = FindWidgetKids(doc, fieldDict);
            if (widgetKids.Count > 0)
            {
                // Use the first Widget annotation
                (rect, pageNumber) = ExtractWidgetInfo(doc, widgetKids[0], pageRefToNumber);
            }
        }

        return new PdfField(
            fullName: fullName,
            partialName: partialName,
            fieldType: fieldType,
            value: value,
            defaultValue: defaultValue,
            options: options,
            rect: rect,
            pageNumber: pageNumber,
            isReadOnly: isReadOnly,
            isRequired: isRequired,
            isMultiline: isMultiline,
            rawDictionary: fieldDict);
    }

    /// <summary>
    /// Find Widget annotation dictionaries associated with a field.
    /// A field's /Kids array may contain Widget annotations (/Subtype /Widget).
    /// </summary>
    private static List<PdfDictionary> FindWidgetKids(PdfDocument doc, PdfDictionary fieldDict)
    {
        var widgets = new List<PdfDictionary>();
        var kidsObj = fieldDict.GetOptional("Kids");
        if (kidsObj == null || doc.Resolve(kidsObj) is not PdfArray kidsArray)
            return widgets;

        foreach (var kidRef in kidsArray)
        {
            if (doc.Resolve(kidRef) is PdfDictionary kidDict)
            {
                var subtype = kidDict.GetNameOrNull("Subtype");
                if (subtype == "Widget")
                    widgets.Add(kidDict);
            }
        }

        return widgets;
    }

    /// <summary>
    /// Extract rectangle and page number from a Widget annotation dictionary.
    /// </summary>
    private static (PdfRectangle? rect, int? pageNumber) ExtractWidgetInfo(
        PdfDocument doc,
        PdfDictionary widgetDict,
        Dictionary<(int, int), int> pageRefToNumber)
    {
        PdfRectangle? rect = null;
        int? pageNumber = null;

        // Extract /Rect
        var rectObj = widgetDict.GetOptional("Rect");
        if (rectObj != null && doc.Resolve(rectObj) is PdfArray rectArray && rectArray.Count >= 4)
        {
            // Rect = [left bottom right top]
            if (rectArray[0].TryGetNumber(out var left) &&
                rectArray[1].TryGetNumber(out var bottom) &&
                rectArray[2].TryGetNumber(out var right) &&
                rectArray[3].TryGetNumber(out var top))
            {
                rect = new PdfRectangle(left, bottom, right, top);
            }
        }

        // Extract page number from /P (page reference)
        var pageRef = widgetDict.GetOptional("P");
        if (pageRef is PdfReference pr)
        {
            if (pageRefToNumber.TryGetValue((pr.ObjectNum, pr.Generation), out var pn))
                pageNumber = pn;
        }

        return (rect, pageNumber);
    }

    /// <summary>
    /// Extract a string value from a PDF object.
    /// Handles PdfString, PdfName, and returns null otherwise.
    /// </summary>
    private static string? ExtractString(PdfDocument doc, PdfObject? obj)
    {
        if (obj == null)
            return null;

        obj = doc.Resolve(obj);

        if (obj is PdfString str)
            return str.Value;

        if (obj is PdfName name)
            return name.Value;

        return null;
    }

    /// <summary>
    /// Extract the /Opt array for choice fields.
    /// /Opt is an array of strings, or an array of [exportValue, displayValue] pairs.
    /// Returns the display values.
    /// </summary>
    private static IReadOnlyList<string>? ExtractOptions(PdfDocument doc, PdfObject? optObj)
    {
        if (optObj == null)
            return null;

        if (doc.Resolve(optObj) is not PdfArray optArray)
            return null;

        var options = new List<string>();
        foreach (var item in optArray)
        {
            if (item is PdfString str)
            {
                options.Add(str.Value);
            }
            else if (item is PdfName name)
            {
                options.Add(name.Value);
            }
            else if (doc.Resolve(item) is PdfArray pair && pair.Count >= 2)
            {
                // [exportValue, displayValue] — we want displayValue (index 1)
                var displayValue = pair[1];
                if (displayValue is PdfString ds)
                    options.Add(ds.Value);
                else if (displayValue is PdfName dn)
                    options.Add(dn.Value);
            }
        }

        return options.Count > 0 ? options.AsReadOnly() : null;
    }
}
