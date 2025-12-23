#!/bin/bash
# Test script: Verify redaction works across multiple words in birth certificate PDF
# Created: 2024-12-22
# Related: Issue #86 (TJ operator bounding box fix), Issue #87 (substring limitation)

set -e

PDFER=/home/marc/pdfe/PdfEditor.Redaction.Cli/bin/Release/net8.0/pdfer
SOURCE_PDF="/home/marc/Downloads/Birth Certificate Request (PDF).pdf"
TEST_DIR=/tmp/pdfe_birth_cert_test
mkdir -p "$TEST_DIR"

# Check if source PDF exists
if [ ! -f "$SOURCE_PDF" ]; then
  echo "ERROR: Birth certificate PDF not found at: $SOURCE_PDF"
  echo "Please update SOURCE_PDF path in this script."
  exit 1
fi

# 20 random words from the birth certificate
WORDS=(
  "STREET"
  "MAIDEN"
  "DATE"
  "ATTACH"
  "MAIN"
  "CERTIFICATE"
  "REQUESTER"
  "PLEASE"
  "COPY"
  "ADDRESS"
  "RELATION"
  "MADE"
  "WALLET"
  "REASON"
  "BIRTH"
  "MIDDLE"
  "CITY"
  "TELEPHONE"
  "REGISTRANT"
  "FIRST"
)

echo "Birth Certificate Redaction Verification Test"
echo "=============================================="
echo "Source PDF: $SOURCE_PDF"
echo "Testing ${#WORDS[@]} words"
echo ""

PASS=0
FAIL=0
FAILED_WORDS=()

for word in "${WORDS[@]}"; do
  OUTPUT_PDF="$TEST_DIR/redacted_${word}.pdf"

  echo -n "Testing '$word'... "

  # Run redaction
  if $PDFER redact "$SOURCE_PDF" "$OUTPUT_PDF" "$word" > /dev/null 2>&1; then
    # Verify removal
    if $PDFER verify "$OUTPUT_PDF" "$word" > /dev/null 2>&1; then
      echo "✓ PASS"
      PASS=$((PASS + 1))
    else
      echo "✗ FAIL (verification failed)"
      FAIL=$((FAIL + 1))
      FAILED_WORDS+=("$word")
    fi
  else
    echo "✗ FAIL (redaction failed)"
    FAIL=$((FAIL + 1))
    FAILED_WORDS+=("$word")
  fi
done

echo ""
echo "=============================================="
echo "Results: $PASS passed, $FAIL failed"
echo "=============================================="

if [ $FAIL -gt 0 ]; then
  echo ""
  echo "Failed words: ${FAILED_WORDS[*]}"
  echo ""
  echo "Known limitations:"
  echo "  - STREET: Substring within 'NUMBER  STREET' (Issue #87)"
fi

if [ $FAIL -eq 0 ]; then
  echo "SUCCESS: All words redacted successfully!"
  exit 0
else
  echo "PARTIAL SUCCESS: Some words failed (see above)"
  exit 1
fi
