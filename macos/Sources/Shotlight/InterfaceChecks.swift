import AppKit

// Exercise user-visible behaviors with generated images and isolated storage/clipboard.
func interfaceBehaviorCheck() -> String {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("Shotlight-interface-check-\(UUID().uuidString)")
    let domain = "local.shotlight.interface-check.\(UUID().uuidString)"
    let defaults = UserDefaults(suiteName: domain)!
    let pasteboard = NSPasteboard(name: NSPasteboard.Name(domain))
    defer { try? FileManager.default.removeItem(at: root); defaults.removePersistentDomain(forName: domain); pasteboard.releaseGlobally() }
    do {
        guard CopyPreference.closesEditor(in: defaults) else { return "FAIL: default close-on-copy" }
        CopyPreference.save(false, in: defaults)
        guard !CopyPreference.closesEditor(in: UserDefaults(suiteName: domain)!) else { return "FAIL: copy preference persistence" }
        let image = NSImage(size: NSSize(width: 900, height: 460))
        image.lockFocus(); NSColor.white.setFill(); NSRect(origin: .zero, size: image.size).fill(); image.unlockFocus()
        let store = try CaptureStore(directory: root)
        let record = try store.add(image: image)
        let editor = EditorController(image: image, captureID: record.id, store: store, pasteboard: pasteboard)
        defer { editor.detachArchive(); editor.window?.isDocumentEdited = false; editor.window?.close() }
        let window = editor.window!
        window.setFrameOrigin(NSPoint(x: -10000, y: -10000)); window.orderFront(nil)
        window.setContentSize(NSSize(width: 680, height: 300)); editor.fitToWindow()
        guard editor.scroll.documentVisibleRect.insetBy(dx: -1, dy: -1).contains(editor.canvas.bounds), editor.scroll.magnification < 1, editor.zoomControls.selectedSegment == 0 else { return "FAIL: full capture at compact Fit" }
        let fitted = editor.scroll.magnification
        editor.zoomControls.selectedSegment = 1; editor.selectPreviewScale(editor.zoomControls)
        window.setContentSize(NSSize(width: 720, height: 550))
        guard editor.scroll.magnification == 1, !editor.fittingPreview else { return "FAIL: explicit 100% after resize" }
        editor.fitToWindow()
        guard editor.scroll.magnification > fitted, editor.scroll.documentVisibleRect.insetBy(dx: -1, dy: -1).contains(editor.canvas.bounds) else { return "FAIL: refit after resize" }
        editor.closesAfterCopy = CopyPreference.closesEditor(in: defaults)
        editor.canvas.beginTextEditing(at: NSPoint(x: 30, y: 400)); editor.canvas.textEditor?.string = "Keep editing after copy"
        let copyEvent = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: .command, timestamp: 0, windowNumber: window.windowNumber, context: nil, characters: "c", charactersIgnoringModifiers: "c", isARepeat: false, keyCode: 8)!
        guard window.performKeyEquivalent(with: copyEvent), window.isVisible, editor.canvas.textEditor == nil,
              editor.copyButton.title == "Copy", pasteboard.data(forType: .png) != nil,
              try store.marks(record.id).last?.text == "Keep editing after copy" else { return "FAIL: Command-C keep-open draft retention" }
        editor.canvas.beginTextEditing(at: NSPoint(x: 40, y: 320)); editor.canvas.textEditor?.string = "Toolbar copy"
        editor.copyButton.performClick(nil)
        guard window.isVisible, try store.marks(record.id).count == 2 else { return "FAIL: toolbar keep-open copy" }
        editor.closesAfterCopy = true
        guard editor.copyButton.title == "Copy & Close" else { return "FAIL: close-on-copy label" }
        editor.copyButton.performClick(nil)
        guard !window.isVisible, try CaptureStore(directory: root).marks(record.id).count == 2 else { return "FAIL: toolbar copy-close retention" }

        // Thumbnail pixels must change on edit and return to the original after undo.
        let original = try store.add(image: image)
        let thumbURL = root.appendingPathComponent(original.id.uuidString).appendingPathComponent("thumbnail.png")
        let before = try Data(contentsOf: thumbURL)
        let line = Mark(tool: .pen, points: [NSPoint(x: 20, y: 230), NSPoint(x: 880, y: 230)], color: .red, width: 30)
        var notifications = 0; store.onChange = { notifications += 1 }
        try store.save(original.id, marks: [line], undoHistory: [[]])
        let after = try Data(contentsOf: thumbURL)
        guard after != before, notifications == 1,
              let bitmap = NSBitmapImageRep(data: after), let red = bitmap.colorAt(x: 120, y: 75)?.usingColorSpace(.deviceRGB),
              red.redComponent > 0.8, red.greenComponent < 0.2 else { return "FAIL: annotated thumbnail pixels" }
        try store.save(original.id, marks: [], redoHistory: [[line]])
        guard let undone = NSBitmapImageRep(data: try Data(contentsOf: thumbURL)),
              let white = undone.colorAt(x: 120, y: 75)?.usingColorSpace(.deviceRGB),
              white.redComponent > 0.95, white.greenComponent > 0.95, white.blueComponent > 0.95 else { return "FAIL: undo thumbnail refresh" }
        // Simulate an older draft: its cached thumbnail lacks the annotations.
        try store.save(original.id, marks: [line], undoHistory: [[]])
        let draftURL = root.appendingPathComponent(original.id.uuidString).appendingPathComponent("draft.json")
        var draft = try JSONSerialization.jsonObject(with: Data(contentsOf: draftURL)) as! [String: Any]
        draft.removeValue(forKey: "thumbnailVersion")
        try JSONSerialization.data(withJSONObject: draft).write(to: draftURL); try before.write(to: thumbURL)
        let reloaded = try CaptureStore(directory: root)
        _ = reloaded.thumbnail(original.id)
        guard let upgraded = NSBitmapImageRep(data: try Data(contentsOf: thumbURL)),
              let upgradedRed = upgraded.colorAt(x: 120, y: 75)?.usingColorSpace(.deviceRGB), upgradedRed.greenComponent < 0.2,
              try reloaded.undoHistory(original.id) == [[]] else { return "FAIL: older draft thumbnail upgrade" }

        var applied = 0, proposed: (Int, Bool)?
        let settings = ShortcutSettingsController(shortcut: .standard, historyLimit: 50, captureCount: { store.records.count }, clearHistory: {}) { _, limit, closes in
            applied += 1; proposed = (limit, closes); return nil
        }
        defer { settings.window?.close() }
        settings.window?.setContentSize(NSSize(width: 520, height: 480))
        settings.window?.setFrameOrigin(NSPoint(x: -10000, y: -10000)); settings.window?.orderFront(nil)
        settings.limitField.stringValue = "0"; settings.save()
        guard applied == 0, settings.historyError.stringValue.contains("1 to 500"), settings.shortcutError.stringValue.isEmpty, settings.message.stringValue.contains("preferred") else { return "FAIL: inline retention validation" }
        let errorRect = settings.settingsDocument.convert(settings.historyError.bounds, from: settings.historyError)
        guard settings.scroll.documentVisibleRect.insetBy(dx: -1, dy: -1).contains(errorRect) else { return "FAIL: inline error visible in short settings window" }
        settings.limitField.stringValue = "1"; settings.updateRetentionWarning()
        guard settings.retentionWarning.stringValue.contains("remove 1 older draft") else { return "FAIL: actual retention removal count" }
        var confirmedCount = 0
        settings.confirmReduction = { limit, count in confirmedCount = count; return false }
        settings.save()
        guard applied == 0, confirmedCount == 1, store.records.count == 2 else { return "FAIL: cancelled retention reduction" }
        settings.confirmReduction = { _, _ in true }; settings.closeOnCopy.state = .off; settings.save()
        guard applied == 1, proposed?.0 == 1, proposed?.1 == false else { return "FAIL: confirmed settings and copy preference" }
        return "PASS: fit/100%, both copy modes, retained drafts, annotated/legacy thumbnails, inline errors and retention confirmation."
    } catch { return "FAIL: interface behaviors \(error.localizedDescription)" }
}
