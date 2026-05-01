#!/usr/bin/env bash
# Run the exploratory differential test against the full pdf.js corpus,
# chunked across separate dotnet test processes so SkiaSharp's native
# memory doesn't accumulate to OOM.
#
# Why chunked: 684 PDFs × ~60 MB peak per iteration = 40+ GB of memory
# churn. SkiaSharp uses native allocations that .NET's GC reclaims
# lazily; in a tight loop they pile up faster than finalizers run.
# A single test process previously consumed 26.7 GB RSS before the
# kernel OOM-killed it (and Claude's session along with it) on
# 2026-05-01. Process exit is the only reliable native-memory release.
#
# Each chunk processes ~50 PDFs, peaks at ~600 MB, and exits. The
# slice JSONs (exploratory-chunk-NNN-of-MMM.json) are merged into
# a single exploratory-report.json at the end.
#
# Usage:
#   scripts/run-exploratory-corpus.sh                 # default: 14 chunks
#   scripts/run-exploratory-corpus.sh --chunks 7      # custom chunk count
#   scripts/run-exploratory-corpus.sh --chunks 14 --tiny  # 10-PDF smoke run
#
# Output: Pdfe.Rendering.Tests/bin/Debug/net10.0/exploratory-report.json

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT"

CHUNKS=14
CONFIG="Debug"
TINY=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --chunks)   CHUNKS="$2"; shift 2 ;;
        --release)  CONFIG="Release"; shift ;;
        --tiny)     TINY=1; shift ;;
        --help|-h)
            sed -n '2,18p' "$0"; exit 0 ;;
        *)
            echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

CORPUS="$ROOT/test-pdfs/pdfjs"
if [[ ! -d "$CORPUS" ]]; then
    echo "✗ pdf.js corpus not found at $CORPUS" >&2
    echo "  run scripts/download-pdfjs-corpus.sh first" >&2
    exit 1
fi

# When --tiny, run against a 10-PDF subset in an OUT-OF-TREE temp
# directory so we can never accidentally orphan or clobber the real
# corpus. We point the dotnet test at the temp dir via env var.
TINY_DIR=""
if [[ "$TINY" == "1" ]]; then
    TINY_DIR="$(mktemp -d /tmp/pdfe-tiny-pdfjs.XXXXXX)"
    trap 'rm -rf "$TINY_DIR"' EXIT
    ls "$CORPUS"/*.pdf 2>/dev/null | head -10 | while read -r pdf; do
        cp "$pdf" "$TINY_DIR/$(basename "$pdf")"
    done
    # Hijack the corpus path the test uses by overlaying a symlink.
    # The .NET test reads `test-pdfs/pdfjs` relative to repo root; a
    # symlink keeps the real corpus untouched at its real path.
    REAL_CORPUS_BACKUP="$ROOT/test-pdfs/pdfjs"
    SYMLINKED_CORPUS="$ROOT/test-pdfs/.pdfjs-tiny-link-$$"
    ln -s "$TINY_DIR" "$SYMLINKED_CORPUS"
    # Move real corpus out of the way and replace with the symlink, so
    # the test reads the tiny set. Restore in trap.
    mv "$REAL_CORPUS_BACKUP" "$REAL_CORPUS_BACKUP.swapped-$$"
    mv "$SYMLINKED_CORPUS" "$REAL_CORPUS_BACKUP"
    # Atomic restore that survives any crash mid-run.
    trap 'rm -f "$REAL_CORPUS_BACKUP";
          [[ -d "$REAL_CORPUS_BACKUP.swapped-$$" ]] && mv "$REAL_CORPUS_BACKUP.swapped-$$" "$REAL_CORPUS_BACKUP";
          rm -rf "$TINY_DIR"' EXIT
    echo "▶ tiny mode: corpus symlinked to $TINY_DIR ($(ls "$TINY_DIR" | wc -l) PDFs)"
fi

echo "▶ Building Pdfe.Cli ($CONFIG)"
# Build once. Each chunk then runs the published binary directly,
# avoiding the ~3-min `dotnet test` VSTest startup tax per invocation.
# Net effect: 14 chunks now take ~2 min total instead of 65+.
dotnet build -c "$CONFIG" Pdfe.Cli/Pdfe.Cli.csproj >/dev/null
PDFE_BIN="$ROOT/Pdfe.Cli/bin/$CONFIG/net10.0/pdfe"
if [[ ! -x "$PDFE_BIN" ]]; then
    echo "✗ pdfe binary not found at $PDFE_BIN" >&2
    exit 1
fi

# All slice JSONs land in a stable directory beside the test bin so
# the merge step finds them.
BIN_DIR="$ROOT/Pdfe.Rendering.Tests/bin/$CONFIG/net10.0"
mkdir -p "$BIN_DIR"
rm -f "$BIN_DIR"/exploratory-chunk-*.json "$BIN_DIR"/exploratory-report.json

echo "▶ Running $CHUNKS chunks"
echo "  (each chunk is a separate `pdfe corpus-scan` process — bounded RSS)"
chunk_failures=0
chunk_start=$(date +%s)
for ((i=0; i<CHUNKS; i++)); do
    pct=$(( (i * 100) / CHUNKS ))
    printf "  chunk %d/%d (%d%%) " "$((i + 1))" "$CHUNKS" "$pct"
    slice_path=$(printf '%s/exploratory-chunk-%03d-of-%03d.json' "$BIN_DIR" "$i" "$CHUNKS")

    # Use a timeout as belt-and-suspenders.
    chunk_ok=0
    timeout 600 "$PDFE_BIN" corpus-scan "$CORPUS" \
        --output "$slice_path" \
        --chunk "$i" \
        --total "$CHUNKS" \
        > "/tmp/exploratory-chunk-$i.log" 2>&1 && chunk_ok=1 || true

    if [[ "$chunk_ok" == "1" && -f "$slice_path" ]]; then
        stats=$(python3 -c "
import json
d=json.load(open('$slice_path'))
print(f'{d[\"total\"]} pdfs, peak {d[\"peakRssBytes\"]//1024//1024} MB')
" 2>/dev/null || echo "ok")
        echo "✓ $stats"
    else
        echo "✗ failed — see /tmp/exploratory-chunk-$i.log"
        chunk_failures=$((chunk_failures + 1))
    fi
done
chunk_elapsed=$(( $(date +%s) - chunk_start ))
echo "  total chunk runtime: ${chunk_elapsed}s"

echo
echo "▶ Merging $CHUNKS chunk reports → exploratory-report.json"
python3 - "$BIN_DIR" "$CHUNKS" <<'PY'
import json, os, sys, glob
bin_dir, expected = sys.argv[1], int(sys.argv[2])
slices = sorted(glob.glob(os.path.join(bin_dir, "exploratory-chunk-*.json")))
print(f"  found {len(slices)} slice file(s) (expected {expected})")

merged_entries = []
counts = {}
peak = 0
generated_utcs = []
for path in slices:
    with open(path) as f:
        d = json.load(f)
    merged_entries.extend(d.get("entries", []))
    for k, v in d.get("counts", {}).items():
        counts[k] = counts.get(k, 0) + v
    peak = max(peak, d.get("peakRssBytes", 0))
    if d.get("generatedUtc"):
        generated_utcs.append(d["generatedUtc"])

out = {
    "generatedUtc": max(generated_utcs) if generated_utcs else None,
    "corpus": "test-pdfs/pdfjs",
    "chunksMerged": len(slices),
    "expectedChunks": expected,
    "counts": counts,
    "total": len(merged_entries),
    "perChunkPeakRssBytes": peak,
    # JSON keys come through as PascalCase from the .NET serializer
    # (Path, Status, …); use case-insensitive lookup defensively.
    "entries": sorted(merged_entries, key=lambda e: e.get("path", e.get("Path", ""))),
}
out_path = os.path.join(bin_dir, "exploratory-report.json")
with open(out_path, "w") as f:
    json.dump(out, f, indent=2)

print(f"  wrote {out_path}")
print(f"  total: {out['total']}")
for k in sorted(counts, key=counts.get, reverse=True):
    print(f"    {counts[k]:4d}  {k}")
PY

if (( chunk_failures > 0 )); then
    echo
    echo "⚠ $chunk_failures chunk(s) failed — merged report may be partial"
    exit 1
fi
echo "✓ done"
