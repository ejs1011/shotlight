import AppKit
import Carbon


struct CaptureShortcut: Codable, Equatable {
    let keyCode: UInt32
    let modifiers: UInt32
    let keyLabel: String
    static let standard = CaptureShortcut(keyCode: UInt32(kVK_ANSI_S), modifiers: UInt32(controlKey | shiftKey), keyLabel: "S")
    var label: String {
        var label = ""
        for (flag, symbol) in [(UInt32(controlKey), "⌃"), (UInt32(optionKey), "⌥"), (UInt32(shiftKey), "⇧"), (UInt32(cmdKey), "⌘")] {
            if modifiers & flag != 0 { label += symbol }
        }
        return label + keyLabel
    }
    static func from(_ event: NSEvent) -> CaptureShortcut? {
        let flags = event.modifierFlags.intersection([.command, .control, .option, .shift])
        guard !flags.intersection([.command, .control, .option]).isEmpty else { return nil }
        var modifiers: UInt32 = 0
        for (flag, carbon) in [(NSEvent.ModifierFlags.command, UInt32(cmdKey)), (.control, UInt32(controlKey)), (.option, UInt32(optionKey)), (.shift, UInt32(shiftKey))] {
            if flags.contains(flag) { modifiers |= carbon }
        }
        let names: [UInt16: String] = [36:"Return",48:"Tab",49:"Space",51:"Delete",53:"Escape",76:"Enter",117:"Forward Delete",123:"←",124:"→",125:"↓",126:"↑",122:"F1",120:"F2",99:"F3",118:"F4",96:"F5",97:"F6",98:"F7",100:"F8",101:"F9",109:"F10",103:"F11",111:"F12",105:"F13",107:"F14",113:"F15",106:"F16",64:"F17",79:"F18",80:"F19",90:"F20"]
        guard let label = names[event.keyCode] ?? event.charactersIgnoringModifiers?.uppercased(), !label.isEmpty else { return nil }
        // Command-C always exports the current screenshot.
        guard !(modifiers == UInt32(cmdKey) && label == "C") else { return nil }
        return CaptureShortcut(keyCode: UInt32(event.keyCode), modifiers: modifiers, keyLabel: label)
    }
    static func load(_ defaults: UserDefaults) -> CaptureShortcut {
        guard let data = defaults.data(forKey: "captureShortcut"), let saved = try? JSONDecoder().decode(Self.self, from: data) else { return .standard }
        return saved
    }
    func save(_ defaults: UserDefaults) { defaults.set(try? JSONEncoder().encode(self), forKey: "captureShortcut") }
}

final class ShortcutRecorder: NSButton {
    var recording = false
    var recorded: ((CaptureShortcut?) -> Void)?
    override var acceptsFirstResponder: Bool { true }
    @objc func beginRecording() {
        recording = true; title = "Press a shortcut…"; window?.makeFirstResponder(self)
    }
    override func mouseDown(with event: NSEvent) { beginRecording() }
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if recording { record(event); return true }
        return super.performKeyEquivalent(with: event)
    }
    override func keyDown(with event: NSEvent) {
        if recording { record(event) } else { super.keyDown(with: event) }
    }
    func record(_ event: NSEvent) {
        if event.keyCode == 53 && event.modifierFlags.intersection([.command,.control,.option,.shift]).isEmpty {
            recording = false; recorded?(nil); return
        }
        guard let shortcut = CaptureShortcut.from(event) else {
            title = "Include ⌘, ⌃, or ⌥ (⌘C is reserved)"; return
        }
        recording = false; recorded?(shortcut)
    }
}

final class ScreenshotWindow: NSWindow {
    var copyScreenshot: (() -> Void)?
    static func isCopyShortcut(_ event: NSEvent) -> Bool {
        event.modifierFlags.intersection([.command,.control,.option,.shift]) == .command && event.charactersIgnoringModifiers?.lowercased() == "c"
    }
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if Self.isCopyShortcut(event), let copyScreenshot { copyScreenshot(); return true }
        return super.performKeyEquivalent(with: event)
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    var status: NSStatusItem!
    var editors: [EditorController] = []
    var hotKey: EventHotKeyRef?
    var capturing = false
    var frozenCapture: FrozenCaptureController?
    var captureShortcut = CaptureShortcut.load(.standard)
    var captureMenuItem: NSMenuItem!
    var settingsController: ShortcutSettingsController?
    var welcomeController: WelcomeController?
    var closesAfterCopy = CopyPreference.closesEditor(in: .standard)
    var captureStore: CaptureStore?
    var historyController: CaptureHistoryController?
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        status.button?.image = NSImage(systemSymbolName: "camera.viewfinder", accessibilityDescription: "Shotlight")
        let menu = NSMenu()
        captureMenuItem = menu.addItem(withTitle: "Capture Area  \(captureShortcut.label)", action: #selector(capture), keyEquivalent: "")
        captureMenuItem.target = self
        menu.addItem(withTitle: "Recent Captures…",action: #selector(showHistory),keyEquivalent: "").target = self
        menu.addItem(withTitle: "Settings…", action: #selector(showSettings), keyEquivalent: ",").target = self
        menu.addItem(withTitle: "About Shotlight", action: #selector(about), keyEquivalent: "").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit Shotlight", action: #selector(quit), keyEquivalent: "q").target = self
        status.menu = menu
        do {
            let root = ProcessInfo.processInfo.environment["SHOTLIGHT_HISTORY_DIR"].map { URL(fileURLWithPath: $0,isDirectory: true) } ?? FileManager.default.urls(for: .applicationSupportDirectory,in: .userDomainMask)[0].appendingPathComponent("Shotlight/Captures",isDirectory: true)
            let savedLimit = UserDefaults.standard.integer(forKey: "historyLimit")
            captureStore = try CaptureStore(directory: root,limit: savedLimit == 0 ? 50 : savedLimit)
            captureStore?.onChange = { [weak self] in self?.historyController?.reload(); self?.editors.forEach { $0.updateNavigation() } }
        } catch { alert("Recent history unavailable",error.localizedDescription) }
        var type = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, context in
            guard let context else { return OSStatus(eventNotHandledErr) }
            let delegate = Unmanaged<AppDelegate>.fromOpaque(context).takeUnretainedValue()
            DispatchQueue.main.async { delegate.capture() }
            return noErr
        }, 1, &type, Unmanaged.passUnretained(self).toOpaque(), nil)
        if let error = registerShortcut(captureShortcut) { alert("Shortcut unavailable", error) }
        if ProcessInfo.processInfo.environment["SHOTLIGHT_CHECK"] == "1" {
            let image = NSImage(size: NSSize(width: 900,height: 500))
            image.lockFocus(); NSColor.white.setFill(); NSRect(x: 0,y: 0,width: 900,height: 500).fill()
            ("Screenshot annotation" as NSString).draw(at: NSPoint(x: 60,y: 380), withAttributes: [.font: NSFont.systemFont(ofSize: 32), .foregroundColor: NSColor.black])
            image.unlockFocus()
            let editor = openCaptured(image: image,originalPNG: nil)
            _ = openCaptured(image: image,originalPNG: nil)
            editor.canvas.marks = [Mark(tool: .arrow,points: [NSPoint(x: 100,y: 200),NSPoint(x: 350,y: 340)],color: .systemRed,width: 4),Mark(tool: .rectangle,points: [NSPoint(x: 50,y: 360),NSPoint(x: 470,y: 430)],color: .systemBlue,width: 3)]
            editor.canvas.history = [[]]; editor.updateUndo(); _ = editor.flushArchive(); editor.showWindow(nil); editor.window?.title = exportCheck() + " " + keyboardCheck() + " " + historyCheck(); NSApp.activate(ignoringOtherApps: true)
        } else if !UserDefaults.standard.bool(forKey: "hasSeenWelcome") {
            UserDefaults.standard.set(true, forKey: "hasSeenWelcome")
            welcomeController = WelcomeController(shortcut: captureShortcut) { [weak self] in self?.capture() }
            welcomeController?.showWindow(nil); NSApp.activate(ignoringOtherApps: true)
        }
    }
    @objc func about() {
        alert("Shotlight", "Capture from the camera icon in your menu bar, or press \(captureShortcut.label). Change it in Settings. The desktop freezes immediately; drag to select an area on the frozen image. Escape cancels. Then annotate, copy, or save. Command–C copies the screenshot. Settings controls whether copying closes the editor (on by default). Captures and annotations are retained automatically; reopen them from Recent Captures.\n\nIf capture is denied, allow Shotlight in System Settings → Privacy & Security → Screen & System Audio Recording, then reopen the app.")
    }
    func alert(_ title: String, _ message: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert(); alert.messageText = title; alert.informativeText = message; alert.runModal()
    }
    func registerShortcut(_ shortcut: CaptureShortcut) -> String? {
        if shortcut == captureShortcut && hotKey != nil { return nil }
        var candidate: EventHotKeyRef?
        let result = RegisterEventHotKey(shortcut.keyCode, shortcut.modifiers, EventHotKeyID(signature: 0x53484F54,id: 1),GetApplicationEventTarget(),0,&candidate)
        guard result == noErr else { return "That shortcut could not be registered. It may be used by another app. Your previous shortcut is unchanged; try a different combination." }
        if let hotKey { UnregisterEventHotKey(hotKey) }
        hotKey = candidate; captureShortcut = shortcut; captureMenuItem?.title = "Capture Area  \(shortcut.label)"
        return nil
    }
    @objc func showSettings() {
        if let settingsController, settingsController.window?.isVisible == true { settingsController.window?.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); return }
        settingsController = ShortcutSettingsController(shortcut: captureShortcut,historyLimit: captureStore?.limit ?? 50,closesAfterCopy: closesAfterCopy,captureCount: { [weak self] in self?.captureStore?.records.count ?? 0 },clearHistory: { [weak self] in self?.clearHistory() }) { [weak self] shortcut,limit,closes in
            guard let self else { return .general("Shotlight is unavailable.") }
            guard self.flushEditors() else { return .general("A screenshot could not be retained. Try saving it before changing settings.") }
            let oldShortcut = self.captureShortcut
            if let error = self.registerShortcut(shortcut) { return .shortcut(error) }
            do { try self.captureStore?.setLimit(limit) }
            catch { _ = self.registerShortcut(oldShortcut); return .history(error.localizedDescription) }
            shortcut.save(.standard); UserDefaults.standard.set(limit,forKey: "historyLimit")
            self.closesAfterCopy = closes; CopyPreference.save(closes, in: .standard)
            self.editors.forEach { $0.closesAfterCopy = closes }; return nil
        }
        settingsController?.showWindow(nil); NSApp.activate(ignoringOtherApps: true)
    }
    @objc func showHistory() {
        guard let store = captureStore else { alert("Recent history unavailable","Reopen Shotlight to try loading history again."); return }
        if historyController == nil { historyController = CaptureHistoryController(store: store) { [weak self] id in self?.openHistory(id) } }
        historyController?.reload(); historyController?.showWindow(nil); NSApp.activate(ignoringOtherApps: true)
    }
    func configureEditor(_ editor: EditorController) {
        editor.closesAfterCopy = closesAfterCopy; editors.append(editor)
        editor.onClose = { [weak self,weak editor] in self?.editors.removeAll { $0 === editor } }
        editor.navigate = { [weak self,weak editor] direction in
            guard let self,let editor,let id = editor.captureID,let store = self.captureStore,
                  let index = store.records.firstIndex(where: { $0.id == id }),store.records.indices.contains(index+direction), editor.flushArchive() else { return }
            let target = store.records[index+direction]
            if let existing = self.editors.first(where: { $0 !== editor && $0.captureID == target.id }) { existing.showWindow(nil); return }
            do { try editor.loadCapture(target.id,store: store) }
            catch { self.alert("Could not reopen screenshot",error.localizedDescription) }
        }
    }
    @discardableResult func openCaptured(image: NSImage,originalPNG: Data?) -> EditorController {
        let editor: EditorController
        do {
            guard let store = captureStore else { throw CaptureHistoryError.missingCapture }
            let record = try store.add(image: image,originalPNG: originalPNG)
            editor = EditorController(image: image,captureID: record.id,store: store)
        } catch {
            editor = EditorController(image: image)
            alert("Screenshot not retained", "Save or copy this screenshot before closing its editor. " + error.localizedDescription)
        }
        configureEditor(editor); editor.showWindow(nil); NSApp.activate(ignoringOtherApps: true); return editor
    }
    func openHistory(_ id: UUID) {
        if let existing = editors.first(where: { $0.captureID == id }) { existing.showWindow(nil); NSApp.activate(ignoringOtherApps: true); return }
        guard let store = captureStore else { return }
        do {
            let editor = EditorController(image: try store.image(id),captureID: id,store: store)
            editor.canvas.marks = try store.marks(id); editor.canvas.history = try store.undoHistory(id); editor.canvas.undone = try store.redoHistory(id); editor.updateUndo(); configureEditor(editor); editor.showWindow(nil); NSApp.activate(ignoringOtherApps: true)
        } catch { alert("Could not reopen screenshot",error.localizedDescription) }
    }
    func flushEditors() -> Bool {
        for editor in editors { editor.canvas.finishTextEditing(); if !editor.flushArchive() { return false } }
        return true
    }
    @objc func clearHistory() {
        guard let store = captureStore else { return }
        let alert = NSAlert(); alert.messageText = "Clear screenshot history?"; alert.informativeText = "Retained drafts will move to Trash, and open screenshot editors will close."; alert.addButton(withTitle: "Cancel"); alert.addButton(withTitle: "Clear History")
        guard alert.runModal() == .alertSecondButtonReturn,flushEditors() else { return }
        do {
            try store.clear()
            for editor in Array(editors) { editor.detachArchive(); editor.window?.isDocumentEdited = false; editor.window?.close() }
        } catch { self.alert("Could not clear history",error.localizedDescription) }
    }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply { flushEditors() ? .terminateNow : .terminateCancel }
    func applicationWillTerminate(_ notification: Notification) { if let hotKey { UnregisterEventHotKey(hotKey) } }
    @objc func quit() { NSApp.terminate(nil) }
    @objc func capture() {
        guard !capturing else { return }
        capturing = true
        DesktopSnapshot.capture { [weak self] result in
            guard let self else { return }
            switch result {
            case .failure(let error):
                self.capturing = false; self.alert("Capture failed", error.localizedDescription)
            case .success(let snapshots):
                let controller = FrozenCaptureController(snapshots: snapshots) { [weak self] image, png in
                    guard let self else { return }
                    self.frozenCapture = nil; self.capturing = false
                    if let image { self.openCaptured(image: image, originalPNG: png) }
                }
                self.frozenCapture = controller; controller.show()
            }
        }
    }
}

enum Tool: Int { case pen, arrow, rectangle, text }
struct Mark: Equatable {
    var tool: Tool
    var points: [NSPoint]
    var color: NSColor
    var width: CGFloat
    var text: String = ""
}
final class InlineTextView: NSTextView {
    override func keyDown(with event: NSEvent) {
        if (event.keyCode == 36 || event.keyCode == 76) && event.modifierFlags.contains(.shift) {
            insertText("\n", replacementRange: selectedRange())
            return
        }
        super.keyDown(with: event)
    }
}
final class Canvas: NSView, NSTextViewDelegate {
    let image: NSImage
    var marks: [Mark] = []
    var undone: [[Mark]] = []
    var history: [[Mark]] = []
    var textEditor: NSTextView?
    var editingIndex: Int?
    var editingTop = NSPoint.zero
    var editingColor = NSColor.systemRed
    var editingWidth: CGFloat = 4
    var current: Mark?
    var tool: Tool = .arrow
    var color = NSColor.systemRed { didSet { if textEditor != nil { editingColor = color; refreshTextEditor() } } }
    var strokeWidth: CGFloat = 4
    var fontSize: CGFloat = 24 { didSet { if textEditor != nil { editingWidth = fontSize/6; refreshTextEditor() } } }
    var changed: (() -> Void)?
    init(image: NSImage) { self.image = image; super.init(frame: NSRect(origin: .zero, size: image.size)); wantsLayer = true }
    required init?(coder: NSCoder) { fatalError() }
    override var acceptsFirstResponder: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        image.draw(in: bounds)
        for (index, mark) in marks.enumerated() where index != editingIndex { draw(mark) }
        if let current { draw(current) }
    }
    func draw(_ mark: Mark) {
        guard let a = mark.points.first, let b = mark.points.last else { return }
        mark.color.setStroke(); mark.color.setFill()
        if mark.tool == .text {
            (mark.text as NSString).draw(in: NSRect(origin: a, size: textSize(mark.text, width: mark.width)), withAttributes: textAttributes(color: mark.color, width: mark.width))
            return
        }
        let path = NSBezierPath(); path.lineWidth = mark.width; path.lineCapStyle = .round; path.lineJoinStyle = .round
        switch mark.tool {
        case .pen:
            path.move(to: a)
            if mark.points.count == 1 { path.line(to: NSPoint(x: a.x + 0.1, y: a.y)) }
            for p in mark.points.dropFirst() { path.line(to: p) }
        case .rectangle: path.appendRect(NSRect(x: min(a.x,b.x), y: min(a.y,b.y), width: abs(b.x-a.x), height: abs(b.y-a.y)))
        case .arrow:
            path.move(to: a); path.line(to: b)
            let angle = atan2(b.y-a.y,b.x-a.x), length = max(12,mark.width * 4)
            for offset in [-CGFloat.pi/6, CGFloat.pi/6] {
                path.move(to: b); path.line(to: NSPoint(x: b.x-length*cos(angle+offset), y: b.y-length*sin(angle+offset)))
            }
        case .text: break
        }
        path.stroke()
    }
    func point(_ event: NSEvent) -> NSPoint {
        let p = convert(event.locationInWindow, from: nil)
        return NSPoint(x: max(0,min(bounds.width,p.x)), y: max(0,min(bounds.height,p.y)))
    }
    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        let p = point(event)
        if tool == .text {
            finishTextEditing()
            let index = marks.indices.reversed().first { index in
                let mark = marks[index]
                return mark.tool == .text && NSRect(origin: mark.points[0], size: textSize(mark.text, width: mark.width)).insetBy(dx: -5, dy: -5).contains(p)
            }
            beginTextEditing(at: p, index: index)
        } else { current = Mark(tool: tool, points: [p], color: color, width: strokeWidth); needsDisplay = true }
    }
    override func mouseDragged(with event: NSEvent) {
        guard current != nil else { return }
        if tool == .pen { current!.points.append(point(event)) }
        else { current!.points = [current!.points[0], point(event)] }
        needsDisplay = true
    }
    override func mouseUp(with event: NSEvent) {
        guard let current else { return }; recordChange(); marks.append(current); self.current = nil; undone.removeAll(); needsDisplay = true; changed?()
    }
    func textAttributes(color: NSColor, width: CGFloat) -> [NSAttributedString.Key: Any] {
        [.font: NSFont.systemFont(ofSize: max(16, width * 6), weight: .semibold), .foregroundColor: color]
    }
    func textSize(_ text: String, width: CGFloat) -> NSSize {
        let size = ((text.isEmpty ? " " : text) as NSString).size(withAttributes: textAttributes(color: .black, width: width))
        return NSSize(width: ceil(size.width) + 2, height: ceil(size.height) + 2)
    }
    func recordChange() { history.append(marks); undone.removeAll() }
    func beginTextEditing(at point: NSPoint, index: Int? = nil) {
        finishTextEditing()
        editingIndex = index
        let mark = index.map { marks[$0] }
        editingColor = mark?.color ?? color; editingWidth = mark?.width ?? fontSize/6
        if let mark { fontSize = max(16, mark.width*6) }
        if let mark { editingTop = NSPoint(x: mark.points[0].x, y: mark.points[0].y + textSize(mark.text, width: mark.width).height) }
        else { editingTop = point }
        let editor = InlineTextView(frame: .zero)
        editor.isRichText = false; editor.drawsBackground = false
        editor.isAutomaticQuoteSubstitutionEnabled = false; editor.isAutomaticDashSubstitutionEnabled = false
        editor.isAutomaticTextReplacementEnabled = false; editor.isAutomaticSpellingCorrectionEnabled = false
        editor.textContainerInset = .zero; editor.textContainer?.lineFragmentPadding = 0
        editor.textContainer?.widthTracksTextView = false; editor.textContainer?.containerSize = NSSize(width: 100000, height: 100000)
        editor.isHorizontallyResizable = false; editor.isVerticallyResizable = false
        editor.delegate = self; editor.string = mark?.text ?? ""
        editor.setAccessibilityLabel("Screenshot text annotation")
        textEditor = editor; addSubview(editor); refreshTextEditor()
        window?.makeFirstResponder(editor); editor.setSelectedRange(NSRange(location: editor.string.utf16.count, length: 0))
        needsDisplay = true; changed?()
    }
    func refreshTextEditor() {
        guard let editor = textEditor else { return }
        editor.font = NSFont.systemFont(ofSize: max(16, editingWidth * 6), weight: .semibold)
        editor.textColor = editingColor; editor.insertionPointColor = editingColor
        editor.typingAttributes = textAttributes(color: editingColor, width: editingWidth)
        let size = textSize(editor.string, width: editingWidth)
        editor.frame = NSRect(x: editingTop.x, y: editingTop.y - size.height, width: max(40, size.width + 8), height: size.height)
        changed?()
    }
    func textDidChange(_ notification: Notification) { refreshTextEditor() }
    func textView(_ textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        if commandSelector == #selector(NSResponder.cancelOperation(_:)) { finishTextEditing(cancel: true); return true }
        if commandSelector == #selector(NSResponder.insertNewline(_:)) {
            if NSApp.currentEvent?.modifierFlags.contains(.shift) == true { return false }
            finishTextEditing(); return true
        }
        return false
    }
    func finishTextEditing(cancel: Bool = false) {
        guard let editor = textEditor else { return }
        let text = editor.string
        if !cancel {
            if let index = editingIndex {
                if text.isEmpty { recordChange(); marks.remove(at: index) }
                else if text != marks[index].text || editingColor != marks[index].color || editingWidth != marks[index].width {
                    recordChange()
                    marks[index] = Mark(tool: .text, points: [NSPoint(x: editingTop.x, y: editingTop.y - textSize(text, width: editingWidth).height)], color: editingColor, width: editingWidth, text: text)
                }
            } else if !text.isEmpty {
                recordChange()
                marks.append(Mark(tool: .text, points: [NSPoint(x: editingTop.x, y: editingTop.y - textSize(text, width: editingWidth).height)], color: editingColor, width: editingWidth, text: text))
            }
        }
        textEditor = nil; editingIndex = nil; editor.removeFromSuperview()
        window?.makeFirstResponder(self); needsDisplay = true; changed?()
    }
    func undoMark() {
        finishTextEditing()
        if let previous = history.popLast() { undone.append(marks); marks = previous; needsDisplay = true; changed?() }
    }
    func redoMark() {
        finishTextEditing()
        if let next = undone.popLast() { history.append(marks); marks = next; needsDisplay = true; changed?() }
    }
    func draftMarks() -> [Mark] {
        var snapshot = marks
        if let editor = textEditor {
            if !editor.string.isEmpty {
                let draft = Mark(tool: .text,points: [NSPoint(x: editingTop.x,y: editingTop.y-textSize(editor.string,width: editingWidth).height)],color: editingColor,width: editingWidth,text: editor.string)
                if let index = editingIndex { snapshot[index] = draft } else { snapshot.append(draft) }
            } else if let index = editingIndex { snapshot.remove(at: index) }
        }
        return snapshot
    }
    func draftUndoHistory() -> [[Mark]] { draftMarks() == marks ? history : history + [marks] }
    func draftRedoHistory() -> [[Mark]] { draftMarks() == marks ? undone : [] }
    func png() -> Data? {
        finishTextEditing()
        // Preserve capture pixel resolution, independent of the editor's screen scale.
        guard let rep = image.representations.max(by: { $0.pixelsWide < $1.pixelsWide }),
              let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: rep.pixelsWide, pixelsHigh: rep.pixelsHigh, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
              let context = NSGraphicsContext(bitmapImageRep: bitmap) else { return nil }
        bitmap.size = bounds.size
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = context
        context.cgContext.scaleBy(x: CGFloat(rep.pixelsWide)/bounds.width, y: CGFloat(rep.pixelsHigh)/bounds.height)
        image.draw(in: bounds); for mark in marks { draw(mark) }
        NSGraphicsContext.restoreGraphicsState()
        return bitmap.representation(using: .png, properties: [:])
    }
}

final class EditorController: NSWindowController, NSWindowDelegate {
    var canvas: Canvas
    var captureID: UUID?
    var archive: CaptureStore?
    var pendingSave: DispatchWorkItem?
    var lastArchiveError: String?
    var navigate: ((Int) -> Void)?
    let scroll = PreviewScrollView()
    var previousButton: NSMenuItem!
    var nextButton: NSMenuItem!
    var compactTools: [NSButton] = []
    let moreMenu = NSMenu()
    let widthMenu = NSMenu()
    let captureLabel = NSTextField(labelWithString: "")
    var onClose: (() -> Void)?
    var undoButton: NSButton!
    var redoButton: NSButton!
    var copyButton: CompactButton!
    var sizeButton: CompactButton!
    let zoomControls = NSSegmentedControl(labels: ["Fit", "100%"], trackingMode: .selectOne, target: nil, action: nil)
    let zoomLabel = NSTextField(labelWithString: "100%")
    var fittingPreview = true
    var closesAfterCopy = true { didSet { updateCopyAction() } }
    let copyPasteboard: NSPasteboard
    init(image: NSImage,captureID: UUID? = nil,store: CaptureStore? = nil,pasteboard: NSPasteboard = .general) {
        canvas = Canvas(image: image); self.captureID = captureID; archive = store; copyPasteboard = pasteboard
        let screen = NSScreen.main?.visibleFrame.size ?? NSSize(width: 1200, height: 800)
        let size = NSSize(width: min(max(image.size.width+56,760),screen.width-80), height: min(max(image.size.height+120,360),screen.height-80))
        let window = ScreenshotWindow(contentRect: NSRect(origin: .zero,size: size), styleMask: [.titled,.closable,.miniaturizable,.resizable], backing: .buffered, defer: false)
        super.init(window: window)
        window.copyScreenshot = { [weak self] in self?.copyImage() }
        window.title = "Shotlight — Annotate Screenshot"; window.delegate = self; window.center(); window.isReleasedWhenClosed = false
        let root = BackgroundView(); window.contentView = root
        let clip = CenteredClipView(); scroll.contentView = clip
        scroll.hasHorizontalScroller = true; scroll.hasVerticalScroller = true; scroll.scrollerStyle = .overlay
        scroll.documentView = canvas; scroll.drawsBackground = true; scroll.backgroundColor = .windowBackgroundColor
        scroll.allowsMagnification = true; scroll.minMagnification = 0.01; scroll.maxMagnification = 2
        scroll.manualZoom = { [weak self] in self?.fittingPreview = false }
        scroll.zoomChanged = { [weak self] in self?.updateZoomControls() }
        scroll.automaticallyAdjustsContentInsets = false
        scroll.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(scroll)

        let tools = NSStackView(); tools.spacing = 2
        for (index,symbol) in ["pencil","arrow.up.right","rectangle","textformat"].enumerated() {
            let button = ShotlightUI.icon(symbol,label: ["Pen","Arrow","Rectangle","Text — click to type or edit"][index],target: self,action: #selector(selectCompactTool(_:)))
            button.tag = index; button.setButtonType(.pushOnPushOff); button.state = index == 1 ? .on : .off
            compactTools.append(button); tools.addArrangedSubview(button)
        }
        let color = NSColorWell(); color.color = .systemRed; color.colorWellStyle = .minimal; color.isBordered = false
        color.target = self; color.action = #selector(selectColor(_:)); color.toolTip = "Annotation color"; color.setAccessibilityLabel("Annotation color")
        color.translatesAutoresizingMaskIntoConstraints = false; NSLayoutConstraint.activate([color.widthAnchor.constraint(equalToConstant: 32),color.heightAnchor.constraint(equalToConstant: 32)])
        sizeButton = ShotlightUI.textButton("4 px", symbol: "lineweight", width: 76, target: self, action: #selector(showWidthMenu(_:)))
        widthMenu.autoenablesItems = false
        undoButton = ShotlightUI.icon("arrow.uturn.backward",label: "Undo (⌘Z)",target: self,action: #selector(undo)); undoButton.keyEquivalent = "z"
        redoButton = ShotlightUI.icon("arrow.uturn.forward",label: "Redo (⇧⌘Z)",target: self,action: #selector(redo)); redoButton.keyEquivalent = "z"; redoButton.keyEquivalentModifierMask = [.command,.shift]
        copyButton = ShotlightUI.textButton("Copy & Close", symbol: "doc.on.doc", width: 128, target: self, action: #selector(copyImage)); copyButton.primary = true
        let save = ShotlightUI.icon("square.and.arrow.down",label: "Save PNG (⌘S)",target: self,action: #selector(saveImage)); save.keyEquivalent = "s"
        let capture = ShotlightUI.icon("viewfinder",label: "New capture",target: self,action: #selector(newCapture))
        let more = ShotlightUI.icon("ellipsis",label: "More — history, zoom & settings",target: self,action: #selector(showMoreMenu(_:)))
        moreMenu.autoenablesItems = false
        let info = NSMenuItem(title: "Screenshot",action: nil,keyEquivalent: ""); info.isEnabled = false; moreMenu.addItem(info)
        for (title,symbol,action) in [("Recent captures…","clock.arrow.circlepath",#selector(recentCaptures)),("Previous capture","chevron.left",#selector(previousCapture)),("Next capture","chevron.right",#selector(nextCapture))] {
            let item = NSMenuItem(title: title,action: action,keyEquivalent: ""); item.target = self; item.image = NSImage(systemSymbolName: symbol,accessibilityDescription: title); moreMenu.addItem(item)
            if action == #selector(previousCapture) { previousButton = item }; if action == #selector(nextCapture) { nextButton = item }
        }
        moreMenu.addItem(.separator())
        let zoom = NSMenuItem(title: "Zoom",action: nil,keyEquivalent: ""); let zoomMenu = NSMenu(); zoom.submenu = zoomMenu
        let fit = NSMenuItem(title: "Fit to Window", action: #selector(fitToWindow), keyEquivalent: ""); fit.target = self; fit.tag = 0; zoomMenu.addItem(fit)
        for percent in [25,50,75,100,150,200] { let item = NSMenuItem(title: "\(percent)%",action: #selector(selectZoom(_:)),keyEquivalent: ""); item.tag = percent; item.target = self; item.state = percent == 100 ? .on : .off; zoomMenu.addItem(item) }
        moreMenu.addItem(zoom)
        let settings = NSMenuItem(title: "Settings…",action: #selector(openSettings),keyEquivalent: ""); settings.target = self; moreMenu.addItem(settings)
        let toolbar = NSStackView(views: [capture,ShotlightUI.divider(),tools,ShotlightUI.divider(),color,sizeButton!,ShotlightUI.divider(),undoButton,redoButton,ShotlightUI.divider(),copyButton!,save,more]); toolbar.spacing = 5; toolbar.alignment = .centerY
        let surface = ShotlightUI.card(toolbar,padding: 7); surface.wantsLayer = true; surface.layer?.shadowOpacity = 0.14; surface.layer?.shadowRadius = 14; surface.layer?.shadowOffset = CGSize(width: 0,height: -3)
        surface.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(surface)
        NSLayoutConstraint.activate([surface.centerXAnchor.constraint(equalTo: root.centerXAnchor),surface.bottomAnchor.constraint(equalTo: root.bottomAnchor,constant: -12)])
        captureLabel.font = .systemFont(ofSize: 12); captureLabel.textColor = .secondaryLabelColor
        captureLabel.lineBreakMode = .byTruncatingTail
        captureLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        zoomControls.target = self; zoomControls.action = #selector(selectPreviewScale(_:)); zoomControls.setAccessibilityLabel("Preview scale")
        zoomLabel.font = .monospacedDigitSystemFont(ofSize: 12, weight: .regular); zoomLabel.textColor = .secondaryLabelColor
        zoomLabel.widthAnchor.constraint(equalToConstant: 48).isActive = true
        let header = NSStackView(views: [captureLabel, NSView(), zoomControls, zoomLabel]); header.spacing = 12
        header.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(header)
        NSLayoutConstraint.activate([
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24), header.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -24), header.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor), scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor), scroll.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 12), scroll.bottomAnchor.constraint(equalTo: surface.topAnchor, constant: -12)
        ])
        window.contentMinSize = NSSize(width: 680,height: 300)
        connectCanvas(); updateUndo(); updateNavigation(); updateSizeControl(); updateCopyAction()
        root.layoutSubtreeIfNeeded(); fitToWindow()
    }
    required init?(coder: NSCoder) { fatalError() }
    func button(_ name: String, _ action: Selector) -> NSButton { let b = NSButton(title: name,target: self,action: action); b.bezelStyle = .rounded; b.keyEquivalentModifierMask = .command; return b }
    func connectCanvas() {
        canvas.changed = { [weak self] in self?.updateUndo(); self?.updateSizeControl(); self?.scheduleArchiveSave() }
    }
    func scheduleArchiveSave() {
        pendingSave?.cancel()
        guard let id = captureID,archive?.contains(id) == true else { return }
        captureLabel.stringValue = "Saving to Recent Captures…"
        let task = DispatchWorkItem { [weak self] in
            guard let self,let id = self.captureID,let archive = self.archive,archive.contains(id) else { return }
            do { try archive.save(id,marks: self.canvas.draftMarks(),undoHistory: self.canvas.draftUndoHistory(),redoHistory: self.canvas.draftRedoHistory()); self.lastArchiveError = nil; self.updateNavigation() }
            catch { self.lastArchiveError = error.localizedDescription; self.captureLabel.stringValue = "Draft not retained — save or copy"; self.captureLabel.isHidden = false }
        }
        pendingSave = task; DispatchQueue.main.asyncAfter(deadline: .now()+0.25,execute: task)
    }
    @discardableResult func flushArchive() -> Bool {
        pendingSave?.cancel(); pendingSave = nil
        guard let id = captureID,let archive,archive.contains(id) else { return true }
        do { try archive.save(id,marks: canvas.draftMarks(),undoHistory: canvas.draftUndoHistory(),redoHistory: canvas.draftRedoHistory()); lastArchiveError = nil; updateNavigation(); return true }
        catch {
            lastArchiveError = error.localizedDescription
            let alert = NSAlert(); alert.messageText = "Could not retain this screenshot"; alert.informativeText = error.localizedDescription; alert.runModal(); return false
        }
    }
    func detachArchive() { pendingSave?.cancel(); pendingSave = nil; archive = nil; captureID = nil; updateNavigation() }
    func updateNavigation() {
        guard let archive,let id = captureID,let index = archive.records.firstIndex(where: { $0.id == id }) else {
            previousButton?.isEnabled = false; nextButton?.isEnabled = false; captureLabel.stringValue = "Outside recent history — save or copy"; captureLabel.isHidden = false; return
        }
        previousButton?.isEnabled = index+1 < archive.records.count; nextButton?.isEnabled = index > 0
        captureLabel.stringValue = lastArchiveError == nil ? "Saved to Recent Captures" : "Draft not retained — save or copy"
        captureLabel.toolTip = lastArchiveError ?? archive.records[index].displayDate; captureLabel.isHidden = false
    }
    func loadCapture(_ id: UUID,store: CaptureStore) throws {
        let image = try store.image(id),marks = try store.marks(id),history = try store.undoHistory(id),undone = try store.redoHistory(id)
        canvas.finishTextEditing(); guard flushArchive() else { return }
        pendingSave?.cancel(); canvas.changed = nil
        let tool = canvas.tool,color = canvas.color,width = canvas.strokeWidth,fontSize = canvas.fontSize
        canvas = Canvas(image: image); canvas.marks = marks; canvas.history = history; canvas.undone = undone; canvas.tool = tool; canvas.color = color; canvas.strokeWidth = width; canvas.fontSize = fontSize
        captureID = id; archive = store; scroll.documentView = canvas; connectCanvas(); updateUndo(); updateNavigation(); updateSizeControl(); fitToWindow()
        window?.title = "Shotlight — Annotate Screenshot"; window?.makeFirstResponder(canvas)
    }
    @objc func previousCapture() { navigate?(1) }
    @objc func nextCapture() { navigate?(-1) }
    @objc func recentCaptures() { (NSApp.delegate as? AppDelegate)?.showHistory() }
    func updateUndo() { undoButton.isEnabled = !canvas.history.isEmpty || canvas.textEditor != nil; redoButton.isEnabled = !canvas.undone.isEmpty; window?.isDocumentEdited = !canvas.marks.isEmpty }
    @objc func selectCompactTool(_ sender: NSButton) {
        canvas.finishTextEditing(); canvas.tool = Tool(rawValue: sender.tag) ?? .arrow
        for button in compactTools { button.state = button === sender ? .on : .off }; updateSizeControl()
    }
    @objc func selectColor(_ sender: NSColorWell) { canvas.color = sender.color }
    func updateSizeControl() {
        let text = canvas.tool == .text
        let selected = text ? max(16, canvas.textEditor == nil ? canvas.fontSize : canvas.editingWidth*6) : canvas.strokeWidth
        let unit = text ? "pt" : "px"
        sizeButton.title = "\(Int(selected.rounded())) \(unit)"
        sizeButton.image = NSImage(systemSymbolName: text ? "textformat.size" : "lineweight", accessibilityDescription: nil)
        sizeButton.toolTip = text ? "Text size" : "Stroke width"; sizeButton.setAccessibilityLabel("\(sizeButton.toolTip!): \(sizeButton.title)")
        widthMenu.removeAllItems()
        for value in text ? [16, 24, 48] : [2, 4, 8] {
            let item = NSMenuItem(title: "\(value) \(unit)", action: #selector(selectCompactWidth(_:)), keyEquivalent: "")
            item.target = self; item.tag = value; item.state = abs(selected-CGFloat(value)) < 0.01 ? .on : .off; widthMenu.addItem(item)
        }
    }
    @objc func showWidthMenu(_ sender: NSButton) { updateSizeControl(); widthMenu.popUp(positioning: nil,at: NSPoint(x: 0,y: sender.bounds.maxY+6),in: sender) }
    @objc func selectCompactWidth(_ sender: NSMenuItem) {
        if canvas.tool == .text { canvas.fontSize = CGFloat(sender.tag) }
        else { canvas.strokeWidth = CGFloat(sender.tag) }
        updateSizeControl()
    }
    @objc func showMoreMenu(_ sender: NSButton) {
        let rep = canvas.image.representations.first
        moreMenu.items.first?.title = "\(rep?.pixelsWide ?? Int(canvas.image.size.width)) × \(rep?.pixelsHigh ?? Int(canvas.image.size.height)) px"
        updateNavigation(); moreMenu.popUp(positioning: nil,at: NSPoint(x: 0,y: sender.bounds.maxY+6),in: sender)
    }
    @objc func selectZoom(_ sender: NSMenuItem) {
        fittingPreview = false
        scroll.setMagnification(CGFloat(sender.tag)/100, centeredAt: NSPoint(x: canvas.bounds.midX, y: canvas.bounds.midY)); updateZoomControls()
    }
    @objc func selectPreviewScale(_ sender: NSSegmentedControl) {
        if sender.selectedSegment == 0 { fitToWindow() }
        else {
            fittingPreview = false
            scroll.setMagnification(1, centeredAt: NSPoint(x: canvas.bounds.midX, y: canvas.bounds.midY)); updateZoomControls()
        }
    }
    @objc func fitToWindow() {
        fittingPreview = true; window?.contentView?.layoutSubtreeIfNeeded()
        let available = scroll.contentSize
        let factor = min(1, max(1, available.width-48)/canvas.bounds.width, max(1, available.height-48)/canvas.bounds.height)
        scroll.setMagnification(max(scroll.minMagnification, factor), centeredAt: NSPoint(x: canvas.bounds.midX, y: canvas.bounds.midY))
        updateZoomControls()
    }
    func updateZoomControls() {
        let percent = Int((scroll.magnification*100).rounded()); zoomLabel.stringValue = "\(percent)%"
        zoomControls.selectedSegment = fittingPreview ? 0 : abs(scroll.magnification-1) < 0.001 ? 1 : -1
        if let menu = moreMenu.items.first(where: { $0.title == "Zoom" })?.submenu {
            for item in menu.items { item.state = item.tag == 0 ? (fittingPreview ? .on : .off) : (!fittingPreview && abs(scroll.magnification-CGFloat(item.tag)/100) < 0.001 ? .on : .off) }
        }
    }
    func windowDidResize(_ notification: Notification) { if fittingPreview { fitToWindow() } }
    func updateCopyAction() {
        guard let copyButton else { return }
        copyButton.title = closesAfterCopy ? "Copy & Close" : "Copy"
        copyButton.attributedTitle = NSAttributedString(string: copyButton.title, attributes: [.font: NSFont.systemFont(ofSize: 13, weight: .medium), .foregroundColor: NSColor.white])
        copyButton.toolTip = "\(copyButton.title) (⌘C)"; copyButton.setAccessibilityLabel(copyButton.toolTip!)
    }
    @objc func openSettings() { (NSApp.delegate as? AppDelegate)?.showSettings() }
    @objc func newCapture() { (NSApp.delegate as? AppDelegate)?.capture() }
    @objc func undo() { canvas.undoMark() }
    @objc func redo() { canvas.redoMark() }
    func failure(_ message: String) { let alert = NSAlert(); alert.messageText = "Could not export screenshot"; alert.informativeText = message; alert.runModal() }
    @objc func copyImage() { _ = copy(to: copyPasteboard) }
    @discardableResult func copy(to pasteboard: NSPasteboard) -> Bool {
        guard let data = canvas.png() else { failure("The PNG could not be rendered."); return false }
        guard flushArchive() else { return false }
        pasteboard.clearContents()
        guard pasteboard.setData(data,forType: .png) else { failure("The clipboard could not be updated."); return false }
        window?.isDocumentEdited = false
        if closesAfterCopy { window?.close() }
        else { captureLabel.stringValue = "Copied · " + (captureID.flatMap { archive?.contains($0) } == true ? "Saved to Recent Captures" : "Keep this editor open to retain your draft") }
        return true
    }
    @objc func saveImage() {
        let panel = NSSavePanel(); panel.allowedContentTypes = [.png]; panel.nameFieldStringValue = "Screenshot-\(Int(Date().timeIntervalSince1970)).png"
        guard let window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let self, let url = panel.url else { return }
            guard let data = self.canvas.png() else { self.failure("The PNG could not be rendered."); return }
            guard self.flushArchive() else { return }
            do { try data.write(to: url,options: .atomic); window.isDocumentEdited = false; window.title = "Shotlight — \(url.lastPathComponent)" }
            catch { self.failure(error.localizedDescription) }
        }
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        canvas.finishTextEditing()
        guard flushArchive() else { return false }
        if let id = captureID,archive?.contains(id) == true { return true }
        guard sender.isDocumentEdited else { return true }
        let alert = NSAlert(); alert.messageText = "Close this screenshot?"; alert.informativeText = "Unsaved annotations will be discarded."; alert.addButton(withTitle: "Keep Editing"); alert.addButton(withTitle: "Discard")
        return alert.runModal() == .alertSecondButtonReturn
    }
    func windowWillClose(_ notification: Notification) { pendingSave?.cancel(); onClose?() }
}
func exportCheck() -> String {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 400, pixelsHigh: 200, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    bitmap.size = NSSize(width: 200, height: 100)
    let image = NSImage(size: bitmap.size); image.addRepresentation(bitmap)
    let canvas = Canvas(image: image)
    canvas.marks = [Mark(tool: .rectangle, points: [NSPoint(x: 20,y: 20),NSPoint(x: 80,y: 80)],color: .red,width: 4)]
    let png = canvas.png()!
    let output = NSBitmapImageRep(data: png)!
    print("dimensions",output.pixelsWide,output.pixelsHigh); fflush(stdout)
    guard output.pixelsWide == 400 && output.pixelsHigh == 200 else { return "FAIL: pixel dimensions" }
    let red = output.colorAt(x: 40,y: 100)!.usingColorSpace(.deviceRGB)!
    print("pixel",red); fflush(stdout)
    guard red.redComponent > 0.8 && red.alphaComponent > 0.8 else { return "FAIL: annotation scale" }
    canvas.history = [[]]
    canvas.undoMark(); guard canvas.marks.isEmpty && canvas.undone.count == 1 else { return "FAIL: undo" }
    canvas.redoMark(); guard canvas.marks.count == 1 && canvas.undone.isEmpty else { return "FAIL: redo" }
    canvas.marks = []; canvas.history = []; canvas.undone = []
    canvas.beginTextEditing(at: NSPoint(x: 20, y: 90))
    canvas.textEditor?.string = "First line"; canvas.textEditor?.setSelectedRange(NSRange(location: 10, length: 0))
    let newline = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: .shift, timestamp: 0, windowNumber: 0, context: nil, characters: "\r", charactersIgnoringModifiers: "\r", isARepeat: false, keyCode: 36)!
    canvas.textEditor?.keyDown(with: newline)
    guard canvas.textEditor?.string == "First line\n" else { return "FAIL: Shift-Return" }
    canvas.textEditor?.string = "First line\nSecond line"; canvas.refreshTextEditor(); canvas.finishTextEditing()
    guard canvas.marks.count == 1, canvas.marks[0].text.contains("\n") else { return "FAIL: inline multiline text" }
    let original = canvas.marks[0].text
    canvas.beginTextEditing(at: .zero, index: 0); canvas.textEditor?.string = "Edited"; canvas.finishTextEditing()
    canvas.undoMark(); guard canvas.marks[0].text == original else { return "FAIL: undo text edit" }
    canvas.redoMark(); guard canvas.marks[0].text == "Edited" else { return "FAIL: redo text edit" }
    canvas.beginTextEditing(at: .zero, index: 0); canvas.textEditor?.string = "Cancelled"; canvas.finishTextEditing(cancel: true)
    guard canvas.marks[0].text == "Edited" else { return "FAIL: cancel text edit" }
    canvas.beginTextEditing(at: .zero, index: 0); canvas.textEditor?.string = "Exported while typing"
    guard canvas.png() != nil, canvas.textEditor == nil, canvas.marks[0].text == "Exported while typing" else { return "FAIL: export active text" }
    return "PASS: Retina export, inline text, edit, cancel, undo and redo."
}

func keyboardCheck() -> String {
    func event(_ characters: String, flags: NSEvent.ModifierFlags, code: UInt16) -> NSEvent {
        NSEvent.keyEvent(with: .keyDown,location: .zero,modifierFlags: flags,timestamp: 0,windowNumber: 0,context: nil,characters: characters,charactersIgnoringModifiers: characters,isARepeat: false,keyCode: code)!
    }
    let copy = event("c",flags: .command,code: UInt16(kVK_ANSI_C))
    guard ScreenshotWindow.isCopyShortcut(copy), !ScreenshotWindow.isCopyShortcut(event("c",flags: [.command,.shift],code: UInt16(kVK_ANSI_C))) else { return "FAIL: copy key routing" }
    guard CaptureShortcut.from(copy) == nil, CaptureShortcut.from(event("s",flags: .shift,code: UInt16(kVK_ANSI_S))) == nil else { return "FAIL: reserved shortcuts" }
    guard let shortcut = CaptureShortcut.from(event("k",flags: [.command,.shift],code: UInt16(kVK_ANSI_K))), shortcut.label == "⇧⌘K" else { return "FAIL: shortcut recording" }
    let domain = "local.shotlight.test.\(UUID().uuidString)"
    let defaults = UserDefaults(suiteName: domain)!
    defer { defaults.removePersistentDomain(forName: domain) }
    shortcut.save(defaults)
    guard CaptureShortcut.load(defaults) == shortcut else { return "FAIL: shortcut persistence" }
    let image = NSImage(size: NSSize(width: 100,height: 100))
    image.lockFocus(); NSColor.white.setFill(); NSRect(x: 0,y: 0,width: 100,height: 100).fill(); image.unlockFocus()
    let editor = EditorController(image: image)
    editor.canvas.beginTextEditing(at: NSPoint(x: 10,y: 80)); editor.canvas.textEditor?.string = "Copy this draft"
    let pasteboard = NSPasteboard(name: NSPasteboard.Name("Shotlight-test-\(UUID().uuidString)"))
    // Exercise the exact window key-equivalent route with an isolated clipboard.
    var copied = false, closed = false
    editor.onClose = { closed = true }
    (editor.window as! ScreenshotWindow).copyScreenshot = {
        copied = editor.copy(to: pasteboard)
    }
    guard editor.window!.performKeyEquivalent(with: copy), copied, closed, editor.canvas.textEditor == nil, editor.canvas.marks.last?.text == "Copy this draft", pasteboard.data(forType: .png) != nil else { return "FAIL: copy and dismiss while typing" }
    pasteboard.releaseGlobally()
    return "PASS: shortcuts, persistence, Command-C dismiss."
}

let app = NSApplication.shared
if CommandLine.arguments.contains("--run-checks") {
    let result = exportCheck() + " " + keyboardCheck() + " " + historyCheck() + " " + frozenCaptureCheck() + " " + compactToolbarCheck() + " " + interfaceBehaviorCheck()
    print(result)
    exit(result.contains("FAIL:") ? 1 : 0)
}
if let flag = CommandLine.arguments.firstIndex(of: "--render-previews"), CommandLine.arguments.indices.contains(flag+1) {
    do { try renderInterfacePreviews(to: URL(fileURLWithPath: CommandLine.arguments[flag+1],isDirectory: true)); exit(0) }
    catch { print("Preview error: \(error)"); exit(1) }
}
let delegate = AppDelegate()
app.delegate = delegate
app.run()
