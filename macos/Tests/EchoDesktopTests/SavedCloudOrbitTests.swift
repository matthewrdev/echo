import AppKit
import SceneKit
import XCTest

@testable import EchoDesktop

final class SavedCloudOrbitTests: XCTestCase {
    @MainActor
    func testSavedSparseAndDenseCloudOrbit() async throws {
        let environment = ProcessInfo.processInfo.environment
        guard let project = environment["ECHO_TEST_ORBIT_PROJECT"], let runID = environment["ECHO_TEST_ORBIT_RUN"]
        else { throw XCTSkip("Set ECHO_TEST_ORBIT_PROJECT and ECHO_TEST_ORBIT_RUN to inspect saved cloud orbit.") }
        let root = URL(fileURLWithPath: project)
        let json = ArtifactJSONReader()
        let run: RunData = try json.read(root.appendingPathComponent("runs/\(runID)/run.json"))
        let reader = SavedArtifactReader(
            scenes: NativeSceneFactory(), images: ArtifactImageRenderer(), glb: GlbAssetReader(),
            featureMatches: FeatureMatchIndexer(json: json),
            depths: SavedDepthArtifactReader(maps: DepthMapReader(), images: ArtifactImageRenderer(), json: json),
            fusion: SavedFusionArtifactReader(json: json, scenes: NativeSceneFactory()), json: json)
        for stage in [PipelineStage.alignment, .cloud] {
            let presentation = try await reader.load(
                root: root, run: run, stage: stage, unit: nil, mode: "All support", slice: 0.5)
            let view = ArtifactSceneView(frame: CGRect(x: 0, y: 0, width: 1200, height: 800))
            view.scene = try XCTUnwrap(presentation.scene)
            view.pointOfView = view.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
            view.fitArtifact()
            view.allowsCameraControl = true
            let window = NSWindow(contentRect: view.frame, styleMask: [.titled], backing: .buffered, defer: false)
            window.contentView = view
            window.orderFront(nil)
            defer { window.orderOut(nil) }
            try await Task.sleep(for: .milliseconds(200))
            let node = try XCTUnwrap(view.scene?.rootNode.childNode(withName: "artifact", recursively: false))
            let source = try XCTUnwrap(node.geometry?.sources(for: .vertex).first)
            var location = NSPoint.zero
            var distance = Double.infinity
            source.data.withUnsafeBytes { bytes in
                for index in stride(from: 0, to: source.vectorCount, by: max(1, source.vectorCount / 1000)) {
                    let offset = source.dataOffset + index * source.dataStride
                    func component(_ axis: Int) -> Double {
                        let start = offset + axis * source.bytesPerComponent
                        return source.bytesPerComponent == 4
                            ? Double(bytes.loadUnaligned(fromByteOffset: start, as: Float.self))
                            : bytes.loadUnaligned(fromByteOffset: start, as: Double.self)
                    }
                    let point = view.projectPoint(
                        node.convertPosition(SCNVector3(component(0), component(1), component(2)), to: nil))
                    guard point.z > 0, point.z < 1 else { continue }
                    let delta = hypot(point.x - view.bounds.midX, point.y - view.bounds.midY)
                    if delta < distance {
                        location = NSPoint(x: point.x, y: point.y)
                        distance = delta
                    }
                }
            }
            XCTAssertTrue(view.bounds.contains(location))
            let orbit = ArtifactOrbitInteraction()
            let original = view.pointOfView?.simdWorldTransform
            let start = ContinuousClock.now
            XCTAssertTrue(orbit.begin(at: location, in: view))
            let elapsed = start.duration(to: .now)
            print("\(stage.rawValue): picked from \(source.vectorCount) points in \(elapsed)")
            XCTAssertLessThan(elapsed, .milliseconds(500), "Picking must not freeze the viewport")
            XCTAssertEqual(view.pointOfView?.simdWorldTransform, original)
            let pivot = view.defaultCameraController.target
            let projected = view.projectPoint(pivot)
            XCTAssertLessThan(hypot(projected.x - location.x, projected.y - location.y), 13)
            orbit.drag(to: NSPoint(x: location.x + 100, y: location.y + 30), in: view)
            let moved = view.projectPoint(pivot)
            XCTAssertEqual(moved.x, projected.x, accuracy: 0.01)
            XCTAssertEqual(moved.y, projected.y, accuracy: 0.01)
            orbit.end(in: view)
            if let output = environment["ECHO_TEST_ORBIT_RENDER"] {
                let folder = URL(fileURLWithPath: output)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let bitmap = try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(view.snapshot().tiffRepresentation)))
                try XCTUnwrap(bitmap.representation(using: .png, properties: [:])).write(
                    to: folder.appendingPathComponent("\(stage.rawValue)-orbit.png"))
            }
        }
    }
}
