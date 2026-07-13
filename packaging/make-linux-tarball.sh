#!/usr/bin/env bash
# Bundle the published Linux binary with a desktop entry + icon so the app can
# show its logo in menus and file managers (Dolphin/Nautilus). Produces
# StorageManager-linux-x64.tar.gz, which extracts to a StorageManager/ folder.
#
# Usage: make-linux-tarball.sh <published-binary> <version> <output-dir>
set -euo pipefail

binary="$1"; version="$2"; outdir="$3"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
binary="$(cd "$(dirname "$binary")" && pwd)/$(basename "$binary")"
mkdir -p "$outdir"; outdir="$(cd "$outdir" && pwd)"
logo="$here/../src/StorageManager/Assets/logo.png"

work="$(mktemp -d)"
stage="$work/StorageManager"
mkdir -p "$stage"

cp "$binary" "$stage/StorageManager"
chmod +x "$stage/StorageManager"
cp "$here/linux/StorageManager.desktop" "$stage/StorageManager.desktop"
cp "$here/linux/install.sh" "$stage/install.sh"
chmod +x "$stage/install.sh"

# 256x256 PNG icon — the SAME artwork as the Windows and macOS builds.
if command -v convert >/dev/null 2>&1 && [ -f "$logo" ]; then
  convert "$logo" -resize 256x256 "$stage/StorageManager.png" 2>/dev/null || cp "$logo" "$stage/StorageManager.png"
else
  cp "$logo" "$stage/StorageManager.png"
fi

cat > "$stage/README.txt" <<EOF
Storage Manager $version (Linux x64)

Run it directly:        ./StorageManager
Add to the app menu:    ./install.sh      (logo shows in menus & Dolphin/Nautilus)
Remove it again:        ./install.sh --uninstall

Keep this folder in place after installing — the menu entry runs the binary here.
EOF

tar -C "$work" -czf "$outdir/StorageManager-linux-x64.tar.gz" StorageManager
rm -rf "$work"
echo "packaged $outdir/StorageManager-linux-x64.tar.gz"
