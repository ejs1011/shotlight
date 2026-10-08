import AppKit
import ScreenCaptureKit

struct FrozenScreen {
    let displayID: CGDirectDisplayID
    let frame: NSRect
    let pixels: CGImage
    var size: NSSize { frame.size }
    var image: NSImage { NSImage(cgImage: pixels, size: size) }

    // Selection coordinates are local AppKit points, measured from the bottom left.
    // CGImage crop coordinates are pixels, measured from the top left.
    func crop(_ rect: NSRect) -> (image: NSImage, png: Data)? {
        let bounded = rect.intersection(NSRect(origin: .zero, size: size))
        guard !bounded.isNull, bounded.width >= 2, bounded.height >= 2 else { return nil }
        let sx = CGFloat(pixels.width) / size.width, sy = CGFloat(pixels.height) / size.height
        let left = floor(bounded.minX * sx), right = ceil(bounded.maxX * sx)
        let top = floor((size.height - bounded.maxY) * sy), bottom = ceil((size.height - bounded.minY) * sy)
        let pixelRect = CGRect(x: left, y: top, width: right-left, height: bottom-top)
        guard let cropped = pixels.cropping(to: pixelRect) else { return nil }
        let logicalSize = NSSize(width: CGFloat(cropped.width)/sx, height: CGFloat(cropped.height)/sy)
        let bitmap = NSBitmapImageRep(cgImage: cropped); bitmap.size = logicalSize
        guard let png = bitmap.representation(using: .png, properties: [:]) else { return nil }
        let image = NSImage(size: logicalSize); image.addRepresentation(bitmap)
        return (image, png)
    }
}

enum FrozenCaptureError: LocalizedError {
    case unavailable
    var errorDescription: String? { "The desktop snapshot could not be captured. Try again; if needed, allow Shotlight in System Settings → Privacy & Security → Screen & System Audio Recording." }
}

enum DesktopSnapshot {
    static func capture(completion: @escaping (Result<[FrozenScreen], Error>) -> Void) {
        let screens = NSScreen.screens
        guard !screens.isEmpty else { completion(.failure(FrozenCaptureError.unavailable)); return }
        if #available(macOS 14.0, *) {
            SCShareableContent.getExcludingDesktopWindows(false, onScreenWindowsOnly: true) { content, error in
                DispatchQueue.main.async {
                    guard let content else { completion(.failure(error ?? FrozenCaptureError.unavailable)); return }
                    let group = DispatchGroup()
                    var frozen: [FrozenScreen] = [], failure: Error?
                    // Capture every display before showing any selection windows. No live
                    // stream or second screenshot is used when the selection finishes.
                    for screen in screens {
                        let id = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
                        guard let display = content.displays.first(where: { $0.displayID == id }) else { failure = FrozenCaptureError.unavailable; continue }
                        let filter = SCContentFilter(display: display, excludingWindows: [])
                        let config = SCStreamConfiguration()
                        config.width = Int((screen.frame.width * screen.backingScaleFactor).rounded())
                        config.height = Int((screen.frame.height * screen.backingScaleFactor).rounded())
                        config.showsCursor = false
                        config.captureResolution = .best
                        let frame = screen.frame
                        group.enter()
                        SCScreenshotManager.captureImage(contentFilter: filter, configuration: config) { image, error in
                            DispatchQueue.main.async {
                                if let image { frozen.append(FrozenScreen(displayID: id, frame: frame, pixels: image)) }
                                else { failure = error ?? FrozenCaptureError.unavailable }
                                group.leave()
                            }
                        }
                    }
                    group.notify(queue: .main) {
                        if let failure { completion(.failure(failure)) }
                        else { completion(.success(frozen)) }
                    }
                }
            }
        } else {
            captureLegacy(screens: screens, completion: completion)
        }
    }

    // macOS 13 uses the system tool for noninteractive full-display snapshots.
    // The same frozen selection UI and crop path are used on every OS version.
    private static func captureLegacy(screens: [NSScreen], completion: @escaping (Result<[FrozenScreen], Error>) -> Void) {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("Shotlight-freeze-\(UUID().uuidString)")
        do { try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true) }
        catch { completion(.failure(error)); return }
        // screencapture's output order follows the active display list, main first.
        var ids = [CGDirectDisplayID](repeating: 0, count: 32), count: UInt32 = 0
        guard CGGetActiveDisplayList(UInt32(ids.count), &ids, &count) == .success else {
            try? FileManager.default.removeItem(at: directory); completion(.failure(FrozenCaptureError.unavailable)); return
        }
        let activeIDs = Array(ids.prefix(Int(count)))
        let orderedIDs = [CGMainDisplayID()] + activeIDs.filter { $0 != CGMainDisplayID() }
        let urls = orderedIDs.map { directory.appendingPathComponent("\($0).png") }
        let task = Process(); task.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
        task.arguments = ["-x", "-t", "png"] + urls.map(\.path)
        task.standardError = FileHandle.nullDevice
        task.terminationHandler = { task in
            DispatchQueue.main.async {
                defer { try? FileManager.default.removeItem(at: directory) }
                guard task.terminationStatus == 0 else { completion(.failure(FrozenCaptureError.unavailable)); return }
                var frozen: [FrozenScreen] = []
                for screen in screens {
                    let id = (screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value ?? 0
                    let url = directory.appendingPathComponent("\(id).png")
                    guard let bitmap = NSBitmapImageRep(data: (try? Data(contentsOf: url)) ?? Data()), let pixels = bitmap.cgImage else { completion(.failure(FrozenCaptureError.unavailable)); return }
                    frozen.append(FrozenScreen(displayID: id, frame: screen.frame, pixels: pixels))
                }
                completion(.success(frozen))
            }
        }
        do { try task.run() }
        catch { try? FileManager.default.removeItem(at: directory); completion(.failure(error)) }
    }
}

final class FrozenSelectionWindow: NSWindow {
    var cancelCapture: (() -> Void)?
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
    override func performKeyEquivalent(with event: NSEvent) -> Bool {
        if event.keyCode == 53 { cancelCapture?(); return true }
        return super.performKeyEquivalent(with: event)
    }
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { cancelCapture?() } else { super.keyDown(with: event) }
    }
    override func cancelOperation(_ sender: Any?) { cancelCapture?() }
}

final class FrozenSelectionView: NSView {
    let snapshot: FrozenScreen
    let background: NSImage
    var anchor: NSPoint?
    var selection: NSRect?
    var selected: ((NSRect) -> Void)?
    var cancelled: (() -> Void)?
    private(set) var finished = false
    init(snapshot: FrozenScreen) {
        self.snapshot = snapshot; background = snapshot.image
        super.init(frame: NSRect(origin: .zero, size: snapshot.size))
        setAccessibilityLabel("Frozen screenshot. Drag to select an area. Escape cancels.")
    }
    required init?(coder: NSCoder) { fatalError() }
    override var acceptsFirstResponder: Bool { true }
    override var isOpaque: Bool { true }
    override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }
    override func draw(_ dirtyRect: NSRect) {
        background.draw(in: bounds, from: .zero, operation: .copy, fraction: 1)
        NSColor.black.withAlphaComponent(0.25).setFill()
        if let selection {
            NSRect(x: 0, y: 0, width: bounds.width, height: selection.minY).fill()
            NSRect(x: 0, y: selection.maxY, width: bounds.width, height: bounds.height-selection.maxY).fill()
            NSRect(x: 0, y: selection.minY, width: selection.minX, height: selection.height).fill()
            NSRect(x: selection.maxX, y: selection.minY, width: bounds.width-selection.maxX, height: selection.height).fill()
            let border = NSBezierPath(rect: selection); border.lineWidth = 2
            NSColor.black.withAlphaComponent(0.8).setStroke(); border.stroke()
            border.lineWidth = 1; NSColor.white.setStroke(); border.stroke()
            let width = Int((selection.width * CGFloat(snapshot.pixels.width) / bounds.width).rounded())
            let height = Int((selection.height * CGFloat(snapshot.pixels.height) / bounds.height).rounded())
            badge("\(width) × \(height) px", near: NSPoint(x: selection.minX, y: selection.minY-28))
        } else {
            bounds.fill()
            badge("Drag to capture · Esc to cancel", near: NSPoint(x: (bounds.width-260)/2, y: bounds.height-56))
        }
    }
    private func badge(_ text: String, near point: NSPoint) {
        let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.monospacedSystemFont(ofSize: 12, weight: .medium), .foregroundColor: NSColor.white]
        let textSize = (text as NSString).size(withAttributes: attributes)
        let box = NSRect(x: max(4, min(point.x, bounds.width-textSize.width-20)), y: max(4, min(point.y, bounds.height-28)), width: textSize.width+16, height: 24)
        NSColor.black.withAlphaComponent(0.8).setFill(); NSBezierPath(roundedRect: box, xRadius: 5, yRadius: 5).fill()
        (text as NSString).draw(at: NSPoint(x: box.minX+8, y: box.minY+5), withAttributes: attributes)
    }
    private func boundedPoint(_ event: NSEvent) -> NSPoint {
        let p = convert(event.locationInWindow, from: nil)
        return NSPoint(x: min(max(p.x, 0), bounds.width), y: min(max(p.y, 0), bounds.height))
    }
    override func mouseDown(with event: NSEvent) {
        guard !finished else { return }
        window?.makeKey(); window?.makeFirstResponder(self)
        anchor = boundedPoint(event); selection = nil; needsDisplay = true
    }
    override func mouseDragged(with event: NSEvent) {
        guard !finished, let anchor else { return }
        let p = boundedPoint(event)
        selection = NSRect(x: min(anchor.x,p.x), y: min(anchor.y,p.y), width: abs(p.x-anchor.x), height: abs(p.y-anchor.y))
        needsDisplay = true
    }
    override func mouseUp(with event: NSEvent) {
        guard !finished, anchor != nil else { return }
        mouseDragged(with: event); anchor = nil
        guard let selection, selection.width >= 2, selection.height >= 2 else { self.selection = nil; needsDisplay = true; return }
        finished = true; selected?(selection)
    }
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { cancelled?() } else { super.keyDown(with: event) }
    }
    override func cancelOperation(_ sender: Any?) { cancelled?() }
    func finishSelection() { finished = true; anchor = nil; selection = nil }
}

final class FrozenCaptureController {
    private(set) var windows: [FrozenSelectionWindow] = []
    private var completion: ((NSImage?, Data?) -> Void)?
    private var previousApp: NSRunningApplication?
    init(snapshots: [FrozenScreen], completion: @escaping (NSImage?, Data?) -> Void) {
        self.completion = completion
        for snapshot in snapshots {
            let window = FrozenSelectionWindow(contentRect: snapshot.frame, styleMask: .borderless, backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false; window.hasShadow = false; window.isOpaque = true
            window.backgroundColor = .black; window.level = .screenSaver; window.animationBehavior = .none
            window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
            window.isExcludedFromWindowsMenu = true; window.sharingType = .none
            let view = FrozenSelectionView(snapshot: snapshot); window.contentView = view
            view.selected = { [weak self] rect in
                guard let crop = snapshot.crop(rect) else { return }
                self?.finish(image: crop.image, png: crop.png)
            }
            view.cancelled = { [weak self] in self?.cancel() }
            window.cancelCapture = { [weak self] in self?.cancel() }
            windows.append(window)
        }
    }
    func show() {
        previousApp = NSWorkspace.shared.frontmostApplication
        for window in windows { window.orderFrontRegardless(); window.display() }
        let target = windows.first { $0.frame.contains(NSEvent.mouseLocation) } ?? windows.first
        target?.makeKey(); target?.makeFirstResponder(target?.contentView)
        NSApp.activate(ignoringOtherApps: true)
    }
    func cancel() { finish(image: nil, png: nil) }
    private func finish(image: NSImage?, png: Data?) {
        guard let completion else { return }
        self.completion = nil
        windows.forEach { ($0.contentView as? FrozenSelectionView)?.finishSelection(); $0.orderOut(nil); $0.close() }; windows.removeAll()
        if image == nil { previousApp?.activate(options: []) }
        completion(image, png)
    }
}

func frozenCaptureCheck() -> String {
    func pattern(width: Int, height: Int) -> CGImage {
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        for y in 0..<height { for x in 0..<width {
            bitmap.setColor(NSColor(deviceRed: CGFloat(x)/CGFloat(width-1), green: CGFloat(y)/CGFloat(height-1), blue: 0.25, alpha: 1), atX: x, y: y)
        } }
        return bitmap.cgImage!.copy(colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!)!
    }
    func mouse(_ type: NSEvent.EventType, _ p: NSPoint, window: NSWindow) -> NSEvent {
        NSEvent.mouseEvent(with: type, location: p, modifierFlags: [], timestamp: 0, windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: 1, pressure: 1)!
    }
    func same(_ a: NSColor?, _ b: NSColor?) -> Bool {
        guard let a = a?.usingColorSpace(.sRGB), let b = b?.usingColorSpace(.sRGB) else { return false }
        return abs(a.redComponent-b.redComponent) < 0.01 && abs(a.greenComponent-b.greenComponent) < 0.01 && abs(a.blueComponent-b.blueComponent) < 0.01 && abs(a.alphaComponent-b.alphaComponent) < 0.01
    }
    func color(_ bitmap: NSBitmapImageRep, x: Int, y: Int) -> NSColor? {
        guard let raw = bitmap.colorAt(x: x, y: y) else { return nil }
        return NSColor(colorSpace: bitmap.colorSpace, components: [raw.redComponent, raw.greenComponent, raw.blueComponent, raw.alphaComponent], count: 4)
    }
    let original = pattern(width: 400, height: 240)
    var liveDesktop = original
    let retina = FrozenScreen(displayID: 11, frame: NSRect(x: 0, y: 0, width: 200, height: 120), pixels: liveDesktop)
    // The desktop advances to a different frame after activation. Selection must
    // continue to render and export the original frame, including after redraws.
    liveDesktop = pattern(width: 600, height: 360)
    var cropImage: NSImage?, cropPNG: Data?, completions = 0
    let controller = FrozenCaptureController(snapshots: [retina]) { image, png in
        completions += 1; cropImage = image; cropPNG = png
    }
    let window = controller.windows[0], view = window.contentView as! FrozenSelectionView
    guard view.snapshot.pixels.width != liveDesktop.width else { return "FAIL: frozen frame replaced" }
    let from = NSPoint(x: 150, y: 90), to = NSPoint(x: 40, y: 30)
    view.mouseDown(with: mouse(.leftMouseDown, from, window: window))
    view.mouseDragged(with: mouse(.leftMouseDragged, to, window: window))
    // Rendering the selected interior must match the frozen original, before the crop.
    let rendered = view.bitmapImageRepForCachingDisplay(in: view.bounds)!
    view.cacheDisplay(in: view.bounds, to: rendered)
    let frozenBitmap = NSBitmapImageRep(cgImage: original)
    let px = Int(100 * CGFloat(rendered.pixelsWide)/200), py = Int(50 * CGFloat(rendered.pixelsHigh)/120)
    if let path = ProcessInfo.processInfo.environment["SHOTLIGHT_FROZEN_PREVIEW"], let png = rendered.representation(using: .png, properties: [:]) { try? png.write(to: URL(fileURLWithPath: path)) }
    guard same(color(rendered, x: px, y: py), color(frozenBitmap, x: 200, y: 100)) else {
        print("frozen redraw", rendered.pixelsWide, rendered.pixelsHigh, rendered.colorSpace, frozenBitmap.colorSpace, String(describing: color(rendered,x:px,y:py)), String(describing: color(frozenBitmap,x:200,y:100)), String(describing: view.selection))
        return "FAIL: frozen selection redraw"
    }
    view.mouseUp(with: mouse(.leftMouseUp, to, window: window))
    guard completions == 1, controller.windows.isEmpty, let cropImage, cropImage.size == NSSize(width: 110, height: 60), let cropPNG, let output = NSBitmapImageRep(data: cropPNG), output.pixelsWide == 220, output.pixelsHigh == 120 else { return "FAIL: reverse drag / Retina crop" }
    for (x,y) in [(0,0),(50,40),(219,119)] {
        guard same(color(output,x:x,y:y), color(frozenBitmap,x:x+80,y:y+60)) else { return "FAIL: crop pixel orientation / frozen content" }
    }
    let exported = NSBitmapImageRep(data: Canvas(image: cropImage).png()!)!
    guard same(color(exported,x:50,y:40), color(output,x:50,y:40)) else { return "FAIL: frozen crop editor export" }
    controller.cancel(); guard completions == 1 else { return "FAIL: capture completed twice" }

    let secondary = FrozenScreen(displayID: 22, frame: NSRect(x: -320, y: 300, width: 320, height: 180), pixels: pattern(width: 320, height: 180))
    var secondaryOutput: Data?
    let multiple = FrozenCaptureController(snapshots: [retina,secondary]) { _, png in secondaryOutput = png }
    let secondWindow = multiple.windows[1], secondView = secondWindow.contentView as! FrozenSelectionView
    // A click or tiny drag keeps selection active, without producing a capture.
    secondView.mouseDown(with: mouse(.leftMouseDown, NSPoint(x: 30,y: 40), window: secondWindow))
    secondView.mouseUp(with: mouse(.leftMouseUp, NSPoint(x: 30,y: 40), window: secondWindow))
    guard secondaryOutput == nil, multiple.windows.count == 2 else { return "FAIL: empty selection" }
    secondView.mouseDown(with: mouse(.leftMouseDown, NSPoint(x: 30,y: 40), window: secondWindow))
    secondView.mouseUp(with: mouse(.leftMouseUp, NSPoint(x: 500,y: 500), window: secondWindow))
    guard multiple.windows.isEmpty, let secondaryOutput, let secondaryBitmap = NSBitmapImageRep(data: secondaryOutput), secondaryBitmap.pixelsWide == 290, secondaryBitmap.pixelsHigh == 140, same(color(secondaryBitmap,x:0,y:0), color(NSBitmapImageRep(cgImage: secondary.pixels),x:30,y:0)) else { return "FAIL: secondary display / selection bounds" }

    var cancelled = 0
    let cancellation = FrozenCaptureController(snapshots: [retina, secondary]) { image, png in if image == nil && png == nil { cancelled += 1 } }
    let escape = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: 0, context: nil, characters: "\u{1b}", charactersIgnoringModifiers: "\u{1b}", isARepeat: false, keyCode: 53)!
    (cancellation.windows[1].contentView as! FrozenSelectionView).keyDown(with: escape)
    guard cancelled == 1, cancellation.windows.isEmpty else { return "FAIL: cancel frozen capture" }
    return "PASS: frozen redraw, reverse/Retina crop, secondary display, bounds, export, Escape."
}
