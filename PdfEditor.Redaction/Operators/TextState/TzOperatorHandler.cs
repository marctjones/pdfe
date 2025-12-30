namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for Tz (set horizontal scaling) operator.
/// Sets the horizontal scaling percentage (100 = normal, 50 = half width).
/// </summary>
public class TzOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Tz";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tz: scale Tz
        if (operands.Count >= 1)
        {
            state.HorizontalScaling = GetDouble(operands[0]);
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
            _ => 100.0 // Default to 100% if invalid
        };
    }
}
