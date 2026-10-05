import AppKit

enum CopyPreference {
    static func closesEditor(in defaults: UserDefaults) -> Bool {
        defaults.object(forKey: "closeEditorAfterCopy") == nil || defaults.bool(forKey: "closeEditorAfterCopy")
    }
    static func save(_ closes: Bool, in defaults: UserDefaults) { defaults.set(closes, forKey: "closeEditorAfterCopy") }
}

enum SettingsFailure {
    case shortcut(String), history(String), general(String)
}

final class ShortcutSettingsController: NSWindowController, NSTextFieldDelegate {
    var draft: CaptureShortcut
    let recorder = ShortcutRecorder()
    let message = NSTextField(wrappingLabelWithString: "Click the shortcut, then press your preferred key combination. Include Command, Control, or Option.")
    let shortcutError = NSTextField(wrappingLabelWithString: "")
    let historyError = NSTextField(wrappingLabelWithString: "")
    let retentionWarning = NSTextField(wrappingLabelWithString: "")
    let applicationMessage = NSTextField(wrappingLabelWithString: "")
    let limitField = NSTextField(string: "50")
    let closeOnCopy = NSButton(checkboxWithTitle: "Close editor after copying", target: nil, action: nil)
    let scroll = NSScrollView()
    let settingsDocument = CaptureHistoryList()
    let stack = NSStackView()
    let clearHistory: () -> Void
    let captureCount: () -> Int
    let apply: (CaptureShortcut, Int, Bool) -> SettingsFailure?
    var confirmReduction: ((Int, Int) -> Bool)?

    init(shortcut: CaptureShortcut, historyLimit: Int, closesAfterCopy: Bool = true,
         captureCount: @escaping () -> Int = { 0 }, clearHistory: @escaping () -> Void,
         apply: @escaping (CaptureShortcut, Int, Bool) -> SettingsFailure?) {
        draft = shortcut; self.apply = apply; self.clearHistory = clearHistory; self.captureCount = captureCount
        limitField.stringValue = String(historyLimit)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 720), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window)
        window.contentView = BackgroundView(frame: window.contentView!.bounds)
        window.title = "Shotlight Settings"; window.isReleasedWhenClosed = false
        window.setContentSize(NSSize(width: 520, height: min(740, (NSScreen.main?.visibleFrame.height ?? 820)-80)))
        let root = window.contentView!
        recorder.title = draft.label; recorder.bezelStyle = .rounded
        recorder.target = recorder; recorder.action = #selector(ShortcutRecorder.beginRecording)
        recorder.setAccessibilityLabel("Capture shortcut")
        recorder.recorded = { [weak self] shortcut in
            guard let self else { return }
            if let shortcut { self.draft = shortcut }
            self.recorder.title = self.draft.label; self.shortcutError.stringValue = ""; self.resizeDocument()
        }
        recorder.controlSize = .large; recorder.font = .systemFont(ofSize: 15, weight: .medium)
        for label in [message, shortcutError, historyError, retentionWarning, applicationMessage] {
            label.font = .systemFont(ofSize: 12); label.textColor = .secondaryLabelColor
        }
        for label in [shortcutError, historyError, applicationMessage] { label.textColor = .systemRed }
        let reset = NSButton(title: "Restore default", target: self, action: #selector(restoreDefault)); reset.bezelStyle = .rounded
        let shortcutStack = vertical([ShotlightUI.label("Capture shortcut", size: 15, weight: .semibold), recorder, message, shortcutError, reset])
        let shortcutCard = ShotlightUI.card(shortcutStack)

        closeOnCopy.state = closesAfterCopy ? .on : .off
        let copyNote = NSTextField(wrappingLabelWithString: "Applies to the Copy button and ⌘C. Your editable capture stays in Recent Captures either way.")
        copyNote.font = .systemFont(ofSize: 12); copyNote.textColor = .secondaryLabelColor
        let copyStack = vertical([ShotlightUI.label("Copying", size: 15, weight: .semibold), closeOnCopy, copyNote])
        let copyCard = ShotlightUI.card(copyStack)

        let historyNote = NSTextField(wrappingLabelWithString: "Keep editable screenshots so you can come back to them later. Older drafts are removed when this limit is reached.")
        historyNote.font = .systemFont(ofSize: 12); historyNote.textColor = .secondaryLabelColor
        limitField.delegate = self; limitField.setAccessibilityLabel("Captures to keep, from 1 to 500")
        limitField.alignment = .center; limitField.font = .systemFont(ofSize: 14)
        limitField.widthAnchor.constraint(equalToConstant: 70).isActive = true
        let retention = NSStackView(views: [ShotlightUI.label("Captures to keep · 1–500"), limitField]); retention.spacing = 24
        let clear = NSButton(title: "Clear history…", target: self, action: #selector(clearRecentHistory)); clear.bezelStyle = .rounded
        let historyStack = vertical([ShotlightUI.label("Recent captures", size: 15, weight: .semibold), historyNote, retention, historyError, retentionWarning, clear])
        let historyCard = ShotlightUI.card(historyStack)

        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16
        for view in [ShotlightUI.label("Settings", size: 26, weight: .semibold), shortcutCard, copyCard, historyCard] { stack.addArrangedSubview(view) }
        scroll.hasVerticalScroller = true; scroll.drawsBackground = false; scroll.documentView = settingsDocument
        scroll.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(scroll)
        stack.translatesAutoresizingMaskIntoConstraints = false; settingsDocument.addSubview(stack)

        let cancel = NSButton(title: "Cancel", target: self, action: #selector(cancel)); cancel.bezelStyle = .rounded; cancel.keyEquivalent = "\u{1b}"
        let save = NSButton(title: "Save changes", target: self, action: #selector(save)); save.bezelStyle = .rounded; save.keyEquivalent = "\r"
        let actions = NSStackView(views: [NSView(), cancel, save]); actions.spacing = 10
        let footer = vertical([applicationMessage, actions]); footer.spacing = 8; footer.translatesAutoresizingMaskIntoConstraints = false; root.addSubview(footer)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor), scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: root.topAnchor), scroll.bottomAnchor.constraint(equalTo: footer.topAnchor, constant: -16),
            footer.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 26), footer.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -26), footer.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -18),
            actions.widthAnchor.constraint(equalTo: footer.widthAnchor), applicationMessage.widthAnchor.constraint(equalTo: footer.widthAnchor),
            stack.leadingAnchor.constraint(equalTo: settingsDocument.leadingAnchor, constant: 26), stack.topAnchor.constraint(equalTo: settingsDocument.topAnchor, constant: 26), stack.widthAnchor.constraint(equalToConstant: 468)
        ])
        for card in [shortcutCard, copyCard, historyCard] { card.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true }
        for (label, parent) in [(recorder, shortcutStack), (message, shortcutStack), (shortcutError, shortcutStack), (copyNote, copyStack), (historyNote, historyStack), (historyError, historyStack), (retentionWarning, historyStack)] {
            label.widthAnchor.constraint(equalTo: parent.widthAnchor).isActive = true
        }
        updateRetentionWarning(); resizeDocument(); root.layoutSubtreeIfNeeded()
        scroll.contentView.scroll(to: .zero); window.center()
    }
    required init?(coder: NSCoder) { fatalError() }
    private func vertical(_ views: [NSView]) -> NSStackView {
        let result = NSStackView(views: views); result.orientation = .vertical; result.alignment = .leading; result.spacing = 12; return result
    }
    func resizeDocument() {
        for label in [shortcutError, historyError, retentionWarning, applicationMessage] { label.isHidden = label.stringValue.isEmpty }
        settingsDocument.layoutSubtreeIfNeeded()
        settingsDocument.frame = NSRect(x: 0, y: 0, width: 520, height: stack.fittingSize.height+52)
    }
    func updateRetentionWarning() {
        guard let limit = Int(limitField.stringValue), (1...500).contains(limit) else { retentionWarning.stringValue = ""; return }
        let removing = max(0, captureCount()-limit)
        retentionWarning.stringValue = removing > 0 ? "Keeping \(limit) captures will remove \(removing) older \(removing == 1 ? "draft" : "drafts") when you save." : ""
    }
    func controlTextDidChange(_ notification: Notification) {
        historyError.stringValue = ""; updateRetentionWarning(); resizeDocument()
    }
    @objc func restoreDefault() { recorder.recording = false; draft = .standard; recorder.title = draft.label; shortcutError.stringValue = ""; resizeDocument() }
    @objc func clearRecentHistory() { clearHistory(); updateRetentionWarning(); resizeDocument() }
    @objc func cancel() { window?.close() }
    @objc func save() {
        recorder.recording = false; recorder.title = draft.label
        shortcutError.stringValue = ""; historyError.stringValue = ""; applicationMessage.stringValue = ""
        guard let limit = Int(limitField.stringValue), (1...500).contains(limit) else {
            showFailure(.history("Enter a history limit from 1 to 500.")); return
        }
        let removing = max(0, captureCount()-limit)
        if removing > 0, !(confirmReduction?(limit, removing) ?? confirmRemoving(limit: limit, count: removing)) { return }
        if let failure = apply(draft, limit, closeOnCopy.state == .on) { showFailure(failure) }
        else { window?.close() }
    }
    private func confirmRemoving(limit: Int, count: Int) -> Bool {
        let alert = NSAlert(); alert.messageText = "Keep only \(limit) captures?"
        alert.informativeText = "This removes \(count) older \(count == 1 ? "draft" : "drafts") from Recent Captures. Exported PNGs are unaffected."
        alert.addButton(withTitle: "Cancel"); alert.addButton(withTitle: "Remove Older Drafts")
        return alert.runModal() == .alertSecondButtonReturn
    }
    func showFailure(_ failure: SettingsFailure) {
        let field: NSView
        let error: NSView
        switch failure {
        case .shortcut(let text): shortcutError.stringValue = text; field = recorder; error = shortcutError
        case .history(let text): historyError.stringValue = text; field = limitField; error = historyError
        case .general(let text): applicationMessage.stringValue = text; resizeDocument(); return
        }
        resizeDocument()
        let region = settingsDocument.convert(field.bounds, from: field).union(settingsDocument.convert(error.bounds, from: error))
        settingsDocument.scrollToVisible(region.insetBy(dx: -8, dy: -8)); window?.makeFirstResponder(field)
        if field === limitField { limitField.selectText(nil) }
    }
}

final class WelcomeController: NSWindowController {
    init(shortcut: CaptureShortcut, capture: @escaping () -> Void) {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 350), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window); window.title = "Welcome to Shotlight"; window.isReleasedWhenClosed = false
        let note = NSTextField(wrappingLabelWithString: "Capture an area with \(shortcut.label) or the camera icon in your menu bar. Annotate it, then copy or save. Editable drafts stay on this Mac. Copy closes the editor by default; you can change that in Settings.")
        let permission = NSTextField(wrappingLabelWithString: "macOS will ask for Screen Recording access on your first capture. Allow Shotlight in System Settings → Privacy & Security if prompted.")
        note.font = .systemFont(ofSize: 13); permission.font = .systemFont(ofSize: 12); permission.textColor = .secondaryLabelColor
        let button = NSButton(title: "Capture Area", target: self, action: #selector(startCapture)); button.bezelStyle = .rounded; button.keyEquivalent = "\r"
        let stack = NSStackView(views: [ShotlightUI.label("Welcome to Shotlight", size: 24, weight: .semibold), note, permission, button]); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 20
        window.contentView = BackgroundView(); stack.translatesAutoresizingMaskIntoConstraints = false; window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor, constant: 26), stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor, constant: -26), stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor, constant: 26), note.widthAnchor.constraint(equalTo: stack.widthAnchor), permission.widthAnchor.constraint(equalTo: stack.widthAnchor)])
        window.center(); self.capture = capture
    }
    private var capture: (() -> Void)?
    required init?(coder: NSCoder) { fatalError() }
    @objc private func startCapture() { window?.close(); capture?() }
}
