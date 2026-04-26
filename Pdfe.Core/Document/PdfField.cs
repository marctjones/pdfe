using Pdfe.Core.Primitives;

namespace Pdfe.Core.Document;

/// <summary>
/// Represents a single PDF form field from an interactive form (AcroForm).
/// A field may have a text value (/V), a default value (/DV), flags (/Ff),
/// and widget annotations that define its visual representation on pages.
/// PDF spec §12.7.
/// </summary>
public sealed class PdfField
{
    /// <summary>
    /// The fully-qualified field name, formed by concatenating the /T entries
    /// of the field and its ancestors with dots. For example, "Address.City".
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// The partial field name (/T entry) for this specific field, not including parent names.
    /// </summary>
    public string PartialName { get; }

    /// <summary>
    /// The field type (Button, Text, Choice, Signature, or Unknown).
    /// </summary>
    public PdfFieldType FieldType { get; }

    /// <summary>
    /// The field's current value (/V entry), or null if not set.
    /// For text fields, this is a string. For choice fields, this is
    /// typically one of the option values. For buttons, this is usually
    /// a name like /Yes or /Off.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// The field's default value (/DV entry), or null if not set.
    /// Used when the field has no explicit value.
    /// </summary>
    public string? DefaultValue { get; }

    /// <summary>
    /// For choice fields, the list of available options (/Opt array).
    /// Each element is a string (the display/export value) or a 2-element
    /// array [exportValue, displayValue]. This property contains the
    /// display values. Null for non-choice fields.
    /// </summary>
    public IReadOnlyList<string>? Options { get; }

    /// <summary>
    /// The field's bounding rectangle on the page (from /Rect in the
    /// associated Widget annotation), or null if the field has no visual
    /// representation or the rectangle could not be parsed.
    /// </summary>
    public PdfRectangle? Rect { get; }

    /// <summary>
    /// The 1-based page number where the field's Widget annotation is located,
    /// or null if the field is not associated with any page or the page
    /// number could not be determined.
    /// </summary>
    public int? PageNumber { get; }

    /// <summary>
    /// Whether the field is read-only (flag Ff bit 0 set).
    /// Read-only fields cannot be modified by the user.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Whether the field is required (flag Ff bit 1 set).
    /// Required fields must have a value when the form is submitted.
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>
    /// For text fields, whether the field allows multiple lines of text
    /// (flag Ff bit 12 set). Only applies to /FT /Tx fields.
    /// </summary>
    public bool IsMultiline { get; }

    /// <summary>
    /// The raw field dictionary (/FT, /T, /V, /DV, /Ff, etc.).
    /// Allows access to additional entries not exposed by properties.
    /// </summary>
    public PdfDictionary RawDictionary { get; }

    public PdfField(
        string fullName,
        string partialName,
        PdfFieldType fieldType,
        string? value,
        string? defaultValue,
        IReadOnlyList<string>? options,
        PdfRectangle? rect,
        int? pageNumber,
        bool isReadOnly,
        bool isRequired,
        bool isMultiline,
        PdfDictionary rawDictionary)
    {
        FullName = fullName;
        PartialName = partialName;
        FieldType = fieldType;
        Value = value;
        DefaultValue = defaultValue;
        Options = options;
        Rect = rect;
        PageNumber = pageNumber;
        IsReadOnly = isReadOnly;
        IsRequired = isRequired;
        IsMultiline = isMultiline;
        RawDictionary = rawDictionary;
    }

    public override string ToString()
    {
        var parts = new List<string> { $"{FieldType} '{FullName}'" };

        if (Value != null)
            parts.Add($"= \"{Value}\"");

        if (PageNumber.HasValue)
            parts.Add($"on page {PageNumber}");

        if (IsReadOnly)
            parts.Add("(read-only)");

        return string.Join(" ", parts);
    }
}
