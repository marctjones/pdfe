#!/bin/bash
# Validate redaction across all veraPDF corpus categories
# Usage: ./scripts/validate-all-categories.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
LOG_DIR="$SCRIPT_DIR/../logs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/corpus_validation_$(date +%Y%m%d_%H%M%S).log"

echo "Logging to: $LOG_FILE"
echo ""

# Categories in recommended testing order (simple → complex)
CATEGORIES=(
    "6.1 File structure"
    "6.7 Metadata"
    "6.6 Actions"
    "6.2 Graphics"
    "6.4 Transparency"
    "6.5 Annotations"
    "6.3 Fonts"
    "6.9 Interactive Forms"
)

echo "=========================================="
echo "veraPDF Corpus: Category-by-Category Validation"
echo "=========================================="
echo "Start time: $(date)"
echo ""

total_categories=${#CATEGORIES[@]}
current=0
passed_categories=0
failed_categories=0

for category in "${CATEGORIES[@]}"; do
    ((current++))
    echo ""
    echo "=========================================="
    echo "[$current/$total_categories] Category: $category"
    echo "=========================================="

    if "$SCRIPT_DIR/test-category.sh" "$category"; then
        ((passed_categories++))
        echo "✅ Category PASSED"
    else
        ((failed_categories++))
        echo "❌ Category FAILED"
    fi
done

echo ""
echo "=========================================="
echo "FINAL RESULTS"
echo "=========================================="
echo "End time: $(date)"
echo ""
echo "Categories tested: $total_categories"
echo "Categories passed: $passed_categories"
echo "Categories failed: $failed_categories"
echo ""

if [ $passed_categories -eq $total_categories ]; then
    echo "🎉 ALL CATEGORIES PASSED!"
    exit 0
elif [ $passed_categories -ge $(( total_categories * 80 / 100 )) ]; then
    echo "✅ Good: 80%+ category pass rate"
    exit 0
else
    echo "⚠️  Warning: Less than 80% category pass rate"
    exit 1
fi
