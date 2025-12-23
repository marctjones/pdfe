using System.Text;

namespace PdfEditor.Redaction.ContentStream;

/// <summary>
/// Low-level tokenizer for PDF content streams.
/// Handles PDF string parsing with proper escape sequences.
/// </summary>
public static class ContentStreamTokenizer
{
    // Lazy encoding initialization
    private static readonly Lazy<Encoding> Windows1252Encoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("Windows-1252");
    });

    /// <summary>
    /// Extract all text strings from a content stream.
    /// Returns the raw strings and their byte positions for modification.
    /// </summary>
    public static IReadOnlyList<TextStringToken> ExtractTextStrings(byte[] contentBytes)
    {
        var tokens = new List<TextStringToken>();
        int i = 0;

        while (i < contentBytes.Length)
        {
            // Skip whitespace
            while (i < contentBytes.Length && IsWhitespace(contentBytes[i]))
                i++;

            if (i >= contentBytes.Length)
                break;

            // Check for literal string: (...)
            if (contentBytes[i] == (byte)'(')
            {
                var token = ParseLiteralString(contentBytes, ref i);
                if (token != null)
                    tokens.Add(token);
            }
            // Check for hex string: <...>
            else if (contentBytes[i] == (byte)'<' && i + 1 < contentBytes.Length && contentBytes[i + 1] != (byte)'<')
            {
                var token = ParseHexString(contentBytes, ref i);
                if (token != null)
                    tokens.Add(token);
            }
            else
            {
                i++;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Find text operators (Tj, TJ, ', ") and their positions.
    /// </summary>
    public static IReadOnlyList<TextOperatorToken> FindTextOperators(byte[] contentBytes)
    {
        var operators = new List<TextOperatorToken>();
        int i = 0;

        while (i < contentBytes.Length)
        {
            // Look for Tj operator (2-char, needs bounds check)
            if (i + 1 < contentBytes.Length &&
                contentBytes[i] == (byte)'T' && contentBytes[i + 1] == (byte)'j')
            {
                if (IsOperatorBoundary(contentBytes, i, 2))
                {
                    operators.Add(new TextOperatorToken
                    {
                        OperatorType = "Tj",
                        Position = i,
                        Length = 2
                    });
                }
            }
            // Look for TJ operator (2-char, needs bounds check)
            else if (i + 1 < contentBytes.Length &&
                     contentBytes[i] == (byte)'T' && contentBytes[i + 1] == (byte)'J')
            {
                if (IsOperatorBoundary(contentBytes, i, 2))
                {
                    operators.Add(new TextOperatorToken
                    {
                        OperatorType = "TJ",
                        Position = i,
                        Length = 2
                    });
                }
            }
            // Look for ' operator (quote, show string) - 1-char operator
            else if (contentBytes[i] == (byte)'\'')
            {
                if (IsOperatorBoundary(contentBytes, i, 1))
                {
                    operators.Add(new TextOperatorToken
                    {
                        OperatorType = "'",
                        Position = i,
                        Length = 1
                    });
                }
            }
            // Look for " operator (double quote, set spacing and show) - 1-char operator
            else if (contentBytes[i] == (byte)'"')
            {
                if (IsOperatorBoundary(contentBytes, i, 1))
                {
                    operators.Add(new TextOperatorToken
                    {
                        OperatorType = "\"",
                        Position = i,
                        Length = 1
                    });
                }
            }

            i++;
        }

        return operators;
    }

    private static TextStringToken? ParseLiteralString(byte[] bytes, ref int pos)
    {
        if (bytes[pos] != (byte)'(')
            return null;

        int startPos = pos;
        pos++; // Skip opening (

        var content = new List<byte>();
        int parenDepth = 1;

        while (pos < bytes.Length && parenDepth > 0)
        {
            byte b = bytes[pos];

            if (b == (byte)'\\' && pos + 1 < bytes.Length)
            {
                // Handle escape sequence
                pos++;
                byte escaped = bytes[pos];
                switch (escaped)
                {
                    case (byte)'n': content.Add((byte)'\n'); break;
                    case (byte)'r': content.Add((byte)'\r'); break;
                    case (byte)'t': content.Add((byte)'\t'); break;
                    case (byte)'b': content.Add((byte)'\b'); break;
                    case (byte)'f': content.Add((byte)'\f'); break;
                    case (byte)'(': content.Add((byte)'('); break;
                    case (byte)')': content.Add((byte)')'); break;
                    case (byte)'\\': content.Add((byte)'\\'); break;
                    default:
                        // Check for octal escape (\ddd)
                        if (escaped >= '0' && escaped <= '7')
                        {
                            int octalValue = escaped - '0';
                            int digitsRead = 1;

                            while (digitsRead < 3 && pos + 1 < bytes.Length &&
                                   bytes[pos + 1] >= '0' && bytes[pos + 1] <= '7')
                            {
                                pos++;
                                octalValue = (octalValue * 8) + (bytes[pos] - '0');
                                digitsRead++;
                            }
                            content.Add((byte)octalValue);
                        }
                        else
                        {
                            // Unknown escape - just include the character
                            content.Add(escaped);
                        }
                        break;
                }
            }
            else if (b == (byte)'(')
            {
                parenDepth++;
                content.Add(b);
            }
            else if (b == (byte)')')
            {
                parenDepth--;
                if (parenDepth > 0)
                    content.Add(b);
            }
            else
            {
                content.Add(b);
            }

            pos++;
        }

        return new TextStringToken
        {
            StringType = StringType.Literal,
            StartPosition = startPos,
            EndPosition = pos,
            RawBytes = bytes[startPos..pos],
            DecodedBytes = content.ToArray(),
            DecodedText = Windows1252Encoding.Value.GetString(content.ToArray())
        };
    }

    private static TextStringToken? ParseHexString(byte[] bytes, ref int pos)
    {
        if (bytes[pos] != (byte)'<')
            return null;

        int startPos = pos;
        pos++; // Skip opening <

        var hexChars = new List<char>();

        while (pos < bytes.Length && bytes[pos] != (byte)'>')
        {
            byte b = bytes[pos];
            if (!IsWhitespace(b))
            {
                if ((b >= '0' && b <= '9') || (b >= 'A' && b <= 'F') || (b >= 'a' && b <= 'f'))
                {
                    hexChars.Add((char)b);
                }
            }
            pos++;
        }

        if (pos < bytes.Length && bytes[pos] == (byte)'>')
            pos++; // Skip closing >

        // Odd number of hex digits: final digit assumed 0
        if (hexChars.Count % 2 == 1)
            hexChars.Add('0');

        var decodedBytes = new byte[hexChars.Count / 2];
        for (int i = 0; i < decodedBytes.Length; i++)
        {
            string hex = new string(new[] { hexChars[i * 2], hexChars[i * 2 + 1] });
            decodedBytes[i] = Convert.ToByte(hex, 16);
        }

        return new TextStringToken
        {
            StringType = StringType.Hex,
            StartPosition = startPos,
            EndPosition = pos,
            RawBytes = bytes[startPos..pos],
            DecodedBytes = decodedBytes,
            DecodedText = Windows1252Encoding.Value.GetString(decodedBytes)
        };
    }

    private static bool IsWhitespace(byte b) =>
        b == ' ' || b == '\t' || b == '\n' || b == '\r' || b == '\f' || b == 0;

    private static bool IsOperatorBoundary(byte[] bytes, int pos, int length)
    {
        // Check that operator is preceded by whitespace or delimiter
        if (pos > 0)
        {
            byte before = bytes[pos - 1];
            if (!IsWhitespace(before) && before != ')' && before != ']' && before != '>')
                return false;
        }

        // Check that operator is followed by whitespace or end
        int afterPos = pos + length;
        if (afterPos < bytes.Length)
        {
            byte after = bytes[afterPos];
            if (!IsWhitespace(after) && after != '(' && after != '[' && after != '<')
                return false;
        }

        return true;
    }
}

/// <summary>
/// A text string token extracted from content stream.
/// </summary>
public class TextStringToken
{
    public required StringType StringType { get; init; }
    public required int StartPosition { get; init; }
    public required int EndPosition { get; init; }
    public required byte[] RawBytes { get; init; }
    public required byte[] DecodedBytes { get; init; }
    public required string DecodedText { get; init; }

    public int Length => EndPosition - StartPosition;
}

/// <summary>
/// A text operator token found in content stream.
/// </summary>
public class TextOperatorToken
{
    public required string OperatorType { get; init; }
    public required int Position { get; init; }
    public required int Length { get; init; }
}

public enum StringType
{
    Literal,  // (string)
    Hex       // <hexstring>
}
