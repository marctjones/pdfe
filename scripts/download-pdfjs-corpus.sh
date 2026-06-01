#!/usr/bin/env bash
# Mirror Mozilla pdf.js's full PDF test corpus.
#
# pdf.js (mozilla/pdf.js, Apache-2.0) ships ~684 public regression PDFs
# at test/pdfs/. Each was contributed because it broke pdf.js or another
# renderer in some real-world way — fonts, encodings, encryption, forms,
# annotations, image XObjects, malformed-but-recoverable inputs, and
# more. Apache-2.0 licensed; safe to consume as test fixtures.
#
# We mirror the whole corpus rather than curating, because:
#   • coverage scales linearly with corpus size — every additional file
#     potentially exercises a new code path;
#   • pdf.js found 684 of these worth keeping; we'd be guessing if we
#     picked a "good" subset;
#   • disk + bandwidth are cheap (~80 MB total, all gitignored);
#   • the differential harness is corpus-agnostic — it picks up
#     whatever is in test-pdfs/pdfjs/.
#
# Output:
#   test-pdfs/pdfjs/<name>.pdf
#   test-pdfs/pdfjs/.manifest.tsv  (sha256<TAB>name<TAB>size)
#
# Idempotent: skips files whose local sha256 matches the manifest, so
# re-running is essentially free once seeded. Pass --force to re-fetch
# everything.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT="$ROOT/test-pdfs/pdfjs"
mkdir -p "$OUT"

REPO="mozilla/pdf.js"
REF="${PDFJS_REF:-master}"
API="https://api.github.com/repos/$REPO/contents/test/pdfs?ref=$REF"
RAW="https://raw.githubusercontent.com/$REPO/$REF/test/pdfs"

FORCE=0
[[ "${1:-}" == "--force" ]] && FORCE=1

echo "▶ Listing pdf.js test corpus (ref=$REF)"
LIST_TMP="$(mktemp)"
trap 'rm -f "$LIST_TMP"' EXIT

# GitHub returns up to 1000 entries in a single contents call (we have
# ~684, so one call suffices). Use whatever auth token is around to
# raise the rate limit.
auth_args=()
if [[ -n "${GITHUB_TOKEN:-}" ]]; then
    auth_args=(-H "Authorization: Bearer $GITHUB_TOKEN")
fi
curl -fsSL "${auth_args[@]}" "$API" -o "$LIST_TMP"

# Extract just the .pdf entries.
mapfile -t PDFS < <(python3 - "$LIST_TMP" <<'PY'
import json, sys
data = json.load(open(sys.argv[1]))
for d in data:
    if d.get("name", "").endswith(".pdf"):
        print(d["name"])
PY
)
echo "  ${#PDFS[@]} PDFs in upstream corpus"

# Load existing manifest as known-sha lookup so we can short-circuit
# unchanged files.
MANIFEST="$OUT/.manifest.tsv"
declare -A KNOWN_SHA
if [[ -f "$MANIFEST" && "$FORCE" == "0" ]]; then
    while IFS=$'\t' read -r sha name _size; do
        [[ -z "$sha" || -z "$name" ]] && continue
        KNOWN_SHA["$name"]="$sha"
    done < "$MANIFEST"
fi

NEW_MANIFEST="$(mktemp)"
trap 'rm -f "$LIST_TMP" "$NEW_MANIFEST"' EXIT

ok=0; skipped=0; failed=0
for name in "${PDFS[@]}"; do
    dest="$OUT/$name"

    # If the local file already matches its manifest sha, just re-emit
    # the manifest line and move on.
    if [[ "$FORCE" == "0" && -f "$dest" ]]; then
        cur_sha="$(sha256sum "$dest" | cut -d' ' -f1)"
        if [[ "${KNOWN_SHA[$name]:-}" == "$cur_sha" ]]; then
            size=$(stat -c '%s' "$dest")
            printf '%s\t%s\t%s\n' "$cur_sha" "$name" "$size" >> "$NEW_MANIFEST"
            skipped=$((skipped+1))
            continue
        fi
    fi

    # URL-encode any spaces or special chars in the name (mostly issue
    # numbers don't need this, but defensive).
    enc_name="$(python3 -c 'import sys, urllib.parse; print(urllib.parse.quote(sys.argv[1]))' "$name")"
    if curl -fsSL --max-time 60 -o "$dest.tmp" "$RAW/$enc_name" 2>/dev/null; then
        mv "$dest.tmp" "$dest"
        sha="$(sha256sum "$dest" | cut -d' ' -f1)"
        size=$(stat -c '%s' "$dest")
        printf '%s\t%s\t%s\n' "$sha" "$name" "$size" >> "$NEW_MANIFEST"
        ok=$((ok+1))
        # Quietly progress — full output for ~684 files is noisy.
        if (( ok % 50 == 0 )); then
            echo "  …$ok downloaded"
        fi
    else
        rm -f "$dest.tmp"
        echo "  ✗ $name — download failed" >&2
        failed=$((failed+1))
    fi
done

sort "$NEW_MANIFEST" -o "$MANIFEST"

echo
echo "▶ Done"
echo "  downloaded fresh : $ok"
echo "  already current  : $skipped"
echo "  failed           : $failed"
echo "  total in corpus  : $((ok + skipped))"
echo "  manifest         : $MANIFEST"
echo
echo "Run the differential harness over them with:"
echo "  dotnet test Pdfe.Rendering.Tests --filter \"FullyQualifiedName~Differential\""
