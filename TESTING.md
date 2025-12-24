# Testing Guide for pdfe

This document describes the test suite structure, how to run tests, and how to work with broken/skipped tests.

## Test Projects

1. **PdfEditor.Tests** - Main GUI/Editor tests (783 tests)
   - Integration tests
   - Unit tests
   - UI tests (headless)
   - Automation script tests

2. **PdfEditor.Redaction.Tests** - Redaction library tests
   - Unit tests for redaction engine
   - Content stream parsing tests

3. **PdfEditor.Redaction.Cli.Tests** - CLI (pdfer) tests
   - Command-line tool tests
   - Corpus integration tests

## Running Tests

### Run All Tests
```bash
# Build and run all tests
./scripts/run-all-tests.sh

# Skip build, just run tests
./scripts/run-all-tests.sh --no-build
```

### Run Automation Script Tests Only
```bash
# Build and run automation tests
./scripts/run-automation-tests.sh

# Skip build
./scripts/run-automation-tests.sh --no-build
```

### Run Specific Test Categories
```bash
# Run tests by category
dotnet test --filter "Category=Performance"
dotnet test --filter "Category=ExternalValidator"

# Run tests by component
dotnet test --filter "FullyQualifiedName~Integration"
dotnet test --filter "FullyQualifiedName~Unit"

# Run tests by tool dependency
dotnet test --filter "Tool=veraPDF"
dotnet test --filter "Tool=qpdf"
```

### Run Tests Excluding Broken Tests
```bash
# Exclude character-level tests (not implemented - issue #98)
dotnet test --filter "FullyQualifiedName!~CharacterLevel & FullyQualifiedName!~CharacterMatcher"

# Exclude all known broken tests
./scripts/run-passing-tests.sh
```

## Test Categories and Traits

Tests use xUnit `[Trait]` attribute for categorization:

### Category Traits
- `Category=ExternalValidator` - Requires external tools (veraPDF, qpdf, mutool)
- `Category=Performance` - Performance/benchmarking tests
- `Category=Corpus` - Tests against large PDF corpus
- `Category=Broken` - Known broken tests with tracking issues

### Tool Traits
- `Tool=veraPDF` - Requires veraPDF installation
- `Tool=qpdf` - Requires qpdf installation
- `Tool=mutool` - Requires mutool (MuPDF) installation
- `Tool=Multiple` - Requires multiple external tools

## Test Status (As of 2025-12-23)

### Overall Results
```
Total: 783 tests
  ✅ Passed: 756 (97%)
  ❌ Failed: 19 (2%)
  ⚠️  Skipped: 8 (1%)
```

### Known Failures (With Issues)

**Character-Level Redaction (13 tests) - Issue #98**
- Not implemented yet (v1.4.0 feature)
- Tests exist for future glyph-splitting redaction
- Files: `CharacterMatcherTests.cs`, `CharacterLevelTextFilterTests.cs`, `CharacterLevelRedactionTests.cs`

**Metadata Redaction (1 test) - Issue #99**
- `MetadataRedactionIntegrationTests.SanitizeMetadata_EmptyDocument`
- Edge case handling needed

**Render Integration (1 test) - Issue #100**
- `RenderIntegrationTest.RenderPage_ShouldComplete`
- May require GUI environment

**Scripted GUI (1 test) - Issue #101**
- `ScriptedGuiTests.Script_InvalidSyntax_ReturnsCompilationError`
- Error handling edge case

**Automation Scripts (3 tests) - Issues #95, #97**
- `AutomationScript_VeraPdfCorpusSample` - Corpus has no text (#97)
- `AutomationScript_BirthCertificateSpecificWords` - Text leak (#95)
- `AutomationScript_RedactText` - Text leak (#95)

### Intentionally Skipped (6 tests) - Issue #59
- Waiting for full GUI integration
- Located in `ScriptedGuiTests.cs`
- All marked with `Skip = "Requires GUI integration (#59)"`

## Test Output Verbosity

xUnit test output can be very noisy. Control verbosity with:

```bash
# Minimal output (just summary)
dotnet test --verbosity minimal

# Quiet (errors only)
dotnet test --verbosity quiet

# Normal (default)
dotnet test --verbosity normal

# Detailed (all test output)
dotnet test --verbosity detailed
```

### Automation Script Verbosity

Automation scripts intentionally show progress messages because tests take 5-30 seconds each:
- Loading PDFs
- Extracting text
- Applying redactions
- Verification

This is intentional to prevent "test hung?" concerns.

## Working with Broken Tests

### Adding a Broken Test Trait

When a test is broken and has a tracking issue:

```csharp
[Fact]
[Trait("Category", "Broken")]
[Trait("Issue", "98")]  // GitHub issue number
public void CharacterLevel_Test_NotYetImplemented()
{
    // Test implementation
}
```

### Temporarily Skipping a Test

For tests that need to be fixed but aren't yet:

```csharp
[Fact(Skip = "Broken - See issue #99")]
public void SanitizeMetadata_EmptyDocument()
{
    // Test implementation
}
```

### Running Only Passing Tests

```bash
# Exclude broken category
dotnet test --filter "Category!=Broken"

# Or use the helper script
./scripts/run-passing-tests.sh
```

## Test Data

### Test PDFs
- Location: `test-pdfs/` (gitignored)
- Download: `./scripts/download-test-pdfs.sh`
- Includes: veraPDF corpus (2,694 PDFs), sample PDFs

### Generated Test PDFs
- Created by `TestPdfGenerator.cs`
- Used in unit/integration tests
- Deterministic content for reliable testing

## External Tool Dependencies

Some tests require external tools:

```bash
# Install on Debian/Ubuntu
sudo apt-get install qpdf mupdf-tools

# Install veraPDF
# See: https://verapdf.org/
```

Skip external tool tests:
```bash
dotnet test --filter "Category!=ExternalValidator"
```

## CI/CD Considerations

For continuous integration:

1. **Run only passing tests** in CI pipeline:
   ```bash
   dotnet test --filter "Category!=Broken"
   ```

2. **Separate external tool tests** (may not have tools installed):
   ```bash
   dotnet test --filter "Category!=ExternalValidator"
   ```

3. **Use minimal verbosity** for cleaner logs:
   ```bash
   dotnet test --verbosity minimal --nologo
   ```

## Adding New Tests

### Test Naming Convention
- `{ComponentName}Tests.cs` for unit tests
- `{Feature}IntegrationTests.cs` for integration tests
- Descriptive method names: `RedactArea_InvalidInput_ThrowsException`

### Test Organization
```
PdfEditor.Tests/
├── Unit/                    # Fast, isolated tests
├── Integration/             # Multi-component tests
├── UI/                      # GUI/headless UI tests
├── Security/                # Security verification tests
└── Utilities/               # Test helpers
```

### Add Traits
```csharp
[Fact]
[Trait("Category", "Integration")]
[Trait("Component", "RedactionEngine")]
public void MyNewTest() { ... }
```

## Debugging Failed Tests

### Run Single Test with Details
```bash
dotnet test --filter "MyTestName" --logger "console;verbosity=detailed"
```

### Attach Debugger
In VS Code or Rider, set breakpoint and run test in debug mode.

### Check Test Logs
```bash
# Automation test logs
ls -lht logs/automation_tests_*.log | head -5

# All test logs
ls -lht logs/all_tests_*.log | head -5

# View latest
tail -100 logs/automation_tests_*.log
```

## Test Milestones

### v1.3.0 (Current)
- ✅ 756 passing tests (97% pass rate)
- ✅ Birth certificate automation test passes
- ⚠️  Known limitations: substring redaction leaks (#95)
- 🚧 Character-level redaction not yet implemented (#98)

### v1.4.0 (Planned)
- Implement character-level redaction (#98)
- Fix remaining broken tests (#99, #100, #101)
- Target: 100% pass rate on implemented features

## Getting Help

- **Test infrastructure issues**: Create issue with label `testing`
- **Broken test without issue**: Create issue with reproduction steps
- **Questions about tests**: Check CLAUDE.md or create Discussion
