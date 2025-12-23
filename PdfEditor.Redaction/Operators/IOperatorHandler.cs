namespace PdfEditor.Redaction.Operators;

/// <summary>
/// Interface for PDF operator handlers. Each operator has its own handler
/// implementation, allowing independent development and testing.
/// </summary>
public interface IOperatorHandler
{
    /// <summary>
    /// The operator string this handler processes (e.g., "Tj", "Tm", "BT").
    /// </summary>
    string OperatorName { get; }

    /// <summary>
    /// Process the operator and update parser state.
    /// </summary>
    /// <param name="operands">The operands preceding the operator.</param>
    /// <param name="state">Current parser state to read/modify.</param>
    /// <returns>Optional PdfOperation if this operator produces visible output.</returns>
    PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state);
}

/// <summary>
/// Category of operators for organization.
/// </summary>
public enum OperatorCategory
{
    /// <summary>Text object operators (BT, ET).</summary>
    TextObject,

    /// <summary>Text state operators (Tf, Tc, Tw, etc.).</summary>
    TextState,

    /// <summary>Text positioning operators (Td, TD, Tm, T*).</summary>
    TextPositioning,

    /// <summary>Text showing operators (Tj, TJ, ', ").</summary>
    TextShowing,

    /// <summary>Graphics state operators (q, Q, cm).</summary>
    GraphicsState,

    /// <summary>Path construction operators (m, l, c, etc.).</summary>
    PathConstruction,

    /// <summary>Path painting operators (S, f, B, etc.).</summary>
    PathPainting,

    /// <summary>XObject operators (Do).</summary>
    XObject,

    /// <summary>Color operators (g, rg, k, etc.).</summary>
    Color
}
