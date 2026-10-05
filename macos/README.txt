SHOTLIGHT 0.6 — frozen screenshot capture, annotation, and recent history

Requires macOS 13 or later and an Apple silicon Mac.

WHAT IS NEW IN 0.6
A compact floating icon toolbar keeps the screenshot front and center.
Hover an icon for its label. The indigo Copy button copies and closes;
More (•••) contains recent captures, previous/next, zoom, and settings.
Recent Captures now uses thumbnail cards and Settings groups its controls.
The Mac interface follows the system light or dark appearance.

GET STARTED
Quit the previous version, unzip Shotlight-macOS-arm64-0.6.zip, and open Shotlight.app.
It runs from the camera icon in the menu bar. Choose Capture Area or press
your capture shortcut (Control–Shift–S by default). The whole desktop is
captured first and stays frozen while you drag a rectangle. Releasing the
mouse crops that original snapshot and opens the annotation editor.
Moving content cannot change the screenshot while you select the area.
Escape cancels capture. Allow screen recording if macOS requests it.

Every connected display gets its own frozen image. Start a selection on
the display you want to capture; selections stay within that display.
Drag in any direction. The size label shows pixels. A click without a
rectangle keeps capture active. The dimming, border, and size label are
never included in the screenshot. Cropping preserves Retina resolution.
Only the selected area enters history; cancelling creates no capture.

RECOVER A SCREENSHOT
Every new capture is retained automatically before its editor opens.
Open Recent Captures from the menu bar or the editor’s More (•••) menu. Click a thumbnail to
reopen that screenshot. The browser shows capture times, newest first.
Previous moves to an older capture; Next moves to a newer one.
Closing, saving, or copying a capture keeps it in history.
History survives quitting and reopening Shotlight.

Annotations are stored separately from the original screenshot, so text
remains editable. Undo/redo history is retained as well. Drawing changes
are saved when a stroke finishes; text is saved as you type. Closing,
copying, navigation, and quitting flush pending changes immediately.

The default history limit is 50 captures. Settings lets you choose 1–500.
Exceeding that limit removes the oldest captures. Editing an older capture
does not move it ahead of newer captures. An open editor for an evicted
capture remains available to save or copy and shows that it is no longer
in recent history.

Clear History in Settings moves retained drafts to Trash and closes open
capture editors after confirmation. Exported PNGs are separate files.
History begins with captures taken in this version; screenshots discarded
by previous versions cannot be recovered.

ANNOTATE AND EXPORT
Choose Pen, Arrow, Rectangle, or Text. Drag to draw. Click to type text on
the screenshot. Return commits, Shift-Return adds a line, Escape cancels.
Click existing text with the Text tool to edit it again. Clear its contents
and commit to remove it. Color and line-width controls update text live.

Command–C and Copy & Close export an annotated PNG to the clipboard and
close the editor. Text being edited is committed before copying.
Command–S exports a PNG to your chosen folder. Large images can be scrolled. More → Zoom adjusts the preview only.
Export preserves the original pixel resolution. PNG exports are flattened;
editable drafts stay in Recent Captures.

SHORTCUT SETTINGS
Choose Settings from the menu bar or the editor’s More (•••) menu. Click Capture shortcut,
press a combination, and Save. Include Command, Control, or Option.
Command–C is reserved for copying captures. If a shortcut cannot be
registered, the prior one remains active. Restore Default selects
Control–Shift–S. Shortcut and retention settings persist across launches.

LOCAL STORAGE
No upload, account, analytics, external dependencies, or network service.
Capture uses Apple's ScreenCaptureKit on macOS 14 and later. macOS 13 uses
Apple's built-in /usr/sbin/screencapture for full-display snapshots.
The selector displays static images and never recaptures on mouse release.
History is stored in:
  ~/Library/Application Support/Shotlight/Captures
Each draft includes an original PNG, thumbnail, and annotation metadata.
Temporary full-screen files on macOS 13 are removed after snapshot loading.

BUILD FROM SOURCE
Install Apple's Xcode Command Line Tools, then run:
  ./scripts/build-app.sh
from the macos folder. This builds artifacts/Shotlight.app and signs it locally
(ad hoc). It does not require a paid Apple Developer account. The app is
a personal-use build and is not notarized for distribution. A ZIP is also
written to artifacts/Shotlight-macOS-arm64.zip from the clean, verified
staging bundle, before synced folders can add Finder metadata.

CHECKS
The executable supports --run-checks for AppKit regression checks using
synthetic screenshots, temporary history, and an isolated clipboard.
Verified draft recovery from disk, Retina coordinate restoration,
editable text, persisted undo/redo, copy-close retention, limit pruning,
clearing, keyboard shortcuts, inline editing, and PNG export. Frozen
selection checks use generated images to verify redraws after the source
changes, cropping in both directions, Retina pixel coordinates, display
edges, secondary display origins, editor export, and Escape cancellation.
UI checks verified the thumbnail browser, recovering an unexported capture
with its annotations, automatic saving while typing, Previous/Next,
retention settings, and clearing the generated test history to Trash.
