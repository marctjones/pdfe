namespace PdfEditor.Redaction.Operators.TextPositioning;

/// <summary>
/// Handler for T* (move to start of next text line) operator.
/// Equivalent to: 0 -TL Td
/// Moves to the next line using the current text leading value.
/// </summary>
public class TStarOperatorHandler : IOperatorHandler
{
    public string OperatorName => "T*";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // T* is equivalent to: 0 -TL Td
        // Move down by text leading (TL is typically positive, so we negate it)
        var tx = 0.0;
        var ty = -state.TextLeading;

        // Translate text line matrix
        var translateMatrix = PdfMatrix.Translate(tx, ty);
        state.TextLineMatrix = translateMatrix.Multiply(state.TextLineMatrix);

        // Set text matrix to text line matrix
        state.TextMatrix = state.TextLineMatrix;

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = state.InTextObject
        };
    }
}
