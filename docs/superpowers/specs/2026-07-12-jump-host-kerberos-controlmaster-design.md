# Jump-Host Mounts, Kerberos Delegation & Shared Control Socket — Design

**Date:** 2026-07-12
**Status:** Approved design, pre-implementation
**Builds on:** the StorageManager Core-engine + GUI/TUI/CLI architecture, and the
existing `Auth` (Kerberos), `Doctor` (`SshConfigParser`/`ConfigFixer`),
`Mounting`, and `Errors` (`PreflightResult`/`FixAction`) modules.

## 1. Purpose

Let a user mount storage on a **final target reachable only through a jump host**
(e.g. `cplab175.ph.ed.ac.uk` via `student.ph.ed.ac.uk` or `staff.ph.ed.ac.uk`),
authenticating with a single password that is turned into a **Kerberos ticket**
and delegated user→jump→target. The connection is held open as a **shared SSH
control socket** so the user's own `ssh` reuses it and reaches the target
directly. A watchdog tears everything down when the link is broken for 15s and
reconnects automatically while the ticket is valid.

## 2. Decisions (from brainstorming)

| Question | Decision |
|---|---|
| Auth path | **Kerberos-first**: password → `kinit -f` forwardable TGT → GSSAPI + delegation. **Password-on-both-hops fallback** when Kerberos is unavailable. |
| Socket sharing | **Write Host blocks into `~/.ssh/config`** (via `ConfigFixer`, backed up) so the tool *and* the user's `ssh` share the master. |
| 15s teardown | **Unmount + `ssh -O exit`**, then **auto-reconnect** using the still-valid TGT (bounded retries); fall back to manual Reconnect when the ticket is gone. |
| Realm | **Domain→realm map**, configurable: `ed.ac.uk`→`ED.AC.UK`, `cern.ch`→`CERN.CH`; extensible (`fnal.gov`→`FNAL.GOV` later). |
| Windows | Full jump/Kerberos/ControlMaster on **Unix**; on **Windows**, add a one-click **MIT Kerberos for Windows** installer + preflight + `KRB5CCNAME` wiring so ticket/Status features work; Windows jump-*mount* wired but **best-effort** pending hardware verification. |
| No password storage | Unchanged — the password only ever feeds `kinit`/askpass; the TGT (≈10h) is what enables unattended reconnect. |

## 3. Connection model

The whole feature is organized around **one SSH master connection to the target,
tunnelled through the jump host**, shared by the mount and the user's shell.

1. **Password → ticket.** `RealmMap` maps the target domain to a realm; the
   principal is `<user>@<REALM>`. `KerberosHelper` runs `kinit -f` (forwardable)
   with the password, then verifies via the ticket cache (existing pattern —
   success is confirmed by re-reading `klist`, never by `kinit`'s exit alone).
2. **Profile written.** `SshProfileWriter` produces two Host blocks and applies
   them with `ConfigFixer` (span-scoped, `~/.ssh/config` backed up first):
   - **Target** (`cplab175.ph.ed.ac.uk`): `HostName`, `User`, `ProxyJump <jump>`,
     `GSSAPIAuthentication yes`, `GSSAPIDelegateCredentials yes`,
     `ControlMaster auto`, `ControlPath ~/.ssh/cm/%r@%h:%p`, `ControlPersist 30s`,
     `ServerAliveInterval 5`, `ServerAliveCountMax 3`.
   - **Jump** (`student.ph.ed.ac.uk`): `HostName`, `User`,
     `GSSAPIAuthentication yes`, `GSSAPIDelegateCredentials yes`,
     `ServerAliveInterval 5`, `ServerAliveCountMax 3`.
   The `~/.ssh/cm/` directory is created (0700) if missing.
3. **Master established.** `ControlMaster.EstablishAsync(target)` runs
   `ssh -M -N -f -o BatchMode=yes <target>`; GSSAPI + the delegated ticket carry
   the connection user→jump→target with no further prompts. `ControlPersist`
   keeps it alive, so a later `ssh cplab175.ph.ed.ac.uk` **reuses this master and
   reaches the target directly** — no second hop through the jump.
4. **Mount rides the master.** `sshfs` for the target uses the same `ControlPath`
   (from the written config, or an explicit `ssh_command`), so the mount is a
   channel over the one master. There is no `-o reconnect`, so when the master
   dies the mount's transport exits and the watchdog can unmount cleanly.

### Fallback (no Kerberos)

If `RealmMap` has no realm or the Kerberos tools are absent, mount with
**password on both hops**: `ProxyJump` plus the existing askpass answering the
same password for the jump and target prompts (`NumberOfPasswordPrompts` raised
to cover two hops). No delegated ticket, so GSSAPI-only services (AFS/EOS) won't
work from the target — the UI states this.

## 4. The 15s watchdog & auto-reconnect

`ServerAliveInterval 5 × ServerAliveCountMax 3 = 15s`, so the master exits ~15s
after the link dies. `ConnectionMonitor` (a `DispatcherTimer`/loop, ~3s tick)
polls `ssh -O check <target>` (exit 0 = master alive) **and** the mount's
`IsMounted`:

- **Both healthy** → nothing; record `lastHealthy = now`.
- **Unhealthy** → if it has been unhealthy for **≥15s continuously**:
  1. Unmount the drive.
  2. `ssh -O exit <target>` to guarantee the master socket is gone.
  3. **Auto-reconnect**: if `KerberosHelper.Status().HasValidTicket`, re-establish
     the master and remount. Retry with backoff, capped (≈5 attempts / ≈2 min).
  4. If the ticket is invalid/expired or retries are exhausted → stop and show
     **Reconnect** (re-enter password → fresh `kinit` → full re-establish).

This guarantees the invariant: a link broken for 15s drops the socket and
unmounts, and any future `ssh` must reconnect.

## 5. Windows Kerberos support

- **Preflight/install.** A `KerberosPreflight` reports whether `kinit`/`klist`
  exist. On Windows, when absent, `PreflightResult` carries a
  `FixAction(WingetInstall, "MIT.Kerberos")` so the existing GUI "Install for me"
  button installs **MIT Kerberos for Windows**; elsewhere it offers the package
  command. `KerberosCli` already searches `C:\Program Files\MIT\Kerberos\bin`.
- **Credential cache.** After install, the app sets `KRB5CCNAME` (API or a file
  ccache) for child processes so both our `kinit`/`klist` and the mount's cygwin
  `ssh` see the same tickets.
- **Scope.** This makes the Kerberos ticket helper and the Status view work on
  Windows. The Windows jump-*mount* (SSHFS-Win's cygwin `ssh` doing GSSAPI +
  ControlMaster) is wired the same way but **flagged best-effort**: it needs
  verification on real Windows hardware, and `ssh -O` control-socket parity under
  cygwin may require adjustment. Direct (non-jump) Windows mounts are unchanged.

## 6. Components

All new logic lives in `src/StorageManager.Core` (namespace `StorageManager.*`),
each unit small and testable behind an injectable process runner.

```
src/StorageManager.Core/
  Auth/
    RealmMap.cs            domain → realm (configurable, defaults ED.AC.UK/CERN.CH)
    KerberosHelper.cs      (extend) forwardable kinit: Authenticate(..., forwardable:true)
    KerberosPreflight.cs   tools-present check → PreflightResult (+ winget on Windows)
  Ssh/
    IProcessRunner.cs      RunAsync(file,args,ct) → (exit,stdout,stderr); real impl + fake
    ControlMaster.cs       EstablishAsync / CheckAsync / ExitAsync via ssh -M/-O
    SshProfileWriter.cs    target+jump Host blocks → SuggestedFix[] → ConfigFixer
  Connection/
    JumpConnection.cs      orchestrator: kinit → writeProfile → master → mount → monitor
    ConnectionMonitor.cs   15s health loop → teardown + auto-reconnect (bounded)
  Config.cs                (extend) JumpHost?, Realm map, KeepControlSocket
```

Front-ends:
- **GUI** — mount form gains an optional **"Jump host"** dropdown
  (`student.ph.ed.ac.uk` / `staff.ph.ed.ac.uk` / none) and uses the existing host
  field as the **final target**. When a jump is chosen, the connect path routes
  through `JumpConnection`. A small note shows "SSH socket kept open — your own
  `ssh` will reuse it."
- **TUI** — the same prompts (target, jump, password) in the connect flow.
- Direct mounts (no jump) keep the current code path untouched.

## 7. Data flow

```
Connect (target, jump, user, password)
  └─ RealmMap.realmFor(target)              → REALM
  └─ KerberosHelper.Authenticate(user@REALM, password, forwardable:true)
        └─ verify via klist                  → TGT (or fallback to password auth)
  └─ SshProfileWriter.apply(target, jump)    → ConfigFixer writes ~/.ssh/config (+backup)
  └─ ControlMaster.EstablishAsync(target)    → ssh -M -N -f  (GSSAPI via jump)
  └─ Mount target over the shared ControlPath
  └─ ConnectionMonitor.start(target)         → every 3s: ssh -O check + IsMounted
        └─ unhealthy ≥15s → unmount + ssh -O exit → auto-reconnect | Reconnect
```

## 8. Error handling

- `kinit` failure (bad password/realm) → translated message; no profile written,
  no master. (`kinit` success is verified via `klist`, not exit code.)
- Master establish failure → surface `ssh` stderr through `ErrorTranslator`
  (host-key, GSSAPI, jump-unreachable → VPN hint), leave `~/.ssh/config` as the
  backup-and-write left it (idempotent; re-running is safe).
- Unmount/`ssh -O exit` are best-effort and never throw out of teardown.
- Auto-reconnect is bounded; exhaustion is a clear, non-looping "Reconnect" state.

## 9. Testing

- **Unit (fake `IProcessRunner`):** `RealmMap` (domain matching, overrides);
  `SshProfileWriter` (exact Host-block fixes; ProxyJump present; re-parse yields
  intended effective values; idempotent); `ControlMaster` (correct `ssh -M`/`-O
  check`/`-O exit` argv; check maps exit code→bool); `ConnectionMonitor` (drives a
  fake clock + fake runner: stays up while healthy, tears down at ≥15s, auto-
  reconnects with a valid ticket, stops when ticket invalid, respects the retry
  cap); `KerberosPreflight` (Windows→winget fix, Unix→command).
- **Manual (Linux, real hosts):** end-to-end mount of a cplab target via
  student/staff; confirm `ssh cplab175` reuses the master (`ssh -O check` +
  `ControlPath` present); pull the network and confirm teardown at ~15s and
  auto-reconnect; confirm `~/.ssh/config` backup.
- **Windows:** installer path and ticket/Status verified on hardware; jump-mount
  best-effort as noted.

## 10. Suggested build order

1. `IProcessRunner` + fake; `RealmMap` (+ tests).
2. `KerberosHelper` forwardable kinit; `KerberosPreflight` (+ winget) (+ tests).
3. `SshProfileWriter` over `ConfigFixer` (+ tests).
4. `ControlMaster` (+ tests).
5. `ConnectionMonitor` with fake clock/runner (+ tests) — the riskiest logic.
6. `JumpConnection` orchestrator wiring 1–5 (+ tests).
7. Config additions; GUI jump dropdown + target; TUI prompts.
8. Windows `KRB5CCNAME` wiring; docs (README/SIGNING/CONTRIBUTING as needed).

## 11. Out of scope (v1)

- Windows jump-mount guaranteed parity (best-effort only).
- Fermilab realm beyond adding the map entry when needed.
- Multiple simultaneous jump targets sharing one master pool (each connection
  gets its own master; the existing multi-window model still applies).
- Storing passwords or tickets anywhere new (TGT lives in the normal ccache).
