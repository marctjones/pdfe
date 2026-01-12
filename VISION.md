# pdfe Vision

## Purpose
A cross-platform PDF editor focused on **true content-level redaction** for privacy and security.

## Core Principles

1. **Security-First Redaction** - Remove text/graphics from PDF structure, not just visual covering. Text extraction tools cannot recover redacted content.

2. **Cross-Platform** - Runs on Windows, Linux, and macOS via .NET 8 + Avalonia UI.

3. **Permissive Licensing** - All dependencies use MIT, Apache 2.0, or BSD-3 licenses. No copyleft restrictions.

4. **Self-Contained** - Working toward zero external PDF library dependencies with Pdfe.Core (parsing/writing) and Pdfe.Rendering (SkiaSharp-based rendering).

## v2.0 Goals

- **Pdfe.Core**: Pure .NET PDF parser/writer supporting PDF 1.4-2.0
- **Pdfe.Rendering**: SkiaSharp-based renderer replacing PDFium dependency
- **Glyph-Level Redaction**: Surgical removal of individual characters
- **veraPDF Compliance**: Pass validation on all saved files

## Success Metrics

- 600+ tests passing (598 currently)
- veraPDF corpus compatibility (2,694 test files)
- Visual rendering 90%+ match with PDFium
- Real-world document support (birth certificates, government forms)

## Non-Goals (v2.0)

- PDF form filling/creation
- Digital signature creation (verification only)
- Multimedia features (sounds, movies, 3D)
- Full accessibility/tagged PDF support