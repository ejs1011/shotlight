// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "Shotlight", platforms: [.macOS(.v13)], products: [.executable(name: "Shotlight", targets: ["Shotlight"])], targets: [.executableTarget(name: "Shotlight", linkerSettings: [.linkedFramework("AppKit"), .linkedFramework("Carbon"), .linkedFramework("ScreenCaptureKit")])])
