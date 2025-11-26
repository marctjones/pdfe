# PdfEditor

A cross-platform desktop PDF editor built with **C# + .NET 8 + Avalonia UI** featuring TRUE content-level redaction.

[![Release](https://img.shields.io/github/v/release/marctjones/pdfe)](https://github.com/marctjones/pdfe/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-600%20passing-brightgreen)](PdfEditor.Tests)

## Features

### Core Features
- **Open and view PDF documents** with smooth rendering
- **Page manipulation** - Add, remove, and rotate pages
- **Text selection** - Select and copy text from PDFs
- **Search** - Find text with highlighting and navigation
- **Zoom and pan** - Multiple zoom modes (fit width, fit page, actual size)
- **Page thumbnails** - Sidebar with clickable thumbnails
- **Keyboard shortcuts** - Full keyboard navigation support
- **Cross-platform** - Windows, Linux, and macOS

### TRUE Content-Level Redaction
Unlike most PDF redaction tools that just draw black boxes over content, PdfEditor implements **true content removal**:

- **Text is REMOVED** from the PDF structure, not just hidden
- **Verified with external tools** (pdftotext, PdfPig) - redacted text cannot be extracted
- **Clipboard history** shows exactly what text was removed
- **600+ automated tests** verify redaction integrity

## Installation

### From Releases (Recommended)

Download the latest release for your platform from [GitHub Releases](https://github.com/marctjones/pdfe/releases).

**Linux:**
```bash
curl -sSL https://raw.githubusercontent.com/marctjones/pdfe/main/install-from-release.sh | bash
```

**Windows (PowerShell):**
```powershell
irm https://raw.githubusercontent.com/marctjones/pdfe/main/install-from-release.ps1 -OutFile install.ps1; .\install.ps1
```

**macOS:**
```bash
# Download and extract from releases, then:
chmod +x PdfEditor-macos-*/PdfEditor
./PdfEditor-macos-*/PdfEditor
```

### From Source

```bash
# Clone the repository
git clone https://github.com/marctjones/pdfe.git
cd pdfe

# Build and run
cd PdfEditor
dotnet restore
dotnet run
```

## Usage

### Basic Operations
1. **Open a PDF**: File > Open or Ctrl+O
2. **Navigate pages**: Page Up/Down, arrow keys, or click thumbnails
3. **Zoom**: Ctrl+Plus/Minus, or use View menu
4. **Search**: Ctrl+F to find text

### Redaction
1. **Enable redaction mode**: Click "Redact Mode" button or press R
2. **Select area**: Click and drag to select the area to redact
3. **Apply redaction**: Click "Apply Redaction" to permanently remove content
4. **Verify**: Check clipboard history sidebar to see what was removed
5. **Save**: File > Save to save the redacted PDF

### Keyboard Shortcuts
| Action | Shortcut |
|--------|----------|
| Open file | Ctrl+O |
| Save | Ctrl+S |
| Find | Ctrl+F |
| Zoom in | Ctrl+Plus |
| Zoom out | Ctrl+Minus |
| Actual size | Ctrl+1 |
| Fit width | Ctrl+2 |
| Fit page | Ctrl+3 |
| Next page | Page Down |
| Previous page | Page Up |
| Toggle redaction | R |
| Toggle text selection | T |

## Technology Stack

### Framework & UI
- **.NET 8.0** - Cross-platform runtime
- **Avalonia UI 11.1.3** (MIT) - Cross-platform XAML UI framework
- **ReactiveUI** (MIT) - MVVM framework

### PDF Libraries (All Permissive Licenses)
- **PdfSharpCore 1.3.65** (MIT) - PDF manipulation
- **PDFtoImage 4.0.2** (MIT) - PDF rendering via PDFium
- **PdfPig 0.1.11** (Apache 2.0) - PDF parsing and text extraction
- **SkiaSharp 2.88.8** (MIT) - 2D graphics

## Project Structure

```
pdfe/
├── PdfEditor/                 # Main application
│   ├── Models/               # Data models
│   ├── Services/             # Business logic
│   │   ├── Redaction/       # Redaction engine components
│   │   ├── PdfDocumentService.cs
│   │   ├── PdfRenderService.cs
│   │   ├── RedactionService.cs
│   │   ├── PdfTextExtractionService.cs
│   │   └── PdfSearchService.cs
│   ├── ViewModels/           # MVVM view models
│   └── Views/                # UI views
├── PdfEditor.Tests/          # Test suite (600+ tests)
│   ├── Integration/         # Integration tests
│   ├── Unit/               # Unit tests
│   ├── UI/                 # UI tests
│   └── Security/           # Security verification tests
├── PdfEditor.Demo/           # Demo application
└── PdfEditor.Validator/      # PDF validation tools
```

## Testing

Run the comprehensive test suite:

```bash
cd PdfEditor.Tests
dotnet test
```

The test suite includes:
- **Unit tests** - ViewModel, coordinate conversion, PDF operations
- **Integration tests** - End-to-end redaction workflows
- **GUI simulation tests** - Coordinate conversion validation
- **Security tests** - Verify content is truly removed

## Building

### Development Build
```bash
cd PdfEditor
dotnet build
dotnet run
```

### Release Build
```bash
# Linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

## Documentation

- **[ARCHITECTURE_DIAGRAM.md](ARCHITECTURE_DIAGRAM.md)** - Visual architecture diagrams
- **[REDACTION_ENGINE.md](REDACTION_ENGINE.md)** - Deep dive on redaction implementation
- **[TESTING_GUIDE.md](TESTING_GUIDE.md)** - Test infrastructure guide
- **[REDACTION_AI_GUIDELINES.md](REDACTION_AI_GUIDELINES.md)** - Guidelines for AI assistants
- **[CLAUDE.md](CLAUDE.md)** - Development guidelines

## License

MIT License - See [LICENSES.md](LICENSES.md) for complete dependency licensing.

All dependencies use permissive licenses (MIT, Apache 2.0, BSD-3). No copyleft obligations.

## Contributing

Contributions are welcome! Key areas:
1. Additional PDF operator support
2. Performance optimization for large PDFs
3. Accessibility improvements
4. Additional file format exports
