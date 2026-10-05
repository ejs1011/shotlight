import AppKit

// Real AppKit controls, synthetic content, and temporary history only.
func renderInterfacePreviews(to directory: URL) throws {
    try FileManager.default.createDirectory(at: directory,withIntermediateDirectories: true)
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("Shotlight-preview-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: root) }
    let store = try CaptureStore(directory: root)
    let image = NSImage(size: NSSize(width: 900,height: 460))
    image.lockFocus()
    NSColor(calibratedRed: 0.98,green: 0.976,blue: 0.965,alpha: 1).setFill(); NSRect(x: 0,y: 0,width: 900,height: 460).fill()
    func text(_ text: String,_ point: NSPoint,_ size: CGFloat,_ weight: NSFont.Weight = .regular) {
        (text as NSString).draw(at: point,withAttributes: [.font: NSFont.systemFont(ofSize: size,weight: weight),.foregroundColor: NSColor(calibratedWhite: 0.23,alpha: 1)])
    }
    text("WEEKEND NOTES",NSPoint(x: 48,y: 416),12)
    text("A day out of the ordinary.",NSPoint(x: 48,y: 315),32,.bold)
    text("A few good places, a slower pace, and room to wander.",NSPoint(x: 48,y: 287),14)
    let colors = [NSColor(calibratedRed: 0.86,green: 0.91,blue: 0.84,alpha: 1),NSColor(calibratedRed: 0.84,green: 0.90,blue: 0.94,alpha: 1),NSColor(calibratedRed: 0.95,green: 0.88,blue: 0.81,alpha: 1)]
    for i in 0..<3 {
        let x = CGFloat(52+i*267); colors[i].setFill(); NSBezierPath(roundedRect: NSRect(x: x,y: 156,width: 225,height: 102),xRadius: 6,yRadius: 6).fill()
        text(["Start somewhere green","Take the scenic route","Stay for the sunset"][i],NSPoint(x: x,y: 120),16,.semibold)
        text(["09:00 · Coffee & the gardens","12:30 · A walk by the water","17:45 · The best view in town"][i],NSPoint(x: x,y: 94),12)
    }
    text("SAVED PLACES  /  OCTOBER COLLECTION",NSPoint(x: 48,y: 28),12)
    image.unlockFocus()
    let record = try store.add(image: image)
    let editor = EditorController(image: image,captureID: record.id,store: store)
    editor.canvas.marks = [Mark(tool: .rectangle,points: [NSPoint(x: 48,y: 102),NSPoint(x: 282,y: 264)],color: .systemIndigo,width: 3),Mark(tool: .arrow,points: [NSPoint(x: 620,y: 355),NSPoint(x: 708,y: 263)],color: .systemRed,width: 4),Mark(tool: .text,points: [NSPoint(x: 382,y: 395)],color: .systemIndigo,width: 4,text: "Ready for a little adventure")]
    editor.canvas.needsDisplay = true; _ = editor.flushArchive()
    func render(_ controller: NSWindowController,_ name: String,_ size: NSSize? = nil,dark: Bool = false) throws {
        guard let window = controller.window,let view = window.contentView else { return }
        window.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        if let size { window.setContentSize(size) }
        window.setFrameOrigin(NSPoint(x: -10000,y: -10000)); window.animationBehavior = .none; window.orderFront(nil)
        view.layoutSubtreeIfNeeded(); view.displayIfNeeded()
        RunLoop.current.run(until: Date().addingTimeInterval(0.1))
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw CaptureHistoryError.invalidDraft }
        view.cacheDisplay(in: view.bounds,to: bitmap)
        let composited = NSImage(size: view.bounds.size); composited.lockFocus()
        window.effectiveAppearance.performAsCurrentDrawingAppearance {
            NSColor.windowBackgroundColor.setFill(); view.bounds.fill(); bitmap.draw(in: view.bounds)
        }
        composited.unlockFocus()
        guard let tiff = composited.tiffRepresentation,let data = NSBitmapImageRep(data: tiff)?.representation(using: .png,properties: [:]) else { throw CaptureHistoryError.invalidDraft }
        try data.write(to: directory.appendingPathComponent(name+".png")); window.orderOut(nil)
    }
    try render(editor,"mac-editor",NSSize(width: 1040,height: 640))
    try render(editor,"mac-editor-compact",NSSize(width: 720,height: 550))
    try render(editor,"mac-editor-dark",NSSize(width: 1040,height: 640),dark: true)
    for i in 0..<5 { try store.add(image: image,capturedAt: Date().addingTimeInterval(Double(-i-1)*3600)) }
    let history = CaptureHistoryController(store: store,open: { _ in })
    try render(history,"mac-recent-captures")
    let settings = ShortcutSettingsController(shortcut: .standard,historyLimit: 50,clearHistory: {},apply: { _,_ in nil })
    try render(settings,"mac-settings")
    editor.window?.close(); history.window?.close(); settings.window?.close()
}

func compactToolbarCheck() -> String {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil,pixelsWide: 400,pixelsHigh: 200,bitsPerSample: 8,samplesPerPixel: 4,hasAlpha: true,isPlanar: false,colorSpaceName: .deviceRGB,bytesPerRow: 0,bitsPerPixel: 0)!
    rep.size = NSSize(width: 200,height: 100); let image = NSImage(size: rep.size); image.addRepresentation(rep)
    let editor = EditorController(image: image); defer { editor.window?.close() }
    editor.compactTools[Tool.text.rawValue].performClick(nil)
    guard editor.canvas.tool == .text,editor.compactTools.filter({ $0.state == .on }).count == 1 else { return "FAIL: compact tool selection" }
    let thick = editor.widthMenu.items[2]
    _ = NSApp.sendAction(thick.action!,to: thick.target,from: thick)
    guard editor.canvas.strokeWidth == 8 else { return "FAIL: compact stroke menu" }
    let zoom = editor.moreMenu.items.first(where: { $0.title == "Zoom" })!.submenu!.items.first(where: { $0.tag == 150 })!
    _ = NSApp.sendAction(zoom.action!,to: zoom.target,from: zoom)
    guard abs(editor.scroll.magnification-1.5) < 0.01,let png = editor.canvas.png(),let output = NSBitmapImageRep(data: png),output.pixelsWide == 400,output.pixelsHigh == 200 else { return "FAIL: preview zoom changed Retina export" }
    var navigation = 0; editor.navigate = { navigation = $0 }
    _ = NSApp.sendAction(editor.previousButton.action!,to: editor.previousButton.target,from: editor.previousButton)
    guard navigation == 1 else { return "FAIL: compact history navigation" }
    return "PASS: compact toolbar, size menu, history navigation, zoom and Retina export."
}
