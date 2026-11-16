import AppKit
import Metal
import SceneKit
import XCTest

@testable import EchoDesktop

final class CloudCameraInspectionTests: XCTestCase {
    func testFrustumUsesAlignedPoseAndRejectsPointsOutsideOrBehindThePhoto() throws {
        let photo = try photograph()
        let pose = CameraPose(
            imageId: photo.id, centre: [10, 20, 30], rotation: [0, 0, 1, 0, 1, 0, -1, 0, 0], focalPixels: 50,
            source: "aligned")
        let frustum = try XCTUnwrap(CameraFrustum(camera: pose, photo: photo))
        XCTAssertTrue(frustum.contains(SIMD3(12, 20, 30)))
        XCTAssertTrue(frustum.contains(SIMD3(12, 21.5, 28)))
        XCTAssertFalse(frustum.contains(SIMD3(12, 21.7, 30)))
        XCTAssertFalse(frustum.contains(SIMD3(12, 20, 27.9)))
        XCTAssertFalse(frustum.contains(SIMD3(9, 20, 30)))
        XCTAssertFalse(frustum.contains(SIMD3(10, 20, 30)))
        let fallback = try XCTUnwrap(CameraFrustum(camera: camera(x: 0, focal: 0), photo: photo))
        XCTAssertEqual(fallback.halfWidth, 1)
        XCTAssertEqual(fallback.halfHeight, 0.8)
    }

    @MainActor
    func testSourceCameraViewMatchesThePhotoOrientationAndLens() throws {
        let frustum = try XCTUnwrap(CameraFrustum(camera: camera(x: 0, z: 0, focal: 50), photo: photograph()))
        let viewpoint = SourceCameraViewpoint().make(frustum: frustum, extent: SCNVector3(10, 10, 10))
        let scene = SCNScene()
        scene.rootNode.addChildNode(viewpoint)
        let view = SCNView(frame: NSRect(x: 0, y: 0, width: 200, height: 160))
        view.scene = scene
        view.pointOfView = viewpoint
        _ = view.snapshot()
        let centre = view.projectPoint(SCNVector3(0, 0, 2))
        let topLeft = view.projectPoint(SCNVector3(-2, -1.6, 2))
        let bottomRight = view.projectPoint(SCNVector3(2, 1.6, 2))
        XCTAssertEqual(centre.x, 100, accuracy: 0.01)
        XCTAssertEqual(centre.y, 80, accuracy: 0.01)
        XCTAssertEqual(topLeft.x, 0, accuracy: 0.01)
        XCTAssertEqual(topLeft.y, 160, accuracy: 0.01)
        XCTAssertEqual(bottomRight.x, 200, accuracy: 0.01)
        XCTAssertEqual(bottomRight.y, 0, accuracy: 0.01)
    }

    @MainActor
    func testCameraHighlightRendersSolidAndFadedPointsAndClearsWithoutCopyingVertices() throws {
        let device = try XCTUnwrap(MTLCreateSystemDefaultDevice())
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [
                CloudPoint(position: [-1, 0, 0], colour: Data([240, 20, 20]), error: 0),
                CloudPoint(position: [1, 0, 0], colour: Data([20, 20, 240]), error: 0),
            ], photos: [], root: URL(fileURLWithPath: "/tmp"))
        scene.background.contents = NSColor.white
        let artifact = try XCTUnwrap(scene.rootNode.childNode(withName: "artifact", recursively: false))
        let original = try XCTUnwrap(artifact.geometry)
        let viewpoint = try XCTUnwrap(scene.rootNode.childNode(withName: "viewer-camera", recursively: false))
        viewpoint.position = SCNVector3(0, 0, 10)
        viewpoint.look(at: SCNVector3Zero)
        viewpoint.camera?.usesOrthographicProjection = true
        viewpoint.camera?.orthographicScale = 3
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = viewpoint
        let highlighter = CloudCameraHighlight()
        let red: (NSColor) -> Bool = { $0.redComponent - $0.blueComponent > 0.3 }
        let blue: (NSColor) -> Bool = { $0.blueComponent - $0.redComponent > 0.3 }
        let faintBlue: (NSColor) -> Bool = {
            let difference = $0.blueComponent - $0.redComponent
            return difference > 0.02 && difference < 0.2
        }
        highlighter.show(try XCTUnwrap(CameraFrustum(camera: camera(x: -1), photo: photograph())), on: artifact)
        XCTAssertTrue(artifact.geometry?.sources.first === original.sources.first)
        let focused = try snapshot(renderer)
        XCTAssertGreaterThan(countPixels(focused, matching: red), 0)
        XCTAssertEqual(countPixels(focused, matching: blue), 0)
        XCTAssertGreaterThan(countPixels(focused, matching: faintBlue), 0)
        XCTAssertEqual(
            artifact.childNode(withName: "cloud-context", recursively: false)?.geometry?.firstMaterial?
                .writesToDepthBuffer, false)

        highlighter.show(try XCTUnwrap(CameraFrustum(camera: camera(x: 1), photo: photograph())), on: artifact)
        let switched = try snapshot(renderer)
        XCTAssertEqual(countPixels(switched, matching: red), 0)
        XCTAssertGreaterThan(countPixels(switched, matching: blue), 0)
        highlighter.clear()
        XCTAssertTrue(artifact.geometry === original)
        XCTAssertNil(artifact.childNode(withName: "cloud-context", recursively: false))
        let cleared = try snapshot(renderer)
        XCTAssertGreaterThan(countPixels(cleared, matching: red), 0)
        XCTAssertGreaterThan(countPixels(cleared, matching: blue), 0)
    }

    @MainActor
    func testDenseCameraPickingAndSelectionSurviveAReplacementScene() throws {
        let photo = try photograph()
        let pose = camera(x: -1)
        let view = ArtifactSceneView(frame: NSRect(x: 0, y: 0, width: 800, height: 600))
        view.cloudInspection = CloudInspectionData(root: URL(fileURLWithPath: "/tmp"), cameras: [pose], photos: [photo])
        for _ in 0..<2 {
            view.scene = NativeSceneFactory().make(
                reconstruction: nil, points: [CloudPoint(position: [0, 0, 0], colour: nil, error: 0)],
                photos: [], root: URL(fileURLWithPath: "/tmp"))
            view.pointOfView = view.scene?.rootNode.childNode(withName: "viewer-camera", recursively: false)
            view.pointOfView?.position = SCNVector3(0, 0, 10)
            view.pointOfView?.look(at: SCNVector3Zero)
            _ = view.snapshot()
            let screen = view.projectPoint(SCNVector3(-1, 0, -2))
            let location = NSPoint(x: screen.x, y: view.isFlipped ? view.bounds.height - screen.y : screen.y)
            XCTAssertEqual(view.pick(at: location, cameras: [pose.imageId: pose], tracks: []), .camera(photo.id))
            view.highlight(.camera(photo.id))
            XCTAssertNotNil(view.scene?.rootNode.childNode(withName: "cloud-context", recursively: true))
        }
        view.highlight(nil)
        XCTAssertNil(view.scene?.rootNode.childNode(withName: "cloud-context", recursively: true))
    }

    private func photograph() throws -> Photo {
        try JSONDecoder().decode(
            Photo.self,
            from: Data(
                """
                {"id":"photo","name":"photo.jpg","source":"photo.jpg","preview":"preview.jpg",
                 "width":100,"height":80,"camera":"test","focalPixels":50,"enabled":true,"sets":[],"metadata":{}}
                """.utf8))
    }

    private func camera(x: Double, z: Double = -2, focal: Double = 500) -> CameraPose {
        CameraPose(
            imageId: "photo", centre: [x, 0, z], rotation: [1, 0, 0, 0, 1, 0, 0, 0, 1], focalPixels: focal,
            source: "test")
    }

    private func snapshot(_ renderer: SCNRenderer) throws -> NSBitmapImageRep {
        let image = renderer.snapshot(atTime: 0, with: CGSize(width: 300, height: 180), antialiasingMode: .none)
        return try XCTUnwrap(NSBitmapImageRep(data: XCTUnwrap(image.tiffRepresentation)))
    }

    private func countPixels(_ bitmap: NSBitmapImageRep, matching predicate: (NSColor) -> Bool) -> Int {
        var count = 0
        for y in 0..<bitmap.pixelsHigh {
            for x in 0..<bitmap.pixelsWide {
                if let color = bitmap.colorAt(x: x, y: y)?.usingColorSpace(.deviceRGB), predicate(color) {
                    count += 1
                }
            }
        }
        return count
    }
}
