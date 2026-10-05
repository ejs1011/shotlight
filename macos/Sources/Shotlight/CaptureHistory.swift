import AppKit

struct StoredPoint: Codable { let x: Double; let y: Double }
struct StoredMark: Codable {
    let tool: Int
    let points: [StoredPoint]
    let rgba: [Double]
    let width: Double
    let text: String
    init(_ mark: Mark) {
        tool = mark.tool.rawValue; points = mark.points.map { StoredPoint(x: $0.x, y: $0.y) }
        let color = mark.color.usingColorSpace(.deviceRGB) ?? .red
        rgba = [color.redComponent, color.greenComponent, color.blueComponent, color.alphaComponent]
        width = mark.width; text = mark.text
    }
    func restored() throws -> Mark {
        guard let tool = Tool(rawValue: tool), !points.isEmpty, rgba.count == 4, width.isFinite, width > 0,
              points.allSatisfy({ $0.x.isFinite && $0.y.isFinite }), rgba.allSatisfy({ $0.isFinite && (0...1).contains($0) }) else { throw CaptureHistoryError.invalidDraft }
        return Mark(tool: tool, points: points.map { NSPoint(x: $0.x,y: $0.y) }, color: NSColor(deviceRed: rgba[0],green: rgba[1],blue: rgba[2],alpha: rgba[3]), width: width, text: text)
    }
}
struct CaptureRecord: Codable {
    let id: UUID
    let capturedAt: Date
    let imageWidth: Double
    let imageHeight: Double
    var marks: [StoredMark]
    var undoHistory: [[StoredMark]]? = nil
    var redoHistory: [[StoredMark]]? = nil
    var size: NSSize { NSSize(width: imageWidth,height: imageHeight) }
    var displayDate: String { capturedAt.formatted(date: .abbreviated,time: .standard) }
}
enum CaptureHistoryError: LocalizedError {
    case invalidDraft, missingCapture
    var errorDescription: String? {
        switch self {
        case .invalidDraft: return "The stored screenshot draft could not be read."
        case .missingCapture: return "This screenshot is no longer in recent history."
        }
    }
}

final class CaptureStore {
    let directory: URL
    private(set) var records: [CaptureRecord] = [] // Newest capture first; edits do not change this order.
    private(set) var limit: Int
    var onChange: (() -> Void)?
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()
    init(directory: URL, limit: Int = 50) throws {
        self.directory = directory; self.limit = min(500,max(1,limit))
        encoder.dateEncodingStrategy = .millisecondsSince1970; decoder.dateDecodingStrategy = .millisecondsSince1970
        try FileManager.default.createDirectory(at: directory,withIntermediateDirectories: true,attributes: [.posixPermissions: 0o700])
        try reload(); try trim()
    }
    private func folder(_ id: UUID) -> URL { directory.appendingPathComponent(id.uuidString,isDirectory: true) }
    func contains(_ id: UUID) -> Bool { records.contains { $0.id == id } }
    func record(_ id: UUID) -> CaptureRecord? { records.first { $0.id == id } }
    private func reload() throws {
        records = try FileManager.default.contentsOfDirectory(at: directory,includingPropertiesForKeys: nil,options: [.skipsHiddenFiles]).compactMap { url in
            guard let id = UUID(uuidString: url.lastPathComponent), let data = try? Data(contentsOf: url.appendingPathComponent("draft.json")),
                  let record = try? decoder.decode(CaptureRecord.self,from: data), record.id == id,
                  record.imageWidth > 0, record.imageWidth.isFinite, record.imageHeight > 0, record.imageHeight.isFinite,
                  FileManager.default.fileExists(atPath: url.appendingPathComponent("original.png").path) else { return nil }
            return record
        }.sorted { a,b in a.capturedAt == b.capturedAt ? a.id.uuidString > b.id.uuidString : a.capturedAt > b.capturedAt }
    }
    private func trim() throws {
        while records.count > limit {
            let oldest = records.last!
            try FileManager.default.removeItem(at: folder(oldest.id))
            records.removeLast()
        }
    }
    @discardableResult func add(image: NSImage, originalPNG: Data? = nil, capturedAt: Date = Date()) throws -> CaptureRecord {
        guard let png = originalPNG ?? image.tiffRepresentation.flatMap({ NSBitmapImageRep(data: $0)?.representation(using: .png,properties: [:]) }) else { throw CaptureHistoryError.invalidDraft }
        let record = CaptureRecord(id: UUID(),capturedAt: capturedAt,imageWidth: image.size.width,imageHeight: image.size.height,marks: [])
        let staging = directory.appendingPathComponent(".pending-\(record.id.uuidString)",isDirectory: true)
        try FileManager.default.createDirectory(at: staging,withIntermediateDirectories: true)
        do {
            try png.write(to: staging.appendingPathComponent("original.png"),options: .atomic)
            try encoder.encode(record).write(to: staging.appendingPathComponent("draft.json"),options: .atomic)
            if let thumbnail = thumbnailPNG(image) { try thumbnail.write(to: staging.appendingPathComponent("thumbnail.png"),options: .atomic) }
            try FileManager.default.moveItem(at: staging,to: folder(record.id))
        } catch { try? FileManager.default.removeItem(at: staging); throw error }
        records.append(record); records.sort { a,b in a.capturedAt > b.capturedAt }
        try trim(); onChange?(); return record
    }
    func image(_ id: UUID) throws -> NSImage {
        guard let record = record(id), let image = NSImage(contentsOf: folder(id).appendingPathComponent("original.png")) else { throw CaptureHistoryError.missingCapture }
        image.size = record.size // Preserve annotation coordinates at Retina scale.
        return image
    }
    func marks(_ id: UUID) throws -> [Mark] {
        guard let record = record(id) else { throw CaptureHistoryError.missingCapture }
        return try record.marks.map { try $0.restored() }
    }
    func undoHistory(_ id: UUID) throws -> [[Mark]] {
        guard let record = record(id) else { throw CaptureHistoryError.missingCapture }
        if let history = record.undoHistory { return try history.map { try $0.map { try $0.restored() } } }
        let marks = try self.marks(id)
        return marks.indices.map { Array(marks.prefix($0)) }
    }
    func redoHistory(_ id: UUID) throws -> [[Mark]] {
        guard let record = record(id) else { throw CaptureHistoryError.missingCapture }
        return try (record.redoHistory ?? []).map { try $0.map { try $0.restored() } }
    }
    func thumbnail(_ id: UUID) -> NSImage? {
        NSImage(contentsOf: folder(id).appendingPathComponent("thumbnail.png")) ?? NSImage(contentsOf: folder(id).appendingPathComponent("original.png"))
    }
    func save(_ id: UUID, marks: [Mark], undoHistory: [[Mark]] = [], redoHistory: [[Mark]] = []) throws {
        guard let index = records.firstIndex(where: { $0.id == id }) else { throw CaptureHistoryError.missingCapture }
        var updated = records[index]; updated.marks = marks.map(StoredMark.init)
        updated.undoHistory = undoHistory.map { $0.map(StoredMark.init) }; updated.redoHistory = redoHistory.map { $0.map(StoredMark.init) }
        try encoder.encode(updated).write(to: folder(id).appendingPathComponent("draft.json"),options: .atomic)
        records[index] = updated
    }
    func setLimit(_ value: Int) throws {
        let old = limit; limit = min(500,max(1,value))
        do { try trim() } catch { limit = old; onChange?(); throw error }
        onChange?()
    }
    func clear(moveToTrash: Bool = true) throws {
        if moveToTrash { try FileManager.default.trashItem(at: directory,resultingItemURL: nil) }
        else { try FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory,withIntermediateDirectories: true,attributes: [.posixPermissions: 0o700])
        records = []; onChange?()
    }
    private func thumbnailPNG(_ image: NSImage) -> Data? {
        guard image.size.width > 0, image.size.height > 0,
              let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil,pixelsWide: 240,pixelsHigh: 150,bitsPerSample: 8,samplesPerPixel: 4,hasAlpha: true,isPlanar: false,colorSpaceName: .deviceRGB,bytesPerRow: 0,bitsPerPixel: 0),
              let context = NSGraphicsContext(bitmapImageRep: bitmap) else { return nil }
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = context
        NSColor(calibratedWhite: 0.15,alpha: 1).setFill(); NSRect(x: 0,y: 0,width: 240,height: 150).fill()
        let scale = min(240 / image.size.width,150 / image.size.height)
        let size = NSSize(width: image.size.width * scale,height: image.size.height * scale)
        image.draw(in: NSRect(x: (240-size.width)/2,y: (150-size.height)/2,width: size.width,height: size.height))
        NSGraphicsContext.restoreGraphicsState()
        return bitmap.representation(using: .png,properties: [:])
    }
}

final class CaptureHistoryButton: NSButton {
    override var isFlipped: Bool { false }
    var captureID: UUID?
    var detail = ""
    override func draw(_ dirtyRect: NSRect) {
        let card = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5,dy: 0.5),xRadius: 12,yRadius: 12)
        (cell?.isHighlighted == true ? NSColor.systemIndigo.withAlphaComponent(0.1) : .controlBackgroundColor).setFill(); card.fill()
        NSColor.separatorColor.withAlphaComponent(0.4).setStroke(); card.lineWidth = 1; card.stroke()
        let preview = NSRect(x: 12,y: 68,width: bounds.width-24,height: bounds.height-80)
        NSColor.windowBackgroundColor.setFill(); NSBezierPath(roundedRect: preview,xRadius: 7,yRadius: 7).fill()
        if let image {
            let factor = min((preview.width-12)/image.size.width,(preview.height-12)/image.size.height)
            let size = NSSize(width: image.size.width*factor,height: image.size.height*factor)
            image.draw(in: NSRect(x: preview.midX-size.width/2,y: preview.midY-size.height/2,width: size.width,height: size.height))
        }
        (title as NSString).draw(in: NSRect(x: 14,y: 37,width: bounds.width-28,height: 20),withAttributes: [.font: NSFont.systemFont(ofSize: 13,weight: .semibold),.foregroundColor: NSColor.labelColor])
        (detail as NSString).draw(in: NSRect(x: 14,y: 15,width: bounds.width-28,height: 18),withAttributes: [.font: NSFont.systemFont(ofSize: 11),.foregroundColor: NSColor.secondaryLabelColor])
    }
}
final class CaptureHistoryList: NSView { override var isFlipped: Bool { true } }
final class CaptureHistoryController: NSWindowController, NSWindowDelegate {
    let store: CaptureStore
    let open: (UUID) -> Void
    let scroll = NSScrollView()
    let list = CaptureHistoryList()
    let summary = NSTextField(labelWithString: "")
    init(store: CaptureStore, open: @escaping (UUID) -> Void) {
        self.store = store; self.open = open
        let window = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 840,height: 650),styleMask: [.titled,.closable,.resizable],backing: .buffered,defer: false)
        super.init(window: window); window.contentView = BackgroundView(frame: window.contentView!.bounds); window.title = "Shotlight — Recent Captures"; window.center(); window.isReleasedWhenClosed = false; window.minSize = NSSize(width: 580,height: 300); window.delegate = self
        scroll.hasVerticalScroller = true; scroll.drawsBackground = false; scroll.documentView = list
        list.autoresizingMask = [.width]
        summary.textColor = .secondaryLabelColor; summary.font = .systemFont(ofSize: 12)
        let heading = ShotlightUI.label("Recent captures",size: 26,weight: .semibold)
        for view in [heading,summary,scroll] { view.translatesAutoresizingMaskIntoConstraints = false; window.contentView!.addSubview(view) }
        NSLayoutConstraint.activate([heading.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor,constant: 26),heading.topAnchor.constraint(equalTo: window.contentView!.topAnchor,constant: 24),summary.leadingAnchor.constraint(equalTo: heading.leadingAnchor),summary.topAnchor.constraint(equalTo: heading.bottomAnchor,constant: 8),scroll.topAnchor.constraint(equalTo: summary.bottomAnchor,constant: 22),scroll.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor),scroll.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor),scroll.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor)])
        window.contentView?.layoutSubtreeIfNeeded(); reload()
    }
    func windowDidResize(_ notification: Notification) { reload() }
    required init?(coder: NSCoder) { fatalError() }
    func reload() {
        let origin = scroll.contentView.bounds.origin
        list.subviews.forEach { $0.removeFromSuperview() }
        summary.stringValue = "\(store.records.count) captures · Automatically kept on this Mac · Limit \(store.limit)"
        let width = max(540,scroll.contentSize.width)
        let columns = max(1,Int((width-32)/252)), cardWidth = (width-32-CGFloat(columns-1)*12)/CGFloat(columns)
        let rows = (store.records.count+columns-1)/columns
        list.frame = NSRect(x: 0,y: 0,width: width,height: max(120,CGFloat(rows)*228+20))
        if store.records.isEmpty {
            let label = NSTextField(wrappingLabelWithString: "No captures yet. Your next screenshot will appear here automatically.")
            label.frame = NSRect(x: 24,y: 30,width: width-48,height: 60); label.autoresizingMask = [.width]; list.addSubview(label)
        }
        for (index,record) in store.records.enumerated() {
            let row = CaptureHistoryButton(title: record.displayDate,target: self,action: #selector(openCapture(_:)))
            row.detail = "\(Int(record.imageWidth)) × \(Int(record.imageHeight)) · \(record.marks.count) annotations"; row.isBordered = false
            row.captureID = record.id; row.image = store.thumbnail(record.id); row.image?.size = NSSize(width: 120,height: 75)
            row.imagePosition = .imageLeft; row.imageScaling = .scaleProportionallyDown; row.alignment = .left; row.bezelStyle = .regularSquare; row.font = .systemFont(ofSize: 13)
            row.frame = NSRect(x: 16+CGFloat(index%columns)*(cardWidth+12),y: CGFloat(index/columns)*228,width: cardWidth,height: 214)
            row.setAccessibilityLabel("Capture \(record.displayDate)"); list.addSubview(row)
        }
        scroll.contentView.scroll(to: NSPoint(x: 0,y: min(origin.y,max(0,list.frame.height-scroll.contentSize.height))))
        scroll.reflectScrolledClipView(scroll.contentView)
    }
    @objc func openCapture(_ sender: CaptureHistoryButton) { if let id = sender.captureID { open(id) } }
}

func historyCheck() -> String {
    let root = FileManager.default.temporaryDirectory.appendingPathComponent("Shotlight-history-test-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: root) }
    do {
        let store = try CaptureStore(directory: root,limit: 3)
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil,pixelsWide: 400,pixelsHigh: 200,bitsPerSample: 8,samplesPerPixel: 4,hasAlpha: true,isPlanar: false,colorSpaceName: .deviceRGB,bytesPerRow: 0,bitsPerPixel: 0)!
        bitmap.size = NSSize(width: 200,height: 100)
        let image = NSImage(size: bitmap.size); image.addRepresentation(bitmap)
        let first = try store.add(image: image,capturedAt: Date(timeIntervalSince1970: 1))
        let second = try store.add(image: image,capturedAt: Date(timeIntervalSince1970: 2))
        let third = try store.add(image: image,capturedAt: Date(timeIntervalSince1970: 3))
        let editor = EditorController(image: image,captureID: second.id,store: store)
        editor.canvas.marks = [Mark(tool: .pen,points: [NSPoint(x: 4,y: 8),NSPoint(x: 12,y: 18)],color: .blue,width: 3)]
        editor.canvas.beginTextEditing(at: NSPoint(x: 20,y: 90)); editor.canvas.textEditor?.string = "Retained while typing"
        guard editor.flushArchive(), editor.canvas.textEditor != nil else { return "FAIL: active draft autosave" }
        let restarted = try CaptureStore(directory: root,limit: 3)
        let restored = try restarted.marks(second.id)
        guard try restarted.undoHistory(second.id).last?.count == 1 else { return "FAIL: active text undo persistence" }
        let restoredImage = try restarted.image(second.id)
        guard restarted.records.map({ $0.id }) == [third.id,second.id,first.id],restored.count == 2,restored[1].text == "Retained while typing",
              restored[0].points[1] == NSPoint(x: 12,y: 18),restored[0].width == 3,restoredImage.size == image.size,restoredImage.representations.first?.pixelsWide == 400 else { return "FAIL: restart and editable draft restoration" }
        let originalTop = restored[1].points[0].y
        let reopen = EditorController(image: restoredImage,captureID: second.id,store: restarted)
        reopen.canvas.marks = restored; reopen.canvas.history = try restarted.undoHistory(second.id); reopen.canvas.undone = try restarted.redoHistory(second.id)
        reopen.canvas.beginTextEditing(at: .zero,index: 1); reopen.canvas.textEditor?.string = "Edited after reopening"; reopen.canvas.finishTextEditing()
        guard reopen.flushArchive(),try restarted.marks(second.id)[1].text == "Edited after reopening" else { return "FAIL: re-edit retained text" }
        reopen.canvas.undoMark(); guard reopen.canvas.marks[1].text == "Retained while typing",reopen.canvas.marks[1].points[0].y == originalTop else { return "FAIL: restored text undo" }
        let clipboard = NSPasteboard(name: NSPasteboard.Name("Shotlight-history-\(UUID().uuidString)"))
        defer { clipboard.releaseGlobally() }
        guard reopen.copyAndDismiss(to: clipboard),try CaptureStore(directory: root,limit: 3).marks(second.id)[1].text == "Retained while typing" else { return "FAIL: copy-close retention" }
        let retained = try CaptureStore(directory: root,limit: 3)
        let undoneEditor = EditorController(image: try retained.image(second.id),captureID: second.id,store: retained)
        undoneEditor.canvas.marks = try retained.marks(second.id); undoneEditor.canvas.history = try retained.undoHistory(second.id); undoneEditor.canvas.undone = try retained.redoHistory(second.id)
        guard !undoneEditor.canvas.undone.isEmpty else { return "FAIL: redo persistence" }
        undoneEditor.canvas.redoMark(); guard undoneEditor.canvas.marks[1].text == "Edited after reopening" else { return "FAIL: restored redo" }
        undoneEditor.detachArchive()
        let fourth = try store.add(image: image,capturedAt: Date(timeIntervalSince1970: 4))
        guard store.records.map({ $0.id }) == [fourth.id,third.id,second.id],!FileManager.default.fileExists(atPath: root.appendingPathComponent(first.id.uuidString).path) else { return "FAIL: oldest capture eviction" }
        try editor.loadCapture(third.id,store: store)
        guard editor.captureID == third.id, editor.canvas.marks.isEmpty else { return "FAIL: switching captures" }
        try store.setLimit(1)
        guard store.records.map({ $0.id }) == [fourth.id],try CaptureStore(directory: root,limit: 1).records.count == 1 else { return "FAIL: history limit" }
        try store.clear(moveToTrash: false)
        guard store.records.isEmpty,try CaptureStore(directory: root).records.isEmpty else { return "FAIL: clearing history" }
        editor.detachArchive(); reopen.detachArchive()
        return "PASS: restart, editable drafts, copy-close, navigation, retention, clear."
    } catch { return "FAIL: history \(error.localizedDescription)" }
}
