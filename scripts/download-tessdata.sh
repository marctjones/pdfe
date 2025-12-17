#!/bin/bash
# Download Tesseract language data files for bundling in releases

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
TESSDATA_DIR="$PROJECT_ROOT/tessdata"

echo "Downloading Tesseract language data to $TESSDATA_DIR"

# Create tessdata directory
mkdir -p "$TESSDATA_DIR"

# Download English language data (most common)
echo "Downloading English (eng) language data..."
curl -L -o "$TESSDATA_DIR/eng.traineddata" \
    "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata"

# Optional: Download additional languages
# Uncomment to include more languages in releases

# echo "Downloading German (deu) language data..."
# curl -L -o "$TESSDATA_DIR/deu.traineddata" \
#     "https://github.com/tesseract-ocr/tessdata_fast/raw/main/deu.traineddata"

# echo "Downloading French (fra) language data..."
# curl -L -o "$TESSDATA_DIR/fra.traineddata" \
#     "https://github.com/tesseract-ocr/tessdata_fast/raw/main/fra.traineddata"

# echo "Downloading Spanish (spa) language data..."
# curl -L -o "$TESSDATA_DIR/spa.traineddata" \
#     "https://github.com/tesseract-ocr/tessdata_fast/raw/main/spa.traineddata"

echo ""
echo "Successfully downloaded Tesseract language data!"
echo "Files in $TESSDATA_DIR:"
ls -lh "$TESSDATA_DIR"
