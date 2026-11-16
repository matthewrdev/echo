import Foundation
import SceneKit

struct ExclusionRegion: Codable, Equatable, Identifiable {
    var id = UUID().uuidString
    var enabled = true
    var shape: ExclusionShape = .box
    var centreX = 0.0
    var centreY = 0.0
    var centreZ = 0.0
    var width = 1.0
    var height = 1.0
    var depth = 1.0
    var radius = 0.5

    var isValid: Bool {
        !id.isEmpty && [centreX, centreY, centreZ].allSatisfy(\.isFinite)
            && [width, height, depth, radius].allSatisfy { $0.isFinite && $0 > 0 }
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

    mutating func place(at point: SCNVector3) {
        centreX = point.x
        centreY = point.y
        centreZ = point.z
    }
}
