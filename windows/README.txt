SHOTLIGHT 1.1 FOR WINDOWS — personal, local screenshot capture

Target: Windows 11 on an Intel/AMD 64-bit (x64) PC.

WHAT IS NEW IN 1.1
A compact floating toolbar with rounded icon buttons, hover labels, and
an indigo Copy & Close action. Color and stroke size open small menus.
More (•••) contains recent captures, previous/next, zoom, and settings.
Recent Captures uses thumbnail cards and Settings groups its controls.

GET STARTED
Extract Shotlight-Windows-x64-1.1.zip to a folder, then double-click
Shotlight.exe. No installer, administrator access, or separate .NET
installation is required; the executable includes its runtime.

Shotlight runs from the camera icon in the system tray. Windows may put
it under the tray's hidden-icons arrow. Right-click for Capture Area,
Recent Captures, Settings, and Quit. Double-clicking the icon also captures.
Quit Shotlight from its tray menu; closing an editor leaves it running.

FROZEN CAPTURE
Press Ctrl+Shift+S by default, or choose Capture Area. Shotlight captures
the entire virtual desktop once before displaying its selector. Each
monitor displays its frozen image. Moving content stays static while
you select the area. Drag in either direction; release to open the editor.
Selections stay within the monitor where you start dragging. Escape or
Alt+F4 cancels. A click without a rectangle keeps capture active.
The selection border, dimming, and pixel-size label are never exported.

ANNOTATE
Choose Pen, Arrow, Rectangle, or Text. Drag to draw. Click with Text to
type directly over the screenshot with a transparent background.
Enter commits text; Shift+Enter adds a line; Escape cancels the edit.
Click an existing text annotation with the Text tool to edit it again.
Clear its text and commit to remove it. Color and width controls update
the active text. The selected drawing tool has an indigo highlight. Click the color dot
for presets or a custom color; the adjacent line icon controls stroke size.

Ctrl+Z undoes; Ctrl+Y or Ctrl+Shift+Z redoes. Undo/redo also works for text
edits. More → Zoom changes the editor preview, not the exported pixels.
Large screenshots can be scrolled in both directions.

COPY AND SAVE
Ctrl+C or Copy & Close commits active text, copies the complete annotated
image to the Windows clipboard, and closes the editor. This shortcut also
copies the whole screenshot while typing an annotation. The clipboard
contains both PNG and Windows Bitmap formats for compatibility.
Ctrl+S or Save exports a PNG to a folder you choose. Saving leaves the
editor open. Exports retain the original selected area's pixel dimensions.

RECOVER AN EARLIER SCREENSHOT
New captures enter Recent Captures automatically before the editor opens.
Closing, copying, and saving keep the draft in history. Reopen a screenshot
by clicking its thumbnail in Recent Captures. Previous moves to an older
capture; Next moves to a newer one. Existing open editors are reused.
Editable annotations and undo/redo survive quitting and restarting.
Typing is archived after a 250 ms pause; closing, copying, navigation,
and quitting flush pending edits immediately.

Settings lets you keep 1–500 captures; the default is 50. The oldest
captures are removed when that limit is exceeded. Editing an older draft
does not change its position. An open editor for an evicted capture stays
available to save or copy and shows that it is outside recent history.
Clear History asks for confirmation, moves retained drafts to the Recycle
Bin, and closes their editors. Exported PNGs are separate files.

SHORTCUT SETTINGS
Open Settings. Click the capture shortcut, press the combination, and Save.
Include Ctrl, Alt, or Win. Ctrl+C is reserved for copying screenshots;
F12 is reserved by Windows. If Windows rejects an occupied shortcut,
the current shortcut stays active. Restore Default selects Ctrl+Shift+S.
Shortcut and retention settings persist across launches.

LOCAL STORAGE
No upload, account, analytics, or application network service.
Drafts:    %LOCALAPPDATA%\Shotlight\Captures
Settings:  %LOCALAPPDATA%\Shotlight\settings.json
Each draft has an original PNG, thumbnail, and separate JSON annotations.
Only the selected area enters history. Full-desktop snapshots stay in
memory during selection and are released when capture finishes/cancels.
Windows history is separate from the Mac version's history.

BUILD FROM SOURCE
Install Microsoft's .NET 10 SDK, extract Shotlight-Windows-source.zip,
then run Build.cmd from the source folder. It runs the core checks and
publishes a standalone x64 executable under artifacts\win-x64.

The source also supports cross-building from macOS/Linux:
  dotnet run --project Shotlight.Core.Checks -c Release
  dotnet publish Shotlight/Shotlight.csproj -c Release -r win-x64
    --self-contained true -o artifacts/win-x64
The publish command is one line. EnableWindowsTargeting is set in the
project. No third-party NuGet packages are required. This personal build
does not have a publisher signing certificate.

VERIFICATION
Compiled for win-x64 using .NET SDK 10.0.401 with warnings treated as errors.
14 core checks passed on the Mac build host: selection geometry, undo/redo,
live text drafts, snapshot isolation, restart recovery, original byte
preservation, retention, incomplete/corrupt draft recovery, clearing,
and configurable shortcut/settings persistence.

The GitHub workflow runs the Windows generated-image checks and renders
the actual editor, Recent Captures, and Settings for visual review. Actual
screen capture, mixed-monitor DPI, global hotkeys, and real clipboard
integration still need a manual check on the target Windows computer. Run-Checks.cmd is included for
generated-image Windows checks of the selector, WPF rendering, inline
text, PNG export, and Ctrl+C copy-close with an injected clipboard writer.
Those checks use temporary history and do not capture your desktop or
replace your clipboard. A report is written to Shotlight-checks.txt.
They supplement a manual capture/annotate/copy/reopen check on your PC.
