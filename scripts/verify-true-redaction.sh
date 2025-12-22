#!/bin/bash
# Build-time verification script for TRUE content-level redaction
# This script ensures that the redaction implementation hasn't been
# accidentally simplified or removed, which would create a security vulnerability.
#
# Usage: ./scripts/verify-true-redaction.sh
# Returns: 0 if verification passes, 1 if security issue detected

set -e

echo "🔍 Verifying TRUE content-level redaction implementation..."

REDACTION_SERVICE="PdfEditor/Services/RedactionService.cs"
REDACTION_RESULT="PdfEditor/Models/RedactionResult.cs"

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

ERRORS=0

# Check 1: Verify RedactionResult model exists
echo -n "  ✓ Checking RedactionResult model exists... "
if [ ! -f "$REDACTION_RESULT" ]; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: RedactionResult.cs is missing!"
    echo "       This model is required for tracking redaction modes."
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 2: Verify RedactionMode enum exists with correct values
echo -n "  ✓ Checking RedactionMode enum... "
if ! grep -q "enum RedactionMode" "$REDACTION_RESULT" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: RedactionMode enum is missing!"
    ERRORS=$((ERRORS + 1))
elif ! grep -q "TrueRedaction" "$REDACTION_RESULT" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: TrueRedaction mode is missing from enum!"
    ERRORS=$((ERRORS + 1))
elif ! grep -q "VisualOnly" "$REDACTION_RESULT" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: VisualOnly mode is missing from enum!"
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 3: Verify MANDATORY Console.WriteLine logging exists
echo -n "  ✓ Checking MANDATORY logging (Console.WriteLine)... "
if ! grep -q '\[REDACTION-SECURITY\] TRUE REDACTION' "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: MANDATORY [REDACTION-SECURITY] logging is missing!"
    echo "       This logging cannot be silenced and is critical for verification."
    ERRORS=$((ERRORS + 1))
elif ! grep -q '\[REDACTION-INFO\] Empty area redacted' "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: MANDATORY [REDACTION-INFO] logging is missing!"
    echo "       This logging is required when redaction area is empty (no content)."
    ERRORS=$((ERRORS + 1))
elif ! grep -q '\[REDACTION-CRITICAL-ERROR\]' "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: MANDATORY [REDACTION-CRITICAL-ERROR] logging is missing!"
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 4: Verify RemoveContentInArea returns RedactionResult
echo -n "  ✓ Checking RemoveContentInArea signature... "
if ! grep -q "private RedactionResult RemoveContentInArea" "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: RemoveContentInArea should return RedactionResult!"
    echo "       Current signature indicates it may have been changed to void."
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 5: Verify ContentStreamParser usage (critical for TRUE redaction)
echo -n "  ✓ Checking ContentStreamParser usage... "
if ! grep -q "ContentStreamParser" "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: ContentStreamParser usage is missing!"
    echo "       This is REQUIRED for parsing PDF content streams."
    echo "       Without it, only visual-only redaction is possible."
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 6: Verify ContentStreamBuilder usage (rebuilding streams after filtering)
echo -n "  ✓ Checking ContentStreamBuilder usage... "
if ! grep -q "ContentStreamBuilder" "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: ContentStreamBuilder usage is missing!"
    echo "       This is REQUIRED for rebuilding content streams after filtering."
    echo "       Without it, content cannot be removed from PDF structure."
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 7: Verify no suspicious comments suggesting simplification
echo -n "  ✓ Checking for suspicious simplification comments... "
if grep -qi "TODO.*visual.*only\|HACK.*just.*draw.*black\|simplif.*redaction\|skip.*content.*removal" "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${YELLOW}WARNING${NC}"
    echo "    ⚠️  Found suspicious comments in RedactionService.cs:"
    grep -ni "TODO.*visual.*only\|HACK.*just.*draw.*black\|simplif.*redaction\|skip.*content.*removal" "$REDACTION_SERVICE" 2>/dev/null || true
    echo "    These may indicate planned simplification that would break TRUE redaction."
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Check 8: Verify ReplacePageContent is called (critical for applying changes)
echo -n "  ✓ Checking ReplacePageContent usage... "
if ! grep -q "ReplacePageContent" "$REDACTION_SERVICE" 2>/dev/null; then
    echo -e "${RED}FAILED${NC}"
    echo "    ❌ SECURITY ERROR: ReplacePageContent is not being called!"
    echo "       This method is REQUIRED to apply content stream changes."
    echo "       Without it, filtered content is not written back to PDF."
    ERRORS=$((ERRORS + 1))
else
    echo -e "${GREEN}PASS${NC}"
fi

# Summary
echo ""
if [ $ERRORS -eq 0 ]; then
    echo -e "${GREEN}✅ TRUE redaction verification PASSED${NC}"
    echo "   All critical components are present and accounted for."
    exit 0
else
    echo -e "${RED}❌ TRUE redaction verification FAILED with $ERRORS error(s)${NC}"
    echo ""
    echo "CRITICAL SECURITY ISSUE DETECTED!"
    echo ""
    echo "The redaction implementation may have been modified in a way that"
    echo "compromises TRUE content-level removal. This would result in:"
    echo "  - Text still extractable from 'redacted' PDFs"
    echo "  - Visual-only redaction (security theater)"
    echo "  - UNSAFE for sensitive data"
    echo ""
    echo "DO NOT DEPLOY this build. Review changes to RedactionService.cs"
    echo "and ensure TRUE glyph-level removal is maintained."
    echo ""
    exit 1
fi
