namespace PdfEditor.Redaction.Operators.TextPositioning;

/// <summary>
/// Handler for Tm (set text matrix) operator.
/// Sets the text matrix and text line matrix to the specified values.
/// </summary>
public class TmOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Tm";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tm: a b c d e f Tm
        if (operands.Count >= 6)
        {
            var a = GetDouble(operands[0]);
            var b = GetDouble(operands[1]);
            var c = GetDouble(operands[2]);
            var d = GetDouble(operands[3]);
            var e = GetDouble(operands[4]);
            var f = GetDouble(operands[5]);

            var matrix = PdfMatrix.FromOperands(a, b, c, d, e, f);

            // Set both text matrix and text line matrix
            state.TextMatrix = matrix;
            state.TextLineMatrix = matrix;
        }

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition
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
