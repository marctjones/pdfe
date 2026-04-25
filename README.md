<p align="center">
  <img src="PdfEditor/Assets/pdfe_logo.svg" alt="PDFE Logo" width="128" height="128">
</p>

# pdfe

A cross-platform PDF editor and pure-.NET PDF framework, built with **C# + .NET 8 + Avalonia UI** and shipped with **true content-level redaction**.

[![Release](https://img.shields.io/github/v/release/marctjones/pdfe)](https://github.com/marctjones/pdfe/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-845%20passing-brightgreen)](Pdfe.Core.Tests)
[![Build](https://img.shields.io/badge/build-0%20warnings-brightgreen)](Pdfe.Core)

> **v2.0 is an architectural rewrite.** The PDF stack underneath the editor is now pdfe-owned end-to-end — `Pdfe.Core` (parser/writer), `Pdfe.Rendering` (Skia), and `Pdfe.Ocr` (system tesseract shell) — with no third-party PDF dependencies. See [CHANGELOG.md](CHANGELOG.md) for the full v2.0 release notes.

## What's in the box

```
Pdfe.Core/        Pure-.NET PDF parser, writer, content-stream library
Pdfe.Rendering/   SkiaSharp-based renderer (text, images, paths, transparency)
Pdfe.Ocr/         OCR via the system `tesseract` CLI + differential-OCR auditor
Pdfe.Cli/         `pdfe` command-line tool (render, redact, audit, ocr)
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
- Verified against real-world fixtures (CT birth certificate, government forms) at the pixel and content-stream level

### CLI (`pdfe`)
```bash
pdfe render  <file>  -o out.png  [--page N] [--dpi N]
pdfe redact  <file>  -o out.pdf  --text "PHRASE"
pdfe audit   <file>  [--deep] [--json]
pdfe ocr     <file>
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

`dotnet 8.0` SDK required. No additional native dependencies — the renderer is pure SkiaSharp, the OCR auditor shells out to the system `tesseract` binary if installed (skipped gracefully if not).

## Usage

### Desktop redaction (mark-then-apply)

1. **Enable redaction mode** — toolbar button or press `R`
2. **Mark areas** — click and drag (red dashed outline = pending)
3. **Review pending marks** — sidebar shows preview text
4. **Apply** — toolbar button or `Enter` (permanent removal)
5. **Verify** — Clipboard History panel shows exactly what came out
6. **Save** — defaults to `filename_REDACTED.pdf`

Multiple areas across multiple pages can be marked and applied as a single batch.

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
pdfe redact report.pdf -o report-redacted.pdf --text "ACCOUNT 9876"

# Audit a "redacted" PDF for hidden text leftovers — both structural and rasterized
pdfe audit purportedly-redacted.pdf --deep --json

# Extract text from a scanned PDF (requires system tesseract)
pdfe ocr scan.pdf
```

## Technology stack

### Framework & UI
- **.NET 8.0** — Cross-platform runtime
- **Avalonia UI 11.x** (MIT) — Cross-platform XAML UI
- **ReactiveUI** (MIT) — MVVM framework

### Pdfe libraries (this repo)
- **Pdfe.Core** — Pure-.NET PDF parser, writer, content streams, glyph-level redaction, text extraction with letter positions, hidden-text detection, document authoring
- **Pdfe.Rendering** — SkiaSharp renderer with embedded TrueType + raw-CFF/Type1C support, Type0/CID composite fonts, /Differences-aware encoding, image XObjects, transparency, clipping paths
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
├── Pdfe.Core/                      # PDF parser, writer, content streams, redaction
│   ├── Parsing/                    # Lexer, parser, xref
│   ├── Document/                   # PdfDocument, PdfPage, PageCollection
│   ├── Content/                    # ContentStreamReader/Writer
│   ├── Text/                       # TextExtraction, letter positions
│   │   └── Segmentation/           # GlyphRemover, ImageRedactor, HiddenTextDetector
│   ├── Graphics/                   # PdfGraphics API (paths, text, images)
│   └── Writing/                    # Save, incremental update
│
├── Pdfe.Rendering/                 # Skia-based renderer
│   ├── SkiaRenderer.cs             # Content-stream → SKBitmap
│   ├── Fonts/                      # CFF parser, OpenType wrapper, AGL
│   └── AdobeGlyphList.cs
│
├── Pdfe.Ocr/                       # OCR shim
│   ├── PdfOcrService.cs            # tesseract CLI invocation
│   └── DifferentialOcrAuditor.cs   # render-twice-and-diff hidden-text finder
│
├── Pdfe.Cli/                       # `pdfe` CLI
│   └── Program.cs                  # render / redact / audit / ocr commands
│
├── PdfEditor/                      # Desktop GUI
│   ├── Controls/PdfViewerControl   # Reusable Avalonia PDF viewer
│   ├── Models/                     # HiddenTextHighlight, etc.
│   ├── Services/                   # 7 services on Pdfe.Core / Pdfe.Rendering
│   ├── ViewModels/
│   └── Views/
│
├── Pdfe.Core.Tests/                # 442 tests
├── Pdfe.Rendering.Tests/           # 175 tests, including visual baselines
├── Pdfe.Cli.Tests/                 # 7 tests
├── PdfEditor.Tests/                # 221 tests, including headless GUI
└── test-pdfs/                      # Smoke corpus + sample PDFs
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

**Test counts (v2.0):**
- Pdfe.Core.Tests: 442 passing, 2 skipped
- Pdfe.Rendering.Tests: 175 passing
- Pdfe.Cli.Tests: 7 passing
- PdfEditor.Tests: 221 passing, 2 skipped (require `tesseract` installed)

**Total: 845 tests, 0 failing.**

Test categories:
- Unit tests — primitives, parser, content streams, segmentation, coordinate math
- Integration tests — real-world PDFs (birth certificates, government forms, books)
- Visual regression — PNG-baseline diffs against the renderer corpus
- Headless GUI — `[AvaloniaFact]` tests render `PdfViewerControl` against fixtures
- Security — content-stream verification that redacted text is structurally absent

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

Published binaries land in `bin/Release/net8.0/<runtime>/publish/`.

## Documentation

- **[CHANGELOG.md](CHANGELOG.md)** — v2.0 release notes (full architectural changes)
- **[GitHub Wiki](https://github.com/marctjones/pdfe/wiki)** — Architecture, redaction engine internals, PDF spec reference
- **[CLAUDE.md](CLAUDE.md)** — Development guidelines (also for AI-assisted contributions)
- **[REDACTION_AI_GUIDELINES.md](REDACTION_AI_GUIDELINES.md)** — Critical safety rules for redaction-code changes

## License

MIT License. See [LICENSES.md](LICENSES.md) for the complete dependency-license inventory. All dependencies are permissive (MIT / Apache 2.0 / BSD-3 / BSL-1.0); no copyleft.

## Contributing

Contributions welcome. The biggest open areas tracked in GitHub Issues:

- PDF encryption / password handling (#237) — v2.1
- Annotations (#271), Forms (#272), Tagged PDF (#275) — v2.2
- Advanced transparency (#274) — v2.2
- Partial glyph rasterization for redaction cuts that bisect a glyph (#278)

Smaller-scope improvements (additional operator coverage, performance, accessibility) are good first issues.
