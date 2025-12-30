#!/bin/bash
# Test a specific veraPDF corpus category
# Usage: ./scripts/test-category.sh "6.2 Graphics"

set -e

CATEGORY="$1"
if [ -z "$CATEGORY" ]; then
    echo "Usage: $0 \"Category Name\""
    echo "Example: $0 \"6.2 Graphics\""
    exit 1
fi

PDFER="./PdfEditor.Redaction.Cli/bin/Debug/net8.0/pdfer"
CORPUS="./test-pdfs/verapdf-corpus/veraPDF-corpus-master/PDF_A-1b"
MAX_PDFS=10

# Check if CLI tool exists
if [ ! -f "$PDFER" ]; then
    echo "Error: CLI tool not found at $PDFER"
    echo "Run: dotnet build PdfEditor.Redaction.Cli -c Debug"
    exit 1
fi

# Check if corpus exists
if [ ! -d "$CORPUS/$CATEGORY" ]; then
    echo "Error: Category not found: $CORPUS/$CATEGORY"
    echo "Available categories:"
    find "$CORPUS" -maxdepth 1 -type d -name "6.*" | sort
    exit 1
fi

echo "=========================================="
echo "Testing Category: $CATEGORY"
echo "=========================================="
echo ""

# Find PDFs in category (only *pass* files, skip *fail* files)
PDFS=$(find "$CORPUS/$CATEGORY" -name "*pass*.pdf" 2>/dev/null | head -n $MAX_PDFS)

if [ -z "$PDFS" ]; then
    echo "No *pass*.pdf files found in $CATEGORY"
    exit 0
fi

passed=0
failed=0
skipped=0
total=0

for pdf in $PDFS; do
    ((total++))
    filename=$(basename "$pdf")
    echo -n "[$total] Testing: $filename ... "

    # Try to extract text to find a word to redact
    word=$(pdftotext "$pdf" - 2>/dev/null | grep -o '\b[A-Za-z]\{4,\}\b' | head -1)

    if [ -z "$word" ]; then
        # No text found - just try to redact arbitrary text
        word="test"
    fi

    # Try to redact
    output="/tmp/test_$(basename "$pdf")"
    if $PDFER redact "$pdf" "$output" "$word" > /dev/null 2>&1; then
        # Redaction succeeded - verify output is valid
        if pdfinfo "$output" > /dev/null 2>&1; then
            echo "✅ PASS"
            ((passed++))
        else
            echo "⚠️  WARN: Redacted but output invalid"
            ((failed++))
        fi
        rm -f "$output"
    else
        echo "❌ FAIL"
        ((failed++))
    fi
done

echo ""
echo "=========================================="
echo "Results for $CATEGORY"
echo "=========================================="
echo "Total:   $total PDFs"
echo "Passed:  $passed ($(( passed * 100 / total ))%)"
echo "Failed:  $failed ($(( failed * 100 / total ))%)"
echo ""

if [ $passed -eq $total ]; then
    echo "🎉 All tests passed!"
    exit 0
elif [ $passed -ge $(( total * 90 / 100 )) ]; then
    echo "✅ Good: 90%+ pass rate"
    exit 0
else
    echo "⚠️  Warning: Less than 90% pass rate"
    exit 1
fi
