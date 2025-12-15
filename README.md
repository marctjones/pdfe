# PdfEditor

A cross-platform desktop PDF editor built with **C# + .NET 8 + Avalonia UI** featuring TRUE content-level redaction.

[![Release](https://img.shields.io/github/v/release/marctjones/pdfe)](https://github.com/marctjones/pdfe/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-625%20passing-brightgreen)](PdfEditor.Tests)

## Features

### Core Features
- **Open and view PDF documents** with smooth rendering
- **Page manipulation** - Add, remove, and rotate pages (supports 90°/180°/270° rotation)
- **Text selection** - Select and copy text from PDFs
- **Search** - Find text with highlighting and navigation
- **Zoom and pan** - Multiple zoom modes (fit width, fit page, actual size)
- **Page thumbnails** - Sidebar with clickable thumbnails
- **Keyboard shortcuts** - Full keyboard navigation support
- **Cross-platform** - Windows, Linux, and macOS

### Advanced Features
- **OCR Support** - Extract text from scanned/image-based PDFs using Tesseract
- **Digital Signature Verification** - Validate PDF signatures and detect tampering
- **Redaction Verification** - Automated post-redaction validation to ensure no data leakage
- **Performance Optimization** - Configurable render cache for faster page navigation
- **CLI Validator** - Command-line tools for PDF analysis and verification

### TRUE Content-Level Redaction
Unlike most PDF redaction tools that just draw black boxes over content, PdfEditor implements **true content removal**:

- **Text is REMOVED** from the PDF structure, not just hidden
- **Verified with external tools** (pdftotext, PdfPig) - redacted text cannot be extracted
- **Clipboard history** shows exactly what text was removed
- **Page rotation aware** - accurate redaction on rotated pages
- **625+ automated tests** verify redaction integrity

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

### OCR (Optical Character Recognition)

PdfEditor includes built-in OCR support using Tesseract for extracting text from scanned or image-based PDFs.

**Requirements:**
- Tesseract trained data files in `tessdata/` directory
- Download from: https://github.com/tesseract-ocr/tessdata

**Configuration** (via environment variables):
- `PDFEDITOR_OCR_LANGS` - Languages (default: `eng`, examples: `eng+deu`, `fra+spa`)
- `PDFEDITOR_OCR_BASE_DPI` - Base resolution (default: 350)
- `PDFEDITOR_OCR_HIGH_DPI` - High resolution retry (default: 450)
- `PDFEDITOR_OCR_LOW_CONFIDENCE` - Confidence threshold for retry (default: 0.6)
- `PDFEDITOR_OCR_PREPROCESS` - Enable preprocessing (default: true)
- `PDFEDITOR_OCR_DENOISE_RADIUS` - Denoise radius (default: 0.8)
- `PDFEDITOR_OCR_BINARIZE` - Enable binarization (default: true)

**Features:**
- Automatic grayscale preprocessing for better accuracy
- Low-confidence page retry at higher DPI
- Multi-language support

### Performance Tuning
- `PDFEDITOR_RENDER_CACHE_MAX` - Maximum cached rendered pages (default: 20)
- Cache is automatically cleared when closing documents

### Digital Signature Verification

PdfEditor can verify digital signatures in PDF documents to detect tampering and validate certificate authenticity.

**Features:**
- Signature presence detection
- Certificate validation
- Tampering detection
- Signing time verification

### Redaction Verification (CLI)

The included `PdfEditor.Validator` CLI tool provides comprehensive PDF validation:

**Commands:**
- `verify <file.pdf>` - Quick leakage check (detects text under black boxes)
- `analyze <file.pdf>` - Detailed PDF structure analysis
- `extract-text <file.pdf>` - Extract all text from document
- `compare <before.pdf> <after.pdf>` - Compare two PDFs
- `find-hidden <file.pdf>` - Find hidden content
- `detect-blocking <file.pdf>` - Detect visual blocking (black boxes)
- `content-stream <file.pdf> <page>` - Analyze page content stream

**Usage:**
```bash
cd PdfEditor.Validator
dotnet run -- verify mydocument.pdf
```

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
- **Tesseract 5.2.0** (Apache 2.0) - OCR engine
- **Portable.BouncyCastle 1.9.0** (MIT) - Cryptography for signature verification

## Project Structure

```
pdfe/
├── PdfEditor/                 # Main application
│   ├── Models/               # Data models
│   ├── Services/             # Business logic
│   │   ├── Redaction/       # Redaction engine components
│   │   ├── Verification/    # Post-redaction verification
│   │   ├── PdfDocumentService.cs
│   │   ├── PdfRenderService.cs
│   │   ├── RedactionService.cs
│   │   ├── PdfTextExtractionService.cs
│   │   ├── PdfSearchService.cs
│   │   ├── PdfOcrService.cs
│   │   ├── SignatureVerificationService.cs
│   │   └── CoordinateConverter.cs
│   ├── ViewModels/           # MVVM view models
│   └── Views/                # UI views
├── PdfEditor.Tests/          # Test suite (625+ tests)
│   ├── Integration/         # Integration tests
│   ├── Unit/               # Unit tests
│   ├── UI/                 # UI tests
│   └── Security/           # Security verification tests
├── PdfEditor.Benchmarks/     # Performance benchmarks
└── PdfEditor.Validator/      # PDF validation CLI tools
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

### Coverage & Benchmarks
- `./scripts/run-coverage.sh` – runs the full suite with coverlet instrumentation and enforces 80% line coverage.
- `./scripts/run-benchmarks.sh` – executes BenchmarkDotNet-based redaction benchmarks (Release mode).

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
