# Changelog

All notable changes to pdfe are documented here. Format roughly follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); this project uses
semantic versioning.

## [2.0.0] — 2026-04-25

The headline of v2.0 is **a complete rewrite of the PDF stack**. v1.0 sat on
top of PdfPig + PDFsharp + PDFtoImage (PDFium) + Tesseract.NET; v2.0 ships a
pure-.NET stack of pdfe-owned libraries — Pdfe.Core (parser/writer),
Pdfe.Rendering (SkiaSharp renderer), and Pdfe.Ocr (system tesseract shell) —
with no external PDF dependencies remaining. Same redaction guarantee, same
GUI, fewer moving parts, and the renderer now handles real-world PDFs from
WeasyPrint, Word, XEP, and CJK toolchains without falling back to garbage.

### Added

#### Pdfe.Core — pure-.NET PDF parser, writer, and content-stream library
- M1: parser for objects, indirect references, xref, encrypted streams. Plus
  tolerant recovery for the off-by-one /Length and stale-startxref errors
  that are common in real PDFs.
- M2: text extraction with letter-level positions, replacing PdfPig.
- M3: document writing — incremental save, full rewrite, object streams.
- M4: graphics API — `PdfGraphics` with path, text, image, and state ops.
- Content-stream parsing + serialization (`ContentStreamReader` /
  `ContentStreamWriter`) backing redaction.
- Glyph-level text segmentation: `LetterFinder`, `OperationReconstructor`,
  `GlyphRemover`, plus `PdfPageRedactionExtensions.RedactArea` /
  `RedactAreas` / `RedactText`.
- Image redaction: `ImageRedactor` tracks the CTM through `q`/`Q`/`cm` and
  removes Image XObject `Do` ops that overlap the redaction area.
- Hidden-text detection: `HiddenTextDetector` finds text occluded by later
  opaque obstructions (the classic "black box on top of text" bad-redaction
  pattern). `ObstructionStripper` peels overlays for the differential pass.
- Document authoring: `PdfDocument.CreateNew()`, `Pages.AddBlank(w, h)`,
  `page.GetGraphics()` — synthesize PDFs in-memory without the legacy stack.
- Page manipulation APIs: `Pages.Add`/`Insert`/`RemoveAt`, `page.Rotation`.
- Indirect /Length stream resolution via parser callback (XEP, LibreOffice,
  and other toolchains routinely use this).
- `PdfPage.GetFont` resolves indirect /Font references (WeasyPrint, Word,
  Office, and almost every browser-derived PDF).

#### Pdfe.Rendering — SkiaSharp-based renderer
- M5: full renderer covering text, paths, images, transparency, clipping
  paths, soft masks, ExtGState, color spaces, shading, and inline images.
- Embedded font support:
  - `/FontFile2` (TrueType) loaded directly into SKTypeface.
  - `/FontFile3` raw CFF (Type1C, CIDFontType0C) wrapped into a synthesized
    OpenType container with a Unicode cmap derived from /Differences.
  - `/Encoding` dictionaries with `/Differences` resolved against the Adobe
    Glyph List, falling back to AGL §D.1 `uniXXXX` for non-named glyphs.
  - Per-font glyph widths from the PDF's `/Widths` array (loaded *before*
    CFF wrapping, fixing a stale-state bug where every embedded font was
    wrapped with the previous font's widths).
- Type0 / CIDFontType2 (Identity-H) — full CJK rendering pipeline.
- Browser-style flipped text matrix (`Tm = 1 0 0 -1 e f`) handled correctly
  in both the simple-font and Type0 paths — fixes upside-down rendering
  found in the IRS-1040 footer, every WeasyPrint-produced page, and all CJK.
- Layout-correct text advance for non-embedded fonts via the PDF's `/Widths`
  table (instead of the system fallback's `MeasureText`).
- Tc / Tw scaled by the text-matrix X-scale, per PDF spec 9.4.4 (fixes the
  "Word-derived government form mid-word gap" pattern).
- TJ array kerning routed through the text-matrix X-scale, not Y-scale —
  fixes 6%-per-glyph drift in non-uniform Tm headers (SCOTUS opinions).
- Td/TD offsets transformed through the text matrix per PDF spec 9.4.2.
- Wingdings / dingbat fallback: when an embedded CFF subset wraps cleanly
  but Skia can't extract any glyph outlines, fall back to a system symbol
  font (Noto Sans Symbols2) so the user sees a glyph instead of `⊠`.
- Visual regression test infrastructure with PNG baselines.
- Dropped `PDFtoImage` / `PDFium` native dependency.

#### Pdfe.Ocr — OCR via system `tesseract` CLI
- New project. Shells out to the system tesseract binary, parses TSV
  output, returns `OcrResult` with per-word bounding boxes.
- Differential OCR auditor: render the page twice (once with overlays
  stripped, once without), OCR both, diff the word sets — surfaces text
  hidden inside rasters by overlay, the rasterized analogue of structural
  redaction.
- Replaces the previous Tesseract.NET nuget binding (which pinned to a
  leptonica version no longer shipping on modern Linux).

#### Pdfe.Cli — `pdfe` command-line tool
- `pdfe render <file> -o out.png [--page N] [--dpi N]`
- `pdfe redact <file> -o out.pdf --text "PHRASE"` — glyph-level removal.
- `pdfe audit <file> [--deep] [--json]` — structural and (with `--deep`)
  differential-OCR audit of hidden text.
- `pdfe ocr <file>` — OCR the page and emit TSV.

#### GUI — PdfEditor
- New reusable `PdfViewerControl` (Avalonia UserControl) with overlay layers
  for selection, search highlights, redaction marquee, and hidden-text
  reveal. Replaces the bespoke MainWindow rendering.
- `MainWindow` rewritten on top of `PdfViewerControl`.
- Reveal Hidden Text — Tools → "Reveal Hidden Text" toggle. Yellow boxes
  for structural detections (text covered by rectangles), orange boxes for
  differential-OCR recoveries (text inside rasterized images).
- Open PDF from command-line argument on startup.

### Changed

- All seven GUI services migrated from PdfPig / PDFsharp / PDFtoImage to
  Pdfe.Core / Pdfe.Rendering: `PdfRenderService`, `PdfTextExtractionService`,
  `PdfSearchService`, `SignatureVerificationService`, `PdfDocumentService`,
  `BatesNumberingService`, `RedactionService`.
- `RedactionService` unified — `RedactArea` (mouse marquee) and `RedactText`
  (find-and-redact) now share a single Pdfe.Core pipeline; the previous
  parallel PdfSharp+PdfPig path is gone.
- The legacy `PdfEditor.Redaction` library (and its `pdfer` CLI) deleted —
  glyph-level redaction lives in Pdfe.Core; the Pdfe.Cli `redact` command
  replaces `pdfer`.
- System-font fallback widened: strip the 6-letter PDF subset prefix,
  match by family prefix instead of exact name, and recognize Semibold /
  Medium as Bold. `TimesNewRomanPS-BoldMT` now correctly maps to Times New
  Roman instead of Sans-Serif; `BookmanStd` to Times; `ZapfDingbatsStd` to
  Noto Sans Symbols2.
- Build is clean — 0 warnings, 0 errors across all projects.

### Removed

- **PdfPig 0.1.11** — replaced by `Pdfe.Core.Text`.
- **PDFsharp 6.2.2** — replaced by `Pdfe.Core.Document` + `Pdfe.Core.Writing`.
- **PDFtoImage 4.0.2** + native PDFium — replaced by `Pdfe.Rendering` (Skia).
- **Tesseract.NET nuget** — replaced by `Pdfe.Ocr` (CLI shell).
- **PdfEditor.Redaction** project + **`pdfer` CLI** — replaced by
  Pdfe.Core glyph-level redaction + `pdfe redact`.
- **PdfEditor.Demo** + Validator tools — superseded by Pdfe.Cli + the new
  visual regression suite.

### Fixed

#### Renderer — real-world PDF reliability
- Stream `/Length` as an indirect reference no longer rejected (XEP,
  LibreOffice). Parser exposes an `IndirectObjectResolver` callback which
  `PdfDocument` wires to its own object cache.
- `\<EOL>` line continuations in literal strings (PDF spec 7.3.4.2)
  stripped correctly — fixes the `⊠` placeholders that appeared at the end
  of long underline runs in Word-derived government forms.
- Embedded-font /Widths loaded *before* the CFF→OpenType wrapper runs;
  previously every embedded font got hmtx widths from the previously-active
  font (or zero for the first font), producing visibly broken layout on
  multi-font pages — every page after the cover of any XEP-produced book.
- AGL reverse lookup synthesizes `uniXXXX` names for BMP codepoints not in
  the named-glyph table — required for CFF subsets keyed on uniXXXX names.
- Post-wrap outline probe: if a wrapped CFF resolves cmap entries but
  produces no glyph outlines, fall back to a system font instead of
  rendering empty space (catches a class of XEP-produced ZapfDingbats
  subsets where Skia's CFF interpreter can't extract charstrings).
- Y-flip applied conditionally on the sign of `Tm.d`, fixing upside-down
  text in browser-flipped Tm content (CJK, WeasyPrint, IRS-1040 footer).
- Effective font size computed from the text matrix Y-scale (handles the
  common `1 Tf` + scaled `Tm` idiom).
- Cursor advance honors text-matrix non-uniform scaling.
- `CodePagesEncodingProvider` registered for Windows-1252 / WinAnsi support.
- Search highlights refresh when the user changes pages manually.
- Birth-cert form layout: routes non-embedded fonts through the PDF's
  `/Widths` array for cursor advance instead of the substituted system
  typeface's metrics — fixes mid-word gaps in TJ-kerning-heavy PDFs.

#### Tests
- `PdfViewerControl_PageChanged_FiresEvent` deflaked. Test was timing-
  sensitive on the shared Avalonia headless dispatcher; now waits
  deterministically for the event with a 30-second deadline.

### Verified rendering

The new renderer has been smoke-tested against a real-world corpus:

| PDF | Source | Notes |
|---|---|---|
| Birth Certificate Request (CT) | scanned/scrambled gov form | TJ kerning, Tw column alignment, raster background |
| SCOTUS opinion (Trump v. Anderson) | Court PDF | Non-uniform Tm headers, Type1 PostScript subsets |
| IRS Form 1040 + Instructions | IRS / Adobe Distiller | Type0/Identity-H, Acrobat-distilled, 180° footer text |
| State Dept DS-82 (passport renewal) | XFA + Type0 | Acrobat / XFA mix |
| CDC COVID-19 VIS | CDC | Embedded TrueType, Wingdings dingbats |
| "Business Success with Open Source" | Pragmatic Bookshelf / XEP | 455 pages, multi-font CFF subsets, ZapfDingbats |
| Multilingual CJK fixture | WeasyPrint + Noto CJK | zh-Hans, zh-Hant, ja, ko |

All render essentially identically to mutool / Acrobat at the structural
level. `Pdfe.Rendering.Tests/Visual/` and `PdfEditor.Tests/UI/baselines/`
keep PNG baselines for regression detection.

### Migration

The architectural change is mostly transparent for end users — the desktop
app, the redaction guarantee, and the file format are unchanged. For
embedders moving off the v1.0 surface:

- `PdfEditor.Redaction` (library) → `Pdfe.Core.Text.Segmentation` —
  use `page.RedactArea(rect)` / `page.RedactAreas(rects)` /
  `document.RedactText("phrase")` from `PdfPageRedactionExtensions` /
  `PdfDocumentRedactionExtensions`.
- `pdfer` CLI → `pdfe redact` — same options.
- PdfPig text extraction → `Pdfe.Core.Text` — `PdfDocument.GetText(page)`
  and `PdfDocument.GetLetters(page)`.
- PDFsharp `PdfDocument` → `Pdfe.Core.Document.PdfDocument` — note that
  `PdfDocument.Open(stream)` now takes ownership semantics via
  `Open(stream, ownsStream)`.
- PDFtoImage → `Pdfe.Rendering.SkiaRenderer.RenderPage(page, options)`.

### Known gaps deferred to v2.1+

- PDF encryption / password handling (#237) — v2.1.
- Partial glyph rasterization for redaction cuts that bisect a glyph
  (#278). Current full-glyph removal is conservative-safe.
- PDF Annotations (#271), Interactive Forms (#272), Tagged PDF (#275),
  Advanced Transparency (#274), Multimedia (#273) — v2.2.
- Compass-image-style inline-image-with-Smask cases that still fall back
  to placeholder rendering (covered indirectly by #274).

### Test counts at release

- Pdfe.Core.Tests: 442 passing, 2 skipped
- Pdfe.Rendering.Tests: 175 passing
- Pdfe.Cli.Tests: 7 passing
- PdfEditor.Tests: 221 passing, 2 skipped (require Tesseract installed)

**Total: 845 tests, 0 failing**

---

## [1.0.0] — 2026-01-11

First major stable release. Cross-platform PDF editor with **true
glyph-level redaction** — content removed from the PDF structure, not just
visually covered. Built on PdfPig + PDFsharp + PDFtoImage + Tesseract.NET.

See the GitHub release for full v1.0.0 notes:
https://github.com/marctjones/pdfe/releases/tag/v1.0.0
