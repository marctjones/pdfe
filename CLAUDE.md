# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a cross-platform PDF editor built with **C# + .NET 8 + Avalonia UI** (MVVM architecture). The application runs on Windows, Linux, and macOS, providing PDF viewing, page manipulation, and content-level redaction capabilities.

**Key Features:**
- Open/view PDFs with zoom and pan controls
- Add, remove, and rotate pages
- Text selection and copy
- Search with highlighting
- Content-level redaction (removes text/graphics from PDF structure, not just visual covering)
- Clipboard history showing redacted text
- Page thumbnails sidebar
- Keyboard shortcuts
- All dependencies use permissive licenses (MIT, Apache 2.0, BSD-3)

## ⚠️ CRITICAL: Redaction Code Requirements

**READ BEFORE MODIFYING ANY REDACTION CODE**

This project implements **TRUE glyph-level removal** for PDF redaction. This is a security-critical feature.

### ABSOLUTE RULES

1. **NEVER replace glyph removal with visual-only redaction** (just drawing black boxes)
2. **NEVER simplify by removing content stream parsing/rebuilding**
3. **ALWAYS maintain the full pipeline**: parse → filter → rebuild → replace → draw
4. **ALWAYS run redaction tests** after any changes: `dotnet test --filter "FullyQualifiedName~Redaction"`

### What Glyph Removal Means

- Text glyphs are **REMOVED** from PDF content stream
- Text extraction tools (pdftotext, PdfPig) **cannot find** the text
- Black box is visual confirmation only (secondary)

### Critical Files - DO NOT SIMPLIFY

```
PdfEditor/Services/RedactionService.cs           ← RemoveContentInArea() is critical
PdfEditor/Services/Redaction/ContentStreamParser.cs  ← Parses text operations
PdfEditor/Services/Redaction/ContentStreamBuilder.cs ← Rebuilds without removed ops
```

### Required Test Assertion

```csharp
var textAfter = PdfTestHelpers.ExtractAllText(redactedPdf);
textAfter.Should().NotContain("REDACTED_TEXT",
    "Text must be REMOVED from PDF structure, not just hidden");
```

**See `REDACTION_AI_GUIDELINES.md` for complete documentation.**

## Build and Run Commands

### Basic Development

```bash
# Restore packages (required after cloning or adding dependencies)
cd PdfEditor
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run

# Run in release mode
dotnet run -c Release
```

### Testing

```bash
# Run all tests
cd PdfEditor.Tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName~RedactSimpleText"

# Run only integration tests
dotnet test --filter "FullyQualifiedName~Integration"
```

### Publishing

```bash
# Linux standalone executable
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Windows standalone executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS standalone executable
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

Published executables are in `bin/Release/net8.0/{runtime}/publish/`

### Build Scripts

```bash
# Use provided build scripts
./build.sh          # Linux/macOS
./build.bat         # Windows
```

## Architecture

### MVVM Pattern

The codebase follows strict MVVM separation:

**View Layer** (`Views/`):
- `MainWindow.axaml` - XAML UI definition
- `MainWindow.axaml.cs` - Code-behind (minimal, only event handlers)

**ViewModel Layer** (`ViewModels/`):
- `MainWindowViewModel.cs` - Application state, commands, and business logic orchestration
- Uses ReactiveUI for property change notifications and command binding

**Model Layer** (`Models/`):
- `PageThumbnail.cs` - Data structures

**Service Layer** (`Services/`):
- `PdfDocumentService.cs` - PDF loading, saving, page add/remove
- `PdfRenderService.cs` - PDF-to-image rendering (uses PDFium)
- `RedactionService.cs` - Orchestrates content-level redaction
- `PdfTextExtractionService.cs` - Text extraction from PDFs
- `PdfSearchService.cs` - Search functionality
- `CoordinateConverter.cs` - Coordinate system conversions

### Data Flow

```
User Interaction (View)
    ↓ Command Binding
ViewModel (MainWindowViewModel)
    ↓ Calls Services
Service Layer (PdfDocumentService, RedactionService, etc.)
    ↓ Uses Libraries
PDF Libraries (PdfSharpCore, PdfPig, PDFtoImage)
```

When modifying the UI, update the XAML and bind to ViewModel properties. Never put business logic in code-behind.

## Redaction Engine Architecture

This is the most complex part of the codebase. Located in `Services/Redaction/`:

### Components

1. **ContentStreamParser.cs** (~500 lines, high complexity)
   - Parses PDF content streams into structured operations
   - Tracks graphics state (transformations, colors) via state stack
   - Tracks text state (font, position, spacing)
   - Calculates bounding boxes for each operation
   - Returns list of `PdfOperation` objects

2. **PdfOperation.cs** (~200 lines)
   - Base class and derived types: `TextOperation`, `PathOperation`, `ImageOperation`, `StateOperation`, `TextStateOperation`
   - Each has `BoundingBox` property and `IntersectsWith(Rect)` method
   - Represents parsed PDF operators with position information

3. **TextBoundsCalculator.cs** (~150 lines, high complexity)
   - Calculates accurate text bounding boxes
   - Applies font size, character/word spacing, horizontal scaling
   - Applies text matrix and graphics transformation matrix
   - Handles PDF (bottom-left) to Avalonia (top-left) coordinate conversion

4. **ContentStreamBuilder.cs** (~150 lines)
   - Serializes `PdfOperation` objects back to PDF operator syntax
   - Handles proper escaping and formatting
   - Rebuilds content streams after filtering

5. **State Tracking**
   - `PdfGraphicsState.cs` - Transformation matrix, line width, colors, save/restore stack
   - `PdfTextState.cs` - Font, size, position, spacing, text matrix
   - `PdfMatrix` helper - 2D transformations

6. **RedactionService.cs** (~150 lines)
   - Main entry point for redaction
   - Orchestrates: parse → filter → rebuild → replace → draw black rectangle
   - Method: `RedactArea(PdfPage page, Rect area)`

### Redaction Flow

```
1. Parse content stream → List<PdfOperation> with bounding boxes
2. Filter operations → Remove those intersecting redaction area
3. Rebuild content stream → Serialize remaining operations to PDF syntax
4. Replace page content → Update PDF with new content stream
5. Draw black rectangle → Ensure visual coverage
```

### Coordinate Systems

**Critical**: PDF uses bottom-left origin, Avalonia uses top-left origin.

Conversion: `AvaloniaY = PageHeight - PdfY - RectHeight`

This conversion happens in `TextBoundsCalculator` and when drawing redaction rectangles.

### Supported PDF Operators

- **Text**: `Tj`, `TJ`, `'`, `"`
- **Text State**: `BT`, `ET`, `Tf`, `Td`, `TD`, `Tm`, `T*`, `TL`, `Tc`, `Tw`, `Tz`
- **Graphics State**: `q`, `Q`, `cm`
- **Paths**: `m`, `l`, `c`, `v`, `y`, `h`, `re`, `S`, `s`, `f`, `F`, `f*`, `B`, `B*`, `b`, `b*`
- **Images**: `Do`

## Key Dependencies

Located in `PdfEditor/PdfEditor.csproj`:

**UI Framework:**
- Avalonia 11.1.3 (cross-platform XAML UI)
- ReactiveUI 20.1.1 (MVVM framework)

**PDF Libraries:**
- PDFsharp 6.2.2 (MIT) - PDF structure manipulation
- PdfPig 0.1.11 (Apache 2.0) - PDF parsing, text extraction
- PDFtoImage 4.0.2 (MIT) - PDF rendering via PDFium
- SkiaSharp 2.88.8 (MIT) - 2D graphics

All licenses are permissive (MIT/Apache 2.0/BSD-3), no copyleft restrictions.

## Test Infrastructure

Located in `PdfEditor.Tests/`:

**Framework**: xUnit 2.5.3 with FluentAssertions 6.12.0

**Test Count**: 600+ tests (598 passing, 2 skipped for VeraPDF)

**Utilities:**
- `Utilities/TestPdfGenerator.cs` - Creates test PDFs with known content
- `Utilities/PdfTestHelpers.cs` - PDF inspection and text extraction

**Test Categories:**
- `Integration/` - End-to-end redaction, coordinate conversion, batch processing
- `Unit/` - ViewModel, coordinate conversion, PDF operations
- `UI/` - Headless UI tests, ViewModel integration
- `Security/` - Content removal verification

**Key Test Files:**
- `GuiRedactionSimulationTests.cs` - Simulates exact GUI workflow to catch coordinate issues
- `CoordinateConverterTests.cs` - Validates coordinate math
- `ComprehensiveRedactionTests.cs` - Full redaction pipeline tests

**Running Tests:** See "Build and Run Commands" section above.

## Common Development Workflows

### Adding a New PDF Operation Type

1. Add operator handling in `ContentStreamParser.ParseOperator()`
2. Create new operation class in `PdfOperation.cs` if needed
3. Implement bounding box calculation
4. Add serialization in `ContentStreamBuilder.SerializeOperation()`
5. Add unit tests

### Modifying UI

1. Update XAML in `Views/MainWindow.axaml`
2. Add properties/commands to `ViewModels/MainWindowViewModel.cs`
3. Use ReactiveUI's `[Reactive]` attribute for bindable properties
4. Commands use ReactiveUI's `ReactiveCommand`

### Adding a New Service

1. Create service class in `Services/`
2. Inject into `MainWindowViewModel` constructor
3. Call service methods from ViewModel commands
4. Add corresponding tests in `PdfEditor.Tests/`

## Debugging Notes

### Redaction Not Working

1. Check console output - parser logs operations found/removed
2. Verify coordinate system (PDF vs Avalonia Y-axis)
3. Check bounding box calculations in `TextBoundsCalculator`
4. Enable verbose logging in `ContentStreamParser`

### PDF Rendering Issues

- PDFtoImage uses PDFium (native library)
- Linux requires `libgdiplus`: `sudo apt-get install libgdiplus`
- Check `PdfRenderService.cs` for rendering code

### Build Failures

- Run `dotnet restore` first
- Ensure .NET 8.0 SDK installed: `dotnet --version`
- Clear build artifacts: `dotnet clean`

### Build Warnings

**IMPORTANT**: Always maintain a clean build (0 warnings, 0 errors).

Common warnings and fixes:
- **CS8618** (Non-nullable property not initialized):
  - Add `= null!;` to properties initialized in constructor
  - Example: `public ReactiveCommand<Unit, Unit> SaveCommand { get; } = null!;`
  - See issue #29 for systematic fix

Never let warnings accumulate - fix them proactively when they appear.

## Important Implementation Details

### State Stack Handling

PDF uses `q` (save) and `Q` (restore) operators to manage graphics state. The parser maintains a state stack:

```csharp
case "q": // Save state
    _stateStack.Push(_currentState.Clone());
    break;
case "Q": // Restore state
    if (_stateStack.Count > 0)
        _currentState = _stateStack.Pop();
    break;
```

Always maintain state stack integrity when parsing.

### Text Matrix Transformations

Text position is calculated from text matrix + graphics transformation matrix:

```csharp
var transformedMatrix = textState.TextMatrix.Multiply(graphicsState.TransformationMatrix);
var position = transformedMatrix.Transform(new Point(0, 0));
```

This is critical for accurate text positioning.

### Content Stream Replacement

To replace page content after filtering:

```csharp
page.Contents.Elements.Clear();
page.Contents.CreateSingleContent(newContentBytes);
```

Never manually modify content stream bytes without parsing first.

## Performance Considerations

- Simple page (50 ops): ~10-20ms to redact
- Complex page (500+ ops): ~100-200ms to redact
- For multiple redactions on same page, parse once and filter multiple areas
- Memory usage: ~1-5MB per page during parsing

## Limitations

1. **Font Metrics**: Uses approximation, not actual font dictionaries
2. **Inline Images**: `BI...ID...EI` operators not yet handled
3. **Rotated Pages**: Page rotation (`/Rotate` entry) not fully supported
4. **Clipping Paths**: `W`, `W*` operators not tracked
5. **Form XObjects**: Nested content streams not fully parsed

See GitHub issues labeled `component: redaction-engine` for enhancement tracking.

## File Locations Quick Reference

```
PdfEditor/
├── Services/
│   ├── PdfDocumentService.cs       # PDF load/save/page manipulation
│   ├── PdfRenderService.cs         # PDF to image rendering
│   ├── RedactionService.cs         # Redaction orchestration
│   └── Redaction/
│       ├── ContentStreamParser.cs  # Parse PDF operators
│       ├── ContentStreamBuilder.cs # Build PDF operators
│       ├── PdfOperation.cs         # Operation models
│       ├── TextBoundsCalculator.cs # Text positioning
│       ├── PdfGraphicsState.cs     # Graphics state tracking
│       └── PdfTextState.cs         # Text state tracking
├── ViewModels/
│   └── MainWindowViewModel.cs      # Application state & commands
├── Views/
│   ├── MainWindow.axaml            # UI definition
│   └── MainWindow.axaml.cs         # Code-behind
├── Models/
│   └── PageThumbnail.cs            # Data models
├── App.axaml                       # Application resources
├── Program.cs                      # Entry point
└── PdfEditor.csproj                # Project file

PdfEditor.Tests/
├── Integration/
│   ├── GuiRedactionSimulationTests.cs  # GUI workflow simulation
│   ├── ComprehensiveRedactionTests.cs  # Full redaction tests
│   └── ...
├── Unit/
│   ├── CoordinateConverterTests.cs     # Coordinate math tests
│   └── ...
├── UI/
│   └── HeadlessUITests.cs              # UI integration tests
├── Security/
│   └── ContentRemovalVerificationTests.cs
├── Utilities/
│   ├── TestPdfGenerator.cs
│   └── PdfTestHelpers.cs
└── PdfEditor.Tests.csproj

Documentation:
├── README.md                       # User-facing documentation
├── CLAUDE.md                       # This file - AI assistant guidelines
├── REDACTION_AI_GUIDELINES.md      # AI safety guidelines for redaction
└── LICENSES.md                     # Dependency licenses
```

## Security Notes

This redaction implementation:
- ✅ Removes content from PDF structure (not just visual covering)
- ✅ Handles text, graphics, and images
- ❌ Does NOT remove PDF metadata (XMP, Info dict)
- ❌ Does NOT handle PDF revision history
- ❌ Does NOT remove embedded files/attachments

For maximum security, also remove metadata and flatten the PDF after redaction.

## Current Status (v1.3.0 → v1.4.0)

### v1.3.0 Status

**Redaction:** Whole-operation removal (stable, no corruption)
- ✅ Works correctly - no text doubling/corruption
- ❌ Limitation: Redacting "Birth" removes entire "Birth Certificate"
- Library: `PdfEditor.Redaction.TextRedactor` - proven to work
- GUI: Character-level filtering DISABLED (was buggy)

**What Happened:**
- User testing revealed text corruption in GUI redaction
- Root cause: `CharacterLevelTextFilter` in GUI (not library)
- Temporary fix: Disabled character-level, use whole-operation removal
- See: #103, #104, #106 for bug details

### v1.4.0 Plan: Glyph-Level Redaction

**Goal:** Implement TRUE glyph-level redaction in `PdfEditor.Redaction` library

**Implementation Issues:**
- #112 - Master tracking issue
- #113 - GlyphRemover (orchestrator)
- #114 - LetterFinder (spatial matching)
- #115 - TextSegmenter (split operations)
- #116 - OperationReconstructor (build new ops)
- #117 - ContentStreamBuilder (Tm operator emission)
- #118 - TextRedactor integration
- #119 - Comprehensive tests

**Resources:**
- Discussion: https://github.com/marctjones/pdfe/discussions/120 (implementation plan)
- Wiki: `docs/WIKI_PAGES_TODO.md` (PDF operators, content streams, glyph-level redaction)

**Timeline:** 20-26 hours

**Why it will work:**
- All logic in library (not GUI)
- Spatial matching (not text search)
- Use PdfPig's accurate letter positions
- Proper ContentStreamBuilder integration (no raw bytes)

## Task Tracking and GitHub Issues

**IMPORTANT**: This project uses GitHub Issues for ALL task tracking, feature requests, bugs, and enhancements.

### Rules for Task Management

1. **DO NOT add TODO comments** in code
   - ❌ Bad: `// TODO: Add error handling`
   - ✅ Good: Create GitHub issue, reference in code: `// See issue #25`

2. **DO NOT create scattered enhancement lists** in documentation
   - ❌ Bad: Adding "Future Enhancements" sections to docs
   - ✅ Good: Create GitHub issues with proper labels

3. **DO reference GitHub issues** when relevant
   - In code comments: `// Handles deleted files - See issue #25`
   - In documentation: `Window position/size persistence is tracked in issue #23`
   - In commit messages: `Fixes #17` or `Addresses #19`

4. **ALWAYS create issues proactively** when you identify:
   - Bugs or problems
   - Enhancement opportunities
   - Technical debt
   - Documentation gaps
   - Test coverage needs

### GitHub Issue Labels

The project uses standardized labels (see `scripts/setup-github-labels.sh`):

**Type Labels** (GitHub defaults):
- `bug` - Something isn't working
- `enhancement` - New feature or request
- `documentation` - Improvements to docs
- `security` - Security concerns
- `question` - Further information needed

**Component Labels** (architecture-specific):
- `component: redaction-engine` - Content stream parsing, glyph removal
- `component: pdf-rendering` - PDFium, image rendering, caching
- `component: ui-framework` - Avalonia, XAML, bindings, ReactiveUI
- `component: text-extraction` - Text extraction, OCR, search
- `component: file-management` - Open/save, recent files, document state
- `component: clipboard` - Copy/paste, clipboard history
- `component: verification` - Signature/redaction verification
- `component: coordinates` - PDF/screen coordinate systems

**Priority Labels**:
- `priority: critical` - Blocks usage, data loss, security
- `priority: high` - Important but not blocking
- `priority: medium` - Nice to have
- `priority: low` - Future consideration

**Effort Labels**:
- `effort: small` - < 1 hour
- `effort: medium` - 1-4 hours
- `effort: large` - > 4 hours

**Other Labels**:
- `status: blocked` - Waiting on something else
- `good first issue` - Easy for new contributors
- `help wanted` - Community input needed
- `platform: linux/windows/macos` - Platform-specific issues

### Creating Issues via CLI

```bash
# Create a new issue
gh issue create \
  --title "Add dark mode support" \
  --body "Description of the feature..." \
  --label "enhancement,component: ui-framework,priority: medium"

# View all issues
gh issue list

# View issues by label
gh issue list --label "priority: high"

# Close an issue
gh issue close 42 --comment "Fixed in PR #43"
```

### Issue References in Code

When code relates to a known issue, add a comment:

```csharp
// File existence check for Recent Files
// See issue #25 for enhancement: show user-facing error dialog
if (!System.IO.File.Exists(filePath))
{
    _logger.LogWarning("Recent file not found: {FilePath}", filePath);
    return;
}
```

### Current High-Priority Issues

Check GitHub for the latest, but as of this writing:
- **#95**: Text leak - substring redaction leaves partial text (CRITICAL, security)
- **#96**: Empty area redactions - coordinate mismatch (HIGH)
- **#87**: Substring matching limitations (additional test cases added)

View all: `gh issue list --label "priority: high,priority: critical"`

### Using Discussions for Research and Ideas

GitHub Discussions is enabled for collaborative research, ideas, and questions that don't fit the issue tracker. However, the GitHub CLI doesn't support creating Discussions directly, so we use a hybrid approach:

**When to use Discussions vs Issues:**
- **Discussions:** Research questions, ideas, open-ended exploration, lab notes
- **Issues:** Bugs, features, tasks with clear completion criteria

**Workflow:**
1. Create research issues with `question` label (like #97)
2. When ready for community input, manually convert to Discussion on GitHub
3. Reference the Discussion in related issues

**Checking for Research Topics:**
```bash
# List research/question issues
gh issue list --label "question"

# Check if any are ready to convert to Discussions
# Look for issues with active conversation but no clear action items
```

**Important:** When reaching a stable milestone (like v1.3.0), review Discussions for:
- Ideas that have crystallized into actionable features
- Research findings that inform next steps
- Community feedback on direction

### Bulk Issue Management

Scripts are available for managing issues:
- `scripts/setup-github-labels.sh` - Create all standardized labels
- `scripts/import-github-issues.sh` - Bulk import issues from backlog

## Knowledge Management Strategy

**IMPORTANT**: pdfe uses a four-tier content organization system across Wiki, Discussions, Issues, and Markdown files.

### The Four-Tier System

**Tier 1: Wiki** (Educational, Timeless Reference)
- **Purpose**: Explain concepts, file formats, algorithms, theory
- **Content**: PDF structure, redaction theory, coordinate systems, content streams
- **Audience**: Anyone learning about PDF editing concepts
- **Lifespan**: Timeless - updated when understanding changes
- **Examples**: "PDF Content Streams", "Glyph-Level Redaction", "PDF Coordinate Systems"

**Tier 2: Discussions** (Feedback, Ideas, Lab Notes)
- **Purpose**: Unstructured thoughts, feedback, ideas, Q&A, experiment results
- **Content**: Lab notebooks, feature ideas, usage questions, test findings
- **Audience**: Developers, contributors, future collaborators
- **Lifespan**: Permanent but evolving - stays open for ongoing conversation
- **Examples**: "Lab Notebook: Week of Dec 22", "Idea: Batch Redaction UI", "Corpus Test Results"

**Tier 3: Issues** (Actionable Tasks)
- **Purpose**: Track bugs, features, and tasks with clear completion criteria
- **Content**: Bugs to fix, features to implement, tests to add
- **Audience**: Developers implementing changes
- **Lifespan**: Temporary - closed when completed
- **Examples**: "Fix coordinate conversion (#25)", "Bug: Text extraction fails (#66)"

**Tier 4: Markdown Files** (Code Documentation)
- **Purpose**: Document code architecture, API, project-specific guides
- **Content**: README, CLAUDE.md, REDACTION_AI_GUIDELINES.md
- **Audience**: Developers working with the codebase
- **Lifespan**: Version-controlled - updates with code changes
- **Examples**: "README.md", "CLAUDE.md", "REDACTION_ENGINE.md"

### Decision Matrix: Where Does Content Go?

| Content Type | Wiki | Discussion | Issue | Markdown |
|--------------|------|------------|-------|----------|
| **PDF format spec** | ✅ Primary | - | - | Reference |
| **Algorithm theory** | ✅ Primary | - | - | - |
| **Bug to fix** | - | - | ✅ Primary | - |
| **Feature to implement** | - | Discussion→ | ✅ Primary | - |
| **Research question** | Reference | Discussion→ | ✅ Primary | - |
| **Unstructured thoughts** | - | ✅ Primary | - | - |
| **Feature idea (unvalidated)** | - | ✅ Primary | →Issue | - |
| **Test results** | - | ✅ Primary | Reference | Reference |
| **Usage question** | Reference | ✅ Primary | - | - |
| **Code API docs** | - | - | - | ✅ Primary |
| **Lessons learned** | ✅ Primary | ✅ Initial | - | - |

### Content Migration Guidelines

**FROM Issues TO Discussions**:
Migrate if issue is:
- ❌ Not actionable (no clear completion criteria)
- ❌ Open-ended research without specific goal
- ❌ Ideas without implementation plan
- ❌ Placeholder for "someday maybe"

**FROM Discussions TO Issues**:
Convert when discussion leads to:
- ✅ Specific, actionable task
- ✅ Clear success criteria
- ✅ Decision to implement

**FROM Discussions TO Wiki**:
Migrate when discussion crystallizes into:
- ✅ Documented understanding
- ✅ Educational reference material
- ✅ Timeless knowledge

## Long-Running Commands

**IMPORTANT**: Avoid running long-running commands directly in Claude Code. Instead, create scripts for the user to run in a separate terminal.

### Script Runner Pattern

For long-running tests or builds, use the script runner pattern:

```bash
# Run tests with logging (user runs in separate terminal)
./scripts/run-tests.sh | tee logs/test_$(date +%Y%m%d_%H%M%S).log

# Run corpus tests with logging
./scripts/run-corpus-tests.sh 2>&1 | tee logs/corpus_$(date +%Y%m%d_%H%M%S).log
```

### Creating Scripts for Long-Running Tasks

When you need to run something that takes >30 seconds:

1. **Create a shell script** in `scripts/` with the command
2. **Add logging** with `tee` to capture output
3. **Tell the user** to run it in a separate terminal
4. **Access logs** later via the logged output file

Example script structure:
```bash
#!/bin/bash
# scripts/run-long-task.sh
set -e
LOG_DIR="$(dirname "$0")/../logs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/task_$(date +%Y%m%d_%H%M%S).log"

echo "Logging to: $LOG_FILE"
echo "Run this in a separate terminal:"
echo "  ./scripts/run-long-task.sh 2>&1 | tee $LOG_FILE"

# Actual command here
dotnet test --logger "console;verbosity=detailed"
```

### Available Test Scripts

- `scripts/test.sh` - Run all unit tests
- `scripts/run-corpus-tests.sh` - Run veraPDF corpus tests (long-running)
- `scripts/verify-true-redaction.sh` - Verify redaction removes content

## Test PDF Corpus

**IMPORTANT**: Test PDFs from PDF Association are NOT checked into git due to licensing concerns.

### Downloading Test PDFs

Run the download script to fetch test PDFs locally:

```bash
./scripts/download-test-pdfs.sh
```

This downloads:
- **veraPDF Corpus** - 2,694 PDF/A test files covering PDF/A-1a/1b/2a/2b/2u/3b/4/4e/4f, PDF/UA-1/2
- **Isartor Test Suite** - PDF/A-1 conformance tests
- **Sample PDFs** - Additional challenging test cases

Files are stored in `test-pdfs/` which is gitignored.

### Corpus Test Structure

The `PdfEditor.Redaction.Cli.Tests` project includes corpus-based tests:

```
PdfEditor.Redaction.Cli.Tests/
├── Integration/
│   └── VeraPdfCorpusTests.cs    # Tests against veraPDF corpus
├── Unit/
│   ├── RedactCommandTests.cs    # pdfer redact command
│   ├── VerifyCommandTests.cs    # pdfer verify command
│   ├── SearchCommandTests.cs    # pdfer search command
│   └── InfoCommandTests.cs      # pdfer info command
└── TestHelpers/
    ├── PdferTestRunner.cs       # CLI test runner
    └── TestPdfCreator.cs        # Creates test PDFs
```

### Running Corpus Tests

```bash
# Download corpus first (if not already done)
./scripts/download-test-pdfs.sh

# Run corpus tests
cd PdfEditor.Redaction.Cli.Tests
dotnet test --filter "Category=Corpus"
```
