namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for Tw (set word spacing) operator.
/// Sets the spacing added after each ASCII space character.
/// </summary>
public class TwOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Tw";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tw: wordSpace Tw
        if (operands.Count >= 1)
        {
            state.WordSpacing = GetDouble(operands[0]);
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
