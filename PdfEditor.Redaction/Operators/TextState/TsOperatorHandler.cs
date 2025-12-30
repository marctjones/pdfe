namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for Ts (set text rise) operator.
/// Sets the vertical offset for text rendering (superscript/subscript).
/// </summary>
public class TsOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Ts";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Ts: rise Ts
        if (operands.Count >= 1)
        {
            state.TextRise = GetDouble(operands[0]);
        }

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = state.InTextObject
        };
    }

    private static double GetDouble(object obj)
    {
        return obj switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => 0
        };
    }
}
