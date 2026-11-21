// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "EchoDesktop",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "EchoDesktop", targets: ["EchoDesktop"]),
        .executable(name: "EchoRender", targets: ["EchoRender"]),
    ],
    targets: [
        .target(name: "EchoRendering", exclude: ["README.md"]),
        .executableTarget(name: "EchoDesktop", dependencies: ["EchoRendering"]),
        .executableTarget(name: "EchoRender", dependencies: ["EchoRendering"]),
        .testTarget(name: "EchoDesktopTests", dependencies: ["EchoDesktop", "EchoRendering"])
    ]
)
