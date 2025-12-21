# Debug Instructions for Crash

## Run the Application with Full Logging

```bash
cd /home/marc/pdfe/PdfEditor
dotnet run 2>&1 | tee /tmp/crash_log.txt
```

## What to Look For

When you open a PDF file, you should see detailed step-by-step logs like:

```
>>> STEP 1: LoadDocumentAsync START for: /path/to/file.pdf
>>> STEP 2: Setting _currentFilePath
>>> STEP 3: RaisePropertyChanged(DocumentName)
>>> STEP 4: Calling _documentService.LoadDocument
>>> STEP 5: Setting CurrentPageIndex = 0
>>> STEP 6: Loading page thumbnails
>>> STEP 6: Page thumbnails loaded successfully
>>> STEP 7: Rendering current page
>>> RenderCurrentPageAsync: START
>>> RenderCurrentPageAsync: Calling _renderService.RenderPageAsync for page 0
>>> RenderCurrentPageAsync: Converting to Avalonia bitmap
>>> RenderCurrentPageAsync: Setting CurrentPageImage
>>> RenderCurrentPageAsync: COMPLETE
>>> STEP 7: Current page rendered successfully
>>> STEP 8: RaisePropertyChanged(TotalPages)
>>> STEP 9: RaisePropertyChanged(StatusText)
>>> STEP 10: RaisePropertyChanged(IsDocumentLoaded)
>>> STEP 11: Adding to recent files
>>> STEP 12: LoadDocumentAsync COMPLETE. Total pages: 1
```

## If It Crashes

**The LAST log line before the crash tells you WHERE it crashed.**

For example:
- If you see `>>> STEP 7: Rendering current page` but NOT `>>> RenderCurrentPageAsync: COMPLETE`, it crashed during rendering
- If you see `>>> STEP 10` but NOT `>>> STEP 11`, it crashed when adding to recent files

## After Testing

Send me:
1. The **last 30-40 lines** from `/tmp/crash_log.txt`
2. Any error messages that appeared

Example:
```bash
tail -40 /tmp/crash_log.txt
```

## Expected vs Crash

**Expected (no crash):**
```
>>> STEP 12: LoadDocumentAsync COMPLETE. Total pages: 1
```
The app stays running.

**Crash:**
```
>>> STEP 7: Rendering current page
>>> RenderCurrentPageAsync: START
>>> RenderCurrentPageAsync: Calling _renderService.RenderPageAsync for page 0
[Application ends abruptly - no more logs]
```

The specific step that's missing tells us what failed.

## Common Crash Points

1. **After Step 7 (RenderCurrentPageAsync)** - Rendering/bitmap conversion issue
2. **After Step 10 (IsDocumentLoaded)** - Property binding issue
3. **After Step 11 (Recent files)** - File I/O issue
4. **After Step 12 (Complete)** - UI thread issue after loading

Run it and let me know what the last step was!
