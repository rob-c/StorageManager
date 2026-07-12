# Contributing to Storage Manager

Thanks for your interest in improving Storage Manager. This is a small
cross-platform .NET tool; the guidelines below keep it consistent and easy to
maintain.

## Getting set up

You need the **.NET 8 SDK** (or newer — the projects target `net8.0`).

```bash
git clone https://github.com/rob-c/StorageManager.git
cd StorageManager
dotnet build StorageManager.sln -c Debug
dotnet test tests/StorageManager.Core.Tests/StorageManager.Core.Tests.csproj
```

To run a front-end during development:

```bash
dotnet run --project src/StorageManager -- --help      # CLI help
dotnet run --project src/StorageManager -- --doctor    # SSH Doctor
dotnet run --project src/StorageManager                # TUI (in a terminal) or GUI
```

## Project layout

- `src/StorageManager.Core` — all logic (mounting, connectivity, settings,
  diagnostics, SSH Doctor, VS Code setup, Kerberos, quota, status). **No UI
  dependency.** This is where behaviour lives and where tests go.
- `src/StorageManager` — the executable: the Avalonia GUI, the Spectre.Console
  TUI, and the CLI command handlers. Thin presentation over `Core`.
- `tests/StorageManager.Core.Tests` — xUnit tests for `Core`.

## Conventions

- **Keep logic in `Core`, keep it testable.** Every feature is an engine in
  `Core` with unit tests, plus thin front-ends. Side-effecting work (running
  `ssh`, `kinit`, `code`, filesystem/network calls) sits behind a small
  interface so the logic around it can be tested with a fake. Follow the
  existing modules (`Doctor`, `VsCode`, `Auth`, `Storage`) as templates.
- **Write a test with your change.** Parsers, translators, and decision logic
  should be covered by table-driven tests. `dotnet test` must stay green.
- **Match the surrounding style.** Nullable + implicit usings are on; prefer
  small, focused files with one clear responsibility. Comments explain *why*,
  not *what*.
- **Don't break the safe defaults.** Mounts are read-only unless the user opts
  in; passwords are never written to disk or logs; ssh_config edits are
  span-scoped and backed up first.

## Pull requests

1. Branch off `master`.
2. Make the change with tests; run `dotnet build` and `dotnet test`.
3. Note in the PR what you verified — especially for GUI/TUI/mount paths, which
   aren't covered by automated tests and need a manual check on the relevant OS.
4. Keep PRs focused; unrelated refactors belong in their own PR.

## Releases

Releases are automated. Pushing a `vX.Y.Z` tag runs
`.github/workflows/release.yml`, which builds self-contained single-file
binaries for Windows, Linux, and macOS (x64 + arm64), stamps them with the tag
version, and attaches them plus `SHA256SUMS.txt` to a GitHub Release.

```bash
git tag v1.2.3 -m "Storage Manager v1.2.3"
git push origin v1.2.3
```

## Screenshots

The screenshots in the README and on the website are generated, not captured by
hand, so they stay current. `tools/Screenshots` renders the real Avalonia
windows off-screen with the headless Skia backend:

```bash
dotnet run --project tools/Screenshots -c Release -- docs/screenshots
```

It writes `main.png`, `doctor.png`, `vscode.png`, and `status.png`. Re-run it
after a UI change and commit the updated images.

## License

Storage Manager is licensed under **GPL-3.0**. By contributing, you agree that
your contributions are licensed under the same terms.
