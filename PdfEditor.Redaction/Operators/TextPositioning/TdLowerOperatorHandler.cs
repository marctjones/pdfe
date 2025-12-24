namespace PdfEditor.Redaction.Operators.TextPositioning;

/// <summary>
/// Handler for Td (move to next line with offset) operator.
/// Unlike TD, this does NOT set the text leading.
/// </summary>
public class TdLowerOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Td";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Td: tx ty Td
        if (operands.Count >= 2)
        {
            var tx = GetDouble(operands[0]);
            var ty = GetDouble(operands[1]);

            // Translate text line matrix (does NOT set leading like TD does)
            var translateMatrix = PdfMatrix.Translate(tx, ty);
            state.TextLineMatrix = translateMatrix.Multiply(state.TextLineMatrix);

            // Set text matrix to text line matrix
            state.TextMatrix = state.TextLineMatrix;
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
