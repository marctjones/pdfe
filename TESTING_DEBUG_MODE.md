# Testing Debug Redaction Mode

## Quick Test Instructions

### 1. Start the Application from Terminal

This is **essential** to see the debug logs:

```bash
cd /home/marc/pdfe/PdfEditor
dotnet run
```

### 2. Enable Debug Mode

1. Open **Tools → Preferences** (or press `Ctrl+,`)
2. Scroll to the **Redaction** section
3. Check **"Debug Mode: Verify redactions immediately (logs to console)"**
4. Click **Save**

### 3. Test Redaction

1. **Open a PDF** (File → Open or Ctrl+O)
2. **Enable Redaction Mode** (click "Redact Mode" button or press `R`)
3. **Draw a selection** over some text
4. **Apply Redaction** (click "Apply Redaction" or press Enter)

### 4. Watch the Console

You should see logs like this:

```
[Information] ━━━━━ DEBUG MODE: Verifying redaction immediately ━━━━━
[Information] DEBUG: Starting verification of redacted area (100.00,200.00,300.00x50.00)
[Information] DEBUG: Saving in-memory document to temporary file: /tmp/debug_redaction_verify_xxx.pdf
[Information] DEBUG: Extracting text from redacted area in saved document...
[Information] DEBUG: Text extraction complete. Length: 0 characters
[Information] DEBUG: Extracted text: ''
[Information] DEBUG: ✓ Verification passed: Expected redacted text 'YOUR_TEXT' was NOT found
[Information] DEBUG: ✓ No text found in redacted area - redaction appears successful
[Information] DEBUG: ═══ VERIFICATION PASSED ═══
[Information] ━━━━━ DEBUG MODE: Verification complete ━━━━━
```

## What the Logs Mean

### Successful Redaction

```
DEBUG: ✓ Verification passed: Expected redacted text 'CONFIDENTIAL' was NOT found
DEBUG: ✓ No text found in redacted area - redaction appears successful
DEBUG: ═══ VERIFICATION PASSED ═══
```

**Meaning:**
- Text was **removed from PDF structure** ✓
- PdfPig cannot extract it ✓
- Redaction is working correctly ✓

### Failed Redaction (Should NOT happen!)

```
DEBUG: ❌ VERIFICATION FAILED! Redacted text 'SECRET' was found in extracted text!
DEBUG: This means the redaction did NOT remove the text from the PDF structure!
DEBUG: ═══ VERIFICATION FAILED ═══
```

**Meaning:**
- Text is still in PDF (security issue!) ❌
- Only visual covering occurred ❌
- **Report this immediately!** ❌

### Partial/Incomplete Redaction

```
DEBUG: ⚠ Found text in redacted area: 'some other text'
DEBUG: This may indicate incomplete redaction or text outside the selection
```

**Meaning:**
- Selection area may have overlapped other text ⚠
- Adjust selection to be more precise
- Not necessarily a failure

## Troubleshooting

### No Debug Logs Appear

**Problem:** You don't see any `DEBUG:` logs in the console.

**Solutions:**
1. Make sure you're running from terminal (`dotnet run`)
2. Verify debug mode is enabled in Preferences
3. Check that you actually applied a redaction (not just drew a selection)

### App Crashes When Opening File

**Problem:** App crashes after clicking "Open" or loading a PDF.

**Solutions:**
1. Check the console for error messages
2. Try a different, simpler PDF file
3. Disable debug mode temporarily in Preferences
4. Report the crash with the last few log lines

### "Cannot verify - document is null"

**Problem:** See warning: `DEBUG: Cannot verify - document is null`

**Solutions:**
1. This means no PDF is currently loaded
2. Make sure you opened a file first
3. Try reopening the file

## Advanced Testing

### Test with Multiple Redactions

1. Enable redaction mode
2. Draw first selection, apply redaction (see logs)
3. Draw second selection, apply redaction (see logs)
4. Each redaction should trigger separate verification

### Test with Different Content

Try redacting:
- Simple text (one word)
- Multiple words
- Text with special characters
- Text near page edges
- Very small selections

### Verify the Temporary File

The debug mode creates a temporary file. You can inspect it:

```bash
# In another terminal, watch for temp files
watch -n 0.5 'ls -lh /tmp/debug_redaction_verify_* 2>/dev/null || echo "No files"'
```

The file should:
- Appear briefly during verification
- Be deleted immediately after
- Typically be in `/tmp/` directory

## Expected Performance

- Verification adds ~50-200ms per redaction
- Noticeable but not problematic
- Logs appear in real-time as verification runs

## Turning Off Debug Mode

When you're done testing:

1. Open Preferences (Ctrl+,)
2. Uncheck "Debug Mode: Verify redactions immediately"
3. Click Save

Redactions will still work normally, just without the verification logging.

## What Gets Verified

Debug mode verifies the **IN-MEMORY document**:

- ✓ Same document that will be saved to disk
- ✓ Exact state after redaction applied
- ✓ Before any additional processing
- ✓ Using same extraction method as automated tests

This proves the redaction works **before you save**.

## Summary

Debug mode is a powerful tool to:
- ✅ Confirm redactions are working
- ✅ See real-time verification results
- ✅ Debug coordinate/selection issues
- ✅ Build confidence in the feature

Just remember to **run from terminal** to see the logs!
