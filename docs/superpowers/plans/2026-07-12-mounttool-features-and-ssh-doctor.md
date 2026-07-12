# MountTool Features & SSH Doctor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure MountTool into a testable core library plus GUI/TUI front-ends, add first-run quality-of-life features, and ship an SSH Doctor that audits ssh_config.

**Architecture:** Three projects — `MountTool.Core` (no UI deps, all logic), `MountTool` (Avalonia GUI + Spectre.Console TUI + startup dispatch), `MountTool.Core.Tests` (xUnit). One self-contained binary per platform is still published.

**Tech Stack:** .NET 8 (built with dotnet 9 SDK), Avalonia 11.2.3, Spectre.Console, xUnit.

## Global Constraints

- Target framework `net8.0`; nullable + implicit usings enabled; `InvariantGlobalization`.
- `MountTool.Core` must not reference Avalonia or any UI package.
- Askpass re-invocation path (`Program.cs` env check first, `PPE_ASKPASS_MODE`) must stay byte-for-byte behaviorally identical.
- No password ever written to disk/logs; only lengths recorded.
- Settings/diagnostics live under `ApplicationData/PPEStorageMounter/`.
- ssh_config fixes: back up first, span-scoped edits only, `--dry-run` support.
- Windows always launches GUI; TUI is Linux/macOS only.
- Verification here: `dotnet build` + `dotnet test` on Linux. Mount/GUI/TUI/winget/tray runtime behavior is build-verified only; note it, don't claim runtime-verified.

## Build/verify commands

- Build all: `dotnet build MountTool.sln -c Debug`
- Test: `dotnet test tests/MountTool.Core.Tests/MountTool.Core.Tests.csproj`
- Doctor CLI smoke: `dotnet run --project src/MountTool -- --doctor --json`

---

## Phase 1 — Restructure into Core + Tests (no behavior change)

### Task 1: Create solution and Core library, move logic files

**Files:**
- Create: `MountTool.sln`, `src/MountTool.Core/MountTool.Core.csproj`
- Move: `Config.cs`, `Askpass.cs`, `Mounting/*` from `src/MountTool/` → `src/MountTool.Core/`
- Modify: `src/MountTool/MountTool.csproj` (add ProjectReference to Core)

**Interfaces:**
- Produces: `MountTool.Core` assembly exporting `Config`, `HostEntry`, `Askpass`, and `MountTool.Mounting.*` with unchanged namespaces (`MountTool`, `MountTool.Mounting`).

- [ ] Create `src/MountTool.Core/MountTool.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>MountTool</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```
- [ ] `git mv src/MountTool/Config.cs src/MountTool/Askpass.cs src/MountTool.Core/` and `git mv src/MountTool/Mounting src/MountTool.Core/Mounting`. Namespaces already `MountTool`/`MountTool.Mounting` — no edits needed.
- [ ] Add to `src/MountTool/MountTool.csproj`: `<ItemGroup><ProjectReference Include="../MountTool.Core/MountTool.Core.csproj" /></ItemGroup>`
- [ ] Create `MountTool.sln` referencing both projects: `dotnet new sln`, `dotnet sln add src/MountTool.Core src/MountTool`
- [ ] Run `dotnet build MountTool.sln -c Debug` — Expected: Build succeeded, 0 errors.
- [ ] Commit: `refactor: extract MountTool.Core library (no behavior change)`

### Task 2: Add xUnit test project

**Files:**
- Create: `tests/MountTool.Core.Tests/MountTool.Core.Tests.csproj`, `tests/MountTool.Core.Tests/ConfigDefaultTests.cs`

**Interfaces:**
- Consumes: `Config` from Core.

- [ ] `dotnet new xunit -o tests/MountTool.Core.Tests`, add ProjectReference to Core, `dotnet sln add`.
- [ ] Write first real test locking existing behavior:
```csharp
public class ConfigDefaultTests
{
    [Fact]
    public void Default_lists_expected_hosts()
    {
        var names = Config.Default.HostList.Select(h => h.Name).ToArray();
        Assert.Contains("staff.ph.ed.ac.uk", names);
        Assert.Contains("lxplus.cern.ch", names);
    }
}
```
- [ ] `dotnet test` — Expected: PASS.
- [ ] Commit: `test: add Core test project with Config baseline test`

---

## Phase 2 — CERN paths (spec §5.11)

### Task 3: Expand lxplus default RemotePaths

**Files:**
- Modify: `src/MountTool.Core/Config.cs:28-29`
- Test: `tests/MountTool.Core.Tests/ConfigDefaultTests.cs`

- [ ] Write failing test:
```csharp
[Fact]
public void Lxplus_offers_afs_work_eos_home_and_experiment_roots()
{
    var lxplus = Config.Default.HostList.Single(h => h.Name == "lxplus.cern.ch");
    Assert.Equal(new[]
    {
        "/afs/cern.ch/user/$USER1/$USER",
        "/afs/cern.ch/work/$USER1/$USER",
        "/eos/user/$USER1/$USER",
        "/eos/home-$USER1/$USER",
        "/eos/experiment/atlas",
        "/eos/experiment/cms",
        "/eos/experiment/lhcb",
        "/eos/experiment/alice",
    }, lxplus.RemotePaths);
}
```
- [ ] Run — Expected: FAIL.
- [ ] Update `Config.Default` CERN entry `RemotePaths` to the 8-item list above.
- [ ] Run — Expected: PASS. Commit: `feat: expand lxplus default storage paths`

---

## Phase 3 — Error translation & connectivity (spec §5.2)

### Task 4: ErrorTranslator (pure function)

**Files:**
- Create: `src/MountTool.Core/Errors/ErrorTranslator.cs`, `src/MountTool.Core/Errors/FriendlyError.cs`
- Test: `tests/MountTool.Core.Tests/ErrorTranslatorTests.cs`

**Interfaces:**
- Produces: `record FriendlyError(string Headline, string Guidance, string Raw)`; `static FriendlyError ErrorTranslator.Translate(string stderr, int exitCode, bool twoFactor)`.

- [ ] Failing tests (table-driven) covering: `Permission denied`, remote `No such file or directory`, `Connection reset by peer`, `Host key verification failed`, unknown fallthrough. Example:
```csharp
[Theory]
[InlineData("Permission denied (publickey,password).", "username or password")]
[InlineData("reading remote directory: No such file or directory", "does not exist")]
[InlineData("Connection reset by peer", "refused the connection")]
[InlineData("Host key verification failed.", "known_hosts")]
public void Translate_maps_known_stderr(string stderr, string expectGuidanceFragment)
{
    var e = ErrorTranslator.Translate(stderr, 1, twoFactor: false);
    Assert.Contains(expectGuidanceFragment, e.Guidance, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(stderr, e.Raw);
}
```
- [ ] Run — FAIL. Implement with ordered regex/substring rules; unknown → `Headline="The storage could not be mounted.", Guidance=""`. Include the 2FA delivered-length hint when `twoFactor`.
- [ ] Run — PASS. Commit: `feat: plain-language sshfs error translation`

### Task 5: GatewayProbe (TCP reachability)

**Files:**
- Create: `src/MountTool.Core/Connectivity/GatewayProbe.cs`
- Test: `tests/MountTool.Core.Tests/GatewayProbeTests.cs`

**Interfaces:**
- Produces: `record ProbeResult(bool Reachable, string? Message)`; `static Task<ProbeResult> GatewayProbe.CheckAsync(string host, int port, TimeSpan timeout, CancellationToken ct = default)`.

- [ ] Failing tests using a local `TcpListener` on 127.0.0.1: reachable → `Reachable==true`; a closed/blackhole port with short timeout → `Reachable==false` and VPN-worded message.
- [ ] Run — FAIL. Implement with `TcpClient.ConnectAsync` + `Task.WhenAny` timeout; unreachable message = "Can't reach {host}. If you're off campus, connect to the University VPN first, then try again."
- [ ] Run — PASS. Commit: `feat: gateway reachability probe with VPN-aware message`

---

## Phase 4 — Settings memory (spec §5.1)

### Task 6: SettingsStore

**Files:**
- Create: `src/MountTool.Core/Settings/UserSettings.cs`, `src/MountTool.Core/Settings/SettingsStore.cs`
- Test: `tests/MountTool.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `record UserSettings(string? Username, string? HostName, string? RemotePathTemplate, string? MountTarget, IReadOnlyList<string> CustomHosts, IReadOnlyList<string> CustomPaths)`; `class SettingsStore { SettingsStore(string dir); UserSettings Load(); void Save(UserSettings s); }` — `Load` never throws (corrupt/missing → `UserSettings.Empty`).

- [ ] Failing tests (inject a temp dir): round-trip Save→Load; corrupt JSON → `Empty`; missing file → `Empty`; unwritable dir → Save swallows.
- [ ] Run — FAIL. Implement with `System.Text.Json`, `Directory.CreateDirectory`, try/catch everywhere. Add `static string DefaultDirectory` = `Path.Combine(Environment.GetFolderPath(ApplicationData), "PPEStorageMounter")`.
- [ ] Run — PASS. Commit: `feat: persistent user settings (no password)`

---

## Phase 5 — Diagnostics (spec §5.9)

### Task 7: DiagnosticsLog

**Files:**
- Create: `src/MountTool.Core/Diagnostics/DiagnosticsLog.cs`
- Test: `tests/MountTool.Core.Tests/DiagnosticsLogTests.cs`

**Interfaces:**
- Produces: `class DiagnosticsLog { void Record(string category, string message); string BuildBundle(); }` singleton `DiagnosticsLog.Instance`; ring buffer (cap 500) + append to `PPEStorageMounter/diagnostics.log`. `BuildBundle` prepends OS/tool/sshfs versions.

- [ ] Failing tests: recorded lines appear in `BuildBundle`; a message containing a fake "password=SECRET" token is redacted (redaction regex on `password=`/`Password:` values); ring cap trims oldest.
- [ ] Run — FAIL. Implement; migrate `Askpass.DebugLog` calls to also route here.
- [ ] Run — PASS. Commit: `feat: centralized diagnostics log with redaction`

---

## Phase 6 — SSH Doctor engine (spec §6) — the largest phase

### Task 8: SshConfigParser + model (span-preserving)

**Files:**
- Create: `src/MountTool.Core/Doctor/SshConfigModel.cs`, `src/MountTool.Core/Doctor/SshConfigParser.cs`
- Test: `tests/MountTool.Core.Tests/Doctor/SshConfigParserTests.cs`, fixtures under `tests/.../Doctor/fixtures/`

**Interfaces:**
- Produces:
  - `record SourceSpan(string File, int StartLine, int EndLine)`
  - `record ConfigEntry(string Keyword, string Value, SourceSpan Span)`
  - `record HostBlock(string Pattern, IReadOnlyList<ConfigEntry> Entries, SourceSpan Span)`
  - `record SshConfig(IReadOnlyList<HostBlock> Blocks, IReadOnlyList<string> Files)`
  - `class SshConfigParser { SshConfig Parse(string path); SshConfig ParseText(string text, string file = "<memory>"); }`

- [ ] Failing tests over fixture strings: single Host block keywords parsed; `Include` pulls a second file (use `Parse` with temp files); precedence preserved as document order; spans have correct line numbers; comments/blank lines ignored but line numbers stay accurate.
- [ ] Run — FAIL. Implement a line scanner: `Host`/`Match` starts a block; `Include` (glob via `Directory.GetFiles`) recurses with depth cap 16; keyword split on first whitespace/`=`.
- [ ] Run — PASS. Commit: `feat: ssh_config parser with source spans and Include`

### Task 9: EffectiveConfigResolver

**Files:**
- Create: `src/MountTool.Core/Doctor/EffectiveConfigResolver.cs`
- Test: `tests/.../Doctor/EffectiveConfigResolverTests.cs`

**Interfaces:**
- Produces: `record EffectiveConfig(string Host, IReadOnlyDictionary<string,string> Values, IReadOnlyList<string> JumpChain)`; `class EffectiveConfigResolver { EffectiveConfig Resolve(SshConfig cfg, string host); }` — first-value-wins per keyword, pattern matching with `*`/`?`, follows `ProxyJump`.

- [ ] Failing tests: `Host *` value only applies if no specific block set it first; `ProxyJump bastion` yields `JumpChain=["bastion"]`; wildcard match.
- [ ] Run — FAIL. Implement glob→regex matcher and ordered accumulation.
- [ ] Run — PASS. Commit: `feat: effective ssh config resolver`

### Task 10: Check framework + Finding/SuggestedFix

**Files:**
- Create: `src/MountTool.Core/Doctor/Finding.cs`, `IConfigCheck.cs`, `SuggestedFix.cs`, `DoctorReport.cs`
- Test: covered by individual check tasks.

**Interfaces:**
- Produces:
  - `enum Severity { Info, Warning, Error }`
  - `record SuggestedFix(string Description, string Keyword, string? NewValue, string TargetHostPattern, FixKind Kind)` with `enum FixKind { SetOrReplace, AppendToHost, RemoveLine }`
  - `record Finding(string CheckId, Severity Severity, string Title, string Explanation, string? EffectiveValue, SuggestedFix? Fix)`
  - `interface IConfigCheck { IEnumerable<Finding> Run(DoctorContext ctx); }`
  - `record DoctorContext(SshConfig Config, EffectiveConfig Effective, IReadOnlyList<ProbeOutcome> Probes)`
  - `class DoctorReport { IReadOnlyList<Finding> Findings; }`

- [ ] Compile-only (interfaces). Commit: `feat: ssh doctor check framework types`

### Task 11: Keepalive + DPI checks

**Files:**
- Create: `src/MountTool.Core/Doctor/Checks/KeepaliveCheck.cs`, `Checks/DpiResilienceCheck.cs`
- Test: `tests/.../Doctor/KeepaliveCheckTests.cs`, `DpiResilienceCheckTests.cs`

- [ ] Failing tests: unset `ServerAliveInterval` → Warning + `SuggestedFix(SetOrReplace,"ServerAliveInterval","30")`; value 300 → Warning; `Compression yes` → Info fix to `no`; `IPQoS lowdelay throughput` present → Info suggest `IPQoS none`; probe reporting idle reset at 90s → `ServerAliveInterval 45` recommendation.
- [ ] Implement both checks. Run — PASS. Commit: `feat: keepalive and DPI-resilience checks`

### Task 12: Jump-host + foot-gun checks

**Files:**
- Create: `Checks/JumpHostCheck.cs`, `Checks/FootgunCheck.cs`
- Test: matching test files.

- [ ] Failing tests: `ProxyJump missinghost` with no HostName elsewhere → Error; `StrictHostKeyChecking no` → Warning; `UserKnownHostsFile /dev/null` → Warning; unknown keyword `Hostnme` → Info typo; duplicate `Host foo` blocks → Warning.
- [ ] Implement. Run — PASS. Commit: `feat: jump-host and foot-gun checks`

### Task 13: DoctorProbe (active) behind interface

**Files:**
- Create: `src/MountTool.Core/Doctor/IDoctorProbe.cs`, `DoctorProbe.cs`, `ProbeOutcome.cs`
- Test: `tests/.../Doctor/DoctorProbeTests.cs`

**Interfaces:**
- Produces: `record ProbeOutcome(string Host, int Port, bool Reachable, bool BannerSeen, TimeSpan? IdleResetAfter)`; `interface IDoctorProbe { Task<ProbeOutcome> ProbeAsync(string host, int port, bool idleTest, CancellationToken ct); }`; `class DoctorProbe : IDoctorProbe`.

- [ ] Failing tests with a local listener: sends SSH banner → `BannerSeen==true`; silent accept (no banner) within window → `BannerSeen==false`. Idle test gated + short in tests.
- [ ] Implement TCP connect + read banner with timeout; idle test optional. Run — PASS. Commit: `feat: active doctor probe`

### Task 14: ConfigFixer (span-scoped, backup, dry-run)

**Files:**
- Create: `src/MountTool.Core/Doctor/ConfigFixer.cs`, `src/MountTool.Core/IClock.cs`
- Test: `tests/.../Doctor/ConfigFixerTests.cs`

**Interfaces:**
- Produces: `class ConfigFixer { ConfigFixer(IClock clock); FixOutcome Apply(string path, IReadOnlyList<SuggestedFix> fixes, bool dryRun); }`; `record FixOutcome(string UnifiedDiff, string? BackupPath, bool Applied)`; `interface IClock { DateTime UtcNow { get; } }`.

- [ ] Failing tests over a temp config: `SetOrReplace` replaces the one keyword line, preserves comments; `AppendToHost` adds a line in the block; dry-run returns diff and writes nothing + no backup; real apply writes `config.bak-<fixed-clock>` first; re-parse yields the intended effective value; idempotent second apply is a no-op.
- [ ] Implement using spans; inject fixed clock in tests. Run — PASS. Commit: `feat: span-scoped ssh_config fixer with backup and dry-run`

### Task 15: SshDoctor orchestrator

**Files:**
- Create: `src/MountTool.Core/Doctor/SshDoctor.cs`
- Test: `tests/.../Doctor/SshDoctorTests.cs`

**Interfaces:**
- Produces: `class SshDoctor { SshDoctor(IEnumerable<IConfigCheck> checks, IDoctorProbe probe); Task<DoctorReport> RunAsync(string configPath, string host, bool runProbes, CancellationToken ct); }`.

- [ ] Failing test: given a fixture with a foot-gun, `RunAsync` returns a report containing that finding; probes off → no probe-derived findings.
- [ ] Implement parse→resolve→(probe)→run all checks→aggregate. Run — PASS. Commit: `feat: ssh doctor orchestrator`

---

## Phase 7 — Preflight result + install action (spec §5.3)

### Task 16: PreflightResult type + mounter refactor

**Files:**
- Create: `src/MountTool.Core/Errors/PreflightResult.cs`
- Modify: `Mounting/IMounter.cs`, `MounterBase.cs`, `UnixMounterBase.cs`, `WindowsMounter.cs`
- Test: `tests/.../PreflightTests.cs`

**Interfaces:**
- Produces: `record PreflightResult(string Message, FixAction? Fix)`; `record FixAction(string Label, FixKindUi Kind, string Payload)` with `enum FixKindUi { WingetInstall, OpenUrl, CopyCommand }`. `IMounter.Preflight()` returns `PreflightResult?`.

- [ ] Failing test: Unix preflight with sshfs present but non-empty target → `PreflightResult` message, no Fix; (Windows winget action tested by unit on a `WindowsMounter` seam if feasible, else build-only).
- [ ] Change return type across mounters; Unix returns `FixAction(CopyCommand, apt/dnf command)` when sshfs missing. Windows returns `FixAction(WingetInstall, ...)`.
- [ ] Update `MainWindow.OnConnect` call site to consume `PreflightResult`. Run build + test — PASS. Commit: `feat: structured preflight result with remediation`

---

## Phase 8 — Startup dispatch + CLI + TUI (spec §4.2, §5.10)

### Task 17: Program.cs dispatch

**Files:**
- Modify: `src/MountTool/Program.cs`
- Create: `src/MountTool/Cli/DoctorCli.cs`

**Interfaces:**
- Consumes: `SshDoctor`, `ConfigFixer`, `DoctorReport`.
- Produces: exit-code contract — 0 clean, 1 findings present, 2 error.

- [ ] Keep askpass check first. Add arg parse: `--gui`/`--tui`/`--doctor`/`--diagnostics`/`--help`/`--version`. No flags: Windows→GUI; Unix→TTY?TUI:GUI (`!Console.IsOutputRedirected`).
- [ ] `DoctorCli.Run(args)`: run `SshDoctor` on `~/.ssh/config`, print findings (or `--json` via `JsonSerializer`), `--fix` applies with `ConfigFixer`, `--dry-run` prints diff.
- [ ] Verify: `dotnet run --project src/MountTool -- --doctor --json` prints JSON and exits. Commit: `feat: startup dispatch and --doctor CLI`

### Task 18: Spectre.Console TUI

**Files:**
- Create: `src/MountTool/Tui/TerminalApp.cs`, `Tui/ConnectFlow.cs`, `Tui/DoctorView.cs`
- Modify: `src/MountTool/MountTool.csproj` (add `Spectre.Console` PackageReference)

**Interfaces:**
- Consumes: Core `Config`, mounters, `GatewayProbe`, `ErrorTranslator`, `SettingsStore`, `SshDoctor`.

- [ ] Add `Spectre.Console` (latest 0.49.x). Menu: Connect / Doctor / Diagnostics / Quit. Connect flow prompts host→path→target→username→masked password; runs probe then mount; shows status; mini-menu Open/Reconnect/Disconnect/New/Quit. Doctor renders a `Tree`/`Table`.
- [ ] Build-verify (interactive run not automatable). `dotnet build` green. Commit: `feat: Spectre.Console terminal UI`

---

## Phase 9 — GUI features (build-verified only)

### Task 19: Editable "Other…", reconnect, health, install button

**Files:**
- Modify: `src/MountTool/MainWindow.axaml`, `MainWindow.axaml.cs`, `Dialogs.cs`
- Create: `src/MountTool/Gui/MountSession.cs`

**Interfaces:**
- Produces: `class MountSession { IMounter Mounter; ReconnectContext Ctx; ... }`; `record ReconnectContext(HostEntry Host, string RemotePath, string Target, string Username)`.

- [ ] Editable Other…: swap ComboBox→TextBox on "Other…" selection; validate; persist custom values via `SettingsStore`.
- [ ] Reconnect: on watchdog drop, show Reconnect button reusing `ReconnectContext`, prompt password only.
- [ ] Health: throttled `DriveInfo` on mount point → status suffix "X free of Y".
- [ ] Windows install button wired to `FixAction.WingetInstall` running `winget` and re-preflighting.
- [ ] Pre-fill form from `SettingsStore` on open; save on successful mount.
- [ ] `dotnet build` green. Commit: `feat: editable Other, reconnect, health, prereq install`

### Task 20: Tray + minimize-to-tray + multi-mount

**Files:**
- Modify: `MainWindow.axaml.cs`, `App.axaml.cs`
- Create: `src/MountTool/Gui/TrayController.cs`

- [ ] Add `TrayIcon` while connected (Open/Reconnect/Disconnect/Quit); creation wrapped in try/catch → revert to confirm-unmount on failure.
- [ ] Close-while-connected dialog gains "Minimize to tray".
- [ ] `MainWindow` holds `List<MountSession>` rendered as cards; "New connection" adds one; app close unmounts all.
- [ ] `dotnet build` green. Commit: `feat: system tray, minimize-to-tray, multiple mounts`

---

## Phase 10 — Doctor GUI panel

### Task 21: DoctorPanel

**Files:**
- Create: `src/MountTool/Gui/DoctorWindow.axaml(.cs)`
- Modify: `MainWindow.axaml` (add "SSH Doctor" button)

- [ ] Panel runs `SshDoctor`, lists findings with severity color + per-finding checkbox; "Apply selected" calls `ConfigFixer` (writes backup); "Preview" shows the unified diff; "Run network tests" toggles probes.
- [ ] `dotnet build` green. Commit: `feat: SSH Doctor GUI panel`

---

## Phase 11 — Signing hooks + docs (spec §7)

### Task 22: publish.sh guarded signing hooks + README

**Files:**
- Modify: `publish.sh`, `README.md`
- Create: `docs/SIGNING.md`

- [ ] Add `if [ "$SIGN" = "1" ]; then ...` blocks: Windows `AzureSignTool`/`signtool` (env-guarded), macOS `codesign` + `notarytool` (env-guarded), inert when unset.
- [ ] `docs/SIGNING.md`: how to acquire certs (Azure Trusted Signing / Apple Developer ID), env vars expected. README: link it, note current binaries unsigned.
- [ ] Commit: `docs: code-signing process and guarded publish hooks`

---

## Phase 12 — Integration, docs, merge

### Task 23: README + full verification + merge

- [ ] Update README: new features, TUI usage (`mounttool` on a terminal), `--doctor`, settings/diagnostics locations.
- [ ] Run full `dotnet build MountTool.sln -c Debug` and `dotnet test` — capture output.
- [ ] Verify `--doctor --json` against a crafted fixture config.
- [ ] Merge branch → master (fast-forward or `--no-ff`), only if build + tests green. Record verification evidence in the merge commit / summary.
- [ ] Commit any doc changes: `docs: update README for new features`

---

## Self-Review

- **Spec coverage:** §5.1 SettingsStore→T6; §5.2 errors/probe→T4,T5; §5.3 preflight/install→T16,T19; §5.4 editable Other→T19; §5.5 reconnect→T19; §5.6 health→T19; §5.7 tray→T20; §5.8 multi-mount→T20; §5.9 diagnostics→T7; §5.10 TUI→T18; §5.11 CERN→T3; §6 Doctor→T8–T15,T17,T21; §7 signing→T22. All covered.
- **Placeholders:** none — each code task carries concrete signatures/tests.
- **Type consistency:** `SuggestedFix`/`FixKind` defined T10, consumed T11–T14, T21; `PreflightResult`/`FixAction` T16→T19; `ReconnectContext`/`MountSession` T19→T20; `IClock` T14→used in fixer only.
- **Note:** GUI/TUI/mount/winget/tray tasks are build-verified only on this box; runtime verification per platform is manual and called out in Global Constraints.
