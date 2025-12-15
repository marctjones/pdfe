# Testing Guide

## Overview

This repository ships a broad, automation-first test suite to prove **true glyph-level redaction**, coordinate correctness, and general PDF operations. The suite now contains hundreds of integration, unit, UI, and security tests; key redaction behaviors are verified end to end with independent extraction tools (PdfPig/PDFium).

## Structure & Dependencies

```
PdfEditor.Tests/
├── Integration/    # End-to-end: redaction, metadata, coordinates, export, conformance
├── Unit/           # Math/state/view-model/search/redaction helpers
├── UI/             # Headless UI and input simulation
├── Security/       # Content removal verification and leakage checks
├── Utilities/      # TestPdfGenerator, PdfTestHelpers, shared fixtures
└── TestResults/    # Captured logs/results
```

**Frameworks & libs:** xUnit 2.5.x, FluentAssertions, Serilog, PdfSharpCore/PDFsharp, PdfPig, PDFtoImage/PDFium.

## Running Tests

```bash
# From repo root
dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj

# Verbose
dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj --logger "console;verbosity=detailed"

# Filter examples
dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj --filter "FullyQualifiedName~Redaction"
dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj --filter "ClassName=CoordinateConverterTests"

# Coverage
dotnet test PdfEditor.Tests/PdfEditor.Tests.csproj /p:CollectCoverage=true
```

**Sandbox note:** The vstest host opens a local TCP socket. In restricted sandboxes/containers without socket permissions you may hit `System.Net.Sockets.SocketException (13): Permission denied`. Run locally or adjust the sandbox policy to allow local sockets before treating failures as test regressions.

## What’s Covered (Representative)

- **Redaction:** basic → comprehensive, batch, inline images, form XObjects, blind/forensic verification, position leakage, metadata sanitization, PDF 1.7/2.0 support.
- **Coordinates:** Avalonia ↔ PDF ↔ XGraphics conversions, rotations, GUI simulation, visual coordinate verification.
- **Operations:** file/open/save/merge/export, Bates numbering, search-and-redact flows, external tool validation.
- **Unit-level:** PdfMatrix/graphics/text state, bounds calculators, PdfOperation models, search service, ViewModel behaviors.
- **UI:** headless mouse/keyboard workflows, selection and zoom interactions.

## Utilities

- `Utilities/TestPdfGenerator.cs` – builds fixture PDFs (text, graphics, transforms, multipage).
- `Utilities/PdfTestHelpers.cs` – text extraction, validation, and rendering helpers.
- `PdfEditor.Validator` – CLI helper to analyze/verify redactions (`verify` reports text overlapping black boxes).

## Best Practices Implemented

- Arrange–Act–Assert with descriptive test names.
- FluentAssertions for readable expectations.
- Deterministic fixtures and automatic temp-file cleanup.
- Detailed logging via `ITestOutputHelper` + Serilog for troubleshooting failures.

Run the suite regularly after changes to the redaction engine, coordinate math, or metadata handling; these are security-critical surfaces. New redaction features should add both integration and unit coverage that prove glyph removal (extraction should NOT find redacted text).

## OCR & Rendering knobs (dev/testing)
- OCR env vars: `PDFEDITOR_OCR_LANGS`, `PDFEDITOR_OCR_BASE_DPI`, `PDFEDITOR_OCR_HIGH_DPI`, `PDFEDITOR_OCR_LOW_CONFIDENCE`, `PDFEDITOR_OCR_PREPROCESS`, `PDFEDITOR_OCR_DENOISE_RADIUS`, `PDFEDITOR_OCR_BINARIZE`.
- Render cache env var: `PDFEDITOR_RENDER_CACHE_MAX` (default 20). Cache clears on document close.
