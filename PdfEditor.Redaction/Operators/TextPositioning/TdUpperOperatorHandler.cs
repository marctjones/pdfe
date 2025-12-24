namespace PdfEditor.Redaction.Operators.TextPositioning;

/// <summary>
/// Handler for TD (move to next line with offset and set leading) operator.
/// Same as Td but also sets text leading to -ty.
/// </summary>
public class TdUpperOperatorHandler : IOperatorHandler
{
    public string OperatorName => "TD";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // TD: tx ty TD
        if (operands.Count >= 2)
        {
            var tx = GetDouble(operands[0]);
            var ty = GetDouble(operands[1]);

            // Set text leading to -ty
            state.TextLeading = -ty;

            // Translate text line matrix
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
