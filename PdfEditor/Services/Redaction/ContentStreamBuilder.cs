using PdfSharp.Pdf.Content.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfEditor.Services.Redaction;

/// <summary>
/// Builds a PDF content stream from a list of operations
/// </summary>
public class ContentStreamBuilder
{
    /// <summary>
    /// Build content stream bytes from operations
    /// </summary>
    public byte[] BuildContentStream(List<PdfOperation> operations)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, Encoding.ASCII);
        
        foreach (var operation in operations)
        {
            WriteOperation(writer, operation);
        }
        
        writer.Flush();
        return memoryStream.ToArray();
    }
    
    /// <summary>
    /// Write a single operation to the stream
    /// </summary>
    private void WriteOperation(StreamWriter writer, PdfOperation operation)
    {
        // Get the original CObject and serialize it
        if (operation.OriginalObject is COperator op)
        {
            WriteOperator(writer, op);
        }
        else if (operation.OriginalObject is CSequence sequence)
        {
            foreach (var item in sequence)
            {
                if (item is COperator seqOp)
                    WriteOperator(writer, seqOp);
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
                writer.Write("/");
                writer.Write(nameVal.Name);
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
            
            case CBoolean boolVal:
                writer.Write(boolVal.Value ? "true" : "false");
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
