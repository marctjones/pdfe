namespace PdfEditor.Redaction.Operators.TextObject;

/// <summary>
/// Handler for ET (end text object) operator.
/// Marks the end of a text block.
/// </summary>
public class EtOperatorHandler : IOperatorHandler
{
    public string OperatorName => "ET";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // ET takes no operands
        // Mark operation as inside text block (it's the ending marker, but still within the block)
        bool wasInTextObject = state.InTextObject;
        state.EndTextObject();

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = wasInTextObject  // ET is inside the text block it closes
        };
    }
}
