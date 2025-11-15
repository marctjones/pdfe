# Architecture Diagram

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         PDF Editor Application                          │
│                    (Cross-Platform Desktop - C# + .NET 8)               │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
        ┌───────────────────────────┼───────────────────────────┐
        │                           │                           │
   ┌────▼────┐               ┌──────▼──────┐           ┌───────▼────────┐
   │ Windows │               │    Linux    │           │     macOS      │
   └─────────┘               └─────────────┘           └────────────────┘
```

---

## MVVM Architecture Pattern

```
┌────────────────────────────────────────────────────────────────────────┐
│                              VIEW LAYER                                │
│  ┌──────────────────────────────────────────────────────────────────┐ │
│  │                     MainWindow.axaml (XAML)                      │ │
│  │  ┌────────┐  ┌──────────┐  ┌──────────┐  ┌───────────────────┐ │ │
│  │  │ Toolbar│  │  Canvas  │  │Thumbnail │  │   Status Bar      │ │ │
│  │  │Buttons │  │PDF Viewer│  │ Sidebar  │  │Page 1/10 | 100%  │ │ │
│  │  └────────┘  └──────────┘  └──────────┘  └───────────────────┘ │ │
│  └──────────────────────────────────────────────────────────────────┘ │
└──────────────────────────┬─────────────────────────────────────────────┘
                           │ Data Binding (Two-Way)
                           │ Command Binding
┌──────────────────────────▼─────────────────────────────────────────────┐
│                         VIEWMODEL LAYER                                │
│  ┌──────────────────────────────────────────────────────────────────┐ │
│  │              MainWindowViewModel.cs                              │ │
│  │                                                                  │ │
│  │  Properties:                    Commands:                        │ │
│  │  • CurrentPageImage            • OpenFileCommand                │ │
│  │  • CurrentPageIndex            • SaveFileCommand                │ │
│  │  • ZoomLevel                   • RemovePageCommand              │ │
│  │  • IsRedactionMode             • AddPagesCommand                │ │
│  │  • PageThumbnails              • RedactCommand                  │ │
│  │                                • ZoomIn/OutCommand              │ │
│  └────────────┬───────────────────┬───────────────┬────────────────┘ │
└───────────────┼───────────────────┼───────────────┼──────────────────┘
                │                   │               │
                │ Calls Services    │               │
                │                   │               │
┌───────────────▼──────┐  ┌─────────▼───────┐  ┌────▼──────────────────┐
│                      │  │                 │  │                       │
│  PdfDocumentService  │  │PdfRenderService │  │   RedactionService    │
│                      │  │                 │  │                       │
│  • LoadDocument()    │  │ • RenderPage()  │  │ • RedactArea()        │
│  • RemovePage()      │  │ • RenderThumb() │  │ • RedactAreas()       │
│  • AddPages()        │  │                 │  │                       │
│  • SaveDocument()    │  │                 │  │   ┌───────────────┐   │
│                      │  │                 │  │   │Redaction      │   │
└──────────┬───────────┘  └────────┬────────┘  │   │Engine         │   │
           │                       │           │   │(~1,400 lines) │   │
           │                       │           │   └───────────────┘   │
           │                       │           └───────────────────────┘
           │                       │                       │
┌──────────▼───────────────────────▼───────────────────────▼────────────┐
│                           SERVICE LAYER                               │
│                         (Business Logic)                              │
└───────────────────────────────────────────────────────────────────────┘
                                   │
┌──────────────────────────────────▼────────────────────────────────────┐
│                         PDF LIBRARIES                                 │
│  ┌────────────────┐  ┌──────────────┐  ┌───────────────────────────┐ │
│  │  PdfSharpCore  │  │ PDFtoImage   │  │      PdfPig               │ │
│  │     (MIT)      │  │   (MIT)      │  │   (Apache 2.0)            │ │
│  │                │  │              │  │                           │ │
│  │ PDF Structure  │  │Uses PDFium   │  │  PDF Parsing              │ │
│  │ Manipulation   │  │(BSD-3)       │  │  Text Extraction          │ │
│  └────────────────┘  └──────────────┘  └───────────────────────────┘ │
└───────────────────────────────────────────────────────────────────────┘
```

---

## Redaction Engine Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│                       RedactionService                             │
│                    (Orchestration Layer)                           │
│                                                                    │
│  public void RedactArea(PdfPage page, Rect area)                  │
│  {                                                                 │
│      1. Parse content stream                                      │
│      2. Filter intersecting operations                            │
│      3. Rebuild content stream                                    │
│      4. Replace page content                                      │
│      5. Draw black rectangle                                      │
│  }                                                                 │
└──────┬─────────────────┬─────────────────┬───────────────┬─────────┘
       │                 │                 │               │
       │                 │                 │               │
   ┌───▼────┐      ┌─────▼─────┐    ┌─────▼──────┐  ┌────▼─────┐
   │ Parser │      │  Models   │    │  Builder   │  │  State   │
   └────────┘      └───────────┘    └────────────┘  │ Tracking │
                                                     └──────────┘

┌────────────────────────────────────────────────────────────────────┐
│              ContentStreamParser.cs (500 lines)                    │
│                                                                    │
│  Responsibilities:                                                 │
│  • Parse PDF content stream (CObject tree)                        │
│  • Identify operators (Tj, TJ, m, l, re, S, f, Do, etc.)         │
│  • Track graphics state (q, Q, cm)                               │
│  • Track text state (BT, ET, Tf, Td, Tm)                         │
│  • Calculate bounding boxes for each operation                    │
│  • Create PdfOperation objects                                    │
│                                                                    │
│  Input:  PdfPage                                                  │
│  Output: List<PdfOperation> with bounding boxes                   │
└────────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│                   PdfOperation.cs (200 lines)                      │
│                                                                    │
│  ┌──────────────────┐  ┌──────────────────┐  ┌─────────────────┐ │
│  │ TextOperation    │  │ PathOperation    │  │ ImageOperation  │ │
│  │                  │  │                  │  │                 │ │
│  │ • Text           │  │ • Points[]       │  │ • ResourceName  │ │
│  │ • Position       │  │ • Type           │  │ • Position      │ │
│  │ • FontSize       │  │ • IsStroke       │  │ • Width/Height  │ │
│  │ • BoundingBox    │  │ • IsFill         │  │ • BoundingBox   │ │
│  │                  │  │ • BoundingBox    │  │                 │ │
│  └──────────────────┘  └──────────────────┘  └─────────────────┘ │
│                                                                    │
│  All inherit from: PdfOperation (base class)                      │
│  All implement: IntersectsWith(Rect area)                         │
└────────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│                 Filtering & Rebuilding                             │
│                                                                    │
│  operations.Where(op => !op.IntersectsWith(redactionArea))        │
│                                                                    │
│  ┌──────────────┐         ┌─────────────┐                        │
│  │  Operation   │  ────▶  │   KEEP      │                        │
│  │  Outside     │         │             │                        │
│  │  Redaction   │         │             │                        │
│  └──────────────┘         └─────────────┘                        │
│                                                                    │
│  ┌──────────────┐         ┌─────────────┐                        │
│  │  Operation   │  ────▶  │   REMOVE    │                        │
│  │  Inside      │         │   (Skip)    │                        │
│  │  Redaction   │         │             │                        │
│  └──────────────┘         └─────────────┘                        │
└────────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│           ContentStreamBuilder.cs (150 lines)                      │
│                                                                    │
│  Responsibilities:                                                 │
│  • Serialize PdfOperation objects back to PDF syntax              │
│  • Handle all operand types (int, real, string, name, array)      │
│  • Proper string escaping                                         │
│  • Maintain PDF operator format                                   │
│                                                                    │
│  Input:  List<PdfOperation> (filtered)                            │
│  Output: byte[] (new content stream)                              │
└────────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│                   Replace Page Content                             │
│                                                                    │
│  page.Contents.Elements.Clear()                                   │
│  page.Contents.CreateSingleContent(newBytes)                      │
│                                                                    │
│  ✅ Content permanently removed from PDF!                         │
└────────────────────────────────────────────────────────────────────┘
```

---

## State Tracking During Parsing

```
┌────────────────────────────────────────────────────────────────────┐
│                    PDF Content Stream                              │
│                                                                    │
│  BT                    ← Begin text                               │
│  /F1 12 Tf             ← Set font (F1, size 12)                   │
│  100 700 Td            ← Move text to (100, 700)                  │
│  (Hello World) Tj      ← Show text                                │
│  ET                    ← End text                                 │
│                                                                    │
│  q                     ← Save graphics state                      │
│  1 0 0 1 50 50 cm      ← Transform matrix (translate 50,50)       │
│  100 200 300 50 re     ← Rectangle at (100,200) size 300x50       │
│  f                     ← Fill rectangle                           │
│  Q                     ← Restore graphics state                   │
└────────────────────────────────────────────────────────────────────┘
                                 │
                                 ▼
┌────────────────────────────────────────────────────────────────────┐
│                    Parser with State Tracking                      │
│                                                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │ GraphicsState Stack                                       │   │
│  │ ┌─────────────────────────────────────────────────────┐  │   │
│  │ │ Current: TransformMatrix = Identity                 │  │   │
│  │ │          LineWidth = 1.0                            │  │   │
│  │ │          StrokeColor = Black                        │  │   │
│  │ └─────────────────────────────────────────────────────┘  │   │
│  │ Stack: [saved state, saved state, ...]                   │   │
│  └───────────────────────────────────────────────────────────┘   │
│                                                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │ TextState                                                 │   │
│  │ ┌─────────────────────────────────────────────────────┐  │   │
│  │ │ Font = F1                                           │  │   │
│  │ │ FontSize = 12                                       │  │   │
│  │ │ TextMatrix = [1 0 0 1 100 700]                      │  │   │
│  │ │ CharSpacing = 0                                     │  │   │
│  │ │ WordSpacing = 0                                     │  │   │
│  │ │ HorizontalScaling = 100%                            │  │   │
│  │ └─────────────────────────────────────────────────────┘  │   │
│  └───────────────────────────────────────────────────────────┘   │
│                                                                    │
│  As each operator is encountered:                                 │
│  • Update appropriate state                                       │
│  • Use current state to calculate positions/bounds                │
│  • Create PdfOperation with calculated BoundingBox                │
└────────────────────────────────────────────────────────────────────┘
```

---

## Data Flow: Complete Redaction Process

```
┌──────────────────────────────────────────────────────────────────┐
│ 1. USER ACTION                                                   │
│    User draws rectangle over "CONFIDENTIAL" text                 │
│    Rect(100, 200, 300, 50)                                       │
└────────────────────────┬─────────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 2. PARSE CONTENT STREAM                                          │
│    ContentStreamParser reads page operators                      │
│                                                                  │
│    Found 45 operations:                                          │
│    • TextOperation("CONFIDENTIAL") at (120, 210)  ← INTERSECTS! │
│    • TextOperation("Public Info") at (50, 400)    ← Keep        │
│    • PathOperation(rectangle) at (100, 195)       ← INTERSECTS! │
│    • ... 42 other operations                      ← Keep/Remove │
└────────────────────────┬─────────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 3. FILTER OPERATIONS                                             │
│    Check each operation's BoundingBox against Rect(100,200,300,50) │
│                                                                  │
│    Kept:    42 operations (outside redaction area)              │
│    Removed:  3 operations (inside redaction area)               │
│              ↓                                                   │
│         ┌────────────────────────────┐                          │
│         │ CONFIDENTIAL text operator │  ← REMOVED              │
│         │ Rectangle graphics operator│  ← REMOVED              │
│         │ Background fill operator   │  ← REMOVED              │
│         └────────────────────────────┘                          │
└────────────────────────┬─────────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 4. REBUILD CONTENT STREAM                                        │
│    ContentStreamBuilder serializes 42 kept operations            │
│                                                                  │
│    BT                                                            │
│    /F1 12 Tf                                                     │
│    50 400 Td                                                     │
│    (Public Info) Tj        ← KEPT (outside redaction)           │
│    ET                                                            │
│    ...                                                           │
│    [CONFIDENTIAL operators NOT included]                         │
│                                                                  │
│    New stream: 1,234 bytes                                       │
└────────────────────────┬─────────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 5. REPLACE PAGE CONTENT                                          │
│    page.Contents.Elements.Clear()                                │
│    page.Contents.CreateSingleContent(newBytes)                   │
│                                                                  │
│    PDF structure updated!                                        │
└────────────────────────┬─────────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 6. DRAW BLACK RECTANGLE                                          │
│    XGraphics.DrawRectangle(Black, 100, 200, 300, 50)            │
│                                                                  │
│    Visual confirmation: ████████████████████                     │
└────────────────────────┬─────────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────────┐
│ 7. RESULT                                                        │
│                                                                  │
│    ✅ "CONFIDENTIAL" text: PERMANENTLY REMOVED from PDF          │
│    ✅ Rectangle graphics: PERMANENTLY REMOVED from PDF           │
│    ✅ Visual coverage: Black rectangle visible                   │
│    ✅ Remaining content: Intact and untouched                    │
│                                                                  │
│    If you extract text from this PDF, "CONFIDENTIAL" will       │
│    NOT appear - it's gone from the file structure!              │
└──────────────────────────────────────────────────────────────────┘
```

---

## Coordinate System Transformation

```
PDF Coordinate System (Bottom-Left Origin):

    ┌─────────────────────────────┐  ← (0, 792) Top
    │                             │
    │        Page Content         │
  Y │                             │
  │ │      (100, 700)             │
  ▲ │         ●                   │
  │ │                             │
  0 └─────────────────────────────┘  ← (0, 0) Bottom
    0 ──────────────▶ X ────────→ (612, 0)


Avalonia UI Coordinate System (Top-Left Origin):

  (0, 0) ┌─────────────────────────────┐  ← Top
         │         ●                   │  ← (100, 92)
         │      (100, 92)              │     [Converted from (100, 700)]
         │                             │
       Y │        Page Content         │
       ▼ │                             │
         │                             │
         └─────────────────────────────┘  ← (0, 792) Bottom
         0 ──────────────▶ X ────────→ (612, 792)


Conversion Formula:
  AvaloniaY = PageHeight - PdfY - RectHeight
  Example: 792 - 700 - 0 = 92
```

---

## Technology Stack Layers

```
┌────────────────────────────────────────────────────────────────┐
│                      APPLICATION LAYER                         │
│                     PdfEditor (C# + .NET 8)                    │
│                         ~2,900 lines                           │
└────────────────────────────────────────────────────────────────┘
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                         UI FRAMEWORK                           │
│                      Avalonia 11.1.3 (MIT)                     │
│                   Cross-Platform XAML UI                       │
└────────────────────────────────────────────────────────────────┘
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                         MVVM FRAMEWORK                         │
│                    ReactiveUI 20.1.1 (MIT)                     │
│              Reactive Programming + Data Binding               │
└────────────────────────────────────────────────────────────────┘
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                        PDF LIBRARIES                           │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐ │
│  │PdfSharpCore  │  │ PDFtoImage   │  │   PdfPig            │ │
│  │   (MIT)      │  │   (MIT)      │  │ (Apache 2.0)        │ │
│  └──────────────┘  └──────────────┘  └──────────────────────┘ │
└────────────────────────────────────────────────────────────────┘
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                     RENDERING LIBRARIES                        │
│  ┌──────────────┐                      ┌──────────────────┐   │
│  │   PDFium     │                      │   SkiaSharp      │   │
│  │(BSD-3-Clause)│                      │     (MIT)        │   │
│  │Google's PDF  │                      │  2D Graphics     │   │
│  └──────────────┘                      └──────────────────┘   │
└────────────────────────────────────────────────────────────────┘
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                        .NET RUNTIME                            │
│                     .NET 8.0 (MIT License)                     │
│            Cross-Platform CLR + Base Libraries                 │
└────────────────────────────────────────────────────────────────┘
                              ▼
┌────────────────────────────────────────────────────────────────┐
│                     OPERATING SYSTEM                           │
│         Windows 10/11  │  Linux  │  macOS 10.15+              │
└────────────────────────────────────────────────────────────────┘
```

---

## File Dependencies

```
PdfEditor.csproj
    │
    ├── Avalonia                   (11.1.3, MIT)
    ├── Avalonia.Desktop           (11.1.3, MIT)
    ├── Avalonia.Themes.Fluent     (11.1.3, MIT)
    ├── Avalonia.Fonts.Inter       (11.1.3, MIT)
    ├── Avalonia.ReactiveUI        (11.1.3, MIT)
    │
    ├── PdfSharpCore               (1.3.65, MIT)
    ├── UglyToad.PdfPig            (0.1.8, Apache 2.0)
    ├── PDFtoImage                 (4.0.2, MIT)
    │   └── PDFium                 (BSD-3-Clause) [embedded]
    ├── SkiaSharp                  (2.88.8, MIT)
    │
    ├── ReactiveUI                 (20.1.1, MIT)
    └── ReactiveUI.Fody            (19.5.41, MIT)

All licenses: MIT, Apache 2.0, BSD-3-Clause (Non-Copyleft)
```

---

**This architecture provides:**
- ✅ Clean separation of concerns (MVVM)
- ✅ Testable design
- ✅ Platform independence
- ✅ Extensibility
- ✅ Production-ready quality

Built with C# + .NET 8 + Avalonia UI
~2,900 lines of production code
