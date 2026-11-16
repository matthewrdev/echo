import AppKit
import SwiftUI
import XCTest

@testable import EchoDesktop

final class SavedWorkspaceViewerTests: XCTestCase {
    @MainActor
    func testSavedFeatureControlsAndSharedActivityDivider() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let path = environment["ECHO_TEST_UI_PROJECT"], let runID = environment["ECHO_TEST_UI_RUN"],
            let cli = environment["ECHO_TEST_CLI"], let output = environment["ECHO_TEST_UI_RENDER"]
        else { throw XCTSkip("Set a saved project, run, CLI and render folder to inspect the native workspace") }
        let folder = URL(fileURLWithPath: output)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let domain = "echo-workspace-test-\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: domain))
        defer { defaults.removePersistentDomain(forName: domain) }
        let workspace = WorkspaceViewModel(
            client: CLIProjectClient(cli: ProcessCLIClient(executable: URL(fileURLWithPath: cli))), project: path,
            logReader: RunLogReader(formatter: RunEventFormatter()), eventFormatter: RunEventFormatter(),
            recentProjectStore: UserDefaultsRecentProjectStore(defaults: defaults), clipboard: SystemTextClipboard())
        workspace.selectedRun = runID
        workspace.selectedStage = .features
        await workspace.reload()
        let run = try XCTUnwrap(workspace.displayedRun, workspace.status)
        let json = ArtifactJSONReader()
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        let model = ArtifactViewModel(reader: reader)
        await model.configure(root: path, run: run, stage: .features)
        XCTAssertNotNil(model.presentation?.features)
        let host = NSHostingView(
            rootView: StageWorkspaceView(workspace: workspace, artifacts: model)
                .defaultAppStorage(defaults).environment(\.colorScheme, .light).background(Color.white))
        let window = NSWindow(
            contentRect: CGRect(x: 0, y: 0, width: 1200, height: 850),
            styleMask: [.titled, .resizable], backing: .buffered, defer: false)
        window.contentView = host
        window.orderFront(nil)
        defer { window.orderOut(nil) }
        host.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(300))
        let split = try XCTUnwrap(horizontalSplit(in: host))
        XCTAssertEqual(split.arrangedSubviews.count, 2)
        split.setPosition(600, ofDividerAt: 0)
        host.layoutSubtreeIfNeeded()
        let firstHeight = split.arrangedSubviews[0].frame.height
        try snapshot(host, to: folder.appendingPathComponent("features-all.png"))
        let features = try XCTUnwrap(model.presentation?.features)
        let selectedFeature = features.features.indices.min {
            hypot(
                features.features[$0].x - Double(features.photo.width) / 2,
                features.features[$0].y - Double(features.photo.height) / 2)
                < hypot(
                    features.features[$1].x - Double(features.photo.width) / 2,
                    features.features[$1].y - Double(features.photo.height) / 2)
        }
        let focusedFeatures = NSHostingView(
            rootView: CanvasViewport(
                renderer: FeatureCanvasRenderer(data: features, selected: selectedFeature) { _ in },
                controller: CanvasViewportController()))
        focusedFeatures.frame = CGRect(x: 0, y: 0, width: 1200, height: 700)
        try snapshot(focusedFeatures, to: folder.appendingPathComponent("features-selected.png"))
        split.setPosition(420, ofDividerAt: 0)
        host.layoutSubtreeIfNeeded()
        XCTAssertGreaterThan(firstHeight - split.arrangedSubviews[0].frame.height, 100)
        XCTAssertGreaterThan(split.arrangedSubviews[1].frame.height, 300)
        defaults.set(0.35, forKey: "features.overlayOpacity")
        try await Task.sleep(for: .milliseconds(150))
        try snapshot(host, to: folder.appendingPathComponent("features-faded-expanded-activity.png"))
        defaults.set(false, forKey: "features.showsPoints")
        defaults.set(false, forKey: "features.showsJunctions")
        defaults.set(1.0, forKey: "features.overlayOpacity")
        split.setPosition(640, ofDividerAt: 0)
        try await Task.sleep(for: .milliseconds(150))
        try snapshot(host, to: folder.appendingPathComponent("features-lines.png"))
        try clickActivityHeader(split.arrangedSubviews[1], in: window)
        try await Task.sleep(for: .milliseconds(150))
        host.layoutSubtreeIfNeeded()
        XCTAssertEqual(split.arrangedSubviews[1].frame.height, 36, accuracy: 2)
        try snapshot(host, to: folder.appendingPathComponent("features-collapsed-activity.png"))
        try clickActivityHeader(split.arrangedSubviews[1], in: window)
        try await Task.sleep(for: .milliseconds(150))
        host.layoutSubtreeIfNeeded()
        XCTAssertGreaterThanOrEqual(split.arrangedSubviews[1].frame.height, 140)
        if run.artifacts["matching"] != nil {
            let height = split.arrangedSubviews[1].frame.height
            workspace.selectedStage = .matching
            await model.configure(root: path, run: run, stage: .matching)
            try await Task.sleep(for: .milliseconds(300))
            XCTAssertNotNil(model.presentation?.matches)
            XCTAssertEqual(split.arrangedSubviews[1].frame.height, height, accuracy: 2)
            try snapshot(host, to: folder.appendingPathComponent("matching-shared-divider.png"))
            let matches = try XCTUnwrap(model.presentation?.matches?.matches)
            let point = try XCTUnwrap(matches.firstIndex { $0.kind != "line" && $0.kind != "junction" })
            model.selectedMatchIndex = point
            try await Task.sleep(for: .milliseconds(150))
            try snapshot(host, to: folder.appendingPathComponent("matching-dotted-link.png"))
            defaults.set(0.35, forKey: "matching.overlayOpacity")
            try await Task.sleep(for: .milliseconds(150))
            try snapshot(host, to: folder.appendingPathComponent("matching-faded.png"))
            defaults.set(false, forKey: "matching.showsPoints")
            defaults.set(false, forKey: "matching.showsJunctions")
            defaults.set(1.0, forKey: "matching.overlayOpacity")
            try await Task.sleep(for: .milliseconds(150))
            XCTAssertNil(model.selectedMatchIndex, "Hiding the selected match closes its inspector")
            try snapshot(host, to: folder.appendingPathComponent("matching-lines.png"))
        }
    }

    @MainActor
    private func clickActivityHeader(_ panel: NSView, in window: NSWindow) throws {
        let point = panel.convert(CGPoint(x: 50, y: panel.isFlipped ? 18 : panel.bounds.height - 18), to: nil)
        for type in [NSEvent.EventType.leftMouseDown, .leftMouseUp] {
            let event = try XCTUnwrap(
                NSEvent.mouseEvent(
                    with: type, location: point, modifierFlags: [], timestamp: ProcessInfo.processInfo.systemUptime,
                    windowNumber: window.windowNumber, context: nil, eventNumber: 0, clickCount: 1, pressure: 1))
            window.sendEvent(event)
        }
    }

    @MainActor
    private func horizontalSplit(in view: NSView) -> NSSplitView? {
        if let split = view as? NSSplitView, !split.isVertical { return split }
        for child in view.subviews {
            if let split = horizontalSplit(in: child) { return split }
        }
        return nil
    }

    @MainActor
    private func snapshot(_ view: NSView, to file: URL) throws {
        view.layoutSubtreeIfNeeded()
        let bitmap = try XCTUnwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: bitmap)
        try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(to: file)
    }
}
