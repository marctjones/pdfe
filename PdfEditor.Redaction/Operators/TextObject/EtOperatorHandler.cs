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
        state.EndTextObject();

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition
        };
    }
}
