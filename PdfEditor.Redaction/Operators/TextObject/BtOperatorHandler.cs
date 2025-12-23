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
        state.BeginTextObject();

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition
        };
    }
}
