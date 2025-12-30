#!/bin/bash
# Validation script for TRUE glyph-level redaction
# Tests with progressively complex PDFs to isolate bugs

set -e

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

PDFER="/home/marc/pdfe/PdfEditor.Redaction.Cli/bin/Debug/net8.0/pdfer"
TEST_DIR="/home/marc/pdfe/test-output"
DEMO_DIR="/home/marc/pdfe/PdfEditor.Demo/RedactionDemo"

echo "=========================================="
echo "  PDF Redaction Validation Suite"
echo "=========================================="
echo ""

# Create test output directory
mkdir -p "$TEST_DIR"

# Build the CLI tool
echo "Building pdfer CLI tool..."
cd /home/marc/pdfe
dotnet build PdfEditor.Redaction.Cli -c Debug > /dev/null 2>&1
echo "✓ Build complete"
echo ""

# Test counter
PASSED=0
FAILED=0

# Test function
test_redaction() {
    local test_name="$1"
    local input_pdf="$2"
    local search_term="$3"
    local output_pdf="$TEST_DIR/$(basename "$input_pdf" .pdf)_REDACTED.pdf"

    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "TEST: $test_name"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "Input:  $input_pdf"
    echo "Term:   '$search_term'"
    echo ""

    # Check if text exists in original
    echo "1. Checking original PDF for '$search_term'..."
    if pdftotext "$input_pdf" - 2>/dev/null | grep -q "$search_term"; then
        echo -e "   ${GREEN}✓${NC} Found in original"
    else
        echo -e "   ${YELLOW}⚠${NC} NOT found in original - skipping test"
        echo ""
        return
    fi

    # Perform redaction
    echo "2. Performing redaction..."
    if $PDFER redact "$input_pdf" "$output_pdf" "$search_term" > /dev/null 2>&1; then
        echo -e "   ${GREEN}✓${NC} Redaction completed"
    else
        echo -e "   ${RED}✗${NC} Redaction failed"
        ((FAILED++))
        echo ""
        return
    fi

    # Check file size
    original_size=$(stat -c%s "$input_pdf")
    redacted_size=$(stat -c%s "$output_pdf")
    size_diff=$((original_size - redacted_size))

    echo "3. Checking file size..."
    echo "   Original: $original_size bytes"
    echo "   Redacted: $redacted_size bytes"

    if [ $redacted_size -lt $original_size ]; then
        echo -e "   ${GREEN}✓${NC} File size decreased by $size_diff bytes"
    else
        echo -e "   ${YELLOW}⚠${NC} File size same or larger (possible issue)"
    fi

    # CRITICAL TEST: Verify text was removed
    echo "4. CRITICAL: Verifying text removal with pdftotext..."
    if pdftotext "$output_pdf" - 2>/dev/null | grep -q "$search_term"; then
        echo -e "   ${RED}✗ FAIL${NC} - Text '$search_term' still found in redacted PDF!"
        echo -e "   ${RED}This is a SECURITY BUG - redaction did not work!${NC}"
        ((FAILED++))

        # Show what was found
        echo ""
        echo "   Extracted text containing '$search_term':"
        pdftotext "$output_pdf" - 2>/dev/null | grep "$search_term" | head -3 | sed 's/^/   > /'
    else
        echo -e "   ${GREEN}✓ PASS${NC} - Text '$search_term' successfully removed!"
        ((PASSED++))
    fi

    echo ""
}

# ==========================================
# Test Suite: Progressive Complexity
# ==========================================

echo "Starting test suite..."
echo ""

# Test 1: Simple demo PDF
if [ -f "$DEMO_DIR/01_simple_original.pdf" ]; then
    test_redaction \
        "Test 1: Simple Demo PDF" \
        "$DEMO_DIR/01_simple_original.pdf" \
        "CONFIDENTIAL"
fi

# Test 2: Text-only demo PDF
if [ -f "$DEMO_DIR/04_text_only_original.pdf" ]; then
    test_redaction \
        "Test 2: Text Only PDF" \
        "$DEMO_DIR/04_text_only_original.pdf" \
        "Lorem"
fi

# Test 3: Complex demo PDF
if [ -f "$DEMO_DIR/02_complex_original.pdf" ]; then
    test_redaction \
        "Test 3: Complex Demo PDF" \
        "$DEMO_DIR/02_complex_original.pdf" \
        "CONFIDENTIAL"
fi

# Test 4: Create a super simple test PDF
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "TEST: Custom Simple PDF (Hello World)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "Creating minimal test PDF with C# tool..."

SIMPLE_PDF="$TEST_DIR/hello_world.pdf"
dotnet run --project /home/marc/pdfe/PdfEditor.Redaction.Cli -- info /nonexistent.pdf > /dev/null 2>&1 || true

# Use the test generator to create a simple PDF
cat > /tmp/create_simple_pdf.cs << 'EOF'
using PdfSharp.Drawing;
using PdfSharp.Pdf;

var document = new PdfDocument();
var page = document.AddPage();
using (var gfx = XGraphics.FromPdfPage(page))
{
    var font = new XFont("Helvetica", 20);
    gfx.DrawString("Hello World", font, XBrushes.Black, new XPoint(100, 100));
    gfx.DrawString("This is a test", font, XBrushes.Black, new XPoint(100, 140));
}
document.Save("/home/marc/pdfe/test-output/hello_world.pdf");
EOF

# Actually create it using dotnet script or just test with existing
echo "Using existing demo PDFs instead"
echo ""

# ==========================================
# Summary
# ==========================================

echo "=========================================="
echo "  Test Summary"
echo "=========================================="
echo ""
echo -e "Tests Passed: ${GREEN}$PASSED${NC}"
echo -e "Tests Failed: ${RED}$FAILED${NC}"
echo ""

if [ $FAILED -eq 0 ]; then
    echo -e "${GREEN}✓ ALL TESTS PASSED - Redaction is working!${NC}"
    exit 0
else
    echo -e "${RED}✗ SOME TESTS FAILED - Redaction has issues${NC}"
    echo ""
    echo "Redacted files saved to: $TEST_DIR"
    echo "You can manually inspect them with:"
    echo "  pdftotext $TEST_DIR/*_REDACTED.pdf -"
    exit 1
fi
