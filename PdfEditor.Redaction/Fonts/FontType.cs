namespace PdfEditor.Redaction.Fonts;

/// <summary>
/// PDF font types supported by the redaction engine.
/// </summary>
public enum FontType
{
    /// <summary>
    /// Standard Type 1 font (Western, single-byte encoding).
    /// </summary>
    Type1,

    /// <summary>
    /// TrueType font (Western, single-byte encoding).
    /// </summary>
    TrueType,

    /// <summary>
    /// Type 0 composite font with CID-keyed descendant (CJK, multi-byte encoding).
    /// Also known as CID fonts.
    /// </summary>
    Type0_CID,

    /// <summary>
    /// Type 3 user-defined font.
    /// </summary>
    Type3,

    /// <summary>
    /// OpenType font (may be CFF or TrueType based).
    /// </summary>
    OpenType,

    /// <summary>
    /// Unknown or unrecognized font type.
    /// </summary>
    Unknown
}
