# Development and handoff

Shotlight has two native applications: macOS 0.5 (Swift/AppKit, Apple silicon, macOS 13+) and Windows 1.0 (C#/.NET 10, WPF/WinForms, Windows 11 x64). Source is organized by platform; history formats and settings are local to each app.

## Working across computers

1. Clone `https://github.com/ejs1011/shotlight.git` on each computer. For example, use `~/Documents/Codex/shotlight` on Mac and `C:\Users\YOUR_NAME\Documents\Codex\shotlight` on Windows. Keep each clone in a normal local folder.
2. Open that repository root as a Codex project on each computer. The root `AGENTS.md` carries the project requirements into new chats.
3. Before starting work, inspect `git status` and fetch the latest changes with `git pull --ff-only` when the working tree is clean.
4. After making and verifying changes, review `git diff`, stage the intended files, commit with a useful message, and `git push`.
5. Pull on the second computer before continuing. Changes are shared only after a push and pull; Git is not continuous folder synchronization.

If both computers have changes, commit each set and merge deliberately. If a fast-forward pull fails, ask Codex to inspect and resolve the divergence. Do not overwrite one computer's work or force-push as a shortcut.

For authentication, use GitHub CLI (`gh auth login` followed by `gh auth setup-git`) or GitHub Desktop. A public repository can be cloned without authentication; pushing requires your GitHub account. Configure your preferred Git author name and email on each computer before committing.

Codex conversation synchronization does not transfer local source folders. Keep decisions and unfinished work in repository documentation when they need to follow you to the other computer.

## macOS build and checks

Install Xcode Command Line Tools (`xcode-select --install` if needed), then from the repository root:

```sh
./macos/scripts/build-app.sh
./macos/artifacts/Shotlight.app/Contents/MacOS/Shotlight --run-checks
```

The script targets arm64 macOS 13, links AppKit, Carbon, and ScreenCaptureKit, and ad-hoc signs the app. AppKit checks require a graphical session. Screen recording permission is required for real capture, not generated-image checks. ScreenCaptureKit is used on macOS 14+; macOS 13 uses the system screencapture tool to obtain full-display snapshots.

## Windows build and checks

Install .NET 10 SDK (for example, `winget install --id Microsoft.DotNet.SDK.10`). `windows\Build.cmd` runs core checks, publishes a standalone x64 executable, and copies the user guide and check launcher beside it.

Equivalent commands, run from the repository root:

```powershell
dotnet run --project windows/Shotlight.Core.Checks -c Release
dotnet publish windows/Shotlight/Shotlight.csproj -c Release -r win-x64 --self-contained true -o windows/artifacts/win-x64
```

The app includes its runtime. The SDK is only required to build it. The source has no third-party NuGet package dependencies. Microsoft desktop targeting and runtime packs are restored as needed.

Run Windows checks using the published `Run-Checks.cmd`, or use PowerShell:

```powershell
$exe = (Resolve-Path 'windows/artifacts/win-x64/Shotlight.exe').Path
$report = Join-Path $env:TEMP 'Shotlight-checks.txt'
$result = Start-Process -FilePath $exe -ArgumentList @('--run-checks', '--report', ('"' + $report + '"')) -Wait -PassThru
Get-Content $report
if ($result.ExitCode -ne 0) { throw 'Shotlight checks failed' }
```

Because Shotlight is a GUI executable, use `Start-Process -Wait -PassThru` when checking its exit code from PowerShell. `--run-checks` uses synthetic screenshots, temporary history, and an injected clipboard writer. It does not capture the desktop or replace the real clipboard.

Core checks and Windows cross-compilation also run from macOS/Linux with .NET 10 SDK. Run the core checks and publish sequentially because their projects share intermediate files. Running the Windows executable requires Windows.

## Verification

The initial Mac app passed its AppKit checks on an Apple silicon Mac. The initial Windows build was cross-compiled with SDK 10.0.401, included runtime 10.0.12, and passed all 14 core checks. Its Windows-specific checks were initially only compiled; the repository workflow runs them on Windows and retains their report. Consult the latest [Actions run](https://github.com/ejs1011/shotlight/actions) for actual CI results.

Before relying on a new build, test the applicable platform manually:

- Activate capture while a clock, video, or animation is moving. Confirm the screen stays frozen during selection.
- Capture on every connected monitor, including monitors with different scaling settings. Drag in both directions; cancel with Escape.
- Draw each annotation type, type multiple lines inline, edit existing text, and undo/redo.
- Copy while typing. Confirm the editor closes and the full annotated image pastes correctly into another app.
- Save a PNG and check the selected area's physical pixel dimensions.
- Close an unsaved capture, reopen it from Recent Captures, and verify its annotations after quitting and restarting.
- Change the global shortcut and try an occupied combination. Check retention settings without deleting personal work.

CI generated-image tests cannot establish real display capture, monitor scaling, global hotkey registration, or real clipboard integration. Test those on the user's machine.

## Build downloads

Published app ZIPs live in [GitHub Releases](https://github.com/ejs1011/shotlight/releases), outside Git history. GitHub also provides source ZIPs for release tags. The Windows build workflow uploads an app artifact and a check report for each run. The two applications have independent version numbers; a repository release may bundle both.
