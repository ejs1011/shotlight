import AppKit

final class CompactButton: NSButton {
    var primary = false
    private var hovered = false
    override var state: NSControl.StateValue { didSet { needsDisplay = true } }
    override func updateTrackingAreas() {
        for area in trackingAreas { removeTrackingArea(area) }
        addTrackingArea(NSTrackingArea(rect: bounds,options: [.mouseEnteredAndExited,.activeInKeyWindow,.inVisibleRect],owner: self,userInfo: nil))
        super.updateTrackingAreas()
    }
    override func mouseEntered(with event: NSEvent) { hovered = true; needsDisplay = true }
    override func mouseExited(with event: NSEvent) { hovered = false; needsDisplay = true }
    override func draw(_ dirtyRect: NSRect) {
        if primary || state == .on || hovered {
            let fill: NSColor = primary ? .systemIndigo : state == .on ? .systemIndigo.withAlphaComponent(0.14) : .labelColor.withAlphaComponent(0.06)
            fill.setFill(); NSBezierPath(roundedRect: bounds.insetBy(dx: 1,dy: 1),xRadius: 8,yRadius: 8).fill()
        }
        contentTintColor = primary ? .white : state == .on ? .systemIndigo : .labelColor
        super.draw(dirtyRect)
    }
}

final class BackgroundView: NSView {
    override func draw(_ dirtyRect: NSRect) { NSColor.windowBackgroundColor.setFill(); bounds.fill() }
}

final class SurfaceView: NSView {
    var radius: CGFloat = 12
    override func draw(_ dirtyRect: NSRect) {
        NSColor.controlBackgroundColor.setFill(); let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5,dy: 0.5),xRadius: radius,yRadius: radius); path.fill()
        NSColor.separatorColor.withAlphaComponent(0.45).setStroke(); path.lineWidth = 1; path.stroke()
    }
}

final class CenteredClipView: NSClipView {
    override func constrainBoundsRect(_ proposedBounds: NSRect) -> NSRect {
        var result = super.constrainBoundsRect(proposedBounds)
        if let documentView {
            if documentView.frame.width < result.width { result.origin.x = (documentView.frame.width-result.width)/2 }
            if documentView.frame.height < result.height { result.origin.y = (documentView.frame.height-result.height)/2 }
        }
        return result
    }
}

enum ShotlightUI {
    static func label(_ text: String, size: CGFloat = 13, weight: NSFont.Weight = .regular, secondary: Bool = false) -> NSTextField {
        let label = NSTextField(labelWithString: text); label.font = .systemFont(ofSize: size,weight: weight); label.textColor = secondary ? .secondaryLabelColor : .labelColor; return label
    }
    static func icon(_ symbol: String, label: String, target: AnyObject?, action: Selector) -> CompactButton {
        let button = CompactButton(title: "",target: target,action: action)
        button.image = NSImage(systemSymbolName: symbol,accessibilityDescription: label)?.withSymbolConfiguration(.init(pointSize: 16,weight: .medium))
        button.imagePosition = .imageOnly; button.isBordered = false; button.bezelStyle = .regularSquare
        button.toolTip = label; button.setAccessibilityLabel(label); button.keyEquivalentModifierMask = .command
        button.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([button.widthAnchor.constraint(equalToConstant: 36),button.heightAnchor.constraint(equalToConstant: 36)])
        return button
    }
    static func divider() -> NSView {
        let view = NSBox(); view.boxType = .separator; view.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([view.widthAnchor.constraint(equalToConstant: 1),view.heightAnchor.constraint(equalToConstant: 22)]); return view
    }
    static func card(_ content: NSView, padding: CGFloat = 20) -> SurfaceView {
        let card = SurfaceView(); content.translatesAutoresizingMaskIntoConstraints = false; card.addSubview(content)
        NSLayoutConstraint.activate([content.leadingAnchor.constraint(equalTo: card.leadingAnchor,constant: padding),content.trailingAnchor.constraint(equalTo: card.trailingAnchor,constant: -padding),content.topAnchor.constraint(equalTo: card.topAnchor,constant: padding),content.bottomAnchor.constraint(equalTo: card.bottomAnchor,constant: -padding)])
        return card
    }
}
