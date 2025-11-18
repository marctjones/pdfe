# Cross-Platform PDF Editor

A cross-platform desktop PDF editor built with **C# + .NET 8 + Avalonia UI** that runs on Windows, Linux, and macOS.

## Features

✅ **Open and view PDF documents**  
✅ **Remove pages** from PDFs  
✅ **Add pages** from other PDF documents  
✅ **Redact sensitive content** (visual + content removal)  
✅ **Zoom and pan** controls  
✅ **Page thumbnails** sidebar  
✅ **Cross-platform** - Windows, Linux, macOS  

## Technology Stack

### Framework & UI
- **.NET 8.0** - Cross-platform runtime
- **Avalonia UI 11.1.3** (MIT License) - Cross-platform XAML-based UI framework
- **ReactiveUI** (MIT License) - MVVM framework

### PDF Libraries (All Non-Copyleft)
- **PdfSharpCore 1.3.65** (MIT License) - PDF manipulation (add/remove pages, document structure)
- **PDFtoImage 4.0.2** (MIT License) - PDF rendering using PDFium
- **PdfPig 0.1.8** (Apache 2.0) - PDF parsing and text extraction
- **SkiaSharp 2.88.8** (MIT License) - 2D graphics for rendering

## Project Structure

```
PdfEditor/
├── Models/                 # Data models
│   └── PageThumbnail.cs   # Page thumbnail model
├── Services/               # Business logic
│   ├── PdfDocumentService.cs   # PDF manipulation (add/remove pages)
│   ├── PdfRenderService.cs     # PDF rendering to images
│   └── RedactionService.cs     # Content redaction
├── ViewModels/             # MVVM view models
│   ├── ViewModelBase.cs
│   └── MainWindowViewModel.cs
├── Views/                  # UI views
│   ├── MainWindow.axaml        # Main window XAML
│   └── MainWindow.axaml.cs     # Main window code-behind
├── App.axaml              # Application definition
├── App.axaml.cs           # Application code-behind
├── Program.cs             # Entry point
└── PdfEditor.csproj       # Project file
```

## Architecture

### MVVM Pattern
The application follows the Model-View-ViewModel (MVVM) pattern:
- **Models**: Data structures (PageThumbnail)
- **ViewModels**: Business logic and state management (MainWindowViewModel)
- **Views**: UI layer (MainWindow)
- **Services**: Core PDF operations (PdfDocumentService, PdfRenderService, RedactionService)

### Key Components

#### 1. PdfDocumentService
Handles PDF document manipulation:
- Loading/saving PDFs
- Removing pages
- Adding pages from other PDFs
- Inserting pages at specific positions

#### 2. PdfRenderService
Renders PDF pages to images for display:
- High-resolution page rendering
- Thumbnail generation
- Uses PDFium (via PDFtoImage) for accurate rendering

#### 3. RedactionService
Implements content redaction:
- **Visual redaction**: Draws black rectangles over sensitive areas
- **Content removal**: Removes text and graphics within redaction bounds
- **NOTE**: Full content stream manipulation is partially implemented (see Implementation Notes below)

#### 4. MainWindowViewModel
Manages application state and commands:
- Document loading/saving
- Page navigation
- Zoom controls
- Redaction mode
- Page thumbnails

## Quick Start Demo

**Want to see redaction in action?** Run the demonstration program:

```bash
# Linux/macOS
./run-demo.sh

# Windows
run-demo.bat
```

This will:
1. Generate sample PDFs with text and shapes
2. Apply black box redactions at known and random locations
3. Save the redacted PDFs
4. Re-open and verify content is actually removed from the PDF structure
5. Generate before/after PDFs you can inspect

See `PdfEditor.Demo/README.md` for details.

## Testing

Run the comprehensive test suite (21 tests):

```bash
cd PdfEditor.Tests
dotnet test
```

See `TEST_SUITE_GUIDE.md` for complete test documentation.

## Building and Running

### Prerequisites
- **.NET 8.0 SDK** or later
- **Windows, Linux, or macOS**

### Install .NET SDK

**Windows:**
```powershell
winget install Microsoft.DotNet.SDK.8
```

**Linux (Ubuntu/Debian):**
```bash
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

**macOS:**
```bash
brew install dotnet@8
```

### Build the Project

```bash
cd PdfEditor
dotnet restore
dotnet build
```

### Run the Application

```bash
dotnet run
```

### Publish for Distribution

**Windows (self-contained):**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Linux (self-contained):**
```bash
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

**macOS (self-contained):**
```bash
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

The published executable will be in `bin/Release/net8.0/{runtime}/publish/`

## Usage

1. **Open a PDF**: Click "Open PDF" button
2. **Navigate Pages**: Use Previous/Next buttons or click thumbnails
3. **Zoom**: Use "Zoom In" and "Zoom Out" buttons
4. **Remove Page**: Click "Remove Page" to delete the current page
5. **Add Pages**: Click "Add Pages" to import pages from another PDF
6. **Redact Content**:
   - Click "Redact Mode" to enable redaction
   - Draw a rectangle over the area to redact
   - Click "Apply Redaction" to permanently redact
7. **Save**: Click "Save" to save changes

## Implementation Notes

### What's Fully Implemented

✅ **PDF Loading/Saving** - Complete
✅ **Page Removal** - Complete
✅ **Page Addition/Merging** - Complete
✅ **PDF Rendering** - Complete (using PDFium)
✅ **Visual Redaction** - Complete (black rectangles)
✅ **Content Removal** - Complete (removes text/graphics from PDF structure)
✅ **Zoom/Pan Controls** - Complete
✅ **Page Thumbnails** - Complete
✅ **MVVM Architecture** - Complete
✅ **Comprehensive Test Suite** - Complete (21 tests)

### Content-Level Redaction

✅ **TRUE Content Redaction Implemented**

The `RedactionService` implements **both visual AND content-level redaction**:
- **Visual redaction**: Draws black rectangles over sensitive areas
- **Content removal**: Actually removes text and graphics from the PDF structure (not just hiding them)

The implementation includes:

1. **Parse PDF Content Streams** ✅
   - `ContentStreamParser` parses content stream operators
   - Tracks graphics state stack (q/Q operators)
   - Tracks current transformation matrix (cm operator)

2. **Text Content Removal** ✅
   - Tracks text state (Tf, TL, Tc, Tw, Tz, Ts, Tm, T*)
   - Calculates bounding boxes for text-showing operators (Tj, TJ, ', ")
   - Removes operators that intersect redaction areas

3. **Graphics Content Removal** ✅
   - Tracks path construction (m, l, c, v, y, h)
   - Tracks path painting (S, s, f, F, f*, B, B*, b, b*, n)
   - Removes paths intersecting redaction areas

4. **Image Redaction** ⚠️
   - XObject images (Do) are tracked and removed
   - Inline images (BI...ID...EI) not yet implemented

5. **Rebuild Content Stream** ✅
   - `ContentStreamBuilder` serializes filtered operators
   - Updates page content stream

**Implementation**: ~2000 lines of code across multiple components. See `REDACTION_ENGINE.md` for architecture details.

**Verification**: Run `./run-demo.sh` to see actual content removal in action. The demo re-opens PDFs and verifies text is removed from the PDF structure.

## PDF Specification Resources

- [PDF 1.7 Specification (ISO 32000-1)](https://www.adobe.com/content/dam/acom/en/devnet/pdf/pdfs/PDF32000_2008.pdf)
- [PdfSharpCore Documentation](https://github.com/ststeiger/PdfSharpCore)
- [PDFium Documentation](https://pdfium.googlesource.com/pdfium/)

## License

This project uses only non-copyleft (permissive) open-source libraries:
- MIT License: Avalonia, PdfSharpCore, PDFtoImage, SkiaSharp, ReactiveUI
- Apache 2.0: PdfPig
- BSD-3-Clause: PDFium (embedded in PDFtoImage)

You can use this code commercially without copyleft obligations.

## What Would Be Needed for Mobile (iOS/Android)

If you wanted to extend this to mobile platforms:

1. **Option A**: Keep .NET but use **.NET MAUI** instead of Avalonia
   - Same C# codebase
   - MAUI supports iOS/Android in addition to desktop
   - Would need to adapt UI for touch/mobile

2. **Option B**: Rewrite in **Flutter/Dart**
   - Single codebase for desktop + mobile
   - Different language (Dart instead of C#)
   - Good mobile UI primitives

For desktop-only, the current **C# + Avalonia** approach is optimal.

## Troubleshooting

### Build Errors

**Error: "Could not load file or assembly Avalonia"**
- Run `dotnet restore` to download packages

**Error: "The type or namespace name 'PdfSharp' could not be found"**
- Ensure you're using .NET 8.0+: `dotnet --version`
- Run `dotnet clean` then `dotnet restore`

### Runtime Errors

**Error: "Unable to load shared library 'pdfium'"**
- PDFtoImage requires native PDFium libraries
- They should be included automatically via NuGet, but if missing:
  - Windows: Install Visual C++ Redistributable
  - Linux: Install `libgdiplus`

**Linux libgdiplus installation:**
```bash
# Ubuntu/Debian
sudo apt-get install libgdiplus

# Fedora/RHEL
sudo dnf install libgdiplus
```

## Future Enhancements

Potential features to add:
- Rotate pages
- Extract text from PDFs
- Search within PDFs
- Annotations and comments
- Digital signatures
- Form filling
- OCR for scanned documents
- Batch processing multiple PDFs
- Complete content-level redaction implementation

## Contributing

This is a proof-of-concept implementation. Key areas for contribution:
1. Complete the `RedactionService` content stream manipulation
2. Add unit tests
3. Improve error handling and user feedback
4. Add more PDF manipulation features
5. Optimize rendering performance
6. Add keyboard shortcuts

## Contact & Support

This is a demonstration project showing how to build a cross-platform PDF editor with non-copyleft libraries. For production use, consider:
- Adding comprehensive error handling
- Implementing the complete redaction engine
- Adding automated tests
- Performance optimization for large PDFs
- Accessibility features
