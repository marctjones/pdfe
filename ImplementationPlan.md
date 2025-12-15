# Implementation Plan

This document centralizes the current implementation status and remaining work pulled from the existing documentation set.

## Completed Milestones
- Cross-platform desktop editor with viewing, navigation, page manipulation, search, and text selection flows is functional (`README.md`).
- TRUE content-level redaction pipeline (parse → filter → rebuild → replace → draw) is fully implemented (~1.4k LOC) and verified via 600+ automated tests (`REDACTION_ENGINE.md`, `README.md`).
- Continuous regression protection exists through extensive integration/UI/security suites plus PdfEditor.Validator CLI for leakage checks (`TESTING_GUIDE.md`, `README.md`).

## Active Workstreams

### 1. Testing Roadmap (from `PdfEditor.Tests/README.md`)

| Status | Task |
| --- | --- |
| ☑ | Add unit tests for `PdfMatrix`. |
| ☑ | Add unit tests for graphics/text state tracking. |
| ☑ | Add unit tests for bounding-box calculator logic. |
| ☑ | Add unit tests for the content stream parser. |
| ☑ | Add unit tests for the content stream builder. |
| ☑ | Add error-handling tests (fault injection, malformed PDFs). |
| ☑ | Increase aggregate code coverage to ≥80% (coverage script with 80% threshold). |
| ☑ | Add performance benchmarks for redaction workflows. |

**Owner:** Quality & Test Engineering  
**Notes:** All items remain open; core coverage today is integration-heavy.

### 2. Redaction Engine Enhancements (from `REDACTION_ENGINE.md`)

| Status | Task |
| --- | --- |
| ☐ | Form XObject support: (a) recursive form parsing, (b) new `FormXObjectOperation` & serialization, (c) filtering nested child ops, (d) integration tests. *(Blocked: PdfSharpCore API lacks hooks for parsing form streams without source access.)* |
| ☐ | Font metrics – read `/FontDescriptor`, `/Widths`, `/ToUnicode`, update bounds/intersection logic, add unit tests. *(Blocked: PdfSharpCore distributed as DLLs only; need source/fork to access font data structures.)* |
| ☐ | Font encodings – decode custom encodings & ToUnicode maps, ensure redaction/extraction honor decoded glyphs, add targeted tests. *(Blocked together with font metrics: need PdfSharpCore source to access `/ToUnicode` and encoding tables.)* |
| ☐ | Clipping paths – track `W`/`W*` in graphics state, adjust intersection math, add PDFs verifying clipped content handling. *(Blocked: requires geometric clipping support; PdfSharpCore doesn’t expose path evaluation APIs we can leverage without implementing a custom geometry engine.)* |
| ☐ | Streaming optimization – parse/cache per page, reuse for multiple redactions, add benchmarks showing memory/time gains. *(Deferred: existing pipeline rewrites page content per redaction; caching parses before/after rewrite requires larger architectural change and is not a quick win.)* |
| ☐ | Handle encrypted PDFs – detect encryption, request password, integrate with pipeline, add owner/user password test docs. |
| ☐ | Support Type 3/custom fonts – parse paint procedures, treat glyph streams as operations, add integration tests proving glyph removal. |

**Owner:** Redaction Engine  
**Notes:** Current implementation is production-ready but these items close known accuracy/coverage gaps.

### 3. PDF 2.0 Compliance Alignment (based on PDF Association application notes)

| Status | Task |
| --- | --- |
| Status | Task |
| --- | --- |
| ☐ | Per-object BPC (Application Note 001): detect `/BlackPointCompensation`, expose in model/UI, round-trip through save, add regression tests proving values persist. |
| ☐ | Associated Files (Application Note 002): surface `/AF` entries in UI, ensure save preserves references and optional removal workflows, add tests with attached files. |
| ☐ | Metadata coverage (Application Note 003): traverse page/resource/font/ICC metadata streams, scrub redacted terms, add tests with metadata in each location. |
| ☐ | Regression suites: PDFs covering BPC, associated files, object metadata ensuring redaction + save behave as per notes. |

**Owner:** Platform & Compliance  
**Notes:** These tasks align our feature set with the PDF Association’s published guidance for PDF 2.0 (ISO 32000-2).

### 4. Conformance & Complex-PDF Test Suites

| Status | Task |
| --- | --- |
| Status | Task |
| --- | --- |
| ☐ | CI conformance gates: wire `VeraPdfConformanceTests` + qpdf checks into CI, fail builds when validators fail, document release requirement. |
| ☐ | Isartor automation: download suite, run PdfEditor.Validator/redaction flows, re-validate with veraPDF/qpdf to ensure no PDF/A regressions. |
| ☐ | Visual rendering expansion: make `VisualRenderingTests` sweep entire veraPDF corpus (batched, multiple DPIs), manage baselines, flag diffs. |
| ☐ | Corpus redaction regression: run redaction on representative corpora (veraPDF, Isartor, complex samples) verifying glyph removal and validator success. |

**Owner:** Quality & Test Engineering (in partnership with Redaction team)  
**Notes:** Scripts to download PDF Association corpora already exist; these tasks make the corpora part of the official validation plan.

## Tracking & Next Steps
1. Assign owners/dates for each unchecked task above.
2. Establish acceptance tests and KPIs for “performance benchmarks” and “coverage ≥80%”.
3. Revisit this plan after each release to reflect progress or add new backlog items.
