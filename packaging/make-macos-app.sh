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

# App icon — the SAME artwork as the Windows build (both come from logo.png).
# Build the full size ladder and pack a real .icns: prefer macOS's native
# iconutil, else our own packer. Avoid `convert x.icns`, which Finder often
# refuses to render.
icns="$app/Contents/Resources/AppIcon.icns"
if command -v convert >/dev/null 2>&1 && [ -f "$logo" ]; then
  iconwork="$(mktemp -d)"
  sizes="16 32 64 128 256 512 1024"
  for s in $sizes; do
    convert "$logo" -resize "${s}x${s}" "$iconwork/icon_${s}.png" 2>/dev/null || true
  done
  if command -v iconutil >/dev/null 2>&1; then
    # iconutil consumes a .iconset with Apple's canonical @1x/@2x filenames.
    set="$iconwork/StorageManager.iconset"; mkdir -p "$set"
    cp "$iconwork/icon_16.png"   "$set/icon_16x16.png"
    cp "$iconwork/icon_32.png"   "$set/icon_16x16@2x.png"
    cp "$iconwork/icon_32.png"   "$set/icon_32x32.png"
    cp "$iconwork/icon_64.png"   "$set/icon_32x32@2x.png"
    cp "$iconwork/icon_128.png"  "$set/icon_128x128.png"
    cp "$iconwork/icon_256.png"  "$set/icon_128x128@2x.png"
    cp "$iconwork/icon_256.png"  "$set/icon_256x256.png"
    cp "$iconwork/icon_512.png"  "$set/icon_256x256@2x.png"
    cp "$iconwork/icon_512.png"  "$set/icon_512x512.png"
    cp "$iconwork/icon_1024.png" "$set/icon_512x512@2x.png"
    iconutil -c icns "$set" -o "$icns" 2>/dev/null || true
  fi
  if [ ! -f "$icns" ]; then
    specs=""
    for s in $sizes; do
      [ -f "$iconwork/icon_${s}.png" ] && specs="$specs ${s}:$iconwork/icon_${s}.png"
    done
    python3 "$here/make-icns.py" "$icns" $specs 2>/dev/null || true
  fi
  rm -rf "$iconwork"
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
