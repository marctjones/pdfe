# Sample PDFs for Redaction Testing

This directory contains real-world PDFs that demonstrate various text storage patterns and edge cases for redaction testing.

## PDF Inventory

### birth-certificate-request-scrambled.pdf

**Source**: Public government form
**Issues Demonstrated**:
- Letters encoded out of reading order in content stream
- CharacterMatcher shows scrambled candidates like `"R PLEEQAUSEES TPR FIONRT..."`
- Requires sorting by position to reconstruct reading order

**Test Cases**:
- Redact "FIRST" - single word
- Redact "MAKING" - word within larger phrase
- Redact "DO" - short word
- Redact "(optional)" - word with punctuation

**Known Behavior**:
- PdfPig text selection works correctly
- Current redaction has matching failures due to letter ordering

---

## Adding New Test PDFs

When adding a new problematic PDF:

1. Copy the PDF to this directory with a descriptive name
2. Add an entry to this README documenting:
   - Source (anonymized if needed)
   - Issues demonstrated
   - Specific test cases
   - Known behavior with current implementation
3. Add corresponding test cases in `RealWorldPdfTests.cs`

## PDF Characteristics Checklist

For each PDF, document:

- [ ] Text operator types used (Tj, TJ, ', ")
- [ ] Positioning operators used (Tm, Td, TD, T*)
- [ ] Font encoding type
- [ ] Whether letters are in reading order
- [ ] Presence of Form XObjects with text
- [ ] Any other unusual characteristics
