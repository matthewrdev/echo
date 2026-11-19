import Foundation
import SceneKit
import simd

struct ExclusionRegion: Codable, Hashable, Identifiable, Sendable {
    var id = UUID().uuidString
    var enabled = true
    var shape: ExclusionShape = .box
    var mode: CropRegionMode = .include
    var rotationY = 0.0
    var centreX = 0.0
    var centreY = 0.0
    var centreZ = 0.0
    var width = 1.0
    var height = 1.0
    var depth = 1.0
    var radius = 0.5
    var hull: CropHull?

    var isValid: Bool {
        !id.isEmpty && [centreX, centreY, centreZ, rotationY].allSatisfy(\.isFinite)
            && [width, height, depth, radius].allSatisfy { $0.isFinite && $0 > 0 }
            && (shape != .projectedHull || hull?.isValid == true)
    }

    init(shape: ExclusionShape = .box, bounds: SceneBounds? = nil) {
        self.shape = shape
        if let bounds {
            centreX = bounds.centre.x
            centreY = bounds.centre.y
            centreZ = bounds.centre.z
            width = bounds.span / 4
            height = bounds.span / 4
            depth = bounds.span / 4
            radius = bounds.span / 8
        }
    }

    // Boxes rotate around the vertical Y axis; cylinders remain upright.
    var orientation: simd_quatd {
        let angle = shape == .box ? rotationY.truncatingRemainder(dividingBy: 360) * .pi / 180 : 0
        return simd_quatd(angle: angle, axis: SIMD3(0, 1, 0))
    }

    typealias CodingKeys = ExclusionRegionKey

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: ExclusionRegionKey.self)
        id = try values.decode(String.self, forKey: .id)
        enabled = try values.decode(Bool.self, forKey: .enabled)
        shape = try values.decode(ExclusionShape.self, forKey: .shape)
        mode = try values.decodeIfPresent(CropRegionMode.self, forKey: .mode) ?? .exclude
        rotationY = try values.decodeIfPresent(Double.self, forKey: .rotationY) ?? 0
        centreX = try values.decode(Double.self, forKey: .centreX)
        centreY = try values.decode(Double.self, forKey: .centreY)
        centreZ = try values.decode(Double.self, forKey: .centreZ)
        width = try values.decode(Double.self, forKey: .width)
        height = try values.decode(Double.self, forKey: .height)
        depth = try values.decode(Double.self, forKey: .depth)
        radius = try values.decode(Double.self, forKey: .radius)
        hull = try values.decodeIfPresent(CropHull.self, forKey: .hull)
    }

    mutating func place(at point: SCNVector3) {
        centreX = point.x
        centreY = point.y
        centreZ = point.z
    }
}
