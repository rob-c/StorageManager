#!/usr/bin/env bash
# Register Storage Manager with the desktop so it shows up — with its logo — in
# the application menu and in file managers like Dolphin, Nautilus and Nemo.
#
# It installs into your per-user directories (no root needed); pass --uninstall
# to remove. The binary is run in place from this extracted folder, so keep the
# folder where it is (or move it before installing).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_id="storagemanager"
apps_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
icon_root="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor"
desktop_file="$apps_dir/StorageManager.desktop"

refresh() {
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$apps_dir" 2>/dev/null || true
  command -v gtk-update-icon-cache   >/dev/null 2>&1 && gtk-update-icon-cache -f -t "$icon_root" 2>/dev/null || true
  command -v kbuildsycoca5           >/dev/null 2>&1 && kbuildsycoca5 --noincremental 2>/dev/null || true
  command -v kbuildsycoca6           >/dev/null 2>&1 && kbuildsycoca6 --noincremental 2>/dev/null || true
}

if [ "${1:-}" = "--uninstall" ]; then
  rm -f "$desktop_file"
  rm -f "$icon_root/256x256/apps/$app_id.png" "$icon_root/scalable/apps/$app_id.png"
  refresh
  echo "Storage Manager removed from the application menu."
  exit 0
fi

binary="$here/StorageManager"
icon_src="$here/StorageManager.png"
[ -x "$binary" ] || chmod +x "$binary" 2>/dev/null || true

# Icon into the hicolor theme so menus and file managers resolve "storagemanager".
mkdir -p "$icon_root/256x256/apps"
cp "$icon_src" "$icon_root/256x256/apps/$app_id.png"

# Desktop entry with the real paths filled in. Icon points at an absolute file
# too, so Dolphin renders the launcher's logo even before the icon cache updates.
mkdir -p "$apps_dir"
sed -e "s|__EXEC__|$binary|g" \
    -e "s|__ICON__|$icon_root/256x256/apps/$app_id.png|g" \
    "$here/StorageManager.desktop" > "$desktop_file"
chmod +x "$desktop_file"

refresh
echo "Installed. 'Storage Manager' is now in your application menu with its logo."
echo "Run ./StorageManager directly, or launch it from the menu."
