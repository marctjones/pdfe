namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for Tc (set character spacing) operator.
/// Sets the spacing added between each glyph.
/// </summary>
public class TcOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Tc";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tc: charSpace Tc
        if (operands.Count >= 1)
        {
            state.CharacterSpacing = GetDouble(operands[0]);
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
