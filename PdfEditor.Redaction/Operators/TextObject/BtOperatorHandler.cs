namespace PdfEditor.Redaction.Operators.TextObject;

/// <summary>
/// Handler for BT (begin text object) operator.
/// Initializes text matrices to identity for a new text block.
/// </summary>
public class BtOperatorHandler : IOperatorHandler
{
    public string OperatorName => "BT";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // BT takes no operands
        // Mark operation as NOT inside text block (it's the beginning marker)
        bool wasInTextObject = state.InTextObject;
        state.BeginTextObject();

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = wasInTextObject  // BT itself is NOT inside a text block
        };
    }
}
