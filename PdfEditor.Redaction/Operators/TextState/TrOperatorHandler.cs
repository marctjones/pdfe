namespace PdfEditor.Redaction.Operators.TextState;

/// <summary>
/// Handler for Tr (set text rendering mode) operator.
/// Sets how text is rendered:
///   0 = Fill text (default)
///   1 = Stroke text
///   2 = Fill then stroke
///   3 = Invisible (neither fill nor stroke) - CRITICAL: still extractable!
///   4 = Fill and add to clipping path
///   5 = Stroke and add to clipping path
///   6 = Fill, stroke, and add to clipping path
///   7 = Add to clipping path only
///
/// SECURITY NOTE: Mode 3 (invisible) text is still extractable by text extraction tools.
/// The redaction engine MUST still process invisible text to prevent data leakage.
/// </summary>
public class TrOperatorHandler : IOperatorHandler
{
    public string OperatorName => "Tr";

    public PdfOperation? Handle(IReadOnlyList<object> operands, PdfParserState state)
    {
        // Tr: mode Tr
        if (operands.Count >= 1)
        {
            var mode = GetInt(operands[0]);

            // Validate mode is in valid range (0-7)
            if (mode >= 0 && mode <= 7)
            {
                state.TextRenderingMode = mode;
            }
        }

        return new TextStateOperation
        {
            Operator = OperatorName,
            Operands = operands,
            StreamPosition = state.StreamPosition,
            InsideTextBlock = state.InTextObject
        };
    }

    private static int GetInt(object obj)
    {
        return obj switch
        {
            int i => i,
            double d => (int)d,
            float f => (int)f,
            long l => (int)l,
            decimal m => (int)m,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0
        };
    }
}
