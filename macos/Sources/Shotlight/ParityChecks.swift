import AppKit

private struct ParityFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}
private func demand(_ value: Bool, _ message: String) throws {
    if !value { throw ParityFailure(message: message) }
}
private func parityImage() -> NSImage {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 800, pixelsHigh: 480, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    bitmap.size = NSSize(width: 400, height: 240)
    let image = NSImage(size: bitmap.size); image.addRepresentation(bitmap)
    let context = NSGraphicsContext(bitmapImageRep: bitmap)!
    NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = context
    NSColor.white.setFill(); NSRect(x: 0, y: 0, width: 800, height: 480).fill()
    NSGraphicsContext.restoreGraphicsState(); return image
}

func inlineTextVisibilityCheck() -> String {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("Shotlight-text-edge-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: root) }
    do {
        let image = parityImage(), store = try CaptureStore(directory: root)
        let record = try store.add(image: image)
        let editor = EditorController(image: image, captureID: record.id, store: store)
        defer { editor.detachArchive(); editor.window?.isDocumentEdited = false; editor.window?.close() }
        editor.window?.setFrameOrigin(NSPoint(x: -10000, y: -10000)); editor.window?.orderFront(nil)
        for scale in [0.5, 1.0, 1.5] {
            editor.scroll.setMagnification(scale, centeredAt: NSPoint(x: 200, y: 120))
            editor.canvas.beginTextEditing(at: NSPoint(x: 399, y: 1))
            let inline = editor.canvas.textEditor!
            inline.insertText("First row\nSecond row\nThird row", replacementRange: inline.selectedRange())
            let newline = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: .shift, timestamp: 0, windowNumber: editor.window!.windowNumber, context: nil, characters: "\r", charactersIgnoringModifiers: "\r", isARepeat: false, keyCode: 36)!
            inline.keyDown(with: newline)
            try demand(inline.string == "First row\nSecond row\nThird row\n", "Shift-Return changed the text or committed it")
            inline.layoutManager!.ensureLayout(for: inline.textContainer!)
            let used = inline.layoutManager!.usedRect(for: inline.textContainer!)
            let blank = inline.layoutManager!.extraLineFragmentRect
            try demand(blank.height > 0 && inline.bounds.insetBy(dx: -1, dy: -1).contains(used) && blank.maxY <= inline.bounds.height+1, "A text row or trailing blank row was clipped at \(scale)x")
            try demand(editor.canvas.bounds.contains(inline.frame), "Inline text escaped the image at \(scale)x")
            editor.canvas.fontSize = 32
            try demand(editor.canvas.bounds.contains(inline.frame), "Font resizing clipped edge text")
            let position = inline.frame.origin
            try demand(editor.canvas.draftMarks().last?.points[0] == position && editor.flushArchive(), "Live text did not archive its visible position")
            try demand(try store.marks(record.id).last?.points[0] == position, "Autosave changed the edge position")
            editor.canvas.finishTextEditing(cancel: true)
        }
        editor.canvas.fontSize = 24
        editor.canvas.beginTextEditing(at: NSPoint(x: 399, y: 1))
        editor.canvas.textEditor!.insertText("Edge text\nSecond row\n", replacementRange: NSRange(location: 0, length: 0))
        let visible = editor.canvas.textEditor!.frame.origin
        let text = editor.canvas.textEditor!.string
        let png = editor.canvas.png()!, bitmap = NSBitmapImageRep(data: png)!
        try demand(editor.canvas.marks.last?.points[0] == visible && editor.flushArchive(), "Committing moved edge text")
        try demand(bitmap.pixelsWide == 800 && bitmap.pixelsHigh == 480, "Edge typing changed Retina export resolution")
        var painted = 0
        for y in stride(from: 0, to: bitmap.pixelsHigh, by: 4) {
            for x in stride(from: 0, to: bitmap.pixelsWide, by: 4) {
                if let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB), color.redComponent > 0.6 && color.greenComponent < 0.5 { painted += 1 }
            }
        }
        try demand(painted > 20, "PNG export omitted the edge text")
        let restarted = try CaptureStore(directory: root)
        let reopened = EditorController(image: try restarted.image(record.id), captureID: record.id, store: restarted)
        defer { reopened.detachArchive(); reopened.window?.isDocumentEdited = false; reopened.window?.close() }
        reopened.canvas.marks = try restarted.marks(record.id)
        reopened.canvas.history = try restarted.undoHistory(record.id)
        reopened.canvas.beginTextEditing(at: .zero, index: 0)
        try demand(reopened.canvas.textEditor?.frame.origin == visible && reopened.canvas.textEditor?.string == text, "Reopening moved the text or lost blank rows")
        reopened.canvas.textEditor!.insertText("Cancelled", replacementRange: NSRange(location: 0, length: (text as NSString).length))
        reopened.canvas.finishTextEditing(cancel: true)
        try demand(reopened.canvas.marks[0].text == text && reopened.canvas.marks[0].points[0] == visible, "Cancelling an edit changed the retained text")
        reopened.canvas.undoMark(); try demand(reopened.canvas.marks.isEmpty, "Edge text could not be undone after restart")
        reopened.canvas.redoMark(); try demand(reopened.canvas.marks[0].points[0] == visible, "Redo lost the fitted text position")
        return "PASS: edge text at multiple scales, trailing blank rows, font resizing, autosave, commit/reopen/cancel, undo/redo and Retina export."
    } catch { return "FAIL: inline text visibility \(error.localizedDescription)" }
}

func captureCancellationCheck() -> String {
    let image = parityImage()
    let pixels = (image.representations[0] as! NSBitmapImageRep).cgImage!
    let snapshots = [FrozenScreen(displayID: 1, frame: NSRect(x: -10000, y: -10000, width: 400, height: 240), pixels: pixels), FrozenScreen(displayID: 2, frame: NSRect(x: -10500, y: -10000, width: 400, height: 240), pixels: pixels)]
    func mouse(_ type: NSEvent.EventType, _ point: NSPoint, _ window: NSWindow) -> NSEvent {
        NSEvent.mouseEvent(with: type, location: point, modifierFlags: [], timestamp: 0, windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: 1, pressure: 1)!
    }
    for dragging in [false, true] {
        var cancellations = 0, crops = 0
        let controller = FrozenCaptureController(snapshots: snapshots) { image, _ in if image == nil { cancellations += 1 } else { crops += 1 } }
        let window = controller.windows[1], view = window.contentView as! FrozenSelectionView
        window.level = .normal; window.makeKeyAndOrderFront(nil); window.makeFirstResponder(view)
        if dragging {
            NSApp.sendEvent(mouse(.leftMouseDown, NSPoint(x: 40, y: 40), window))
            NSApp.sendEvent(mouse(.leftMouseDragged, NSPoint(x: 250, y: 180), window))
        }
        let escape = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: window.windowNumber, context: nil, characters: "\u{1b}", charactersIgnoringModifiers: "\u{1b}", isARepeat: false, keyCode: 53)!
        NSApp.sendEvent(escape)
        guard cancellations == 1, crops == 0, controller.windows.isEmpty, view.finished, view.anchor == nil else { controller.cancel(); return "FAIL: dispatched Escape \(dragging ? "during drag" : "before selection")" }
        view.mouseUp(with: mouse(.leftMouseUp, NSPoint(x: 300, y: 200), window)); controller.cancel()
        guard cancellations == 1, crops == 0 else { return "FAIL: cancelled selector completed again" }
    }
    var captured = 0
    let retry = FrozenCaptureController(snapshots: [snapshots[0]]) { image, _ in if image != nil { captured += 1 } }
    let window = retry.windows[0], view = window.contentView as! FrozenSelectionView
    view.mouseDown(with: mouse(.leftMouseDown, NSPoint(x: 20, y: 20), window)); view.mouseUp(with: mouse(.leftMouseUp, NSPoint(x: 100, y: 100), window))
    guard captured == 1, retry.windows.isEmpty else { return "FAIL: new capture after cancellation" }
    return "PASS: dispatched Escape before/during selection, all overlays close, no late crop, fresh capture after cancellation."
}

func captureDeletionCheck() -> String {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("Shotlight-delete-\(UUID().uuidString)")
    let owner = AppDelegate()
    defer { owner.editors.forEach { $0.closeAfterDelete() }; owner.historyController?.window?.close(); try? FileManager.default.removeItem(at: root) }
    do {
        let image = parityImage(), store = try CaptureStore(directory: root)
        owner.captureStore = store
        store.onChange = { [weak owner] in owner?.historyController?.reload(); owner?.editors.forEach { $0.updateNavigation() } }
        let first = try store.add(image: image), second = try store.add(image: image)
        let original = try Data(contentsOf: root.appendingPathComponent(second.id.uuidString).appendingPathComponent("original.png"))
        let savedPNG = root.appendingPathComponent("saved-export.png"); try original.write(to: savedPNG)
        func open(_ id: UUID) throws -> EditorController {
            let editor = EditorController(image: try store.image(id), captureID: id, store: store)
            owner.configureEditor(editor); editor.window?.setFrameOrigin(NSPoint(x: -10000, y: -10000)); editor.window?.orderFront(nil); return editor
        }
        let editor = try open(first.id)
        editor.window?.setContentSize(NSSize(width: 680, height: 350)); editor.window?.contentView?.layoutSubtreeIfNeeded()
        let toolbar = editor.deleteButton.superview!.superview!
        try demand(editor.window!.contentView!.bounds.contains(toolbar.frame), "Trash action clipped the smallest editor toolbar")
        editor.canvas.beginTextEditing(at: NSPoint(x: 30, y: 150)); editor.canvas.textEditor!.string = "Discard this active draft"; editor.canvas.refreshTextEditor()
        let history = CaptureHistoryController(store: store, open: { _ in }, delete: { [weak owner] id in owner?.deleteCapture(id) ?? false })
        owner.historyController = history
        var removals = 0
        owner.recycleCapture = { url in removals += 1; try FileManager.default.removeItem(at: url) }
        owner.confirmCaptureDeletion = { _ in false }
        history.deleteButtons[first.id]!.performClick(nil)
        try demand(removals == 0 && store.records.count == 2 && editor.canvas.textEditor != nil && editor.window!.isVisible, "Cancelled deletion changed a capture or its editor")
        let before = try store.marks(first.id)
        var autosavePaused = false
        owner.recycleCapture = { url in
            RunLoop.current.run(until: Date().addingTimeInterval(0.35))
            autosavePaused = try store.marks(first.id) == before
            removals += 1; try FileManager.default.removeItem(at: url)
        }
        owner.confirmCaptureDeletion = { _ in true }
        editor.deleteButton.performClick(nil)
        try demand(autosavePaused && removals == 1 && !store.contains(first.id) && !editor.window!.isVisible && editor.canvas.textEditor == nil, "Editor deletion autosaved discarded text or left its editor open")
        try demand(history.deleteButtons[first.id] == nil && history.openButtons[second.id] != nil, "History kept a stale deleted card")
        try demand(try CaptureStore(directory: root).records.map(\.id) == [second.id] && Data(contentsOf: savedPNG) == original && Data(contentsOf: root.appendingPathComponent(second.id.uuidString).appendingPathComponent("original.png")) == original, "Deletion changed a neighbor, exported PNG, or restart state")
        let secondEditor = try open(second.id)
        secondEditor.canvas.beginTextEditing(at: NSPoint(x: 20, y: 180)); secondEditor.canvas.textEditor!.string = "Keep after failure"; secondEditor.canvas.refreshTextEditor()
        var errors = 0; owner.reportDeleteError = { _ in errors += 1 }
        owner.recycleCapture = { _ in throw CocoaError(.fileWriteNoPermission) }
        history.deleteButtons[second.id]!.performClick(nil)
        try demand(errors == 1 && store.contains(second.id) && secondEditor.window!.isVisible && secondEditor.canvas.textEditor != nil, "Failed deletion discarded a capture or active edit")
        RunLoop.current.run(until: Date().addingTimeInterval(0.4))
        try demand(try CaptureStore(directory: root).marks(second.id).last?.text == "Keep after failure", "Autosave did not resume after deletion failed")
        owner.recycleCapture = { _ in }
        secondEditor.deleteButton.performClick(nil)
        try demand(errors == 2 && store.contains(second.id) && secondEditor.canvas.textEditor != nil, "A removal that left files behind deleted the record")
        owner.recycleCapture = { url in removals += 1; try FileManager.default.removeItem(at: url) }
        history.deleteButtons[second.id]!.performClick(nil)
        try demand(store.records.isEmpty && !secondEditor.window!.isVisible && history.deleteButtons.isEmpty && history.summary.stringValue.hasPrefix("0 captures"), "History deletion did not close active text or show an empty browser")
        let loose = EditorController(image: image); owner.configureEditor(loose)
        loose.window?.setFrameOrigin(NSPoint(x: -10000, y: -10000)); loose.window?.orderFront(nil)
        loose.canvas.beginTextEditing(at: NSPoint(x: 20, y: 100)); loose.canvas.textEditor!.string = "Outside history"
        loose.deleteButton.performClick(nil)
        try demand(!loose.window!.isVisible && removals == 2, "Discarding an unretained editor changed retained files")
        return "PASS: editor/history delete, cancellation, paused autosave, restart and saved PNG preservation, failed removal recovery, empty history and unretained discard."
    } catch { return "FAIL: capture deletion \(error.localizedDescription)" }
}
