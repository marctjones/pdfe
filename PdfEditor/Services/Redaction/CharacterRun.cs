using Avalonia;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Represents a contiguous run of characters that should be kept or removed.
/// Used to split text operations into segments based on character-level redaction decisions.
/// </summary>
public class CharacterRun
{
    private readonly string _originalText;

    /// <summary>Starting index in the original text</summary>
    public int StartIndex { get; set; }

    /// <summary>Ending index (exclusive) in the original text</summary>
    public int EndIndex { get; set; }

    /// <summary>The substring of text in this run</summary>
    public string Text => _originalText.Substring(StartIndex, EndIndex - StartIndex);

    /// <summary>Whether this run should be kept (true) or removed (false)</summary>
    public bool Keep { get; set; }

    /// <summary>Position where this run starts (PDF coordinates, bottom-left origin)</summary>
    public Point StartPosition { get; set; }

    /// <summary>Width of this run in PDF points</summary>
    public double Width { get; set; }

    public CharacterRun(string originalText)
    {
        _originalText = originalText;
    }
}
