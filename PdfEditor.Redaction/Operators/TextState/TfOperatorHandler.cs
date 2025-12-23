namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for Tf (set text font and size) operator.
/// </summary>
public class TfOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Tf";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tf: font size Tf
        if (operands.Count >= 2)
        {
            // Font name is a PDF name (string starting with /)
            if (operands[0] is string fontName)
            {
                state.FontName = fontName;
            }

            // Font size is numeric
            if (TryGetDouble(operands[1], out var fontSize))
            {
                state.FontSize = fontSize;
            }
        }

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition
        };
    }

    private static bool TryGetDouble(object obj, out double value)
    {
        value = 0;
        switch (obj)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case decimal m:
                value = (double)m;
                return true;
            case string s when double.TryParse(s, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }
}
