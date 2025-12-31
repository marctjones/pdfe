using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Reconstructs PDF text operations from kept text segments.
/// Creates new Tj/TJ operations with proper positioning (Tm operators).
/// </summary>
public class OperationReconstructor
{
    private readonly ILogger<OperationReconstructor> _logger;

    public OperationReconstructor() : this(NullLogger<OperationReconstructor>.Instance)
    {
    }

    public OperationReconstructor(ILogger<OperationReconstructor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reconstruct text operations from kept segments.
    /// </summary>
    /// <param name="segments">Segments to keep (already filtered by TextSegmenter).</param>
    /// <param name="originalOperation">The original text operation being split.</param>
    /// <returns>List of new text operations representing the kept segments.</returns>
    public List<TextOperation> ReconstructOperations(
        List<TextSegment> segments,
        TextOperation originalOperation)
    {
        var newOperations = new List<TextOperation>();

        foreach (var segment in segments)
        {
            // Create new text operation for this segment
            var newOp = new TextOperation
            {
                // Use Tj operator (show string) for simplicity
                Operator = "Tj",

                // Operands contain just the text string
                Operands = new List<object> { segment.Text },

                // Bounding box from segment position/size
                BoundingBox = new PdfRectangle(
                    segment.StartX,
                    segment.StartY,
                    segment.StartX + segment.Width,
                    segment.StartY + segment.Height),

                // Copy text state info from original
                Text = segment.Text,
                FontSize = originalOperation.FontSize,

                // Note: StreamPosition will be set during serialization
                StreamPosition = 0,

                // Empty glyphs list - not needed for serialization
                Glyphs = new List<GlyphPosition>()
            };

            newOperations.Add(newOp);

            _logger.LogDebug("Reconstructed operation for segment [{Start},{End}): '{Text}' at ({X},{Y})",
                segment.StartIndex, segment.EndIndex, segment.Text, segment.StartX, segment.StartY);
        }

        return newOperations;
    }

    /// <summary>
    /// Generate positioning operator (Tm - set text matrix) for a text segment.
    /// </summary>
    /// <param name="segment">The text segment to position.</param>
    /// <param name="fontSize">Font size to use for text matrix scaling.</param>
    /// <param name="pageRotation">Page rotation in degrees (0, 90, 180, 270).</param>
    /// <param name="mediaBoxWidth">Page MediaBox width in points.</param>
    /// <param name="mediaBoxHeight">Page MediaBox height in points.</param>
    /// <returns>Text state operation representing Tm operator.</returns>
    /// <remarks>
    /// PDF text rendering uses: effectiveSize = Tf_size * Tm_scale
    /// Many PDFs use Tf with size 1 and encode the actual size in the Tm matrix.
    /// For example: "/F1 1 Tf" + "9 0 0 9 50 700 Tm" → renders at 9pt.
    ///
    /// We use Tf with size 1 and put the font size in the Tm matrix to match
    /// the common PDF pattern and ensure correct text sizing.
    ///
    /// CRITICAL FIX (Issue #173): For rotated pages, segment coordinates are in
    /// VISUAL space (from PdfPig), but Tm operator needs CONTENT STREAM coordinates.
    /// We must transform visual → content stream coordinates using RotationTransform.
    /// </remarks>
    public TextStateOperation CreatePositioningOperation(
        TextSegment segment,
        double fontSize,
        int pageRotation = 0,
        double mediaBoxWidth = 612,
        double mediaBoxHeight = 792)
    {
        // Tm operator: a b c d e f Tm
        // The text matrix includes font scaling in a and d components:
        // [fontSize 0 0 fontSize x y] Tm
        // Where (x, y) is the text position and fontSize is the scaling factor

        // Ensure we have a valid font size (default to 12 if missing)
        var scale = fontSize > 0 && fontSize < 1000 ? fontSize : 12.0;

        // CRITICAL FIX (Issue #173): Transform visual coordinates to content stream coordinates
        // Segment coordinates come from PdfPig letters which are in VISUAL space (post-rotation).
        // The Tm operator needs coordinates in CONTENT STREAM space (pre-rotation).
        double contentX, contentY;
        if (pageRotation != 0)
        {
            (contentX, contentY) = RotationTransform.VisualToContentStream(
                segment.StartX,
                segment.StartY,
                pageRotation,
                mediaBoxWidth,
                mediaBoxHeight);

            _logger.LogDebug("[ROTATION-FIX] {Rotation}°: Visual ({VX:F1},{VY:F1}) → Content ({CX:F1},{CY:F1})",
                pageRotation, segment.StartX, segment.StartY, contentX, contentY);
        }
        else
        {
            contentX = segment.StartX;
            contentY = segment.StartY;
        }

        return new TextStateOperation
        {
            Operator = "Tm",
            Operands = new List<object>
            {
                scale,    // a - horizontal scaling (font size)
                0.0,      // b - vertical skew
                0.0,      // c - horizontal skew
                scale,    // d - vertical scaling (font size)
                contentX, // e - horizontal position (content stream coords)
                contentY  // f - vertical position (content stream coords)
            },
            StreamPosition = 0,  // Will be set during serialization
            InsideTextBlock = false  // Reconstructed operations are NOT inside the original text block
        };
    }

    /// <summary>
    /// Generate complete operation sequence for segments (with positioning).
    /// Includes BT/ET text block with font selection and Tm operators before each Tj operator.
    /// </summary>
    /// <param name="segments">Segments to reconstruct.</param>
    /// <param name="originalOperation">Original operation for context.</param>
    /// <param name="pageRotation">Page rotation in degrees (0, 90, 180, 270). Default 0.</param>
    /// <param name="mediaBoxWidth">Page MediaBox width in points. Default 612 (US Letter).</param>
    /// <param name="mediaBoxHeight">Page MediaBox height in points. Default 792 (US Letter).</param>
    /// <returns>List of operations (BT, Tf, [Tm, Tj]*, ET).</returns>
    public List<PdfOperation> ReconstructWithPositioning(
        List<TextSegment> segments,
        TextOperation originalOperation,
        int pageRotation = 0,
        double mediaBoxWidth = 612,
        double mediaBoxHeight = 792)
    {
        var operations = new List<PdfOperation>();

        if (segments.Count == 0)
        {
            return operations;
        }

        // Begin text block
        operations.Add(new TextStateOperation
        {
            Operator = "BT",
            Operands = new List<object>(),
            StreamPosition = 0,
            InsideTextBlock = false  // Reconstructed operations are NOT inside the original text block
        });

        // Set font and size (Tf operator)
        var fontName = originalOperation.FontName;
        var fontSize = originalOperation.FontSize;

        // DIAGNOSTIC: Log font information
        _logger.LogDebug("[FONT-DEBUG] Reconstructing with Font='{FontName}', Size={FontSize}",
            fontName ?? "NULL", fontSize);

        // If font info is missing, use defaults to prevent PDF corruption
        // This can happen if the original Tf operator wasn't captured during parsing
        if (string.IsNullOrEmpty(fontName))
        {
            _logger.LogWarning("[FONT-FIX] FontName is NULL/empty, using default /F1");
            fontName = "/F1";  // Default font reference - may not work in all PDFs
        }

        if (fontSize <= 0 || fontSize > 1000)
        {
            _logger.LogWarning("[FONT-FIX] FontSize={FontSize} is invalid, using default 12", fontSize);
            fontSize = 12.0;  // Default 12pt
        }

        // CRITICAL FIX (Issue #XXX): Use Tf with size 1, put actual size in Tm matrix
        // Many PDFs use this pattern: "/F1 1 Tf" + "9 0 0 9 x y Tm" → renders at 9pt
        // Previously we used "/F1 9 Tf" + "1 0 0 1 x y Tm" which also renders at 9pt
        // but the original PDF might use the Tm scaling pattern, so we match that.
        var tfOperation = new TextStateOperation
        {
            Operator = "Tf",
            Operands = new List<object> { fontName, 1.0 },  // Size 1, actual size goes in Tm
            StreamPosition = 0,
            InsideTextBlock = false  // Reconstructed operations are NOT inside the original text block
        };

        operations.Add(tfOperation);

        // Emit text state operators if non-default values (issue #122)
        // This ensures reconstructed text matches original appearance

        // Character spacing (Tc) - default is 0
        if (Math.Abs(originalOperation.CharacterSpacing) > 0.001)
        {
            operations.Add(new TextStateOperation
            {
                Operator = "Tc",
                Operands = new List<object> { originalOperation.CharacterSpacing },
                StreamPosition = 0,
                InsideTextBlock = false
            });
            _logger.LogDebug("[TEXT-STATE] Emitting Tc={CharacterSpacing}", originalOperation.CharacterSpacing);
        }

        // Word spacing (Tw) - default is 0
        if (Math.Abs(originalOperation.WordSpacing) > 0.001)
        {
            operations.Add(new TextStateOperation
            {
                Operator = "Tw",
                Operands = new List<object> { originalOperation.WordSpacing },
                StreamPosition = 0,
                InsideTextBlock = false
            });
            _logger.LogDebug("[TEXT-STATE] Emitting Tw={WordSpacing}", originalOperation.WordSpacing);
        }

        // Horizontal scaling (Tz) - default is 100
        if (Math.Abs(originalOperation.HorizontalScaling - 100.0) > 0.001)
        {
            operations.Add(new TextStateOperation
            {
                Operator = "Tz",
                Operands = new List<object> { originalOperation.HorizontalScaling },
                StreamPosition = 0,
                InsideTextBlock = false
            });
            _logger.LogDebug("[TEXT-STATE] Emitting Tz={HorizontalScaling}", originalOperation.HorizontalScaling);
        }

        // Text rendering mode (Tr) - default is 0 (fill)
        if (originalOperation.TextRenderingMode != 0)
        {
            operations.Add(new TextStateOperation
            {
                Operator = "Tr",
                Operands = new List<object> { originalOperation.TextRenderingMode },
                StreamPosition = 0,
                InsideTextBlock = false
            });
            _logger.LogDebug("[TEXT-STATE] Emitting Tr={TextRenderingMode}", originalOperation.TextRenderingMode);
        }

        // Text rise (Ts) - default is 0
        if (Math.Abs(originalOperation.TextRise) > 0.001)
        {
            operations.Add(new TextStateOperation
            {
                Operator = "Ts",
                Operands = new List<object> { originalOperation.TextRise },
                StreamPosition = 0,
                InsideTextBlock = false
            });
            _logger.LogDebug("[TEXT-STATE] Emitting Ts={TextRise}", originalOperation.TextRise);
        }

        // Text leading (TL) - default is 0
        if (Math.Abs(originalOperation.TextLeading) > 0.001)
        {
            operations.Add(new TextStateOperation
            {
                Operator = "TL",
                Operands = new List<object> { originalOperation.TextLeading },
                StreamPosition = 0,
                InsideTextBlock = false
            });
            _logger.LogDebug("[TEXT-STATE] Emitting TL={TextLeading}", originalOperation.TextLeading);
        }

        foreach (var segment in segments)
        {
            // Add positioning operator with font size for proper text matrix scaling
            // CRITICAL FIX (Issue #173): Pass rotation info for coordinate transformation
            operations.Add(CreatePositioningOperation(segment, fontSize, pageRotation, mediaBoxWidth, mediaBoxHeight));

            // Add text operation
            // CRITICAL: Don't set a bounding box based on segment width!
            // For reconstructed operations, segment width may be inaccurate due to
            // unmapped characters. Use a minimal bbox and rely on PdfPig to provide
            // accurate positions when this gets parsed again.

            // CJK support (Issue #174): Use raw bytes to preserve encoding
            // Must use raw bytes when:
            // 1. CID fonts (2-byte character codes)
            // 2. Fonts with ToUnicode CMap (HasToUnicode) - the text was decoded, need original bytes
            // 3. Any font using non-ASCII character codes (detected by examining text vs raw bytes)
            object operandValue;
            var rawBytes = segment.GetRawBytes();
            bool needRawBytes = segment.IsCidFont || segment.HasToUnicode ||
                                (rawBytes.Length > 0 && ContainsNonAsciiOrMismatch(segment.Text, rawBytes));

            if (needRawBytes && rawBytes.Length > 0)
            {
                operandValue = rawBytes;  // byte[] will be serialized as hex string
                _logger.LogDebug("[CJK-RECONSTRUCT] Using {ByteCount} raw bytes for segment '{Text}' (CID={IsCid}, ToUnicode={HasToUnicode})",
                    rawBytes.Length, segment.Text, segment.IsCidFont, segment.HasToUnicode);
            }
            else
            {
                operandValue = segment.Text;
            }

            var textOp = new TextOperation
            {
                Operator = "Tj",
                Operands = new List<object> { operandValue },
                BoundingBox = new PdfRectangle(
                    segment.StartX,
                    segment.StartY,
                    segment.StartX + (segment.Text.Length * 6.0), // Minimal estimate: 6pts per char
                    segment.StartY + 12.0), // Fixed height
                Text = segment.Text,
                FontName = fontName,
                FontSize = fontSize,
                StreamPosition = 0,
                InsideTextBlock = false,  // Reconstructed operations are NOT inside the original text block
                Glyphs = new List<GlyphPosition>(),
                // CJK support (Issue #174)
                IsCidFont = segment.IsCidFont,
                WasHexString = segment.WasHexString || segment.IsCidFont,
                RawBytes = segment.GetRawBytes()
            };

            operations.Add(textOp);
        }

        // End text block
        operations.Add(new TextStateOperation
        {
            Operator = "ET",
            Operands = new List<object>(),
            StreamPosition = 0,
            InsideTextBlock = false  // Reconstructed operations are NOT inside the original text block
        });

        _logger.LogDebug("Reconstructed {Count} segments into {OpCount} operations (with BT/Tf/ET and positioning)",
            segments.Count, operations.Count);

        return operations;
    }

    /// <summary>
    /// Check if raw bytes represent a custom encoding that differs from simple character-to-byte mapping.
    /// This indicates ToUnicode decoding was used and we need raw bytes for reconstruction.
    /// </summary>
    /// <remarks>
    /// For CJK fonts with ToUnicode CMap, the bytes are character codes (e.g., 0x01, 0x02, 0x03)
    /// that get decoded to Unicode characters (便, 携, 式). The raw bytes must be used for reconstruction.
    ///
    /// For Western fonts with Unicode text (e.g., em dash), PdfSharp writes the Unicode
    /// code point directly, and the "raw bytes" from glyph positions would just be the lower
    /// byte of each character - but we should NOT use them as they're not the real encoding.
    ///
    /// Detection: If all raw bytes are in 0x00-0x1F range (control characters that map to
    /// CJK or other symbols via ToUnicode), then it's a ToUnicode font.
    /// </remarks>
    private static bool ContainsNonAsciiOrMismatch(string text, byte[] rawBytes)
    {
        if (rawBytes.Length == 0 || text.Length == 0)
            return false;

        // Key insight: ToUnicode fonts use low byte values (0x01-0x1F, etc.) as character codes
        // that decode to high Unicode characters. If we see a pattern like:
        // - Raw bytes: 0x04 0x05 0x06 0x03
        // - Decoded text: 文件格式 (Unicode 0x6587, 0x4EF6, 0x683C, 0x5F0F)
        // Then the raw bytes clearly don't correspond to the Unicode values,
        // indicating ToUnicode decoding was used.

        // Check if any character is CJK (U+4E00 to U+9FFF and related ranges)
        // AND the raw bytes are low values (indicating character codes, not actual encoding)
        bool hasCjk = false;
        bool hasHighByte = false;

        foreach (char c in text)
        {
            // CJK ranges
            if ((c >= 0x4E00 && c <= 0x9FFF) ||  // CJK Unified Ideographs
                (c >= 0x3400 && c <= 0x4DBF) ||  // CJK Extension A
                (c >= 0xF900 && c <= 0xFAFF) ||  // CJK Compatibility Ideographs
                (c >= 0x3040 && c <= 0x30FF) ||  // Hiragana + Katakana
                (c >= 0xAC00 && c <= 0xD7AF))    // Hangul Syllables
            {
                hasCjk = true;
            }
        }

        // Check raw bytes - ToUnicode fonts typically use sequential low bytes
        foreach (byte b in rawBytes)
        {
            if (b >= 0x80)
                hasHighByte = true;
        }

        // If text has CJK characters but raw bytes are all low values,
        // this is a ToUnicode-encoded font that needs raw bytes for reconstruction
        if (hasCjk && !hasHighByte)
            return true;

        // Also check for obvious encoding mismatch: if lengths differ significantly
        // (CID fonts might use 2 bytes per character, but we handle that separately)
        // For simple fonts, the number of raw bytes should roughly equal number of chars
        // If they differ significantly, likely a different encoding scheme is in use.

        return false;
    }
}
