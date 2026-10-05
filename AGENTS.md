# Shotlight development

Shotlight is a personal, local-only screenshot application. This repository contains native macOS and Windows implementations. Read README.md and docs/DEVELOPMENT.md before changing or building it.

## Project map

- macos/Sources/Shotlight: Swift/AppKit app, frozen capture, and persistent history.
- macos/scripts/build-app.sh: builds an ad-hoc-signed Apple silicon app in macos/artifacts.
- windows/Shotlight: C# .NET 10 WPF/WinForms app targeting Windows 11 x64.
- windows/Shotlight.Core: platform-independent annotation, selection, history, and settings code.
- windows/Shotlight.Core.Checks: executable regression checks.
- windows/Build.cmd: Windows core checks and standalone x64 publish.

## Behavior to preserve

- Capture each desktop snapshot before showing selection overlays. Crop the frozen image on release; never recapture the moving desktop at that point.
- Keep annotations editable separately from original pixels. Text edits happen inline on the screenshot.
- Command+C on Mac and Ctrl+C on Windows commit active text and copy the full annotated image. Copy closes the editor by default; Settings on either platform can keep it open instead. Both the button and shortcut honor that preference.
- Save PNGs with the original pixel dimensions; preview scaling must not reduce exported resolution.
- Persist new captures before editing and autosave annotations, active text, and undo/redo. Preserve history after copying, saving, or closing.
- Keep configurable capture shortcuts and history retention (default 50, range 1–500).
- Keep the application local-only. Do not add an upload service, analytics, or account requirement.

## Implementation and checks

- Use native platform APIs and existing conventions. Keep build outputs under each platform's artifacts directory.
- Run the checks relevant to a change. For Windows, run core checks before publishing; avoid parallel commands sharing the same project obj directory.
- Windows UI checks must run on Windows. The macOS AppKit checks need a graphical macOS session. A cross-build alone does not verify operating-system integration.
- Use generated images, temporary history directories, and isolated/injected clipboards for automated checks. Do not use personal screenshots or modify normal user history.
- Explain which platform was actually tested and any remaining manual checks.
- Keep source, build instructions, and durable project context in Git. Do not commit app bundles, executables, SDKs, build caches, personal settings, screenshots, or capture histories.
- Read git status before editing. Preserve unrelated local changes. Do not force-push or discard work to synchronize computers.

The two versions have independent codebases. When changing a shared feature, make the intended platform scope clear and keep the documented behavior accurate.
