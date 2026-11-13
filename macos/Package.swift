// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "EchoDesktop",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "EchoDesktop", targets: ["EchoDesktop"])],
    targets: [
        .executableTarget(name: "EchoDesktop"),
        .testTarget(name: "EchoDesktopTests", dependencies: ["EchoDesktop"])
    ]
)
