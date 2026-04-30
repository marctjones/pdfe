<p align="center">
  <img src="PdfEditor/Assets/pdfe_logo.svg" alt="PDFE Logo" width="128" height="128">
</p>

# pdfe

A cross-platform PDF editor and pure-.NET PDF framework, built with **C# + .NET 10 + Avalonia UI** and shipped with **true content-level redaction**, **AcroForm editing/authoring**, and **PDF 2.0 conformance**.

[![Release](https://img.shields.io/github/v/release/marctjones/pdfe)](https://github.com/marctjones/pdfe/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-3500%2B%20passing-brightgreen)](Pdfe.Core.Tests)
[![Build](https://img.shields.io/badge/build-0%20warnings-brightgreen)](Pdfe.Core)

> **v2.1** completed all 15 PDF 2.0 conformance phases (CID fonts, AcroForms, color spaces, optional content groups, structure tree, embedded files, page labels, named destinations) and added **AcroForm editing and authoring** — fill, flatten, click-to-create, and heuristic auto-detect. The PDF stack is pdfe-owned end-to-end — `Pdfe.Core` (parser/writer), `Pdfe.Rendering` (Skia), and `Pdfe.Ocr` (system tesseract shell) — with no third-party PDF dependencies. See [CHANGELOG.md](CHANGELOG.md) for full release notes.

## What's in the box

```
Pdfe.Core/        Pure-.NET PDF parser, writer, content-stream library
Pdfe.Rendering/   SkiaSharp-based renderer (text, images, paths, transparency)
Pdfe.Ocr/         OCR via the system `tesseract` CLI + differential-OCR auditor
Pdfe.Cli/         `pdfe` command-line tool
PdfEditor/        Cross-platform Avalonia desktop app
```

The libraries are usable independently — embed `Pdfe.Core` if you only need parsing and redaction, or `Pdfe.Rendering` if you need page rasterization.

## Features

### Desktop app
- Open, view, navigate PDFs with smooth Skia rendering
- Page manipulation (add, remove, rotate; 90°/180°/270°)
- Text selection and copy with letter-level positions
- Find with highlights and navigation
- Zoom modes: fit width, fit page, actual size, free zoom
- Page thumbnails sidebar
- **AcroForm editing** — click any text/checkbox/dropdown widget and edit inline; save to keep the values interactive or flatten to bake them in
- **AcroForm authoring** — drag-rect on a page to create new fields (Text / Checkbox / Choice / Signature); auto-detect underline placeholders and empty squares as fields
- Reveal Hidden Text — yellow highlights for structural detections (text covered by rectangles), orange for differential-OCR recoveries (text inside rasterized images)
- Digital signature verification
- Bates numbering
- Roslyn-based GUI scripting for automation

### Glyph-level redaction
**Text is removed from the PDF structure, not just visually covered.**

- Glyph-level removal — individual glyphs are excised from content streams
- Image XObject redaction — image overlays that intersect a redaction area are removed, not just blacked out
- External tools (`pdftotext`, mutool, Acrobat copy-paste) cannot recover redacted content
- Mark-then-apply workflow with red dashed previews and a Clipboard History sidebar showing what was removed
- Original protection — defaults the save dialog to `filename_REDACTED.pdf`
- OCG-aware — `RedactText` defaults to `includeHiddenLayers=true` so hidden optional content groups don't slip past
- Verified against real-world fixtures (CT birth certificate, government forms) at the pixel and content-stream level

### AcroForm editing & authoring
- `PdfField.SetValue(string?)` mutates `/V`, sets `/NeedAppearances`, updates `/AS` for buttons; throws on read-only and signature fields
- `PdfDocument.FlattenAcroForm()` bakes values into static page content and strips widget annotations
- `AcroFormAuthoring` extension methods: `AddTextField`, `AddCheckBox`, `AddChoiceField`, `AddSignatureField` — auto-create the AcroForm dict and `/DR/Font/Helv` on first call
- `PdfFormAutoDetector` heuristically suggests fields where the page has horizontal underlines or empty checkbox-sized outlines (Acrobat-style "Prepare Form")
- `PdfDocument.ScrubMetadata(scrubAttachments: true)` strips Info dict, XMP, and embedded files in one call — important when redacted documents may carry the data they were redacted of in attachments (ZUGFeRD, Factur-X)

### CLI (`pdfe`)
```bash
pdfe info              <file>
pdfe text              <file>
pdfe letters           <file>
pdfe render            <file>           -o out.png  [--page N] [--dpi N]
pdfe draw              <file>                                 # graphics-API demo
pdfe redact            <input> <output> <text>  [--case-sensitive]
pdfe fill-form         <input> <output> --field Name=Value [...] [--flatten]
pdfe add-field         <input> <output> --type T --name N --page P --rect "l,b,r,t" [--value v] [--option o]...
pdfe autodetect-fields <input> [output] [--apply]
pdfe audit             <file>           [--deep] [--json]
pdfe ocr               <file>
pdfe demo
```

`audit --deep` runs differential OCR — renders the page twice (once with overlays stripped) and diffs the OCR text — to catch words hidden inside rasterized images by an opaque overlay (the rasterized analogue of a black-box redaction).

### Renderer coverage
The Skia renderer has been smoke-tested against a real-world corpus and renders essentially identically to mutool/Acrobat at the structural level:

| PDF type | Notes |
|---|---|
| State-issued government forms (CT birth-cert, DS-82) | TJ kerning, Tw column alignment, raster backgrounds |
| SCOTUS opinions | Non-uniform Tm, Type1 PostScript subsets |
| IRS Form 1040 + Instructions | Type0/Identity-H, Acrobat-distilled, 180° footers |
| CDC VIS | Embedded TrueType, Wingdings dingbats |
| Pragmatic Bookshelf books (XEP) | 455-page multi-font CFF subsets, ZapfDingbats |
| Multilingual CJK | zh-Hans, zh-Hant, ja, ko via Noto Serif CJK |

See [`Pdfe.Rendering.Tests/Visual/`](Pdfe.Rendering.Tests/Visual) and [`PdfEditor.Tests/UI/baselines/`](PdfEditor.Tests/UI/baselines) for the regression baselines.

### PDF 2.0 conformance
All 15 conformance phases shipped:

| Phase | Feature |
|---|---|
| 3 | Standard 14 fonts via embedded core metrics |
| 4 | Image XObjects (DCT/Flate/CCITTFax) |
| 5 | Composite (Type0/CID) fonts with Identity-H/V |
| 6 | Inline images (BI/ID/EI) |
| 7 | Color spaces (DeviceRGB/CMYK/Gray, ICCBased, Indexed, CalRGB) |
| 8 | Embedded TrueType + raw-CFF/Type1C |
| 9 | CFF parser + MVP subsetter |
| 10 | Annotations (Text, Link, Highlight, Underline, StrikeOut, Squiggly, Stamp, Ink, Widget) |
| 11 | AcroForms — read, fill, flatten, author, auto-detect |
| 12 | Optional Content Groups (OCGs) + structure tree (read-only, redaction-aware) |
| 13 | Document-level embedded files / portfolios — read + scrub |
| 14 | Page labels (`/PageLabels`) and named destinations |
| 15 | Conformance harness — corpus parse + render + round-trip + redaction regression |

## Installation

### From releases

Download the latest from [GitHub Releases](https://github.com/marctjones/pdfe/releases) and run the executable for your platform.

### From source

```bash
git clone https://github.com/marctjones/pdfe.git
cd pdfe
dotnet restore
dotnet run --project PdfEditor
```

`dotnet 10.0` SDK required. No additional native dependencies — the renderer is pure SkiaSharp, the OCR auditor shells out to the system `tesseract` binary if installed (skipped gracefully if not).

## Usage

### Desktop redaction (mark-then-apply)

1. **Enable redaction mode** — toolbar button or press `R`
2. **Mark areas** — click and drag (red dashed outline = pending)
3. **Review pending marks** — sidebar shows preview text
4. **Apply** — toolbar button or `Enter` (permanent removal)
5. **Verify** — Clipboard History panel shows exactly what came out
6. **Save** — defaults to `filename_REDACTED.pdf`

Multiple areas across multiple pages can be marked and applied as a single batch.

### Form fill (existing AcroForm)

1. Open a PDF with form fields. Each field becomes an inline editor on the page (TextBox / ComboBox / CheckBox).
2. Edit a value — commits on Enter or focus loss; the document is marked dirty.
3. Save to keep the form interactive, or use `PdfDocument.FlattenAcroForm` (or `pdfe fill-form … --flatten`) to bake the values into static page content.

### Form authoring (create new fields)

1. Click **📋 Add Field** in the toolbar (or call `doc.AddTextField(...)` etc. from code).
2. Pick a field type from the combo (Text / Checkbox / Choice / Signature).
3. Drag a rect on the page — the new field appears immediately and is editable.
4. **🪄 Auto-detect** scans the current document for likely field positions — long horizontal strokes (text-field underlines) and small square outlines (checkboxes) — and creates them in one click.

### Reveal Hidden Text

`Tools → Reveal Hidden Text` finds text that's been visually hidden by overlays:

- **Yellow boxes** — structural detections from `Pdfe.Core.Text.Segmentation.HiddenTextDetector` (text covered by later filled rectangles, the classic bad-redaction pattern)
- **Orange boxes** — differential-OCR recoveries (`Pdfe.Ocr.DifferentialOcrAuditor`) for text hidden inside rasterized images by an opaque overlay

Useful for auditing third-party redactions before relying on them.

### Keyboard shortcuts

Press **F1** to view all in-app.

| Category | Action | Shortcut |
|---|---|---|
| File | Open / Save / Save As / Close | `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` / `Ctrl+W` |
| Edit | Find / Find Next / Find Previous | `Ctrl+F` / `F3` / `Shift+F3` |
| View | Zoom In / Out / Actual / Fit Width / Fit Page | `Ctrl+Plus/Minus/0/1/2` |
| Navigation | Next/Previous/First/Last Page | `Page Down/Up`, `Home`, `End` |
| Modes | Redaction / Text Selection / Apply | `R` / `T` / `Enter` |
| Pages | Rotate Left / Right | `Ctrl+L` / `Ctrl+R` |

### CLI examples

```bash
# Render page 1 of a PDF at 200 DPI
pdfe render report.pdf -o report-p1.png --page 1 --dpi 200

# Glyph-level redact a phrase
pdfe redact report.pdf report-redacted.pdf "ACCOUNT 9876"

# Audit a "redacted" PDF for hidden text leftovers — both structural and rasterized
pdfe audit purportedly-redacted.pdf --deep --json

# Fill an AcroForm and flatten so the result is no longer interactive
pdfe fill-form blank-w9.pdf w9-filled.pdf --field Name=Acme --field EIN=12-3456789 --flatten

# Add a text field to an existing PDF
pdfe add-field invoice.pdf invoice-with-form.pdf \
  --type Text --name CustomerNote --page 1 --rect "72,200,540,260"

# Auto-detect form fields on a Word-exported PDF and apply them
pdfe autodetect-fields exported-from-word.pdf form-ready.pdf --apply

# Extract text from a scanned PDF (requires system tesseract)
pdfe ocr scan.pdf
```

## Technology stack

### Framework & UI
- **.NET 10.0** — Cross-platform runtime
- **Avalonia UI 11.x** (MIT) — Cross-platform XAML UI
- **ReactiveUI** (MIT) — MVVM framework

### Pdfe libraries (this repo)
- **Pdfe.Core** — Pure-.NET PDF parser, writer, content streams, glyph-level redaction, text extraction with letter positions, hidden-text detection, AcroForm read/fill/flatten/author, OCG + structure tree (read-only), embedded files (read + scrub), page labels, named destinations, document authoring
- **Pdfe.Rendering** — SkiaSharp renderer with embedded TrueType + raw-CFF/Type1C support, Type0/CID composite fonts, /Differences-aware encoding, image XObjects (DCT/Flate/CCITTFax), inline images, transparency, clipping paths, color spaces (DeviceRGB/CMYK/Gray, ICCBased, Indexed, CalRGB)
- **Pdfe.Ocr** — Wrapper around the system `tesseract` CLI + a differential-OCR auditor

### Permissive third-party deps
- **SkiaSharp 2.88.x** (MIT) — 2D graphics
- **Clipper2** (BSL 1.0) — Polygon clipping for redaction geometry
- **Portable.BouncyCastle** (MIT) — Cryptography for signature verification
- **Microsoft.CodeAnalysis.CSharp.Scripting** (MIT) — Roslyn scripting for GUI automation

No copyleft obligations. No PDFium / PDFsharp / PdfPig / Tesseract.NET — all dropped in v2.0.

## Project structure

```
pdfe/
├── Pdfe.Core/                       # PDF parser, writer, content streams, redaction, AcroForm
│   ├── Parsing/                     # Lexer, parser, xref
│   ├── Document/                    # PdfDocument, PdfPage, AcroForm (parse/edit/flatten/author/autodetect),
│   │                                # OCG, structure tree, embedded files, page labels
│   ├── Content/                     # ContentStreamReader/Writer
│   ├── Text/                        # TextExtraction, letter positions
│   │   └── Segmentation/            # GlyphRemover, ImageRedactor, HiddenTextDetector
│   ├── Fonts/                       # CffParser, CffSubsetter
│   ├── Graphics/                    # PdfGraphics API (paths, text, images)
│   └── Writing/                     # Save, incremental update
│
├── Pdfe.Rendering/                  # Skia-based renderer
│   ├── SkiaRenderer.cs              # Content-stream → SKBitmap
│   ├── Fonts/                       # OpenType wrapper, AGL
│   └── ColorSpaces/                 # DeviceRGB/CMYK/Gray, ICCBased, Indexed, CalRGB
│
├── Pdfe.Ocr/                        # OCR shim
│   ├── PdfOcrService.cs             # tesseract CLI invocation
│   └── DifferentialOcrAuditor.cs    # render-twice-and-diff hidden-text finder
│
├── Pdfe.Cli/                        # `pdfe` CLI
│   └── Program.cs                   # 12 subcommands
│
├── PdfEditor/                       # Desktop GUI
│   ├── Controls/PdfViewerControl    # Reusable Avalonia PDF viewer (annotations, links, form-field overlay)
│   ├── Models/                      # HiddenTextHighlight, etc.
│   ├── Services/                    # 7 services on Pdfe.Core / Pdfe.Rendering
│   ├── ViewModels/
│   └── Views/
│
├── Pdfe.Core.Tests/                 # ~2500 tests
├── Pdfe.Rendering.Tests/            # ~265 tests, including visual baselines + corpus
├── Pdfe.Cli.Tests/                  # 22 tests
├── Pdfe.Ocr.Tests/                  # 41 tests (some require tesseract)
├── PdfEditor.Tests/                 # ~745 tests, including headless GUI
└── test-pdfs/                       # Smoke corpus + sample PDFs
```

## Testing

```bash
# Full suite
dotnet test

# Single project
dotnet test Pdfe.Rendering.Tests
dotnet test Pdfe.Core.Tests --filter "Redaction"

# With detailed output
dotnet test --logger "console;verbosity=detailed"
```

**Test counts (v2.1):**
- Pdfe.Core.Tests: ~2500 passing, 2 skipped
- Pdfe.Rendering.Tests: ~265 passing
- Pdfe.Cli.Tests: 22 passing
- Pdfe.Ocr.Tests: ~41 passing (some require `tesseract`)
- PdfEditor.Tests: ~745 passing, 14 skipped (Avalonia headless harness limits)

**Total: ~3,500+ tests, 0 failing.** `Pdfe.Core` line coverage is at 94.3% with a 94% CI gate.

Test categories:
- Unit tests — primitives, parser, content streams, segmentation, coordinate math
- Integration tests — real-world PDFs (birth certificates, government forms, books)
- Visual regression — PNG-baseline diffs against the renderer corpus
- Headless GUI — `[AvaloniaFact]` tests render `PdfViewerControl` against fixtures
- Security — content-stream verification that redacted text is structurally absent
- Conformance — corpus parse + render + round-trip + redaction-regression harness

## Building

```bash
# Linux
dotnet publish PdfEditor -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Windows
dotnet publish PdfEditor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS Intel / Apple Silicon
dotnet publish PdfEditor -c Release -r osx-x64    --self-contained true -p:PublishSingleFile=true
dotnet publish PdfEditor -c Release -r osx-arm64  --self-contained true -p:PublishSingleFile=true
```

Published binaries land in `bin/Release/net10.0/<runtime>/publish/`.

## Documentation

- **[CHANGELOG.md](CHANGELOG.md)** — release notes
- **[GitHub Wiki](https://github.com/marctjones/pdfe/wiki)** — Architecture, redaction engine internals, PDF spec reference
- **[CLAUDE.md](CLAUDE.md)** — Development guidelines (also for AI-assisted contributions)
- **[REDACTION_AI_GUIDELINES.md](REDACTION_AI_GUIDELINES.md)** — Critical safety rules for redaction-code changes

## License

MIT License. See [LICENSES.md](LICENSES.md) for the complete dependency-license inventory. All dependencies are permissive (MIT / Apache 2.0 / BSD-3 / BSL-1.0); no copyleft.

## Contributing

Contributions welcome. The biggest open areas tracked in GitHub Issues:

- In-place text editing (change text inside a paragraph with reflow)
- Annotation authoring (highlight / underline / sticky-note / freehand drawing)
- E-signature workflow (click-to-sign + multi-party + audit trail)
- PDF encryption authoring (we read encrypted PDFs but can't write them)
- PDF → DOCX conversion
- PDF compare / diff
- Field-properties dialog for AcroForm authoring (rename, /Q alignment, JS actions, validation)
- Radio-button groups (parent + /Kids on different /AS names)
- Tab-order management

Smaller-scope improvements (additional operator coverage, performance, accessibility, CFF charstring rewriting) are good first issues.
