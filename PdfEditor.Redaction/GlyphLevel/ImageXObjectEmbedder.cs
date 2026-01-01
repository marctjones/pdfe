using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using SkiaSharp;

namespace PdfEditor.Redaction.GlyphLevel;

/// <summary>
/// Embeds clipped glyph images into PDF as Image XObjects.
/// Handles transparency (soft mask) for partial glyph preservation.
/// Part of issue #209: Embed clipped glyph image as PDF XObject.
/// </summary>
public class ImageXObjectEmbedder
{
    private int _imageCounter;

    /// <summary>
    /// Embed an image with transparency at the specified location on a page.
    /// </summary>
    /// <param name="page">The PdfSharp page to add the image to.</param>
    /// <param name="image">The SkiaSharp bitmap to embed (with alpha channel).</param>
    /// <param name="bounds">The destination bounds in PDF coordinates (bottom-left origin).</param>
    /// <returns>The name of the XObject resource, or null if embedding failed.</returns>
    public string? EmbedImage(PdfPage page, SKBitmap image, PdfRectangle bounds)
    {
        if (page == null || image == null)
            return null;

        try
        {
            // Generate unique image name
            var imageName = $"GlyphImg{++_imageCounter}";

            // Create the image XObject with transparency support
            var xImage = CreateImageXObject(page.Owner, image);
            if (xImage == null)
                return null;

            // Add XObject to page resources
            AddImageToPageResources(page, imageName, xImage);

            // Generate Do operator for content stream
            // The caller is responsible for adding this to the content stream
            // Format: q width 0 0 height x y cm /imageName Do Q
            return imageName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Generate the PDF content stream operators to draw an embedded image.
    /// </summary>
    /// <param name="imageName">The XObject name from EmbedImage.</param>
    /// <param name="bounds">The destination bounds in PDF coordinates.</param>
    /// <returns>PDF operators string to insert into content stream.</returns>
    public static string GetDrawOperators(string imageName, PdfRectangle bounds)
    {
        // q = save graphics state
        // cm = concat transformation matrix [a b c d e f]
        //      For simple positioning: [width 0 0 height x y]
        // Do = paint XObject
        // Q = restore graphics state
        return $"q {bounds.Width:F3} 0 0 {bounds.Height:F3} {bounds.Left:F3} {bounds.Bottom:F3} cm /{imageName} Do Q\n";
    }

    /// <summary>
    /// Create a PDF Image XObject from an SKBitmap with alpha channel support.
    /// </summary>
    private PdfDictionary? CreateImageXObject(PdfDocument document, SKBitmap image)
    {
        try
        {
            // Extract RGB data and alpha mask
            var (rgbData, alphaData, hasTransparency) = ExtractImageData(image);

            // Create the main image XObject
            var imageXObject = new PdfDictionary(document);
            imageXObject.Elements.SetName("/Type", "/XObject");
            imageXObject.Elements.SetName("/Subtype", "/Image");
            imageXObject.Elements.SetInteger("/Width", image.Width);
            imageXObject.Elements.SetInteger("/Height", image.Height);
            imageXObject.Elements.SetName("/ColorSpace", "/DeviceRGB");
            imageXObject.Elements.SetInteger("/BitsPerComponent", 8);
            imageXObject.Elements.SetName("/Filter", "/FlateDecode");

            // Create stream for image data using PDFsharp's method
            var compressedRgb = CompressData(rgbData);
            imageXObject.CreateStream(compressedRgb);

            // Add to document
            document.Internals.AddObject(imageXObject);

            // If image has transparency, create soft mask
            if (hasTransparency && alphaData != null)
            {
                var softMask = CreateSoftMask(document, image.Width, image.Height, alphaData);
                if (softMask != null)
                {
                    imageXObject.Elements.SetReference("/SMask", softMask);
                }
            }

            return imageXObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Create a soft mask (alpha channel) XObject.
    /// </summary>
    private PdfDictionary? CreateSoftMask(PdfDocument document, int width, int height, byte[] alphaData)
    {
        try
        {
            var softMask = new PdfDictionary(document);
            softMask.Elements.SetName("/Type", "/XObject");
            softMask.Elements.SetName("/Subtype", "/Image");
            softMask.Elements.SetInteger("/Width", width);
            softMask.Elements.SetInteger("/Height", height);
            softMask.Elements.SetName("/ColorSpace", "/DeviceGray");
            softMask.Elements.SetInteger("/BitsPerComponent", 8);
            softMask.Elements.SetName("/Filter", "/FlateDecode");

            // Compress and create stream
            var compressedAlpha = CompressData(alphaData);
            softMask.CreateStream(compressedAlpha);

            document.Internals.AddObject(softMask);
            return softMask;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Extract RGB and alpha data from an SKBitmap.
    /// </summary>
    private (byte[] RgbData, byte[]? AlphaData, bool HasTransparency) ExtractImageData(SKBitmap image)
    {
        int width = image.Width;
        int height = image.Height;
        var rgbData = new byte[width * height * 3];
        var alphaData = new byte[width * height];
        bool hasTransparency = false;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel = image.GetPixel(x, y);
                int rgbIndex = (y * width + x) * 3;
                int alphaIndex = y * width + x;

                rgbData[rgbIndex] = pixel.Red;
                rgbData[rgbIndex + 1] = pixel.Green;
                rgbData[rgbIndex + 2] = pixel.Blue;
                alphaData[alphaIndex] = pixel.Alpha;

                if (pixel.Alpha < 255)
                {
                    hasTransparency = true;
                }
            }
        }

        return (rgbData, hasTransparency ? alphaData : null, hasTransparency);
    }

    /// <summary>
    /// Compress data using Deflate (for PDF FlateDecode filter).
    /// </summary>
    private byte[] CompressData(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Add an image XObject to a page's resources.
    /// </summary>
    private void AddImageToPageResources(PdfPage page, string imageName, PdfDictionary imageXObject)
    {
        // Get Resources - PdfSharp creates it automatically if needed
        var resources = page.Resources;

        // Get or create XObject dictionary
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects == null)
        {
            xObjects = new PdfDictionary(page.Owner);
            resources.Elements.SetObject("/XObject", xObjects);
        }

        // Add image reference
        xObjects.Elements.SetReference("/" + imageName, imageXObject);
    }
}
