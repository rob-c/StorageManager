# Cross-Platform SSHFS Mounter — Design

**Date:** 2026-07-10
**Status:** Approved for planning
**Replaces:** `script.ps1` (Windows-only PowerShell/WinForms SSHFS-Win wrapper)

## Purpose

A single small GUI tool, distributed as one standalone executable per platform
(Windows, macOS, Linux), that prompts a user for their university username and
password and mounts a remote directory from a gateway host over SSHFS:

- Windows: `S:`
- macOS: `/Users/<user>/S`
- Linux: `/home/<user>/S`

Users receive the binary, double-click it, type credentials, and get a mounted
drive/folder. No installer for the tool itself.

## Decisions Made

| Decision | Choice |
|---|---|
| FUSE dependency | Detect system sshfs/FUSE; if missing, show per-OS install guidance with links. The tool itself has no runtime dependencies. |
| Stack | C# / .NET 8 + Avalonia UI (MAUI lacks Linux support; WinForms/WPF are Windows-only). |
| Mount mechanism | Shell out to the platform's `sshfs` binary (Option A), passing the password via stdin — the existing `script.ps1` is the working spec for the Windows path. An embedded SFTP + FUSE-bindings approach (Option B) was rejected: same dependencies, far more code and risk. |
| UX model | Same as the .ps1: window stays open while mounted showing status; Connect / Open / Disconnect buttons; closing the window prompts, then unmounts. |
| Configuration | Compiled-in defaults for gateway and remote path, overridable by an optional JSON file next to the executable (see Configuration). |

## Architecture

Single .NET 8 Avalonia project, published self-contained single-file for:
`win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`. All targets build from one
machine via `dotnet publish -r <rid>`; no per-OS build hosts required.

### Components

**`MainWindow` (Avalonia XAML + code-behind or minimal MVVM)**
Replicates the current form: username text box, masked password box, status
label, Connect / Open / Disconnect buttons. Enter triggers Connect. Username
validated against `^[A-Za-z0-9._-]+$`. Closing while connected shows a
Yes/No confirmation, then unmounts. Password is cleared from the UI
immediately after being written to sshfs stdin.

**`IMounter` interface** — one implementation per OS, selected at startup:

```
Task<MountResult> MountAsync(string username, string password, CancellationToken ct);
Task UnmountAsync();
bool IsMounted { get; }
string MountTargetDescription { get; }   // "S:" or "~/S"
PreflightResult Preflight();             // sshfs/FUSE present? target free?
void OpenInFileManager();
```

- **`WindowsMounter`** — finds `sshfs.exe` (default
  `C:\Program Files\SSHFS-Win\bin\sshfs.exe`); mounts to `S:`; sets
  `CYGFUSE=WinFsp`; unmounts by killing the sshfs process tree
  (`taskkill /PID <pid> /T /F`) and waiting for the drive letter to vanish;
  opens with `explorer.exe`. Refuses to mount if `S:` is already in use.
- **`MacMounter`** — finds `sshfs` via `PATH`, `/usr/local/bin`,
  `/opt/homebrew/bin`; mounts to `~/S` (created if missing); unmounts with
  `umount` falling back to `diskutil unmount force`; opens with `open`.
- **`LinuxMounter`** — finds `sshfs` via `PATH` / `/usr/bin`; mounts to `~/S`
  (created if missing); unmounts with `fusermount -u` (fallback `fusermount3
  -u`, then lazy `-uz`); opens with `xdg-open`.

On macOS/Linux the mounter refuses to mount if `~/S` is non-empty or already
a mount point, and removes `~/S` after unmount only if empty.

**`Config`**
Record with `Gateway`, `RemotePath`, and optional `MountTarget` override.
Defaults compiled in: `staff.ph.ed.ac.uk`, `/storage/datastore-group/PPE`,
platform-default target. At startup the app looks for `mount-config.json`
beside the executable; if present and valid it overrides the defaults; if
malformed, the app shows the parse error and exits rather than silently
using defaults.

### sshfs invocation (all platforms)

Carried over from `script.ps1`; password never appears on the command line:

```
sshfs <user>@<gateway>:<remotepath> <target> -f
  -o password_stdin
  -o PreferredAuthentications=password
  -o PubkeyAuthentication=no
  -o NumberOfPasswordPrompts=1
  -o ConnectTimeout=10
  -o StrictHostKeyChecking=accept-new
  -o reconnect
  -o ServerAliveInterval=30 -o ServerAliveCountMax=3
```

Windows additionally: `-o uid=-1,gid=-1 -o ssh_command=/usr/bin/ssh.exe`
(SSHFS-Win's bundled ssh). The process is started with redirected stdio;
stdout/stderr are drained asynchronously; the password is written to stdin
followed by close. Success = mount point appears within 20 s while the
process is still alive. Failure = process exit or timeout → kill, collect
stderr/stdout tail (last ~1800 chars), show in an error dialog with exit code.

### Preflight and install guidance

On Connect (and at startup for the status line), each mounter checks its
dependencies. If missing, a dialog names exactly what to install:

- Windows: WinFsp + SSHFS-Win, with GitHub release download links.
- macOS: `brew install macfuse` + sshfs (with note about the one-time
  System Settings kernel-extension approval, admin required).
- Linux: `sudo apt install sshfs` / `sudo dnf install fuse-sshfs`.

Links are clickable (opens default browser).

## Error Handling

- Empty/invalid username or empty password → warning dialog, focus the field.
- Missing sshfs/FUSE → guidance dialog (above), no crash.
- Target busy (`S:` in use, `~/S` non-empty/mounted) → warning dialog.
- Mount failure → error dialog with sshfs exit code and output tail
  (wrong password surfaces here as "Permission denied").
- Connection lost (mount point vanished when Open is clicked) → reset UI to
  disconnected state, message "Connection was lost."
- App exit always attempts unmount (finally-equivalent), matching the .ps1.

## Testing

- Unit tests for: config load/override/malformed handling, sshfs argument
  construction per platform, username validation, mount-target preflight
  logic (using temp dirs).
- Process interaction behind an interface so mount/unmount flow is testable
  with a fake process.
- End-to-end on Linux in this environment (real sshfs against a local sshd
  or the gateway if reachable).
- Windows/macOS: cross-compiled binaries with a manual smoke-test checklist
  (first-run SmartScreen/Gatekeeper steps included).

## Distribution Caveats (accepted)

- Unsigned binaries: Windows SmartScreen "Run anyway"; macOS right-click →
  Open on first launch (or `xattr -d com.apple.quarantine`).
- macFUSE requires one-time admin approval of its system extension.
- Self-contained single-file binaries are roughly 40–70 MB.

## Out of Scope (for this instance)

- In-app editing of server settings (JSON file covers it for now).
- System tray mode, auto-reconnect UI, key-based auth, saved credentials.

## Amendment (2026-07-10): selectable remote path and mount location

- **Remote folder dropdown** with `/home/$USER`, `/storage/datastore-personal/$USER`,
  `/storage/datastore-group/PPE` (default), and a greyed-out "Other…" placeholder.
  `$USER` is the university username from the form, substituted live.
  A `remotePath` from `mount-config.json` outside this list appears as an extra
  option and becomes the default.
- **Mount location control**: editable absolute path pre-filled with `~/S` on
  macOS/Linux; a dropdown of free drive letters (D:–Z:) pre-selected to `S:`
  (or the configured `mountTarget`) on Windows.
- A fresh `IMounter` is constructed per Connect with the chosen values.

## Amendment (2026-07-10): multiple hosts and PAM two-factor support

- Hosts are config entries `{name, twoFactorPam}`; defaults:
  staff.ph.ed.ac.uk, phcomputeppe01.ph.ed.ac.uk, t3-mw2.ph.ed.ac.uk (no 2FA)
  and lxplus.cern.ch (2FA). Legacy `gateway` configs still load.
- 2FA hosts authenticate via SSH_ASKPASS: the app re-invokes its own binary
  per ssh prompt (marker env var routes it into askpass mode). The
  "user@host's password:" prompt is answered silently from the environment;
  any other prompt (CERN PAM challenge) opens a dialog showing the server's
  prompt text and returns the user's response. Mount wait extends to 120 s
  for 2FA hosts, and `keyboard-interactive` replaces `password_stdin`.
