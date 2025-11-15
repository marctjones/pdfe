# Quick Start Guide

Get the PDF Editor running in 5 minutes.

## Prerequisites

Install **.NET 8.0 SDK**:

**Windows:**
```powershell
winget install Microsoft.DotNet.SDK.8
```

**macOS:**
```bash
brew install dotnet@8
```

**Linux (Ubuntu/Debian):**
```bash
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
export PATH="$PATH:$HOME/.dotnet"
```

**Verify installation:**
```bash
dotnet --version
# Should show 8.0.xxx
```

## Build and Run

### Option 1: Quick Run (Recommended for Testing)

```bash
# Clone or navigate to the project
cd pdfe/PdfEditor

# Restore packages
dotnet restore

# Run the application
dotnet run
```

The application will start in debug mode.

### Option 2: Build Release Binary

**Linux:**
```bash
./build.sh
```

**Windows:**
```cmd
build.bat
```

**Or manually:**
```bash
cd PdfEditor
dotnet build -c Release
dotnet run -c Release
```

### Option 3: Create Standalone Executable

**Windows (64-bit):**
```powershell
cd PdfEditor
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
Executable: `bin\Release\net8.0\win-x64\publish\PdfEditor.exe`

**Linux (64-bit):**
```bash
cd PdfEditor
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```
Executable: `bin/Release/net8.0/linux-x64/publish/PdfEditor`

**macOS (64-bit Intel):**
```bash
cd PdfEditor
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```
Executable: `bin/Release/net8.0/osx-x64/publish/PdfEditor`

**macOS (ARM64/M1/M2):**
```bash
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

## First Use

1. **Launch the application**
2. **Click "Open PDF"** - Select a PDF file to edit
3. **Navigate pages** - Use Previous/Next buttons or click thumbnails
4. **Try features:**
   - **Zoom In/Out** - Zoom controls
   - **Remove Page** - Deletes current page
   - **Add Pages** - Import pages from another PDF
   - **Redact Mode** - Draw rectangle, click "Apply Redaction"
5. **Click "Save"** to save changes

## Troubleshooting

### "dotnet: command not found"

Add .NET to your PATH:

**Linux/macOS:**
```bash
export PATH="$PATH:$HOME/.dotnet"
# Add to ~/.bashrc or ~/.zshrc to make permanent
```

**Windows:**
Add `C:\Program Files\dotnet` to your PATH environment variable.

### "Unable to load shared library 'pdfium'"

**Linux:**
```bash
sudo apt-get update
sudo apt-get install libgdiplus
```

**macOS:**
```bash
brew install mono-libgdiplus
```

### "Could not load file or assembly 'Avalonia'"

```bash
cd PdfEditor
dotnet clean
dotnet restore
dotnet build
```

### Application window doesn't appear

**Linux (Wayland):**
Try forcing X11:
```bash
export GDK_BACKEND=x11
dotnet run
```

**Linux (missing display server):**
Ensure you're running in a graphical environment (not SSH without X forwarding).

### PDF rendering shows blank pages

- Ensure the PDF is valid (try opening in another viewer)
- Check console for error messages
- PDFium may not support some exotic PDF features

## What's Implemented

✅ **Working Features:**
- Open and view PDFs
- Page navigation (next/previous)
- Zoom in/out
- Remove pages
- Add pages from other PDFs
- Page thumbnails
- Visual redaction (black rectangles)
- Save changes

⚠️ **Partial Implementation:**
- **Content-level redaction** - Currently draws black boxes but doesn't remove underlying data
  - See `IMPLEMENTATION_GUIDE.md` for how to complete this

## Next Steps

1. **Read the [README.md](README.md)** for full documentation
2. **Review [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md)** to implement true content redaction
3. **Check [LANGUAGE_COMPARISON.md](LANGUAGE_COMPARISON.md)** to understand why we chose C#/.NET
4. **See [LICENSES.md](LICENSES.md)** for license compliance information

## Development

### Project Structure

```
PdfEditor/
├── Services/          # PDF operations (business logic)
├── ViewModels/        # MVVM view models
├── Views/             # UI (XAML)
├── Models/            # Data models
└── Program.cs         # Entry point
```

### Adding Features

**Example: Add page rotation**

1. **Service** (`Services/PdfDocumentService.cs`):
```csharp
public void RotatePage(int pageIndex, int degrees)
{
    if (_currentDocument == null) return;
    var page = _currentDocument.Pages[pageIndex];
    page.Rotate = degrees;
}
```

2. **ViewModel** (`ViewModels/MainWindowViewModel.cs`):
```csharp
public ReactiveCommand<Unit, Unit> RotatePageCommand { get; }

public MainWindowViewModel()
{
    RotatePageCommand = ReactiveCommand.Create(RotatePage);
}

private void RotatePage()
{
    _documentService.RotatePage(CurrentPageIndex, 90);
    Task.Run(async () => await RenderCurrentPageAsync());
}
```

3. **View** (`Views/MainWindow.axaml`):
```xml
<Button Content="Rotate" Command="{Binding RotatePageCommand}"/>
```

### Debugging

**Visual Studio / Rider:**
- Open `PdfEditor.sln`
- Press F5 to debug

**VS Code:**
- Install C# extension
- Open folder in VS Code
- Press F5

**Console logging:**
```csharp
Console.WriteLine($"Page {pageIndex} loaded");
```

Output appears in terminal/console.

## Performance Tips

### Large PDFs (100+ pages)

1. **Lazy load thumbnails** - Already implemented
2. **Implement page caching** - Cache rendered pages
3. **Lower thumbnail DPI** - Reduce from 150 to 72 DPI

### Slow rendering

- PDFium is already fast
- Reduce render DPI for preview (use 150 instead of 300)
- Enable hardware acceleration (already enabled in Avalonia)

## Building for Distribution

### Windows Installer

Use **Inno Setup** or **WiX Toolset**:

```powershell
# Build single-file exe first
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Then create installer with Inno Setup or WiX
```

### Linux Package

**AppImage:**
```bash
# Use appimagetool
# https://github.com/AppImage/AppImageKit
```

**Debian/Ubuntu (.deb):**
```bash
# Use dpkg-deb or checkinstall
```

**Flatpak:**
```bash
# Create flatpak manifest
# https://docs.flatpak.org/en/latest/
```

### macOS Application Bundle

```bash
# Create .app bundle
mkdir -p PdfEditor.app/Contents/MacOS
mkdir -p PdfEditor.app/Contents/Resources

# Copy executable
cp bin/Release/net8.0/osx-x64/publish/PdfEditor PdfEditor.app/Contents/MacOS/

# Create Info.plist
# (See Avalonia docs for template)

# Sign (if distributing outside App Store)
codesign --force --deep --sign - PdfEditor.app
```

## Getting Help

1. **Check error messages** in the console
2. **Read the README.md** for detailed documentation
3. **Review the source code** - It's well-commented
4. **Avalonia docs**: https://docs.avaloniaui.net/
5. **PdfSharpCore docs**: https://github.com/ststeiger/PdfSharpCore

## Common Use Cases

### Redact Sensitive Information

1. Open the PDF
2. Click "Redact Mode"
3. Draw a rectangle over the sensitive text/area
4. Click "Apply Redaction"
5. Save the PDF

**Note:** This draws a black rectangle. For true content removal, implement the redaction engine (see IMPLEMENTATION_GUIDE.md).

### Merge PDFs

1. Open the first PDF
2. Click "Add Pages"
3. Select the second PDF
4. All pages from the second PDF are appended
5. Save

### Remove Pages

1. Open the PDF
2. Navigate to the page you want to remove
3. Click "Remove Page"
4. Repeat for other pages
5. Save

### Extract Pages

1. Open the PDF with pages you want to extract
2. Remove all unwanted pages
3. Save with a new filename

## What's Missing

To make this production-ready:

1. **Error handling UI** - Show user-friendly error messages
2. **Undo/Redo** - Implement command pattern
3. **True content redaction** - Complete the redaction engine
4. **Tests** - Unit and integration tests
5. **Localization** - Multi-language support
6. **Accessibility** - Screen reader support
7. **More PDF operations** - Rotate, merge, split, etc.
8. **Settings** - User preferences (default zoom, etc.)

See README.md for the full roadmap.

## License

All libraries are non-copyleft (MIT, Apache 2.0, BSD). See [LICENSES.md](LICENSES.md).

You can use this commercially without restrictions.

---

**Ready to code?** Start with the [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md) to implement true content redaction!
