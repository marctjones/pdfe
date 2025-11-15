# Third-Party Licenses

This document lists all third-party libraries used in the PDF Editor and their licenses.

## License Summary

**All libraries used are non-copyleft (permissive) licenses:**
- ✅ MIT License (most permissive)
- ✅ Apache 2.0 License (permissive with patent grant)
- ✅ BSD-3-Clause License (permissive)

**NO copyleft licenses used:**
- ❌ No GPL (GNU General Public License)
- ❌ No LGPL (GNU Lesser General Public License)
- ❌ No AGPL (Affero GPL)

This means you can:
- Use commercially
- Modify the code
- Distribute modified versions
- Use in proprietary software
- No requirement to open-source your code

---

## Libraries and Licenses

### 1. Avalonia UI Framework

**Version:** 11.1.3  
**License:** MIT  
**Copyright:** Copyright (c) The Avalonia Project  
**Website:** https://avaloniaui.net/  
**Source Code:** https://github.com/AvaloniaUI/Avalonia

**License Text:** [MIT License](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md)

**What it does:** Cross-platform UI framework for .NET (similar to WPF)

---

### 2. PdfSharpCore

**Version:** 1.3.65  
**License:** MIT  
**Copyright:** Copyright (c) 2005-2007 empira Software GmbH, Cologne (Germany)  
**Website:** http://www.pdfsharp.net/  
**Source Code:** https://github.com/ststeiger/PdfSharpCore

**License Text:** [MIT License](https://github.com/ststeiger/PdfSharpCore/blob/master/LICENSE.md)

**What it does:** Create, read, and manipulate PDF documents (page operations)

---

### 3. PDFtoImage

**Version:** 4.0.2  
**License:** MIT  
**Copyright:** Copyright (c) David Sungaila  
**Source Code:** https://github.com/sungaila/PDFtoImage

**License Text:** [MIT License](https://github.com/sungaila/PDFtoImage/blob/master/LICENSE)

**What it does:** Convert PDF pages to images using PDFium
**Dependencies:** PDFium (see below)

---

### 4. PDFium (embedded in PDFtoImage)

**License:** BSD-3-Clause  
**Copyright:** Copyright 2014 The PDFium Authors  
**Website:** https://pdfium.googlesource.com/pdfium/  
**Used by:** Google Chrome, Microsoft Edge

**License Text:** [BSD-3-Clause License](https://pdfium.googlesource.com/pdfium/+/refs/heads/main/LICENSE)

**What it does:** PDF rendering engine (Google's open-source PDF library)

**BSD-3-Clause is permissive:**
- Can use commercially
- Can modify
- Must include copyright notice
- Must include license text in distributions

---

### 5. PdfPig (UglyToad.PdfPig)

**Version:** 0.1.8  
**License:** Apache 2.0  
**Copyright:** Copyright © 2017-2024 UglyToad Software  
**Source Code:** https://github.com/UglyToad/PdfPig

**License Text:** [Apache 2.0 License](https://github.com/UglyToad/PdfPig/blob/master/LICENSE)

**What it does:** Extract text and parse PDF structure

**Apache 2.0 is permissive:**
- Can use commercially
- Can modify
- Includes explicit patent grant
- Must include copyright notice and license text

---

### 6. SkiaSharp

**Version:** 2.88.8  
**License:** MIT  
**Copyright:** Copyright (c) 2015-2016 Xamarin, Inc.  
**Copyright:** Copyright (c) 2017-2018 Microsoft Corporation  
**Website:** https://github.com/mono/SkiaSharp  
**Based on:** Skia (Google's 2D graphics library, BSD-3-Clause)

**License Text:** [MIT License](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)

**What it does:** 2D graphics rendering (cross-platform)

---

### 7. ReactiveUI

**Version:** 20.1.1  
**License:** MIT  
**Copyright:** Copyright (c) .NET Foundation and Contributors  
**Website:** https://www.reactiveui.net/  
**Source Code:** https://github.com/reactiveui/ReactiveUI

**License Text:** [MIT License](https://github.com/reactiveui/ReactiveUI/blob/main/LICENSE)

**What it does:** MVVM framework with reactive programming

---

### 8. .NET Runtime

**Version:** 8.0  
**License:** MIT  
**Copyright:** Copyright (c) .NET Foundation and Contributors  
**Website:** https://dotnet.microsoft.com/  
**Source Code:** https://github.com/dotnet/runtime

**License Text:** [MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT)

**What it does:** Cross-platform runtime for .NET applications

---

## License Compatibility Matrix

All licenses are compatible with each other and with commercial use:

| Your Project | MIT | Apache 2.0 | BSD-3-Clause | Compatible? |
|--------------|-----|------------|--------------|-------------|
| **Proprietary/Commercial** | ✅ | ✅ | ✅ | Yes |
| **Open Source (MIT)** | ✅ | ✅ | ✅ | Yes |
| **Open Source (Apache 2.0)** | ✅ | ✅ | ✅ | Yes |
| **Open Source (BSD)** | ✅ | ✅ | ✅ | Yes |
| **GPL** | ✅ | ✅ | ✅ | Yes (but your code becomes GPL) |

---

## Attribution Requirements

To comply with these licenses, you must:

### 1. Include License Texts
Include the full license text for each library in your distribution.

**Recommended approach:**
```
YourApp/
├── licenses/
│   ├── Avalonia-LICENSE.txt
│   ├── PdfSharpCore-LICENSE.txt
│   ├── PDFtoImage-LICENSE.txt
│   ├── PDFium-LICENSE.txt
│   ├── PdfPig-LICENSE.txt
│   ├── SkiaSharp-LICENSE.txt
│   └── ReactiveUI-LICENSE.txt
└── THIRD_PARTY_NOTICES.txt
```

### 2. Include Copyright Notices
Include copyright notices (you can combine them in a single file).

**Example THIRD_PARTY_NOTICES.txt:**
```
This software uses the following open-source libraries:

Avalonia UI - Copyright (c) The Avalonia Project - MIT License
PdfSharpCore - Copyright (c) empira Software GmbH - MIT License
PDFtoImage - Copyright (c) David Sungaila - MIT License
PDFium - Copyright (c) The PDFium Authors - BSD-3-Clause License
PdfPig - Copyright (c) UglyToad Software - Apache 2.0 License
SkiaSharp - Copyright (c) Microsoft Corporation - MIT License
ReactiveUI - Copyright (c) .NET Foundation - MIT License

Full license texts are available in the licenses/ folder.
```

### 3. No Additional Requirements
Unlike GPL, you do NOT need to:
- ❌ Open-source your code
- ❌ Provide source code to users
- ❌ Use the same license for your application
- ❌ Disclose modifications

---

## Commercial Use

**You CAN:**
✅ Sell this software commercially  
✅ Include it in proprietary software  
✅ Modify the libraries  
✅ Distribute without source code  
✅ Use different licenses for your code  

**You MUST:**
✅ Include copyright notices  
✅ Include license texts  
✅ Not use library names/trademarks without permission  

**You DON'T NEED TO:**
❌ Open-source your application  
❌ Provide source code  
❌ Use the same license  
❌ Notify the original authors  

---

## Verification

To verify licenses of installed packages:

```bash
# Install license checker tool
dotnet tool install --global dotnet-project-licenses

# Run in project directory
dotnet-project-licenses --input PdfEditor/PdfEditor.csproj
```

This will generate a report of all NuGet packages and their licenses.

---

## Alternatives Considered (and Why Rejected)

### iText (AGPL License)
**License:** AGPL v3 (copyleft)  
**Why rejected:** Requires your application to be open-source under AGPL  
**Commercial license:** Available for ~$3,000+ per developer

### PDFSharp (non-Core version)
**License:** Mixed (some components GPL)  
**Why rejected:** Contains GPL components  
**Alternative used:** PdfSharpCore (MIT)

---

## Updates and Changes

**How to update licenses when upgrading packages:**

1. Run `dotnet list package` to see current versions
2. Update this LICENSES.md with new versions
3. Re-download license texts from GitHub repositories
4. Update THIRD_PARTY_NOTICES.txt
5. Verify no new copyleft licenses were introduced

---

## Summary

This PDF Editor uses **only permissive, non-copyleft licenses**:
- **MIT License** (most libraries)
- **Apache 2.0** (PdfPig)
- **BSD-3-Clause** (PDFium)

You can use this code commercially without restrictions, as long as you include the required attribution and license texts.

**No GPL, LGPL, or AGPL dependencies.**

For legal advice about license compliance, consult with a qualified attorney. This document is for informational purposes only.
