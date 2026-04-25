# pdfe (PdfEditor) - Project Vision

## Purpose

**pdfe** is a cross-platform PDF editor that solves a critical security problem: most PDF redaction tools just draw black boxes over sensitive content, leaving the text fully extractable underneath. pdfe implements **TRUE content-level redaction** by removing text glyphs from the PDF structure at the content stream level, ensuring redacted information cannot be recovered.

**Who it's for:**
- Government agencies handling sensitive documents (birth certificates, permits, IDs)
- Legal professionals redacting case files and discovery documents  
- Privacy-conscious individuals protecting personal information
- Anyone who needs verifiable, permanent content removal (not just visual hiding)

## Core Principles

### 1. Security First
- **Glyph-level removal**: Text glyphs are removed from PDF content streams, not just covered
- **Verified deletion**: 1600+ automated tests confirm text is unextractable after redaction
- **External validation**: pdftotext, PdfPig, and veraPDF confirm content removal
- **No shortcuts**: Never compromise security for convenience

### 2. Cross-Platform from Day One
- **One codebase**: C# + .NET 8 + Avalonia UI for Windows, Linux, macOS
- **Native performance**: No Electron bloat, true native UI on all platforms
- **Consistent UX**: Same workflow and features across all operating systems

### 3. Permissive Licensing
- **MIT License**: Free for commercial and non-commercial use
- **No copyleft dependencies**: All dependencies use MIT, Apache 2.0, or BSD-3 licenses
- **Open collaboration**: Encourage community contributions and derivative works

### 4. Zero-Compromise Quality
- **Clean builds always**: 0 warnings, 0 errors policy
- **Comprehensive testing**: 1600+ tests across unit, integration, UI, and security layers
- **Real-world validation**: Tested on government forms and veraPDF corpus (2,694 PDFs)
- **Evidence-based fixes**: Document lessons learned, prevent regression

### 5. User-Centric Design
- **Mark-then-apply workflow**: Review all redactions before permanent removal
- **Visual confirmation**: Clipboard history shows exactly what was removed
- **Safety by default**: Suggests `_REDACTED.pdf` suffix to protect originals
- **Automation-ready**: CLI tool and C# scripting for batch operations

## Current State (v1.4.0)

### What Works
- ✅ TRUE glyph-level redaction (text is structurally removed, not just hidden)
- ✅ GUI with mark-then-apply redaction workflow
- ✅ CLI tool (`pdfer`) for batch redaction, search, verification
- ✅ Page manipulation (add, remove, rotate 90°/180°/270°)
- ✅ Text selection, search with highlighting
- ✅ Zoom and pan with thumbnails sidebar
- ✅ Keyboard shortcuts and accessibility
- ✅ GUI automation via Roslyn C# scripting
- ✅ OCR support for scanned PDFs (Tesseract integration)
- ✅ Digital signature verification
- ✅ Partial glyph redaction (preserves visible portions using Clipper2)
- ✅ 1600+ passing tests validating all features

### Known Limitations
- **Image redaction** (#269): Only text redaction works; images are not removed
- **Sequential redactions** (#270): Content can shift ~6pt when performing multiple sequential redactions
- **Metadata**: PDF Info dictionary and XMP metadata not sanitized
- **Form XObjects**: Text in reusable content streams not parsed
- **Encrypted PDFs**: Cannot open password-protected files

## Future Vision: Unified Pdfe Framework (Epic #238)

### The Problem
Current architecture relies on three external PDF libraries:
- **PdfPig** (text extraction) - Apache 2.0, excellent but heavy
- **PDFsharp** (PDF modification) - MIT, good but limited
- **PDFtoImage** (rendering) - wraps native PDFium, platform-dependent

This creates:
- **Dependency risk**: Breaking changes in upstream libraries
- **Integration complexity**: Different APIs for reading vs writing
- **Native dependencies**: PDFium requires platform-specific binaries
- **Limited control**: Can't fix bugs or optimize without upstream changes

### The Solution: pdfe-Owned PDF Stack

Build a complete, pure .NET PDF framework with two libraries:

**Pdfe.Core** - Complete PDF library for reading, writing, and text extraction:
- `Pdfe.Core.Primitives` - PDF object model (dict, array, stream, etc.)
- `Pdfe.Core.Parsing` - PDF file parser (lexer, xref, decompression)
- `Pdfe.Core.Document` - Document structure (pages, catalog, metadata)
- `Pdfe.Core.Content` - Content stream parsing and generation
- `Pdfe.Core.Text` - Text extraction with accurate letter positions
- `Pdfe.Core.Fonts` - Font handling (Type1, TrueType, CID, CMap)
- `Pdfe.Core.Writing` - PDF serialization and saving
- `Pdfe.Core.Encryption` - PDF security (RC4, AES-128, AES-256)

**Pdfe.Rendering** - SkiaSharp-based PDF renderer:
- `Pdfe.Rendering.SkiaRenderer` - Pure .NET rendering (no native deps)
- `Pdfe.Rendering.Graphics` - Path, text, image rendering
- `Pdfe.Rendering.Fonts` - Font caching and glyph rasterization

**Keep Existing**:
- `PdfEditor.Redaction` - Already pdfe-owned, battle-tested
- `PdfEditor` - GUI application
- `PdfEditor.Redaction.Cli` - Command-line tool

### Implementation Phases

| Phase | Component | Effort | Priority | Status |
|-------|-----------|--------|----------|--------|
| 1 | Core Primitives & Parsing (#230) | 3-4 weeks | Critical | Planned |
| 2 | Text Extraction (#231) | 3-4 weeks | Critical | Planned |
| 3 | Document Writing (#232) | 2-3 weeks | Critical | Planned |
| 4 | Graphics API (#233) | 1-2 weeks | High | Planned |
| 5 | Rendering Engine (#234) | 4-6 weeks | High | Planned |
| 6 | Migration (#235) | 1-2 weeks | High | Planned |
| 7 | CJK Support (#236) | 2-3 weeks | Medium | Planned |
| 8 | Encryption (#237) | 1-2 weeks | Low | Planned |

**Total Timeline**: 17-26 weeks (4-6 months)

### Success Criteria
- ✅ Zero external PDF dependencies (only SkiaSharp, Clipper2)
- ✅ All 1600+ existing tests pass
- ✅ Visual rendering matches PDFium quality (90%+ of test cases)
- ✅ Text positions match PdfPig accuracy (±0.5pt tolerance)
- ✅ Performance within 20% of current implementation
- ✅ Handles PDF 1.4 through PDF 2.0

### Benefits
- **Full control**: Fix bugs, optimize, add features without waiting for upstream
- **No native deps**: Pure .NET with SkiaSharp (already cross-platform)
- **Simplified architecture**: One unified API for reading and writing
- **Better integration**: Content stream parser works seamlessly with text extractor
- **Long-term stability**: Own the entire PDF stack

## Goals (Prioritized)

### Immediate (v1.5 - Next 3 months)
- [ ] **Image redaction** (#269): Remove images from content streams
- [ ] **Fix sequential redactions** (#270): Eliminate position shift bug
- [ ] **Metadata sanitization**: Remove XMP and Info dictionary entries
- [ ] **Encrypted PDF support** (#237): Open password-protected files

### Near-term (v2.0 - Next 6 months)  
- [ ] **Pdfe.Core foundation** (#230, #231, #232): Core primitives, text extraction, writing
- [ ] **Form XObject parsing**: Handle reusable content streams
- [ ] **Enhanced OCR workflow**: Improve scanned document handling
- [ ] **Batch redaction UI**: Multi-file processing in GUI

### Long-term (v2.x - Next year)
- [ ] **Complete Pdfe.Core/Rendering** (#233-#237): Full framework implementation
- [ ] **Plugin architecture**: Extensibility for custom features
- [ ] **Advanced redaction patterns**: Regex-based redaction
- [ ] **PDF/A compliance**: Full PDF/A-1b, PDF/A-2b support with veraPDF validation

## Guiding Questions

**When making decisions, ask:**

1. **Does this compromise redaction security?** → If yes, reject
2. **Does this add external dependencies?** → Prefer pdfe-owned solutions
3. **Does this break cross-platform support?** → Windows/Linux/macOS must all work
4. **Does this require restrictive licensing?** → MIT/Apache 2.0/BSD only
5. **Can this be automated/scripted?** → CLI and scripting should support it
6. **Is this verified by tests?** → All redaction must have extraction tests

## Success Metrics

**Technical Excellence:**
- 0 compiler warnings (enforced in CI)
- 95%+ test coverage on redaction engine
- <2 second page render at 150 DPI
- <100ms redaction for typical page

**User Impact:**
- Successful redaction on 99%+ of real-world PDFs
- Verifiable content removal (external tool confirmation)
- Cross-platform feature parity
- Clear error messages and recovery paths

**Community Growth:**
- Active GitHub community (issues, PRs, discussions)
- Documentation for all major features
- Wiki maintained with lessons learned
- CLI examples for common workflows

## Non-Goals

**What pdfe will NOT do:**
- ❌ Become a full PDF editor (annotations, forms, etc.) - focus on redaction
- ❌ Support proprietary formats beyond PDF - PDF only
- ❌ Require cloud services or accounts - fully local/offline
- ❌ Compromise on licensing - always permissive licenses
- ❌ Sacrifice security for convenience - redaction correctness is non-negotiable

---

*This vision guides all development decisions. When in doubt, return to these principles.*

*Last updated: 2026-01-02*