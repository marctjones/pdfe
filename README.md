# PdfEditor

A cross-platform desktop PDF editor built with **C# + .NET 8 + Avalonia UI** featuring TRUE content-level redaction.

[![Release](https://img.shields.io/github/v/release/marctjones/pdfe)](https://github.com/marctjones/pdfe/releases)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-280%20passing-brightgreen)](PdfEditor.Tests)
[![CLI Tests](https://img.shields.io/badge/CLI%20tests-74%20passing-brightgreen)](PdfEditor.Redaction.Cli.Tests)

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
- **CLI Redaction Tool (`pdfer`)** - Command-line tool for batch redaction, search, and verification
- **GUI Automation** - C# scripting support (Roslyn) for automated testing and workflows
- **OCR Support** - Extract text from scanned/image-based PDFs using Tesseract
- **Digital Signature Verification** - Validate PDF signatures and detect tampering
- **Redaction Verification** - Automated post-redaction validation to ensure no data leakage
- **Performance Optimization** - Configurable render cache for faster page navigation

### TRUE Content-Level Redaction
Unlike most PDF redaction tools that just draw black boxes over content, PdfEditor implements **true content removal**:

- **Text is REMOVED** from the PDF structure, not just hidden
- **Glyph-level removal** - Individual text glyphs are removed from content streams
- **Verified with external tools** (pdftotext, PdfPig, pdfer) - redacted text cannot be extracted
- **Clipboard history** shows exactly what text was removed
- **Page rotation aware** - accurate redaction on rotated pages
- **280+ automated tests** verify redaction integrity
- **Real-world validated** - Successfully redacts government forms (birth certificates, etc.)

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

**Zero Setup Required:**
- **Auto-download**: Language data files are automatically downloaded on first use
- **Bundled**: English language data included in releases (no internet required)
- **Just works**: Click OCR button and it handles everything automatically

**Usage:**
1. Open a PDF with scanned/image content
2. Click the **📝 OCR** button in the toolbar (or Tools → Run OCR)
3. Wait for processing (status shown in status bar)
4. Extracted text appears in Clipboard History panel

**Configuration:**
Customize OCR settings through **Tools** → **Preferences** (Ctrl+,):
- **Languages**: English (eng) by default, supports 100+ languages (eng+deu for English+German, etc.)
- **DPI Settings**: Base DPI (350) and high DPI (450) for quality/speed balance
- **Preprocessing**: Grayscale conversion, denoising, binarization options

**Advanced Features:**
- **Auto-download**: Missing language files download automatically from GitHub
- **Smart retry**: Low-confidence pages re-processed at higher DPI
- **Multi-language**: Process documents with mixed languages (e.g., "eng+fra+deu")
- **Progress feedback**: Status messages and completion dialogs
- **Manual download**: If auto-download fails, clear instructions provided

**Supported Languages:**
Download additional languages automatically by setting in Preferences:
- `eng` - English (bundled)
- `deu` - German
- `fra` - French
- `spa` - Spanish
- `ita` - Italian
- `por` - Portuguese
- `rus` - Russian
- `chi_sim` - Simplified Chinese
- `jpn` - Japanese
- ...and 90+ more languages

For multiple languages, use `+` separator (e.g., `eng+deu+fra`)

### Digital Signature Verification

PdfEditor can verify digital signatures in PDF documents to detect tampering and validate certificate authenticity.

**Features:**
- Signature presence detection
- Certificate validation
- Tampering detection
- Signing time verification

### CLI Redaction Tool (`pdfer`)

**NEW in v1.3.0**: Professional command-line tool for PDF redaction, search, and verification.

**Commands:**
- `pdfer redact <input.pdf> <output.pdf> <text>` - Redact text from PDF
- `pdfer search <file.pdf> <text>` - Search for text and show locations
- `pdfer verify <file.pdf> <text>` - Verify text has been removed
- `pdfer info <file.pdf>` - Display PDF information (pages, text count, etc.)

**Features:**
- **Batch redaction** - Redact multiple terms at once
- **Regex support** - Use regular expressions for pattern matching
- **Case-insensitive** - Optional case-insensitive matching
- **JSON output** - Machine-readable output for automation
- **Dry-run mode** - Preview redactions without modifying files
- **Quiet mode** - Suppress output for scripting
- **Terms from file/stdin** - Read redaction terms from file or pipe

**Examples:**
```bash
# Redact a specific term
pdfer redact input.pdf output.pdf "SECRET"

# Redact multiple terms
pdfer redact input.pdf output.pdf "SECRET" "CONFIDENTIAL" "PRIVATE"

# Use regex to redact SSNs
pdfer redact input.pdf output.pdf --regex "\d{3}-\d{2}-\d{4}"

# Search for text
pdfer search document.pdf "John Doe"

# Verify redaction worked
pdfer verify redacted.pdf "SECRET"  # Exit code 0 if not found

# Batch processing with shell script
for pdf in *.pdf; do
    pdfer redact "$pdf" "redacted_$pdf" "CONFIDENTIAL"
done
```

**Installation:**
```bash
# From source
cd PdfEditor.Redaction.Cli
dotnet build -c Release

# Run
./bin/Release/net8.0/pdfer --help
```

## Technology Stack

### Framework & UI
- **.NET 8.0** - Cross-platform runtime
- **Avalonia UI 11.1.3** (MIT) - Cross-platform XAML UI framework
- **ReactiveUI** (MIT) - MVVM framework

### PDF Libraries (All Permissive Licenses)
- **PDFsharp 6.2.2** (MIT) - PDF manipulation
- **PDFtoImage 4.0.2** (MIT) - PDF rendering via PDFium
- **PdfPig 0.1.11** (Apache 2.0) - PDF parsing and text extraction
- **SkiaSharp 2.88.8** (MIT) - 2D graphics
- **Tesseract 5.2.0** (Apache 2.0) - OCR engine
- **Portable.BouncyCastle 1.9.0** (MIT) - Cryptography for signature verification
- **Microsoft.CodeAnalysis.CSharp.Scripting 5.0.0** (MIT) - Roslyn scripting for automation

## Project Structure

```
pdfe/
├── PdfEditor/                      # Main GUI application (Avalonia UI)
│   ├── Models/                    # Data models
│   ├── Services/                  # Business logic
│   │   ├── ScriptingService.cs   # Roslyn C# scripting
│   │   ├── PdfDocumentService.cs
│   │   ├── PdfRenderService.cs
│   │   └── ...
│   ├── ViewModels/                # MVVM view models
│   └── Views/                     # UI views (XAML)
│
├── PdfEditor.Redaction/           # Redaction engine library
│   ├── ContentStream/            # Content stream parsing
│   ├── Operators/                # PDF operator handlers (Tj, TJ, Tm, etc.)
│   ├── TextRedactor.cs           # Main redaction API
│   └── ...
│
├── PdfEditor.Redaction.Cli/       # CLI tool (pdfer)
│   ├── Commands/                 # Redact, Search, Verify, Info
│   └── Program.cs
│
├── PdfEditor.Redaction.Cli.Tests/ # CLI tests (74 tests)
│   ├── Unit/                     # Command tests
│   └── Integration/              # Corpus tests
│
├── PdfEditor.Redaction.Tests/     # Redaction library tests (136 tests)
│   ├── Integration/              # Real-world PDF tests
│   └── Unit/                     # Operator tests
│
├── PdfEditor.Tests/               # GUI tests (144 tests)
│   ├── Integration/              # End-to-end tests
│   ├── Unit/                     # ViewModel tests
│   ├── UI/                       # GUI automation tests
│   └── Security/                 # Verification tests
│
├── automation-scripts/            # GUI automation scripts (.csx)
│   ├── test-birth-certificate.csx
│   ├── test-redact-text.csx
│   └── README.md
│
├── scripts/                       # Build/test shell scripts
│   ├── test.sh
│   ├── build.sh
│   └── demo.sh
│
└── wiki/                          # GitHub wiki (cloned)
    ├── Project-Architecture.md
    ├── Redaction-Engine.md
    └── ...
```

## Testing

Run the comprehensive test suite:

```bash
# All tests (280+ tests across all projects)
dotnet test

# GUI tests
cd PdfEditor.Tests
dotnet test

# CLI tests
cd PdfEditor.Redaction.Cli.Tests
dotnet test

# Redaction library tests
cd PdfEditor.Redaction.Tests
dotnet test

# Run specific test category
dotnet test --filter "FullyQualifiedName~BirthCertificate"
```

**Test Statistics:**
- **280 total tests** across 3 test projects
- **74 CLI tests** (100% passing) - pdfer command validation
- **136 library tests** (97% passing) - Redaction engine
- **70+ GUI tests** (including automation scripts)

**Test Categories:**
- **Unit tests** - ViewModel, operators, coordinate conversion
- **Integration tests** - Real-world PDFs (birth certificates, forms)
- **Corpus tests** - veraPDF test suite (2,694 PDFs)
- **GUI automation tests** - Roslyn scripting-based
- **Security tests** - Verify TRUE content removal

**Scripts:**
- `./scripts/test.sh` - Run all tests with logging
- `./scripts/test-birth-certificate-redaction.sh` - Birth certificate validation

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

- **[GitHub Wiki](https://github.com/marctjones/pdfe/wiki)** - Project architecture, redaction engine, testing guide
- **[AGENT.md](AGENT.md)** - AI assistant guidelines and development documentation
- **[REDACTION_AI_GUIDELINES.md](REDACTION_AI_GUIDELINES.md)** - Critical guidelines for AI-assisted development
- **[LICENSES.md](LICENSES.md)** - Complete dependency licensing

## License

MIT License - See [LICENSES.md](LICENSES.md) for complete dependency licensing.

All dependencies use permissive licenses (MIT, Apache 2.0, BSD-3). No copyleft obligations.

## Contributing

Contributions are welcome! Key areas:
1. Additional PDF operator support
2. Performance optimization for large PDFs
3. Accessibility improvements
4. Additional file format exports
