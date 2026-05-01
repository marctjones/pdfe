#!/usr/bin/env bash
# Build a Debian/Ubuntu .deb installer for pdfe.
#
# Produces a self-contained single-file package — no .NET runtime required
# on the target machine. Bundles the GUI editor and the CLI together.
#
# Layout:
#   /usr/lib/pdfe/PdfEditor          GUI binary
#   /usr/lib/pdfe/pdfe               CLI binary
#   /usr/bin/pdfe                    symlink → /usr/lib/pdfe/pdfe
#   /usr/share/applications/pdfe.desktop
#   /usr/share/icons/hicolor/<size>/apps/pdfe.png
#   /usr/share/icons/hicolor/scalable/apps/pdfe.svg
#   /usr/share/doc/pdfe/{README.md,CHANGELOG.md,copyright}
#
# Usage:
#   scripts/build-deb.sh [--version 2.1.0~rc8] [--arch amd64|arm64]
#                        [--output dist/] [--lintian]
#
# Defaults:
#   --version  derived from `git describe --tags` (rcN tags become ~rcN)
#   --arch     amd64
#   --output   dist/

set -euo pipefail

# ── locate project root ────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT"

# ── defaults ───────────────────────────────────────────────────────────
ARCH="amd64"
OUTPUT_DIR="dist"
VERSION=""
RUN_LINTIAN=0

# ── parse args ─────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case $1 in
        --version)  VERSION="$2";    shift 2 ;;
        --arch)     ARCH="$2";       shift 2 ;;
        --output)   OUTPUT_DIR="$2"; shift 2 ;;
        --lintian)  RUN_LINTIAN=1;   shift ;;
        --help|-h)
            sed -n '2,30p' "$0"
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            echo "Use --help for usage" >&2
            exit 2
            ;;
    esac
done

# ── arch → .NET RID ────────────────────────────────────────────────────
case "$ARCH" in
    amd64) DOTNET_RID="linux-x64" ;;
    arm64) DOTNET_RID="linux-arm64" ;;
    *) echo "Unsupported --arch: $ARCH (use amd64 or arm64)" >&2; exit 2 ;;
esac

# ── version: prefer explicit, else git describe ────────────────────────
if [[ -z "$VERSION" ]]; then
    if git describe --tags --abbrev=0 >/dev/null 2>&1; then
        TAG="$(git describe --tags --abbrev=0)"
        # strip leading 'v' and convert -rcN → ~rcN (Debian sort order:
        # 2.1.0~rc8 < 2.1.0)
        VERSION="${TAG#v}"
        VERSION="${VERSION//-rc/~rc}"
    else
        VERSION="0.0.0"
    fi
fi

PACKAGE="pdfe"
DEB_NAME="${PACKAGE}_${VERSION}_${ARCH}.deb"

echo "▶ Building $DEB_NAME"
echo "  arch        : $ARCH"
echo "  .NET RID    : $DOTNET_RID"
echo "  version     : $VERSION"
echo "  output dir  : $OUTPUT_DIR"

# ── publish self-contained single-file binaries ────────────────────────
PUBLISH_BASE="$ROOT/artifacts/publish/$DOTNET_RID"
rm -rf "$PUBLISH_BASE"
mkdir -p "$PUBLISH_BASE"

publish() {
    local proj="$1" name="$2" outdir="$3"
    echo "▶ Publishing $name → $outdir"
    dotnet publish "$ROOT/$proj" \
        -c Release \
        -r "$DOTNET_RID" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:DebugType=None -p:DebugSymbols=false \
        -o "$outdir" >/dev/null
}

publish "PdfEditor/PdfEditor.csproj"     "PdfEditor (GUI)" "$PUBLISH_BASE/gui"
publish "Pdfe.Cli/Pdfe.Cli.csproj"       "pdfe (CLI)"      "$PUBLISH_BASE/cli"

# Sanity-check the binaries we expect.
[[ -x "$PUBLISH_BASE/gui/PdfEditor" ]] || { echo "GUI binary missing"; exit 1; }
[[ -x "$PUBLISH_BASE/cli/pdfe"      ]] || { echo "CLI binary missing"; exit 1; }

# ── stage Debian tree ──────────────────────────────────────────────────
STAGE="$ROOT/artifacts/deb-stage/$ARCH"
rm -rf "$STAGE"
mkdir -p "$STAGE/DEBIAN"
mkdir -p "$STAGE/usr/lib/pdfe"
mkdir -p "$STAGE/usr/bin"
mkdir -p "$STAGE/usr/share/applications"
mkdir -p "$STAGE/usr/share/doc/pdfe"
for s in 16 32 64 128 256; do
    mkdir -p "$STAGE/usr/share/icons/hicolor/${s}x${s}/apps"
done
mkdir -p "$STAGE/usr/share/icons/hicolor/scalable/apps"

# Binaries: copy the entire publish output (single-file extracts native
# libs at runtime into ~/.cache, and a few .pak/.so files still ship as
# siblings — copy everything so we don't miss any).
cp -a "$PUBLISH_BASE/gui/." "$STAGE/usr/lib/pdfe/"
cp -a "$PUBLISH_BASE/cli/." "$STAGE/usr/lib/pdfe/"

# Symlink the CLI into /usr/bin so `pdfe` is on PATH after install.
ln -sf "/usr/lib/pdfe/pdfe" "$STAGE/usr/bin/pdfe"

# Desktop entry, icons, docs.
install -m 0644 "$ROOT/packaging/deb/pdfe.desktop" \
    "$STAGE/usr/share/applications/pdfe.desktop"
install -m 0644 "$ROOT/PdfEditor/Assets/pdfe_logo.svg" \
    "$STAGE/usr/share/icons/hicolor/scalable/apps/pdfe.svg"
for s in 16 32 64 128 256; do
    install -m 0644 "$ROOT/PdfEditor/Assets/pdfe_logo_${s}.png" \
        "$STAGE/usr/share/icons/hicolor/${s}x${s}/apps/pdfe.png"
done
install -m 0644 "$ROOT/README.md"        "$STAGE/usr/share/doc/pdfe/README.md"
install -m 0644 "$ROOT/CHANGELOG.md"     "$STAGE/usr/share/doc/pdfe/CHANGELOG.md" 2>/dev/null || true
install -m 0644 "$ROOT/LICENSES.md"      "$STAGE/usr/share/doc/pdfe/LICENSES.md" 2>/dev/null || true
install -m 0644 "$ROOT/packaging/deb/copyright" \
    "$STAGE/usr/share/doc/pdfe/copyright"

# Ship the third-party license manifest alongside the binary so it can be
# inspected without launching the GUI. The same JSON is also embedded in
# the executable for the in-app About dialog.
if [[ -f "$ROOT/PdfEditor/Assets/third-party-licenses.json" ]]; then
    install -m 0644 "$ROOT/PdfEditor/Assets/third-party-licenses.json" \
        "$STAGE/usr/share/doc/pdfe/third-party-licenses.json"
fi

# Permissions: binaries 0755, everything else 0644.
chmod 0755 "$STAGE/usr/lib/pdfe/PdfEditor" "$STAGE/usr/lib/pdfe/pdfe"
find "$STAGE/usr/lib/pdfe" -type f \! \( -name PdfEditor -o -name pdfe \) -exec chmod 0644 {} +

# Compute installed-size in KB for the control file (Debian convention).
INSTALLED_SIZE="$(du -ks "$STAGE/usr" | cut -f1)"

# ── DEBIAN/control ─────────────────────────────────────────────────────
cat > "$STAGE/DEBIAN/control" <<EOF
Package: $PACKAGE
Version: $VERSION
Architecture: $ARCH
Maintainer: Marc Jones <marc.t.jones@gmail.com>
Homepage: https://github.com/marctjones/pdfe
Section: graphics
Priority: optional
Installed-Size: $INSTALLED_SIZE
Depends: libc6, libgcc-s1, libstdc++6, libfontconfig1, libice6, libsm6, libx11-6, libicu70 | libicu72 | libicu74
Recommends: tesseract-ocr
Description: Cross-platform PDF editor with true content-level redaction
 pdfe is a PDF editor focused on glyph-level redaction — content is
 actually removed from the PDF structure, not just visually covered —
 plus AcroForm read/fill/flatten/author and a pure-.NET PDF
 parser/writer/renderer.
 .
 Includes the desktop GUI editor and the 'pdfe' command-line toolkit
 (info, text, letters, render, redact, audit, fill-form, add-field,
 autodetect-fields, ocr).
 .
 Built on .NET 10 with Avalonia 11; ships self-contained, no runtime
 needed on the target machine. Optional 'tesseract' dependency is
 picked up at runtime if installed.
EOF

# ── postinst / postrm: refresh icon and desktop caches ─────────────────
cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ -x /usr/bin/update-desktop-database ]; then
    update-desktop-database -q /usr/share/applications || true
fi
if [ -x /usr/bin/gtk-update-icon-cache ]; then
    gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
fi
EOF
chmod 0755 "$STAGE/DEBIAN/postinst"

cat > "$STAGE/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    if [ -x /usr/bin/update-desktop-database ]; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if [ -x /usr/bin/gtk-update-icon-cache ]; then
        gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
    fi
fi
EOF
chmod 0755 "$STAGE/DEBIAN/postrm"

# ── build .deb ─────────────────────────────────────────────────────────
mkdir -p "$OUTPUT_DIR"
DEB_PATH="$OUTPUT_DIR/$DEB_NAME"
echo "▶ Assembling $DEB_PATH"
# --root-owner-group makes the package install with root:root regardless
# of the user that built it (Debian best practice).
dpkg-deb --root-owner-group --build "$STAGE" "$DEB_PATH" >/dev/null

echo
echo "✓ Built $DEB_PATH ($(du -h "$DEB_PATH" | cut -f1))"
echo
echo "Install with:"
echo "  sudo apt install ./$DEB_PATH      # resolves dependencies"
echo "  sudo dpkg -i $DEB_PATH            # plain install"
echo
echo "Verify with:"
echo "  dpkg-deb --info $DEB_PATH"
echo "  dpkg-deb --contents $DEB_PATH | head"

# Optional lintian sanity-check.
if [[ "$RUN_LINTIAN" == "1" ]]; then
    if command -v lintian >/dev/null 2>&1; then
        echo
        echo "▶ Running lintian"
        # Allow non-zero exit so pedantic warnings don't fail the build.
        lintian --no-tag-display-limit "$DEB_PATH" || true
    else
        echo "lintian not installed; skipping --lintian check" >&2
    fi
fi
