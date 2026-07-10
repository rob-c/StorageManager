# PPE Storage Mounter

Cross-platform GUI (Windows / macOS / Linux) that mounts
`staff.ph.ed.ac.uk:/storage/datastore-group/PPE` over SSHFS after prompting
for a university username and password. Replaces `script.ps1`.

Mount target: `S:` on Windows, `~/S` on macOS and Linux.

## Building

Requires the .NET 8+ SDK. `./publish.sh` produces standalone single-file
binaries in `dist/` for win-x64, osx-arm64, osx-x64, and linux-x64 — copy the
relevant file to users, nothing else to install for the tool itself.

## Runtime prerequisites (one-time, per machine)

- **Windows:** [WinFsp](https://winfsp.dev/rel/) then
  [SSHFS-Win](https://github.com/winfsp/sshfs-win/releases)
- **macOS:** `brew install macfuse` and `brew install gromgit/fuse/sshfs-mac`
  (macFUSE needs one-time approval in System Settings → Privacy & Security)
- **Linux:** `sudo apt install sshfs` / `sudo dnf install fuse-sshfs`

The app detects a missing sshfs and shows these instructions itself.

## Configuration

Defaults are compiled in. To override, place `mount-config.json` beside the
executable:

```json
{ "gateway": "staff.ph.ed.ac.uk", "remotePath": "/storage/datastore-group/PPE", "mountTarget": null }
```

`mountTarget` optionally overrides the drive letter (Windows) or mount
directory (macOS/Linux).

## First-run notes for users

- Windows SmartScreen: "More info" → "Run anyway" (binary is unsigned).
- macOS Gatekeeper: right-click the app → Open, first time only.

Design spec: `docs/superpowers/specs/2026-07-10-cross-platform-sshfs-mounter-design.md`
