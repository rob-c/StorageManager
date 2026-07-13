#!/usr/bin/env bash
# Wrap a published osx StorageManager binary in a double-clickable .app bundle and
# zip it, preserving the executable bit. Works cross-platform (the .app is just a
# directory + Info.plist). Ad-hoc code-signs when a signing tool is available
# (codesign on macOS, rcodesign on Linux) so it runs on Apple Silicon.
#
# Usage: make-macos-app.sh <published-binary> <arch-label> <version> <output-dir>
set -euo pipefail

binary="$1"; arch="$2"; version="$3"; outdir="$4"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Absolutise inputs so later `cd`s don't break relative paths.
binary="$(cd "$(dirname "$binary")" && pwd)/$(basename "$binary")"
mkdir -p "$outdir"; outdir="$(cd "$outdir" && pwd)"
logo="$here/../src/StorageManager/Assets/logo.png"

work="$(mktemp -d)"
app="$work/StorageManager.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp "$binary" "$app/Contents/MacOS/StorageManager"
chmod +x "$app/Contents/MacOS/StorageManager"
sed "s/__VERSION__/$version/g" "$here/macos/Info.plist" > "$app/Contents/Info.plist"

# Best-effort app icon from the logo (missing icon just shows a generic one).
if command -v convert >/dev/null 2>&1 && [ -f "$logo" ]; then
  convert "$logo" -resize 512x512 "$app/Contents/Resources/AppIcon.icns" 2>/dev/null || true
fi

# Best-effort ad-hoc signature (required for unsigned binaries to launch on Apple
# Silicon). Skipped silently if no signing tool is present.
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$app" 2>/dev/null || true
elif command -v rcodesign >/dev/null 2>&1; then
  rcodesign sign "$app" 2>/dev/null || true
fi

( cd "$work" && zip -r -q -y "StorageManager-$arch.zip" "StorageManager.app" )
mv "$work/StorageManager-$arch.zip" "$outdir/"
rm -rf "$work"
echo "packaged $outdir/StorageManager-$arch.zip"
