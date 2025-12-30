# PdfEditor.Redaction Library Version

**Current Version**: 0.1.0-alpha

## Version History

| Version | Date | Description |
|---------|------|-------------|
| 0.1.0-alpha | TBD | Initial development - Tj operator support |

## Semantic Versioning

This library follows [Semantic Versioning 2.0.0](https://semver.org/):

- **MAJOR**: Incompatible API changes
- **MINOR**: New functionality (backwards compatible)
- **PATCH**: Bug fixes (backwards compatible)

## Development Milestones

### v0.1.0 (Target: v1.3.0 of PdfEditor)
- [ ] Project infrastructure (csproj, test project)
- [ ] Core interfaces and abstractions
- [ ] Tj operator parsing and redaction
- [ ] TJ operator parsing and redaction
- [ ] Birth certificate form redaction working
- [ ] Integration with PdfEditor main application

### v0.2.0 (Future)
- [ ] Quote operators (' and ")
- [ ] Form XObject support
- [ ] Improved logging and diagnostics

### v0.3.0 (Future)
- [x] CID/CJK font support (Issue #63)
- [ ] Type 3 font handling
- [ ] Invisible text (Tr=3) handling

### v1.0.0 (Stable Release)
- [ ] All common text storage mechanisms
- [ ] Comprehensive test coverage
- [ ] Production-ready API
- [ ] Full documentation

## Integration Status

| Component | Library Version | PdfEditor Version | Status |
|-----------|-----------------|-------------------|--------|
| Core redaction | 0.1.0-alpha | 1.3.0 | Planned |

## Notes

This is an independent library that can be used outside of PdfEditor. The goal is to provide reliable, well-tested glyph-level PDF redaction.
