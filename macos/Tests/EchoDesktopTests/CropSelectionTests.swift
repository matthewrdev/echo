import AppKit
import SceneKit
import XCTest
import simd

@testable import EchoDesktop

final class CropSelectionTests: XCTestCase {
    func testOldSettingsDefaultToExcludeAndCropEditsInvalidateFusion() throws {
        let legacy = """
            {"id":"legacy","enabled":true,"shape":"box","centreX":0,"centreY":0,"centreZ":0,
             "width":1,"height":1,"depth":1,"radius":0.5}
            """
        let region = try JSONDecoder().decode(ExclusionRegion.self, from: Data(legacy.utf8))
        XCTAssertEqual(region.mode, .exclude)
        XCTAssertEqual(region.rotationY, 0)
        var original = ProcessingSettings()
        original.exclusionRegions = [region]
        var changed = original
        changed.exclusionRegions[0].mode = .include
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .fusion)
        changed = original
        changed.exclusionRegions[0].rotationY = 45
        XCTAssertEqual(ProcessingImpact().firstChangedStage(from: original, to: changed), .fusion)
        let decoded = try JSONDecoder().decode(ProcessingSettings.self, from: JSONEncoder().encode(changed))
        XCTAssertEqual(decoded.exclusionRegions, changed.exclusionRegions)
        changed.exclusionRegions[0].rotationY = .infinity
        XCTAssertFalse(changed.exclusionRegions[0].isValid)
    }

    func testVerticalBoxRotationMatchesProcessingAndCylindersStayUpright() {
        var region = ExclusionRegion()
        region.width = 4
        region.height = 0.5
        region.depth = 0.5
        region.centreX = 5
        region.rotationY = 45
        let geometry = CropRegionGeometry(region)
        XCTAssertTrue(geometry.contains(SIMD3(6.060660171779821, 0, -1.0606601717798212)))
        XCTAssertFalse(geometry.contains(SIMD3(6.5, 0, 0)))
        XCTAssertFalse(geometry.contains(SIMD3(6.060660171779821, 0.251, -1.0606601717798212)))
        region.shape = .cylinder
        region.centreX = 0
        region.height = 4
        region.radius = 0.25
        XCTAssertTrue(CropRegionGeometry(region).contains(SIMD3(0, 1.5, 0)))
        XCTAssertFalse(CropRegionGeometry(region).contains(SIMD3(1.5, 0, 0)))
        XCTAssertEqual(region.orientation.angle, 0)
    }

    @MainActor
    func testCountsUseIncludeUnionExclusionPrecedenceAndEnabledState() throws {
        let scene = NativeSceneFactory().make(
            reconstruction: nil,
            points: [0.0, 1, 4, 5].map { CloudPoint(position: [$0, 0, 0], colour: nil, error: 0) },
            photos: [], root: URL(fileURLWithPath: "/tmp"))
        let counter = CropPointCounter(scene: scene)
        XCTAssertEqual(try counter.count(regions: []), CropPointCounts(included: 4))
        var include = ExclusionRegion()
        include.mode = .include
        include.width = 4
        var second = ExclusionRegion()
        second.mode = .include
        second.centreX = 5
        second.width = 2
        var exclude = ExclusionRegion()
        exclude.mode = .exclude
        XCTAssertEqual(try counter.count(regions: [include]), CropPointCounts(included: 2, excluded: 2))
        XCTAssertEqual(try counter.count(regions: [include, exclude]), CropPointCounts(included: 1, excluded: 3))
        XCTAssertEqual(
            try counter.count(regions: [include, second, exclude]), CropPointCounts(included: 3, excluded: 1))
        include.enabled = false
        second.enabled = false
        XCTAssertEqual(try counter.count(regions: [include, second]), CropPointCounts(included: 4))
    }

    @MainActor
    func testCountsReadInterleavedVerticesAndNestedWorldTransforms() throws {
        let values: [Float] = [99, 0, 0, 0, 99, 99, 1, 0, 0, 99, 99, 2, 0, 0, 99]
        let source = SCNGeometrySource(
            data: values.withUnsafeBufferPointer { Data(buffer: $0) }, semantic: .vertex,
            vectorCount: 3, usesFloatComponents: true, componentsPerVector: 3,
            bytesPerComponent: 4, dataOffset: 4, dataStride: 20)
        let geometry = SCNGeometry(
            sources: [source], elements: [SCNGeometryElement(indices: [UInt32(0), 1, 2], primitiveType: .point)])
        let scene = SCNScene()
        let artifact = SCNNode()
        artifact.name = "artifact"
        artifact.position.x = 10
        let child = SCNNode(geometry: geometry)
        child.position.y = 2
        artifact.addChildNode(child)
        scene.rootNode.addChildNode(artifact)
        // Auxiliary scene geometry must never inflate the crop counts.
        scene.rootNode.addChildNode(SCNNode(geometry: geometry))
        var region = ExclusionRegion()
        region.mode = .include
        region.centreX = 11
        region.centreY = 2
        XCTAssertEqual(
            try CropPointCounter(scene: scene).count(regions: [region]), CropPointCounts(included: 1, excluded: 2))
    }

    @MainActor
    func testMeshCountsAreLabelledAsVerticesAndOverlayUsesTheSameRotation() throws {
        let scene = NativeSceneFactory().make(
            reconstruction: nil, points: [], photos: [], root: URL(fileURLWithPath: "/tmp"),
            surface: Surface(
                vertices: [[0, 0, 0], [0, 0, -1.5], [1.5, 0, 0]], faces: [[0, 1, 2]], sourceImages: []))
        var region = ExclusionRegion()
        region.mode = .include
        region.width = 4
        region.height = 0.5
        region.rotationY = 90
        let counts = try CropPointCounter(scene: scene).count(regions: [region])
        XCTAssertEqual(counts, CropPointCounts(included: 2, excluded: 1, isMesh: true))
        XCTAssertEqual(counts.label, "Loaded preview vertices")
        let overlay = ExclusionSceneOverlay()
        overlay.update(scene: scene, regions: [region], selectedID: region.id)
        let root = try XCTUnwrap(scene.rootNode.childNode(withName: "exclusion-regions", recursively: false))
        let node = try XCTUnwrap(root.childNode(withName: region.id, recursively: false))
        let point = node.convertPosition(SCNVector3(1.5, 0, 0), to: nil)
        XCTAssertEqual(point.x, 0, accuracy: 0.00001)
        XCTAssertEqual(point.y, 0, accuracy: 0.00001)
        XCTAssertEqual(point.z, -1.5, accuracy: 0.00001)
        XCTAssertEqual(node.geometry?.firstMaterial?.diffuse.contents as? NSColor, .systemGreen)
        XCTAssertTrue(try CropPointCounter(scene: scene).count(regions: [region]).isMesh)
        XCTAssertEqual(try CropPointCounter(scene: scene).count(regions: [region]).total, 3)
    }
}
