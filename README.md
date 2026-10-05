# Shotlight

A personal screenshot app for Apple silicon Macs and Windows 11 x64 PCs. Capture an area of a frozen screen, annotate it, copy or save it, and return to recent captures later. Everything stays on your computer.

## Download and run

Download an app ZIP from [Releases](https://github.com/ejs1011/shotlight/releases/latest):

- **Mac:** `Shotlight-macOS-arm64-0.6.zip`. Requires macOS 13 or later and Apple silicon. Extract and open `Shotlight.app`, then allow Screen Recording when prompted.
- **Windows:** `Shotlight-Windows-x64-1.1.zip`. Requires Windows 11 on Intel/AMD x64. Extract and run `Shotlight.exe`. The .NET runtime is included.

These are personal builds without a paid publisher certificate or Apple notarization. The Mac app is locally signed.

The current Mac source is **0.7**, with the UI changes described below; the published Mac 0.6 download predates them. Build the current source using the Mac instructions below.

## Features

- Freeze the desktop before drawing the capture rectangle; select an area on any connected display.
- A compact floating toolbar on both platforms. Mac uses a labeled Copy action, contextual stroke/font sizes, and visible Fit/100% preview controls.
- Pen, arrow, rectangle, and editable text directly on the screenshot.
- History and settings in the toolbar’s **More (•••)** menu. Mac also shows the zoom percentage and automatically fits oversized captures.
- Configurable global capture shortcut, initially **Ctrl+Shift+S**.
- **Command+C** on Mac or **Ctrl+C** on Windows copies the annotated screenshot and closes its editor by default. Mac Settings can keep the editor open after copying; the button label changes to **Copy**.
- Save PNGs at the original pixel resolution.
- Automatically retain recent captures, editable annotations, and undo/redo across restarts. Keep 50 by default, configurable from 1 to 500. Mac thumbnails include annotations, and settings confirms reductions that remove older drafts.
- No upload service, account, or analytics.

Detailed controls and storage locations: [macOS guide](macos/README.txt) · [Windows guide](windows/README.txt).

## Use with Codex on either computer

Clone this repository into a local folder on each computer:

```sh
git clone https://github.com/ejs1011/shotlight.git
```

In Codex, choose **Add new project** and select the cloned `shotlight` folder. On Windows, you can use **Ctrl+O** to select it. Open the repository root so Codex reads `AGENTS.md` and can see both versions.

Start with: “Read AGENTS.md and docs/DEVELOPMENT.md. Help me build and test Shotlight on this computer.”

When switching computers, commit and push your changes on the first computer, then pull them on the other. You can ask Codex to do those steps. Git transfers committed source and documentation; your captures, settings, and uncommitted edits stay local. See [working across computers](docs/DEVELOPMENT.md#working-across-computers).

## Build

### macOS

Install Apple's Xcode Command Line Tools, then run from the repository root:

```sh
./macos/scripts/build-app.sh
```

The app is written to `macos/artifacts/Shotlight.app`, with a distribution ZIP at `macos/artifacts/Shotlight-macOS-arm64.zip`. Build on an Apple silicon Mac.

### Windows

Install the **.NET 10 SDK**, then double-click `windows/Build.cmd`, or run this from PowerShell in the repository root:

```powershell
.\windows\Build.cmd
```

The script runs the core checks and publishes `windows\artifacts\win-x64\Shotlight.exe` with its runtime. Use the adjacent `Run-Checks.cmd` to run the Windows checks.

## Source layout and validation

| Folder | Contents |
| --- | --- |
| `macos/` | Native Swift/AppKit application and build script |
| `windows/Shotlight/` | Native C# WPF/WinForms application |
| `windows/Shotlight.Core/` | Annotation, history, settings, and selection logic |
| `windows/Shotlight.Core.Checks/` | Platform-independent regression checks |
| `docs/` | Development, verification, and cross-computer workflow |

The implementations share the intended behavior, but use separate native code and separate local capture histories. A change to one platform does not automatically change the other.

GitHub Actions runs the Windows core checks, builds the x64 application, and runs generated-image Windows checks. It uploads the build and test report as workflow artifacts. Actual screen capture, screen permissions, mixed-monitor scaling, global shortcuts, and pasting into other apps also need manual testing on the target computer. See [the verification checklist](docs/DEVELOPMENT.md#verification).
