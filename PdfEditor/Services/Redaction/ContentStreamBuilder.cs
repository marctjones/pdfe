using Microsoft.Extensions.Logging;
using PdfSharp.Pdf.Content.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Builds a PDF content stream from a list of operations
/// </summary>
public class ContentStreamBuilder
{
    private readonly ILogger<ContentStreamBuilder> _logger;

    public ContentStreamBuilder(ILogger<ContentStreamBuilder> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Build content stream bytes from operations
    /// </summary>
    public byte[] BuildContentStream(List<PdfOperation> operations)
    {
        if (operations == null)
            throw new ArgumentNullException(nameof(operations));

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Building content stream from {Count} operations", operations.Count);

        // Log operation type breakdown
        var textOps = operations.OfType<TextOperation>().Count();
        var partialTextOps = operations.OfType<PartialTextOperation>().Count();
        var pathOps = operations.OfType<PathOperation>().Count();
        var imageOps = operations.OfType<ImageOperation>().Count();
        var inlineImgOps = operations.OfType<InlineImageOperation>().Count();
        var stateOps = operations.OfType<StateOperation>().Count();
        var textStateOps = operations.OfType<TextStateOperation>().Count();
        var genericOps = operations.OfType<GenericOperation>().Count();

        _logger.LogDebug(
            "Operation breakdown: Text={Text}, PartialText={PartialText}, Path={Path}, Image={Image}, InlineImage={InlineImage}, " +
            "State={State}, TextState={TextState}, Generic={Generic}",
            textOps, partialTextOps, pathOps, imageOps, inlineImgOps, stateOps, textStateOps, genericOps);

        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, Encoding.ASCII);

        foreach (var operation in operations)
        {
            WriteOperation(writer, operation);
        }

        writer.Flush();
        var result = memoryStream.ToArray();

        sw.Stop();
        _logger.LogInformation(
            "Content stream built successfully in {ElapsedMs}ms. Size: {SizeBytes} bytes",
            sw.ElapsedMilliseconds, result.Length);

        return result;
    }
    
    /// <summary>
    /// Write a single operation to the stream
    /// </summary>
    private void WriteOperation(StreamWriter writer, PdfOperation operation)
    {
        // Handle partial text operations - they have raw PDF bytes
        if (operation is PartialTextOperation partialText)
        {
            // Flush the StreamWriter to ensure proper byte positioning
            writer.Flush();
            var baseStream = writer.BaseStream;

            // Write the raw PDF bytes for this partial text operation
            baseStream.Write(partialText.RawBytes, 0, partialText.RawBytes.Length);
            baseStream.WriteByte((byte)'\n');  // Add newline after operation

            _logger.LogDebug("Wrote partial text operation: '{Text}' ({Length} bytes)",
                partialText.DisplayText.Length > 20 ? partialText.DisplayText.Substring(0, 20) + "..." : partialText.DisplayText,
                partialText.RawBytes.Length);
            return;
        }

        // Handle inline images specially - they have raw bytes
        if (operation is InlineImageOperation inlineImg)
        {
            // Flush the StreamWriter to ensure proper byte positioning
            writer.Flush();
            var baseStream = writer.BaseStream;

            // Write the raw inline image data (BI...ID...EI)
            baseStream.Write(inlineImg.RawData, 0, inlineImg.RawData.Length);
            baseStream.WriteByte((byte)'\n');  // Add newline after inline image

            _logger.LogDebug("Wrote inline image operation: {Length} bytes", inlineImg.RawData.Length);
            return;
        }

        // Get the original CObject and serialize it
        if (operation.OriginalObject is COperator op)
        {
            // Inline images are handled exclusively via InlineImageOperation.RawData.
            // Skip any direct BI/ID/EI operators to avoid re-emitting inline image bytes.
            if (op.OpCode.Name is "BI" or "ID" or "EI")
            {
                _logger.LogDebug("Skipping inline image operator {Op}", op.OpCode.Name);
                return;
            }
            WriteOperator(writer, op);
        }
        else if (operation.OriginalObject is CSequence sequence)
        {
            foreach (var item in sequence)
            {
                if (item is COperator seqOp)
                {
                    if (seqOp.OpCode.Name is "BI" or "ID" or "EI")
                        continue;
                    WriteOperator(writer, seqOp);
                }
            }
        }
    }
    
    /// <summary>
    /// Write a COperator to the stream
    /// </summary>
    private void WriteOperator(StreamWriter writer, COperator op)
    {
        // Write operands first
        foreach (var operand in op.Operands)
        {
            WriteOperand(writer, operand);
            writer.Write(" ");
        }

        // Write operator
        writer.Write(op.OpCode.Name);
        writer.WriteLine();

        // Debug: Log Tf operators to help diagnose font issues
        if (op.OpCode.Name == "Tf")
        {
            var operandTypes = string.Join(", ", op.Operands.Select(o => $"{o.GetType().Name}={o}"));
            _logger.LogDebug("Wrote Tf operator with operands: [{Operands}]", operandTypes);
        }
    }
    
    /// <summary>
    /// Write a CObject operand to the stream
    /// </summary>
    private void WriteOperand(StreamWriter writer, CObject operand)
    {
        switch (operand)
        {
            case CInteger intVal:
                writer.Write(intVal.Value);
                break;
            
            case CReal realVal:
                // Format with enough precision, but not too many decimals
                writer.Write(realVal.Value.ToString("0.####"));
                break;
            
            case CString strVal:
                // Write as literal string with proper escaping
                writer.Write("(");
                WriteEscapedString(writer, strVal.Value);
                writer.Write(")");
                break;
            
            case CName nameVal:
                // CName.Name already includes the "/" prefix, so just write it directly
                // But ToString() returns with "/" too, so check both
                var name = nameVal.Name;
                if (!name.StartsWith("/"))
                {
                    writer.Write("/");
                }
                writer.Write(name);
                break;
            
            case CArray arrayVal:
                writer.Write("[");
                bool first = true;
                foreach (var item in arrayVal)
                {
                    if (!first) writer.Write(" ");
                    WriteOperand(writer, item);
                    first = false;
                }
                writer.Write("]");
                break;

            default:
                // For other types, try to write as-is
                writer.Write(operand.ToString());
                break;
        }
    }
    
    /// <summary>
    /// Write a string with proper PDF escaping
    /// </summary>
    private void WriteEscapedString(StreamWriter writer, string value)
    {
        foreach (char c in value)
        {
            switch (c)
            {
                case '(':
                case ')':
                case '\\':
                    writer.Write('\\');
                    writer.Write(c);
                    break;
                
                case '\n':
                    writer.Write("\\n");
                    break;
                
                case '\r':
                    writer.Write("\\r");
                    break;
                
                case '\t':
                    writer.Write("\\t");
                    break;
                
                default:
                    if (c < 32 || c > 126)
                    {
                        // Write as octal escape
                        writer.Write($"\\{Convert.ToString(c, 8).PadLeft(3, '0')}");
                    }
                    else
                    {
                        writer.Write(c);
                    }
                    break;
            }
        }
    }
}
