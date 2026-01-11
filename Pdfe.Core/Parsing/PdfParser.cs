using System.Globalization;
using Pdfe.Core.Primitives;

namespace Pdfe.Core.Parsing;

/// <summary>
/// Parser for PDF objects.
/// Uses PdfLexer to tokenize and builds PdfObject instances.
/// </summary>
public class PdfParser : IDisposable
{
    private readonly PdfLexer _lexer;
    private readonly bool _ownsLexer;

    /// <summary>
    /// Creates a new parser with the specified lexer.
    /// </summary>
    public PdfParser(PdfLexer lexer, bool ownsLexer = false)
    {
        _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
        _ownsLexer = ownsLexer;
    }

    /// <summary>
    /// Creates a new parser for the specified stream.
    /// </summary>
    public PdfParser(Stream stream) : this(new PdfLexer(stream, ownsStream: false), ownsLexer: true)
    {
    }

    /// <summary>
    /// Creates a new parser for the specified byte array.
    /// </summary>
    public PdfParser(byte[] data) : this(new PdfLexer(data), ownsLexer: true)
    {
    }

    /// <summary>
    /// The underlying lexer.
    /// </summary>
    public PdfLexer Lexer => _lexer;

    /// <summary>
    /// Current position in the stream.
    /// </summary>
    public long Position => _lexer.Position;

    /// <summary>
    /// Seek to a specific position.
    /// </summary>
    public void Seek(long position) => _lexer.Seek(position);

    /// <summary>
    /// Parse a single PDF object from the current position.
    /// </summary>
    public PdfObject ParseObject()
    {
        var token = _lexer.NextToken();
        return ParseObjectFromToken(token);
    }

    /// <summary>
    /// Parse a PDF object from a token.
    /// </summary>
    private PdfObject ParseObjectFromToken(PdfToken token)
    {
        switch (token.Type)
        {
            case PdfTokenType.Eof:
                throw new PdfParseException("Unexpected end of file");

            case PdfTokenType.Integer:
                return ParsePossibleReference(token);

            case PdfTokenType.Real:
                return new PdfReal(double.Parse(token.Value, CultureInfo.InvariantCulture));

            case PdfTokenType.LiteralString:
                return new PdfString(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(token.Value), isHex: false);

            case PdfTokenType.HexString:
                return PdfString.FromHex(token.Value);

            case PdfTokenType.Name:
                return new PdfName(token.Value);

            case PdfTokenType.ArrayStart:
                return ParseArray();

            case PdfTokenType.DictionaryStart:
                return ParseDictionaryOrStream();

            case PdfTokenType.Keyword:
                return token.Value switch
                {
                    "true" => PdfBoolean.True,
                    "false" => PdfBoolean.False,
                    "null" => PdfNull.Instance,
                    _ => throw new PdfParseException($"Unexpected keyword '{token.Value}' at position {token.Position}")
                };

            default:
                throw new PdfParseException($"Unexpected token {token.Type} at position {token.Position}");
        }
    }

    /// <summary>
    /// After reading an integer, check if it's part of a reference (n g R).
    /// </summary>
    private PdfObject ParsePossibleReference(PdfToken intToken)
    {
        long savedPos = _lexer.Position;
        var token2 = _lexer.NextToken();

        if (token2.Type == PdfTokenType.Integer)
        {
            var token3 = _lexer.NextToken();
            if (token3.IsKeyword("R"))
            {
                // It's a reference
                int objNum = int.Parse(intToken.Value, CultureInfo.InvariantCulture);
                int genNum = int.Parse(token2.Value, CultureInfo.InvariantCulture);
                return new PdfReference(objNum, genNum);
            }
        }

        // Not a reference, restore position and return integer
        _lexer.Seek(savedPos);
        return new PdfInteger(long.Parse(intToken.Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Parse a PDF array.
    /// </summary>
    private PdfArray ParseArray()
    {
        var array = new PdfArray();

        while (true)
        {
            var token = _lexer.NextToken();

            if (token.Type == PdfTokenType.ArrayEnd)
                break;

            if (token.Type == PdfTokenType.Eof)
                throw new PdfParseException("Unterminated array");

            array.Add(ParseObjectFromToken(token));
        }

        return array;
    }

    /// <summary>
    /// Parse a dictionary, and if followed by 'stream', parse as stream.
    /// </summary>
    private PdfObject ParseDictionaryOrStream()
    {
        var dict = ParseDictionaryContents();

        // Check for stream
        long savedPos = _lexer.Position;
        var token = _lexer.NextToken();

        if (token.IsKeyword("stream"))
        {
            return ParseStream(dict);
        }

        // Not a stream, restore position
        _lexer.Seek(savedPos);
        return dict;
    }

    /// <summary>
    /// Parse dictionary contents (after &lt;&lt; and before &gt;&gt;).
    /// </summary>
    private PdfDictionary ParseDictionaryContents()
    {
        var dict = new PdfDictionary();

        while (true)
        {
            var token = _lexer.NextToken();

            if (token.Type == PdfTokenType.DictionaryEnd)
                break;

            if (token.Type == PdfTokenType.Eof)
                throw new PdfParseException("Unterminated dictionary");

            // Key must be a name
            if (token.Type != PdfTokenType.Name)
                throw new PdfParseException($"Expected name in dictionary, got {token.Type} at position {token.Position}");

            var key = new PdfName(token.Value);
            var value = ParseObject();

            dict[key] = value;
        }

        return dict;
    }

    /// <summary>
    /// Parse a stream (dictionary already parsed).
    /// </summary>
    private PdfStream ParseStream(PdfDictionary dict)
    {
        // Get length from dictionary
        int length;
        var lengthObj = dict.GetOptional("Length");
        if (lengthObj is PdfInteger li)
        {
            length = (int)li.Value;
        }
        else if (lengthObj is PdfReference)
        {
            // Length is an indirect reference - this is tricky
            // For now, we'll need to scan for 'endstream'
            throw new PdfParseException("Stream length as indirect reference not yet supported. Use PdfDocumentReader instead.");
        }
        else
        {
            throw new PdfParseException("Stream missing /Length");
        }

        // Read stream data
        var data = _lexer.ReadStreamData(length);

        // Expect 'endstream' keyword
        var token = _lexer.NextToken();
        if (!token.IsKeyword("endstream"))
        {
            // Some PDFs have off-by-one length, try to recover
            if (token.Type == PdfTokenType.Keyword && token.Value.StartsWith("endstream"))
            {
                // Close enough
            }
            else
            {
                throw new PdfParseException($"Expected 'endstream', got '{token.Value}' at position {token.Position}");
            }
        }

        return new PdfStream(dict, data);
    }

    /// <summary>
    /// Parse an indirect object at the current position.
    /// Expects format: "n g obj ... endobj"
    /// </summary>
    public PdfIndirectObject ParseIndirectObject()
    {
        var objNumToken = _lexer.NextToken();
        if (objNumToken.Type != PdfTokenType.Integer)
            throw new PdfParseException($"Expected object number, got {objNumToken.Type} at position {objNumToken.Position}");

        var genNumToken = _lexer.NextToken();
        if (genNumToken.Type != PdfTokenType.Integer)
            throw new PdfParseException($"Expected generation number, got {genNumToken.Type} at position {genNumToken.Position}");

        var objToken = _lexer.NextToken();
        if (!objToken.IsKeyword("obj"))
            throw new PdfParseException($"Expected 'obj', got '{objToken.Value}' at position {objToken.Position}");

        int objNum = int.Parse(objNumToken.Value, CultureInfo.InvariantCulture);
        int genNum = int.Parse(genNumToken.Value, CultureInfo.InvariantCulture);

        var value = ParseObject();

        var endObjToken = _lexer.NextToken();
        if (!endObjToken.IsKeyword("endobj"))
            throw new PdfParseException($"Expected 'endobj', got '{endObjToken.Value}' at position {endObjToken.Position}");

        return new PdfIndirectObject(objNum, genNum, value);
    }

    /// <summary>
    /// Try to parse an indirect object, returning null if not at an object.
    /// </summary>
    public PdfIndirectObject? TryParseIndirectObject()
    {
        long savedPos = _lexer.Position;

        try
        {
            var token1 = _lexer.NextToken();
            if (token1.Type != PdfTokenType.Integer)
            {
                _lexer.Seek(savedPos);
                return null;
            }

            var token2 = _lexer.NextToken();
            if (token2.Type != PdfTokenType.Integer)
            {
                _lexer.Seek(savedPos);
                return null;
            }

            var token3 = _lexer.NextToken();
            if (!token3.IsKeyword("obj"))
            {
                _lexer.Seek(savedPos);
                return null;
            }

            _lexer.Seek(savedPos);
            return ParseIndirectObject();
        }
        catch
        {
            _lexer.Seek(savedPos);
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsLexer)
            _lexer.Dispose();
    }
}
