using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SkiaSharp;

namespace PdfEditor.Redaction.ImageRedaction;

/// <summary>
/// Handles partial image redaction - blacks out portions of images that intersect
/// with redaction areas rather than removing the entire image.
///
/// Issue #276: Partial image redaction (black out only the covered portion)
/// </summary>
public class ImageRedactor
{
    private readonly ILogger _logger;

    public ImageRedactor(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Redact portions of an XObject image that intersect with redaction areas.
    /// </summary>
    /// <param name="page">The PDF page containing the image.</param>
    /// <param name="imageOp">The image operation with bounding box info.</param>
    /// <param name="redactionAreas">Areas to redact in page coordinates.</param>
    /// <returns>True if the image was modified, false otherwise.</returns>
    public bool RedactXObjectImage(
        PdfPage page,
        ImageOperation imageOp,
        IReadOnlyList<PdfRectangle> redactionAreas)
    {
        try
        {
            // Find intersecting redaction areas
            var intersectingAreas = redactionAreas
                .Where(area => imageOp.BoundingBox.IntersectsWith(area))
                .ToList();

            if (intersectingAreas.Count == 0)
            {
                _logger.LogDebug("No redaction areas intersect with image {Name}", imageOp.XObjectName);
                return false;
            }

            // Get the XObject dictionary
            var xObject = GetXObject(page, imageOp.XObjectName);
            if (xObject == null)
            {
                _logger.LogWarning("Could not find XObject {Name} in page resources", imageOp.XObjectName);
                return false;
            }

            // Check if this is an image (not a form)
            var subtype = xObject.Elements.GetName("/Subtype");
            if (subtype != "/Image")
            {
                _logger.LogDebug("XObject {Name} is not an image (subtype: {Subtype})", imageOp.XObjectName, subtype);
                return false;
            }

            // Extract image dimensions
            int width = xObject.Elements.GetInteger("/Width");
            int height = xObject.Elements.GetInteger("/Height");

            if (width <= 0 || height <= 0)
            {
                _logger.LogWarning("Invalid image dimensions for {Name}: {W}x{H}", imageOp.XObjectName, width, height);
                return false;
            }

            // Decode the image to SKBitmap
            var bitmap = DecodeXObjectImage(xObject, width, height);
            if (bitmap == null)
            {
                _logger.LogWarning("Could not decode image {Name}", imageOp.XObjectName);
                return false;
            }

            try
            {
                // Transform redaction areas from page coordinates to image pixel coordinates
                var imageRedactionRects = TransformToImageCoordinates(
                    intersectingAreas, imageOp.BoundingBox, width, height);

                // Draw black rectangles over redaction areas
                using var canvas = new SKCanvas(bitmap);
                using var blackPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Fill
                };

                foreach (var rect in imageRedactionRects)
                {
                    _logger.LogDebug("Drawing black rectangle at ({X},{Y},{W},{H}) in image {Name}",
                        rect.Left, rect.Top, rect.Width, rect.Height, imageOp.XObjectName);
                    canvas.DrawRect(rect, blackPaint);
                }

                // Re-encode and update the XObject
                if (UpdateXObjectImage(xObject, bitmap))
                {
                    _logger.LogInformation("Successfully redacted portions of image {Name}", imageOp.XObjectName);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to update image {Name} after redaction", imageOp.XObjectName);
                    return false;
                }
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redacting XObject image {Name}", imageOp.XObjectName);
            return false;
        }
    }

    /// <summary>
    /// Redact portions of an inline image that intersect with redaction areas.
    /// Returns modified raw bytes for the BI...ID...EI sequence.
    /// </summary>
    /// <param name="inlineImageOp">The inline image operation.</param>
    /// <param name="redactionAreas">Areas to redact in page coordinates.</param>
    /// <returns>Modified raw bytes, or null if no modification was needed or possible.</returns>
    public byte[]? RedactInlineImage(
        InlineImageOperation inlineImageOp,
        IReadOnlyList<PdfRectangle> redactionAreas)
    {
        try
        {
            // Find intersecting redaction areas
            var intersectingAreas = redactionAreas
                .Where(area => inlineImageOp.BoundingBox.IntersectsWith(area))
                .ToList();

            if (intersectingAreas.Count == 0)
            {
                _logger.LogDebug("No redaction areas intersect with inline image");
                return null;
            }

            // Decode the inline image
            var bitmap = DecodeInlineImage(inlineImageOp);
            if (bitmap == null)
            {
                _logger.LogWarning("Could not decode inline image ({W}x{H})",
                    inlineImageOp.ImageWidth, inlineImageOp.ImageHeight);
                return null;
            }

            try
            {
                // Transform redaction areas from page coordinates to image pixel coordinates
                var imageRedactionRects = TransformToImageCoordinates(
                    intersectingAreas, inlineImageOp.BoundingBox,
                    inlineImageOp.ImageWidth, inlineImageOp.ImageHeight);

                // Draw black rectangles over redaction areas
                using var canvas = new SKCanvas(bitmap);
                using var blackPaint = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Fill
                };

                foreach (var rect in imageRedactionRects)
                {
                    _logger.LogDebug("Drawing black rectangle at ({X},{Y},{W},{H}) in inline image",
                        rect.Left, rect.Top, rect.Width, rect.Height);
                    canvas.DrawRect(rect, blackPaint);
                }

                // Re-encode to inline image format
                return EncodeAsInlineImage(bitmap, inlineImageOp);
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error redacting inline image");
            return null;
        }
    }

    /// <summary>
    /// Transform redaction areas from page coordinates to image pixel coordinates.
    /// </summary>
    private List<SKRect> TransformToImageCoordinates(
        IReadOnlyList<PdfRectangle> pageAreas,
        PdfRectangle imageBounds,
        int imageWidth,
        int imageHeight)
    {
        var result = new List<SKRect>();

        // Calculate scale factors
        // PDF image is stretched/positioned via CTM to imageBounds
        double scaleX = imageWidth / imageBounds.Width;
        double scaleY = imageHeight / imageBounds.Height;

        foreach (var pageArea in pageAreas)
        {
            // Calculate intersection with image bounds
            double intersectLeft = Math.Max(pageArea.Left, imageBounds.Left);
            double intersectRight = Math.Min(pageArea.Right, imageBounds.Right);
            double intersectBottom = Math.Max(pageArea.Bottom, imageBounds.Bottom);
            double intersectTop = Math.Min(pageArea.Top, imageBounds.Top);

            if (intersectLeft >= intersectRight || intersectBottom >= intersectTop)
                continue; // No intersection

            // Transform to image coordinates
            // PDF: origin at bottom-left, Y increases upward
            // Image: origin at top-left, Y increases downward
            double imageLeft = (intersectLeft - imageBounds.Left) * scaleX;
            double imageRight = (intersectRight - imageBounds.Left) * scaleX;
            // Note: PDF Y is bottom-up, image Y is top-down
            double imageTop = (imageBounds.Top - intersectTop) * scaleY;
            double imageBottom = (imageBounds.Top - intersectBottom) * scaleY;

            // Clamp to image bounds
            imageLeft = Math.Max(0, Math.Min(imageLeft, imageWidth));
            imageRight = Math.Max(0, Math.Min(imageRight, imageWidth));
            imageTop = Math.Max(0, Math.Min(imageTop, imageHeight));
            imageBottom = Math.Max(0, Math.Min(imageBottom, imageHeight));

            if (imageLeft < imageRight && imageTop < imageBottom)
            {
                result.Add(new SKRect((float)imageLeft, (float)imageTop, (float)imageRight, (float)imageBottom));
            }
        }

        return result;
    }

    /// <summary>
    /// Get an XObject from page resources by name.
    /// </summary>
    private PdfDictionary? GetXObject(PdfPage page, string xObjectName)
    {
        var resources = page.Resources;
        if (resources == null) return null;

        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects == null) return null;

        // Remove leading slash if present
        var name = xObjectName.StartsWith("/") ? xObjectName : "/" + xObjectName;

        // Get reference and resolve
        var item = xObjects.Elements[name];
        if (item is PdfReference reference)
        {
            return reference.Value as PdfDictionary;
        }
        return item as PdfDictionary;
    }

    /// <summary>
    /// Decode an XObject image to an SKBitmap.
    /// </summary>
    private SKBitmap? DecodeXObjectImage(PdfDictionary xObject, int width, int height)
    {
        try
        {
            // Get the image stream data
            var stream = xObject.Stream;
            if (stream == null || stream.Value == null || stream.Value.Length == 0)
            {
                _logger.LogWarning("XObject has no stream data");
                return null;
            }

            // Get color space and bits per component
            var colorSpace = xObject.Elements.GetName("/ColorSpace") ?? "/DeviceRGB";
            int bitsPerComponent = xObject.Elements.GetInteger("/BitsPerComponent");
            if (bitsPerComponent == 0) bitsPerComponent = 8;

            // Decompress the stream (PDFsharp handles this)
            byte[] imageData;
            try
            {
                imageData = stream.Value;
            }
            catch
            {
                // If decompression fails, try to use raw bytes
                imageData = stream.Value;
            }

            // Decode based on color space
            return DecodeImageData(imageData, width, height, colorSpace, bitsPerComponent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode XObject image");
            return null;
        }
    }

    /// <summary>
    /// Decode inline image to an SKBitmap.
    /// </summary>
    private SKBitmap? DecodeInlineImage(InlineImageOperation inlineImageOp)
    {
        try
        {
            // Parse the BI...ID...EI sequence to extract image data
            var imageData = ExtractInlineImageData(inlineImageOp);
            if (imageData == null || imageData.Length == 0)
            {
                _logger.LogWarning("Could not extract inline image data");
                return null;
            }

            // Decode based on color space
            string colorSpace = inlineImageOp.ColorSpace ?? "RGB";
            int bitsPerComponent = inlineImageOp.BitsPerComponent > 0 ? inlineImageOp.BitsPerComponent : 8;

            // Map abbreviated color space names
            colorSpace = colorSpace switch
            {
                "G" => "/DeviceGray",
                "RGB" => "/DeviceRGB",
                "CMYK" => "/DeviceCMYK",
                _ => "/" + colorSpace
            };

            return DecodeImageData(imageData, inlineImageOp.ImageWidth, inlineImageOp.ImageHeight,
                colorSpace, bitsPerComponent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode inline image");
            return null;
        }
    }

    /// <summary>
    /// Decode raw image data to an SKBitmap based on color space.
    /// </summary>
    private SKBitmap? DecodeImageData(byte[] data, int width, int height, string colorSpace, int bitsPerComponent)
    {
        try
        {
            // First try to decode as an encoded image format (JPEG, PNG, etc.)
            var bitmap = SKBitmap.Decode(data);
            if (bitmap != null)
            {
                _logger.LogDebug("Decoded image as encoded format ({W}x{H})", bitmap.Width, bitmap.Height);
                return bitmap;
            }

            // Fall back to raw pixel data decoding
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            bitmap = new SKBitmap(info);

            if (colorSpace == "/DeviceRGB" && bitsPerComponent == 8)
            {
                // RGB 8-bit
                int expectedBytes = width * height * 3;
                if (data.Length >= expectedBytes)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = (y * width + x) * 3;
                            byte r = data[srcIdx];
                            byte g = data[srcIdx + 1];
                            byte b = data[srcIdx + 2];
                            bitmap.SetPixel(x, y, new SKColor(r, g, b));
                        }
                    }
                    return bitmap;
                }
            }
            else if (colorSpace == "/DeviceGray" && bitsPerComponent == 8)
            {
                // Grayscale 8-bit
                int expectedBytes = width * height;
                if (data.Length >= expectedBytes)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            byte gray = data[y * width + x];
                            bitmap.SetPixel(x, y, new SKColor(gray, gray, gray));
                        }
                    }
                    return bitmap;
                }
            }
            else if (colorSpace == "/DeviceCMYK" && bitsPerComponent == 8)
            {
                // CMYK 8-bit - convert to RGB
                int expectedBytes = width * height * 4;
                if (data.Length >= expectedBytes)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = (y * width + x) * 4;
                            byte c = data[srcIdx];
                            byte m = data[srcIdx + 1];
                            byte yy = data[srcIdx + 2];
                            byte k = data[srcIdx + 3];

                            // Simple CMYK to RGB conversion
                            byte r = (byte)(255 * (1 - c / 255.0) * (1 - k / 255.0));
                            byte g = (byte)(255 * (1 - m / 255.0) * (1 - k / 255.0));
                            byte b = (byte)(255 * (1 - yy / 255.0) * (1 - k / 255.0));
                            bitmap.SetPixel(x, y, new SKColor(r, g, b));
                        }
                    }
                    return bitmap;
                }
            }

            _logger.LogWarning("Unsupported image format: {ColorSpace} {BPC}bpc, data length {Len} vs expected",
                colorSpace, bitsPerComponent, data.Length);
            bitmap.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode image data");
            return null;
        }
    }

    /// <summary>
    /// Extract image data from inline image raw bytes.
    /// </summary>
    private byte[]? ExtractInlineImageData(InlineImageOperation inlineImageOp)
    {
        var rawBytes = inlineImageOp.RawBytes;
        if (rawBytes == null || rawBytes.Length == 0)
            return null;

        // Find ID marker (image data start)
        int idIndex = -1;
        for (int i = 0; i < rawBytes.Length - 1; i++)
        {
            if (rawBytes[i] == 'I' && rawBytes[i + 1] == 'D' &&
                (i == 0 || char.IsWhiteSpace((char)rawBytes[i - 1])))
            {
                idIndex = i + 2; // Skip "ID" and whitespace
                if (idIndex < rawBytes.Length && char.IsWhiteSpace((char)rawBytes[idIndex]))
                    idIndex++;
                break;
            }
        }

        if (idIndex < 0)
            return null;

        // Find EI marker (image data end)
        int eiIndex = -1;
        for (int i = rawBytes.Length - 2; i >= idIndex; i--)
        {
            if (rawBytes[i] == 'E' && rawBytes[i + 1] == 'I' &&
                (i == idIndex || char.IsWhiteSpace((char)rawBytes[i - 1])))
            {
                eiIndex = i;
                break;
            }
        }

        if (eiIndex <= idIndex)
            return null;

        // Extract image data
        int dataLength = eiIndex - idIndex;
        // Trim trailing whitespace before EI
        while (dataLength > 0 && char.IsWhiteSpace((char)rawBytes[idIndex + dataLength - 1]))
            dataLength--;

        var imageData = new byte[dataLength];
        Array.Copy(rawBytes, idIndex, imageData, 0, dataLength);

        // Handle filters
        var filter = inlineImageOp.Filter;
        if (!string.IsNullOrEmpty(filter))
        {
            imageData = DecodeFilter(imageData, filter);
        }

        return imageData;
    }

    /// <summary>
    /// Decode image data based on filter.
    /// </summary>
    private byte[] DecodeFilter(byte[] data, string filter)
    {
        try
        {
            return filter switch
            {
                "AHx" or "ASCIIHexDecode" => DecodeAsciiHex(data),
                "A85" or "ASCII85Decode" => DecodeAscii85(data),
                "Fl" or "FlateDecode" => DecodeFlate(data),
                // For other filters, return as-is and hope SkiaSharp can handle it
                _ => data
            };
        }
        catch
        {
            return data;
        }
    }

    private byte[] DecodeAsciiHex(byte[] data)
    {
        var result = new List<byte>();
        int highNibble = -1;

        foreach (byte b in data)
        {
            char c = (char)b;
            if (c == '>') break;

            int nibble;
            if (c >= '0' && c <= '9')
                nibble = c - '0';
            else if (c >= 'A' && c <= 'F')
                nibble = c - 'A' + 10;
            else if (c >= 'a' && c <= 'f')
                nibble = c - 'a' + 10;
            else
                continue; // Skip whitespace

            if (highNibble < 0)
                highNibble = nibble;
            else
            {
                result.Add((byte)((highNibble << 4) | nibble));
                highNibble = -1;
            }
        }

        // Handle odd number of hex digits
        if (highNibble >= 0)
            result.Add((byte)(highNibble << 4));

        return result.ToArray();
    }

    private byte[] DecodeAscii85(byte[] data)
    {
        // Simple ASCII85 decoder
        var result = new List<byte>();
        var group = new List<byte>();

        foreach (byte b in data)
        {
            char c = (char)b;
            if (c == '~') break; // End marker

            if (c == 'z')
            {
                // Special case: z = 00000000
                result.AddRange(new byte[] { 0, 0, 0, 0 });
                continue;
            }

            if (c < '!' || c > 'u')
                continue; // Skip whitespace

            group.Add((byte)(c - '!'));

            if (group.Count == 5)
            {
                uint value = 0;
                for (int i = 0; i < 5; i++)
                    value = value * 85 + group[i];

                result.Add((byte)(value >> 24));
                result.Add((byte)(value >> 16));
                result.Add((byte)(value >> 8));
                result.Add((byte)value);
                group.Clear();
            }
        }

        // Handle partial group
        if (group.Count > 1)
        {
            while (group.Count < 5)
                group.Add(84); // Pad with 'u' - 33 = 84

            uint value = 0;
            for (int i = 0; i < 5; i++)
                value = value * 85 + group[i];

            for (int i = 0; i < group.Count - 1; i++)
                result.Add((byte)(value >> (24 - i * 8)));
        }

        return result.ToArray();
    }

    private byte[] DecodeFlate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();

        // Skip zlib header (2 bytes) if present
        if (data.Length >= 2 && (data[0] & 0x0F) == 8)
        {
            input.ReadByte();
            input.ReadByte();
        }

        using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        deflate.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Update an XObject image with modified bitmap data.
    /// </summary>
    private bool UpdateXObjectImage(PdfDictionary xObject, SKBitmap bitmap)
    {
        try
        {
            // Encode bitmap as RGB data
            var rgbData = new byte[bitmap.Width * bitmap.Height * 3];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    int idx = (y * bitmap.Width + x) * 3;
                    rgbData[idx] = pixel.Red;
                    rgbData[idx + 1] = pixel.Green;
                    rgbData[idx + 2] = pixel.Blue;
                }
            }

            // Compress with Deflate
            using var output = new MemoryStream();
            using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
            {
                deflate.Write(rgbData, 0, rgbData.Length);
            }
            var compressedData = output.ToArray();

            // Update XObject properties
            xObject.Elements.SetInteger("/Width", bitmap.Width);
            xObject.Elements.SetInteger("/Height", bitmap.Height);
            xObject.Elements.SetName("/ColorSpace", "/DeviceRGB");
            xObject.Elements.SetInteger("/BitsPerComponent", 8);
            xObject.Elements.SetName("/Filter", "/FlateDecode");

            // Remove SMask if present (we're replacing with opaque RGB)
            if (xObject.Elements.ContainsKey("/SMask"))
                xObject.Elements.Remove("/SMask");

            // Update stream data
            xObject.CreateStream(compressedData);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update XObject image");
            return false;
        }
    }

    /// <summary>
    /// Encode a bitmap as inline image bytes (BI...ID...EI format).
    /// </summary>
    private byte[]? EncodeAsInlineImage(SKBitmap bitmap, InlineImageOperation originalOp)
    {
        try
        {
            // Encode as RGB data
            var rgbData = new byte[bitmap.Width * bitmap.Height * 3];
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    int idx = (y * bitmap.Width + x) * 3;
                    rgbData[idx] = pixel.Red;
                    rgbData[idx + 1] = pixel.Green;
                    rgbData[idx + 2] = pixel.Blue;
                }
            }

            // Build BI...ID...EI sequence
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, System.Text.Encoding.ASCII);

            writer.Write("BI\n");
            writer.Write($"/W {bitmap.Width}\n");
            writer.Write($"/H {bitmap.Height}\n");
            writer.Write("/CS /RGB\n");
            writer.Write("/BPC 8\n");
            writer.Write("/F /AHx\n"); // Use ASCII Hex for simplicity
            writer.Write("ID\n");
            writer.Flush();

            // Write hex-encoded data
            foreach (byte b in rgbData)
            {
                ms.WriteByte((byte)ToHexChar(b >> 4));
                ms.WriteByte((byte)ToHexChar(b & 0x0F));
            }
            ms.WriteByte((byte)'>'); // End of hex data

            writer.Write("\nEI");
            writer.Flush();

            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to encode inline image");
            return null;
        }
    }

    private static char ToHexChar(int nibble) => nibble < 10 ? (char)('0' + nibble) : (char)('A' + nibble - 10);
}
