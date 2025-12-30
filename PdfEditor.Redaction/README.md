# PdfEditor.Redaction

A .NET library for **TRUE glyph-level PDF redaction**. Unlike simple overlay-based redaction, this library removes text from the PDF content stream structure, making it unrecoverable by text extraction tools.

## Features

- **True Glyph Removal**: Text is removed from PDF content streams, not just covered with black boxes
- **PDF/A Compliance**: Preserves PDF/A metadata and compliance during redaction
- **In-Memory API**: Redact pages in-place without file I/O (ideal for GUI applications)
- **File-Based API**: Simple input→output file redaction for CLI/batch processing
- **Annotation Redaction**: Removes annotations (comments, form fields) in redaction areas
- **Metadata Sanitization**: Cleans document metadata to prevent information leakage

## Installation

```bash
# From the solution root
dotnet add reference PdfEditor.Redaction/PdfEditor.Redaction.csproj
```

## Quick Start

### File-Based API (CLI / Batch Processing)

```csharp
using PdfEditor.Redaction;

var redactor = new TextRedactor();

// Redact all instances of text
var result = redactor.RedactText(
    "/path/to/input.pdf",
    "/path/to/output.pdf",
    "CONFIDENTIAL");

Console.WriteLine($"Redacted {result.RedactionCount} instances");
```

### In-Memory API (GUI / Interactive Applications)

```csharp
using PdfEditor.Redaction;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

var redactor = new TextRedactor();

// Open document
var document = PdfReader.Open("/path/to/input.pdf", PdfDocumentOpenMode.Modify);
var page = document.Pages[0];

// Extract letters once (for performance with multiple redactions)
var letters = redactor.ExtractLettersFromPage(page);

// Redact multiple areas
var area1 = new PdfRectangle(100, 700, 200, 720);
var area2 = new PdfRectangle(100, 650, 200, 670);

var result = redactor.RedactPage(
    page,
    new[] { area1, area2 },
    new RedactionOptions { UseGlyphLevelRedaction = true },
    pageLetters: letters);

// Save when done
document.Save("/path/to/output.pdf");
```

## API Overview

### ITextRedactor Interface

| Method | Description |
|--------|-------------|
| `RedactText()` | Redact all instances of text (file-based) |
| `RedactLocations()` | Redact specific locations (file-based) |
| `RedactPage()` | Redact areas on a page in-place (in-memory) |
| `ExtractLettersFromPage()` | Extract letters for glyph-level redaction |
| `SanitizeDocumentMetadata()` | Clean document metadata |

### RedactionOptions

| Option | Default | Description |
|--------|---------|-------------|
| `UseGlyphLevelRedaction` | `true` | Remove individual glyphs vs whole operations |
| `DrawVisualMarker` | `true` | Draw black rectangle at redaction sites |
| `SanitizeMetadata` | `true` | Clean Info dictionary and XMP metadata |
| `PreservePdfAMetadata` | `true` | Maintain PDF/A identification |
| `RedactAnnotations` | `true` | Remove annotations in redaction areas |
| `RemovePdfATransparency` | `true` | Remove transparency for PDF/A-1 compliance |
| `CaseSensitive` | `true` | Case-sensitive text matching |
| `MarkerColor` | `(0,0,0)` | RGB color for visual markers |

## Coordinate System

**IMPORTANT**: All library APIs use PDF coordinate system.

| Property | Value |
|----------|-------|
| Origin | Bottom-left corner |
| Y Direction | Increases upward |
| Units | Points (72 points = 1 inch) |

### Converting from GUI Coordinates (Top-Left Origin)

```csharp
// GUI/Screen coordinates (top-left origin)
double guiX = 100, guiY = 50;
double guiWidth = 200, guiHeight = 30;

// Convert to PDF coordinates (bottom-left origin)
double pageHeight = page.Height.Point;
var pdfRect = new PdfRectangle(
    left: guiX,
    bottom: pageHeight - guiY - guiHeight,
    right: guiX + guiWidth,
    top: pageHeight - guiY
);
```

### Visual Representation

```
PDF Coordinates          GUI Coordinates
     ↑ Y+                     (0,0)→ X+
     │                            ↓
     │                            Y+
(0,0)└───→ X+
```

## Performance Optimization

### Letter Caching for Multiple Redactions

Extracting letters is expensive. Cache them for multiple redactions on the same page:

```csharp
// ❌ Inefficient: Extracts letters every time
for (int i = 0; i < 10; i++)
{
    redactor.RedactPage(page, new[] { areas[i] });  // Slow!
}

// ✅ Efficient: Extract once, reuse
var letters = redactor.ExtractLettersFromPage(page);
for (int i = 0; i < 10; i++)
{
    redactor.RedactPage(page, new[] { areas[i] }, pageLetters: letters);  // Fast!
}
```

**Performance**: ~10x faster for multiple redactions on the same page.

## PDF/A Compliance

The library maintains PDF/A compliance during redaction:

- **PDF/A-1a/1b**: Transparency removed, metadata preserved
- **PDF/A-2a/2b/2u**: Full support with transparency
- **PDF/A-3**: Full support including embedded files

```csharp
var options = new RedactionOptions
{
    PreservePdfAMetadata = true,     // Keep PDF/A identification
    RemovePdfATransparency = true    // Remove transparency for PDF/A-1
};
```

## Error Handling

```csharp
var result = redactor.RedactPage(page, areas);

if (!result.Success)
{
    Console.WriteLine($"Redaction failed: {result.ErrorMessage}");
    return;
}

Console.WriteLine($"Redacted {result.RedactionCount} areas");
foreach (var detail in result.Details)
{
    Console.WriteLine($"  Page {detail.PageNumber}: '{detail.RedactedText}'");
}
```

## Security Considerations

1. **True Glyph Removal**: Text is removed from content streams, not just hidden
2. **Metadata Sanitization**: Enable `SanitizeMetadata` to remove Info dict and XMP
3. **Annotation Redaction**: Enable `RedactAnnotations` to remove form fields and comments
4. **Verification**: Use `pdftotext` or PdfPig to verify text is truly removed

```bash
# Verify redaction worked
pdftotext output.pdf - | grep -i "CONFIDENTIAL"
# Should return nothing if redaction succeeded
```

## Dependencies

| Library | Purpose | License |
|---------|---------|---------|
| PdfPig | Text extraction, letter positions | Apache 2.0 |
| PDFsharp | PDF manipulation | MIT |

## Related Documentation

- [Redaction Engine](../wiki/Redaction-Engine.md) - How glyph-level redaction works
- [PDF Coordinate Systems](../wiki/PDF-Coordinate-Systems.md) - Coordinate conversion details
- [CLI Tool](../wiki/CLI-Tool.md) - Command-line interface documentation

## License

MIT License - see LICENSE file in the repository root.
