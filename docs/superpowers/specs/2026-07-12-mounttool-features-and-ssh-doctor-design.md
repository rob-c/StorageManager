# MountTool: First-Run Features & SSH Doctor — Design

**Date:** 2026-07-12
**Status:** Approved design, pre-implementation
**Supersedes/extends:** `2026-07-10-cross-platform-sshfs-mounter-design.md`

## 1. Purpose

Make the PPE Storage Mounter genuinely usable by first-time, non-technical
users, and add an SSH configuration diagnostic ("SSH Doctor"). Today the tool
mounts `user@host:remotePath` over SSHFS from an Avalonia GUI. The most common
ways a first-time user fails or gives up are: missing FUSE prerequisites, being
off-VPN with only a raw sshfs error to show for it, retyping every field each
launch, SmartScreen/Gatekeeper scares, and dropped mounts that force a restart.

This design adds those quality-of-life features, a headless/terminal (TUI) mode
for Linux and macOS, and a standalone-capable SSH Doctor that audits an
`ssh_config` (including jump hosts, keepalives, DPI/middlebox resilience, and
common foot-guns) and offers surgical fixes.

## 2. Scope

### In scope
- Project restructure into a testable `MountTool.Core` library + front-ends.
- Prerequisite auto-install (Windows winget; copyable commands elsewhere).
- Gateway reachability probe with VPN-aware, plain-language errors.
- sshfs/ssh stderr → plain-language error translation.
- Remember username / host / path / target (no passwords).
- Editable "Other…" host and path entries, remembered across launches.
- Reconnect after a dropped mount (password re-prompt only).
- Live free-space/health display while connected.
- System-tray presence + minimize-to-tray while connected.
- Multiple simultaneous mounts.
- Centralized diagnostics log + "copy diagnostics" bundle.
- Terminal UI (TUI) mode via Spectre.Console on Linux/macOS.
- SSH Doctor engine (parse → resolve → analyze → probe → report → fix),
  consumed by a GUI panel, the TUI, and a `--doctor` CLI mode.
- Code-signing/notarization **process documentation** and `publish.sh` hooks
  (certs not yet available; signing itself is a follow-up).

### Out of scope (explicitly)
- Storing passwords in any OS keychain; start-at-login / auto-mount.
- SSH tunneling/obfuscation tooling (wstunnel, ssh-over-443 ProxyCommand
  injection). Doctor may *suggest* an existing 443 alternate but never
  configures a tunnel.
- Auto-fixing ssh_config without explicit per-finding user consent.
- Acquiring signing certificates (a follow-up once certs exist).
- A Windows TUI (Windows always launches the GUI).

## 3. Decisions (from brainstorming)

| Question | Decision |
|---|---|
| SSH Doctor form | Shared **Core library** consumed by GUI panel, TUI, and CLI. |
| Doctor fix behavior | **Report + offer fixes**; per-finding consent; always back up first. |
| DPI scope | Passive config tuning **+ active connectivity probes**; no tunneling. |
| Signing | **Plan the process** + add `publish.sh` hooks; no certs yet. |
| Password/keychain | **Out**. Remember username/settings only. |
| Login/auto-mount | **Out**. |
| No-GUI mode | Yes — **TUI** launched by running `mounttool` on a Linux/macOS TTY (not a `mount` subcommand). |
| TUI tech | **Spectre.Console**. |
| Overall architecture | **Approach A** — three projects, one published binary per platform. |

## 4. Architecture

### 4.1 Project layout

```
src/
  MountTool.Core/                 net8.0 class library, NO Avalonia reference
    Config.cs                     (moved from MountTool)
    Askpass.cs                    (moved; still re-invoked by the sshfs child)
    Mounting/                     (moved wholesale)
      IMounter.cs, MounterBase.cs, UnixMounterBase.cs,
      WindowsMounter.cs, MacMounter.cs, LinuxMounter.cs
    Settings/
      SettingsStore.cs            remembered fields + custom hosts/paths
      UserSettings.cs             the persisted record
    Connectivity/
      GatewayProbe.cs             TCP reachability, pre-mount checks
    Errors/
      ErrorTranslator.cs          (stderr,exitCode,context) -> friendly message
      PreflightResult.cs          Message + optional FixAction
    Diagnostics/
      DiagnosticsLog.cs           rolling in-memory + on-disk log, redaction
    Doctor/
      SshConfigParser.cs, SshConfigModel.cs, EffectiveConfigResolver.cs
      Checks/IConfigCheck.cs + individual check classes
      DoctorProbe.cs, IDoctorProbe.cs
      DoctorReport.cs, ConfigFixer.cs, SuggestedFix.cs
  MountTool/                      existing Avalonia executable (GUI + TUI host)
    Program.cs                    startup dispatch (askpass / gui / tui / doctor)
    Gui/                          MainWindow, Dialogs, DoctorPanel, tray, cards
    Tui/                          Spectre.Console front-end (Linux/macOS)
tests/
  MountTool.Core.Tests/           xUnit
```

`MountTool.Core` has **no** Avalonia/UI dependency, so the TUI and `--doctor`
paths never load a display stack (works over SSH / headless). `publish.sh`
still emits one self-contained single-file binary per RID.

### 4.2 Startup dispatch (`Program.cs`)

Order matters; askpass must stay first because the sshfs child re-invokes this
same executable for every auth prompt.

1. `PPE_ASKPASS_MODE=1` (env, see `Askpass.ModeVariable`) → askpass mode. *(unchanged)*
2. Parse args:
   - `--gui` → force Avalonia GUI.
   - `--tui` → force terminal UI.
   - `--doctor [--fix] [--json] [--dry-run] [host]` → run Doctor
     non-interactively and exit (scriptable CLI).
   - `--diagnostics` → print redacted diagnostics bundle and exit.
   - `--help/--version`.
3. No flags:
   - **Windows:** always GUI (binary is `WinExe`, no console subsystem; TUI is
     Linux/macOS only per decision).
   - **Linux/macOS:** if stdout is an interactive TTY
     (`!Console.IsOutputRedirected` and `isatty`) → TUI; else → GUI.

Avalonia's `AppBuilder` is only touched on GUI paths.

## 5. Feature designs

### 5.1 Settings memory
- Record `UserSettings { Username, HostName, RemotePathTemplate, MountTarget,
  WindowBounds?, CustomHosts[], CustomPaths[] }`.
- Location: `Environment.SpecialFolder.ApplicationData/PPEStorageMounter/settings.json`
  (`~/.config/PPEStorageMounter/settings.json` on Linux).
- **Written only after a successful mount** (never remember a typo).
- **No password** ever persisted.
- Corrupt/missing/unwritable → silently use defaults. Settings failures must
  never block mounting. Loaded at startup to pre-fill the form (GUI and TUI).

### 5.2 Gateway probe + friendly errors
- Before spawning sshfs, `GatewayProbe.CheckAsync(host, port=22, timeout≈4s)`
  does a plain `TcpClient` connect.
  - Unreachable/timeout → *"Can't reach {host}. If you're off campus, connect
    to the University VPN first, then try again."* — sshfs is **not** run.
  - Reachable → proceed.
- On sshfs failure, `ErrorTranslator` maps stderr to a
  `(Headline, Guidance, Raw)` triple, shown with the raw text in a
  collapsible/details region:
  - `Permission denied` → wrong username or password.
  - remote `No such file or directory` → that folder doesn't exist for your
    account (offer the folder picker again).
  - `Connection reset|closed by remote host` → server refused; try again / VPN.
  - `Host key verification failed` → known_hosts conflict + what-to-do line.
  - unknown → today's raw message unchanged.
- `ErrorTranslator` is a pure function in Core, unit-tested against captured
  real stderr strings. Existing 2FA "delivered password length" hint is folded
  into the translator's output for that path.

### 5.3 Prerequisite auto-install
- `IMounter.Preflight()` returns `PreflightResult { Message, FixAction? }`
  instead of `string?`. `FixAction` describes an offered remediation.
- **Windows:** WinFsp/SSHFS-Win missing → GUI shows **"Install for me"** running
  `winget install -e --id WinFsp.WinFsp` then `...SSHFS-Win.SSHFS-Win` (winget
  raises its own UAC), streams progress, then re-runs preflight. No winget →
  clickable download URLs.
- **macOS/Linux:** show the exact `brew`/`apt`/`dnf` command with a Copy button
  and clickable links; we do **not** sudo on the user's behalf.
- TUI renders the same remediation as text + a copyable command; `--doctor`/CLI
  paths print it.

### 5.4 Editable "Other…"
- The currently-disabled "Other…" items in host and path combo boxes become
  active. Selecting one swaps the ComboBox for a validated TextBox (+ back
  arrow): hostname pattern for host, absolute path for remote path.
- Accepted custom values are appended to `CustomHosts`/`CustomPaths` in
  settings and appear as normal options next launch. Custom hosts default to
  `TwoFactorPam:false` and the standard path templates.

### 5.5 Reconnect
- On a successful mount, capture `ReconnectContext { HostEntry, ResolvedRemotePath,
  Target, Username }`.
- When the watchdog detects a dropped mount, the status region shows a
  **Reconnect** button (in addition to the "connection lost" text). It re-runs
  the identical mount, prompting only for the password (focused).

### 5.6 Health / free-space display
- While connected, a throttled (≈60s) local `DriveInfo`/`statvfs` on the mount
  point — FUSE forwards it to the remote FS, so no extra ssh call.
- Status line: `Connected as {user} on {target} — {free} free of {total}`.
- Stat failure → omit the suffix; never an error state on its own.

### 5.7 System tray + minimize-to-tray
- Avalonia `TrayIcon` shown while connected; menu: Open / Reconnect (when
  dropped) / Disconnect / Quit.
- Closing the window while connected offers **Minimize to tray** alongside the
  existing confirm-and-disconnect dialog.
- Tray creation failure (e.g. Linux without a StatusNotifier host) is caught;
  close reverts to today's confirm-and-unmount behavior.

### 5.8 Multiple simultaneous mounts
- Extract `MountSession` (owns one `IMounter` + its watchdog + `ReconnectContext`
  + status). `MainWindow` holds `List<MountSession>` rendered as connection
  cards, each with its own status dot and Open/Reconnect/Disconnect. "New
  connection" adds a fresh card/form.
- `MounterBase` is untouched: concurrency lives in the front-end owning N
  mounters, each with its own sshfs process and distinct target. App close
  unmounts all live sessions. TUI mirrors this as a list of active mounts.

### 5.9 Diagnostics
- `DiagnosticsLog` centralizes what the 2FA commits scattered via
  `Askpass.DebugLog`: an in-memory ring + capped/rotated
  `~/.config/PPEStorageMounter/diagnostics.log`.
- Captures OS/version, tool version, sshfs/WinFsp versions, per-attempt args
  (**password redacted**), exit code, translated + raw stderr, probe results,
  host-key fingerprints.
- **Copy diagnostics** button (GUI) / `--diagnostics` (CLI) emit a redacted
  bundle. Redaction is explicit and unit-tested; passwords never enter the log
  (they remain env/stdin only; only lengths are recorded, per existing pattern).

### 5.10 TUI (Spectre.Console)
- `mounttool` on a Linux/macOS TTY → menu: Connect / Doctor / Diagnostics / Quit.
- Connect flow: host → path (live `$USER` substitution shown) → target →
  username → masked password (or askpass for 2FA) → live status spinner → status
  line + mini-menu (Open / Reconnect / Disconnect / New / Quit).
- Presentation only; shares 100% of Core (probe, translator, mounters,
  settings, diagnostics) with the GUI.

## 6. SSH Doctor

### 6.1 Pipeline
`parse → resolve → analyze → probe → report → fix`, all in `MountTool.Core/Doctor/`.

### 6.2 Parse (`SshConfigParser`)
- Reads `~/.ssh/config` and files pulled via `Include` (glob + depth limit),
  into ordered `Host`/`Match` blocks of keyword/value pairs.
- **Preserves source spans** (file, line range, original text) — this is what
  makes surgical, comment-preserving fixes possible; the file is never
  regenerated wholesale.
- Models OpenSSH precedence (first-obtained-value-wins per key) to report the
  *effective* value per host.

### 6.3 Resolve (`EffectiveConfigResolver`)
- For a target host (defaults: configured gateways + any user-named host),
  compute effective settings like `ssh -G`, following
  `ProxyJump`/`ProxyCommand` chains so jump hosts are validated too.
- Where the ssh binary is present, cross-check against real `ssh -G <host>`
  output as an oracle (behind a probe; needs the binary).

### 6.4 Analyze (`IConfigCheck` rules)
Each rule returns findings: severity (Error/Warning/Info), effective value,
plain explanation, optional `SuggestedFix`.

- **Keepalive tuning:** missing/too-high `ServerAliveInterval` (flag unset or
  >60s for interactive), `ServerAliveCountMax`, `TCPKeepAlive` interplay;
  explain idle-disconnect symptoms.
- **Jump-host integrity:** `ProxyJump`/`ProxyCommand` referencing a host with no
  resolvable `HostName`/`User`; mixing `ProxyJump` with a manual `ProxyCommand`;
  legacy `nc` ProxyCommand where `-W`/`ProxyJump` is cleaner; unreachable jump
  (via probe).
- **DPI/middlebox resilience:** `Compression yes` (DPI mangling), aggressive
  `RekeyLimit`, `IPQoS` values some middleboxes drop (suggest
  `IPQoS none`/`throughput`); when a probe shows idle resets, a keepalive
  recommendation tuned to the observed kill time; suggest a host's **existing**
  443 alternate only (no tunneling).
- **Foot-guns:** `StrictHostKeyChecking no`, world-readable config/IdentityFile
  perms, `UserKnownHostsFile /dev/null`, duplicate/shadowing `Host` blocks,
  `ForwardAgent yes` to untrusted hosts, CRLF endings, unknown-keyword typos
  (`Hostname` vs `Host`), overly broad `Host *` leakage.

### 6.5 Probe (`DoctorProbe`, active)
- TCP reach on 22 and 443.
- Handshake-stall detector: connect, expect the SSH banner within a short
  window; a silent mid-handshake stall is a classic DPI signature.
- Optional idle-hold test: hold a raw socket open, report if/when a middlebox
  resets it — **gated behind explicit user confirmation** (slow, ~N s).
- Probe results feed back into analyze so findings become evidence-based
  (e.g. "network resets idle SSH after ~90s → set `ServerAliveInterval 45`").
- Sits behind `IDoctorProbe` so analyze is unit-tested without network.

### 6.6 Report & fix
- Render: Spectre tree (TUI), Avalonia list with per-finding checkboxes (GUI),
  or JSON (`--doctor --json`).
- Apply: **always** write `~/.ssh/config.bak-<timestamp>` first, then apply only
  selected `SuggestedFix` edits via preserved spans (insert/replace one keyword
  line in the right block, or append a keyword to a host). Idempotent.
  `--dry-run` prints a unified diff. Fixes touch only owned lines; comments and
  unrelated formatting preserved.
- **Timestamp note:** the running app uses real `DateTime` for backup names;
  tests inject a fixed clock (`IClock`) so backups and diffs are deterministic
  (the workflow/test tooling cannot call wall-clock time directly).

### 6.7 Testability
- Parser and every check are pure over fixture strings (table-driven xUnit).
- Fixer tested by applying to fixtures and asserting the unified diff **and**
  that a re-parse yields the intended effective value.
- Probes behind `IDoctorProbe`; analyze tested with a fake probe.

## 7. Code signing (process + hooks only)

- **Windows:** document acquiring an OV/EV code-signing certificate or using
  **Azure Trusted Signing**; add a `signtool`/`AzureSignTool` hook in
  `publish.sh` guarded by an env var (`SIGN=1` + cert vars), no-op when unset.
- **macOS:** document Apple Developer ID Application cert + `codesign` +
  `notarytool` submit/staple; add hooks similarly guarded.
- **Linux:** no signing; document that GPG-signed checksums may be published.
- Signing does not run in CI/local builds until certs exist; hooks are inert by
  default so today's unsigned single-file output is unchanged.

## 8. Testing strategy

- **Unit (MountTool.Core.Tests):** `ErrorTranslator`, `SettingsStore`
  round-trip + corruption handling, `SshConfigParser` (Include, precedence,
  spans), every `IConfigCheck`, `ConfigFixer` (diff + re-parse + idempotency +
  backup), `DiagnosticsLog` redaction, `GatewayProbe`/`DoctorProbe` against a
  local listener/fake.
- **Manual verification (as today):** real mount/unmount per platform, winget
  install flow, tray behavior, multi-mount, TUI over SSH. Recorded in the
  implementation plan's verification steps.

## 9. Risks & mitigations

- **winget absent / enterprise-blocked:** always fall back to links; never hard-
  depend on winget.
- **Linux tray unsupported:** catch and revert to confirm-and-unmount.
- **ssh_config edits corrupting a hand-tuned file:** span-scoped edits +
  mandatory backup + `--dry-run` diff + re-parse assertion in tests.
- **Idle-hold probe slowness/hangs:** explicit opt-in, hard timeout, cancelable.
- **Restructure regressions:** move files without behavior change first, keep the
  askpass re-invocation path byte-for-byte, verify a normal mount before adding
  features.

## 10. Suggested build order (feeds the implementation plan)

1. Restructure into Core + Tests + front-ends; move files; green build + one
   verified mount (no behavior change).
2. `ErrorTranslator` + `GatewayProbe` + friendly errors (highest first-run ROI).
3. `SettingsStore` (username/host/path/target) + pre-fill.
4. `PreflightResult` + Windows winget install + copyable commands elsewhere.
5. Reconnect + health display.
6. Editable "Other…".
7. `DiagnosticsLog` + copy/`--diagnostics`.
8. TUI (Spectre.Console) over the now-stable Core.
9. SSH Doctor: parse → resolve → analyze → (probe) → report → fix; wire into
   GUI panel, TUI, and `--doctor` CLI.
10. Tray + minimize-to-tray.
11. Multiple simultaneous mounts (`MountSession` extraction).
12. Signing docs + guarded `publish.sh` hooks.
