namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for TL (set text leading) operator.
/// Sets the vertical distance between baselines of adjacent lines.
/// </summary>
public class TlOperatorHandler : IOperatorHandler
{
    public string OperatorName => "TL";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // TL: leading TL
        if (operands.Count >= 1)
        {
            state.TextLeading = GetDouble(operands[0]);
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
