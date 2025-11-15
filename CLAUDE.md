# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a cross-platform PDF editor built with **C# + .NET 8 + Avalonia UI** (MVVM architecture). The application runs on Windows, Linux, and macOS, providing PDF viewing, page manipulation, and content-level redaction capabilities.

**Key Features:**
- Open/view PDFs with zoom and pan controls
- Add/remove pages
- Content-level redaction (removes text/graphics from PDF structure, not just visual covering)
- Page thumbnails sidebar
- All dependencies use permissive licenses (MIT, Apache 2.0, BSD-3)

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
- PdfSharpCore 1.3.65 (MIT) - PDF structure manipulation
- PdfPig 0.1.11 (Apache 2.0) - PDF parsing, text extraction
- PDFtoImage 4.0.2 (MIT) - PDF rendering via PDFium
- SkiaSharp 2.88.8 (MIT) - 2D graphics

All licenses are permissive (MIT/Apache 2.0/BSD-3), no copyleft restrictions.

## Test Infrastructure

Located in `PdfEditor.Tests/`:

**Framework**: xUnit 2.5.3 with FluentAssertions 6.12.0

**Utilities:**
- `Utilities/TestPdfGenerator.cs` - Creates test PDFs with known content
- `Utilities/PdfTestHelpers.cs` - PDF inspection and text extraction

**Tests:**
- `Integration/RedactionIntegrationTests.cs` - 5 comprehensive integration tests
- Tests verify content removal, not just visual redaction

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

See REDACTION_ENGINE.md for detailed enhancement priorities.

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
│   └── RedactionIntegrationTests.cs
├── Utilities/
│   ├── TestPdfGenerator.cs
│   └── PdfTestHelpers.cs
└── PdfEditor.Tests.csproj

Documentation:
├── README.md                       # User-facing documentation
├── ARCHITECTURE_DIAGRAM.md         # Visual architecture diagrams
├── REDACTION_ENGINE.md            # Deep dive on redaction
├── TESTING_GUIDE.md               # Test infrastructure guide
└── QUICK_REFERENCE.md             # Quick development reference
```

## Security Notes

This redaction implementation:
- ✅ Removes content from PDF structure (not just visual covering)
- ✅ Handles text, graphics, and images
- ❌ Does NOT remove PDF metadata (XMP, Info dict)
- ❌ Does NOT handle PDF revision history
- ❌ Does NOT remove embedded files/attachments

For maximum security, also remove metadata and flatten the PDF after redaction.
