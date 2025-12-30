# PdfEditor.Redaction Library Version

**Current Version**: 1.4.0

## Version History

| Version | Date | Description |
|---------|------|-------------|
| 1.4.0 | 2024-12-30 | In-memory PdfPage API, PDF/A compliance, comprehensive operators |
| 1.3.0 | 2024-12-24 | Glyph-level redaction with spatial letter matching |
| 0.1.0-alpha | 2024-12-22 | Initial development - Tj operator support |

## Semantic Versioning

This library follows [Semantic Versioning 2.0.0](https://semver.org/):

- **MAJOR**: Incompatible API changes
- **MINOR**: New functionality (backwards compatible)
- **PATCH**: Bug fixes (backwards compatible)

## v1.4.0 Features

### In-Memory PdfPage API
- `RedactPage()` - Redact page in-place without file I/O
- `ExtractLettersFromPage()` - Extract letters for glyph-level redaction
- `SanitizeDocumentMetadata()` - Clean document metadata

### PDF/A Compliance
- PDF/A detection (`PdfADetector`)
- Metadata preservation (`PdfAMetadataPreserver`)
- Transparency removal for PDF/A-1 (`PdfATransparencyRemover`)
- VeraPDF validation support (`VeraPdfValidator`)

### Text Operators Supported
- **Text Showing**: Tj, TJ, ' (quote), " (double-quote)
- **Text State**: Tf, Td, TD, Tm, T*, TL, Tc, Tw, Tz, Ts, Tr
- **Graphics State**: q, Q, cm, gs
- **Text Objects**: BT, ET

### Other Features
- Annotation redaction
- Content stream validation
- Font propagation across text blocks

## Development Milestones

### v1.4.0 (Current) ✅
- [x] In-memory PdfPage API
- [x] PDF/A compliance preservation
- [x] All common text operators
- [x] Annotation redaction
- [x] Content stream validation

### v1.5.0 (Planned)
- [ ] Rotated page support (#151)
- [ ] Form XObject redaction improvements
- [ ] CID/CJK font improvements

### v2.0.0 (Future)
- [ ] Inline image redaction
- [ ] Pattern/shading redaction
- [ ] Multi-threaded processing

## Integration Status

| Component | Library Version | Status |
|-----------|-----------------|--------|
| PdfEditor GUI | 1.4.0 | ✅ Integrated |
| PdfEditor CLI (pdfer) | 1.4.0 | ✅ Integrated |
| Standalone library | 1.4.0 | ✅ Available |

## Test Coverage

| Test Suite | Tests | Status |
|------------|-------|--------|
| PdfEditor.Redaction.Tests | 300+ | ✅ Passing |
| PdfEditor.Redaction.Cli.Tests | 74 | ✅ Passing |
| Integration tests | 100+ | ✅ Passing |

## Notes

This is an independent library that can be used outside of PdfEditor. The goal is to provide reliable, well-tested glyph-level PDF redaction.
