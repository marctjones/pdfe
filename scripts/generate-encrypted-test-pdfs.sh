#!/usr/bin/env bash
# Regenerate the encrypted test PDFs used by the Pdfe.Core encryption tests.
# These files are deliberately gitignored — they're reproducible from the
# (committed) scrambled birth certificate source.
#
# Tests under Pdfe.Core.Tests/Parsing/ObjectStreamResolutionTests.cs early-
# return when the files are missing, so this script is only needed to
# exercise the RC4 decryption assertions locally.
#
# Requires: qpdf (sudo apt install qpdf  /  brew install qpdf).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$REPO_ROOT/test-pdfs/sample-pdfs/birth-certificate-request-scrambled.pdf"
DEST_DIR="$REPO_ROOT/test-pdfs/encrypted"

if [ ! -f "$SRC" ]; then
  echo "Source PDF not found: $SRC" >&2
  exit 1
fi

mkdir -p "$DEST_DIR"

# RC4 V=2 R=3 (128-bit) — most common legacy encryption
qpdf --allow-weak-crypto --encrypt '' '' 128 -- "$SRC" "$DEST_DIR/birth-cert-rc4-128.pdf"

# RC4 V=1 R=2 (40-bit legacy) — earliest standard handler
qpdf --allow-weak-crypto --encrypt '' '' 40 -- "$SRC" "$DEST_DIR/birth-cert-rc4-40.pdf"

echo "Regenerated:"
ls -la "$DEST_DIR"
