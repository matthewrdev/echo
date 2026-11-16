import Foundation

struct FeatureOverlayOptions: Equatable {
    var opacity = 1.0
    var showsPoints = true
    var showsLines = true
    var showsJunctions = true

    func shows(_ feature: ImageFeature) -> Bool {
        opacity > 0 && (feature.kind == "junction" ? showsJunctions : showsPoints)
    }

    func shows(_ match: ImageMatch) -> Bool {
        guard opacity > 0 else { return false }
        if match.kind == "line" { return showsLines }
        return match.kind == "junction" ? showsJunctions : showsPoints
    }

    func shows(index: Int, in data: FeaturePresentation) -> Bool {
        if data.features.indices.contains(index) {
            return shows(data.features[index])
        }
        guard opacity > 0, showsLines else { return false }
        return data.lines.indices.contains(index - data.features.count)
    }
}
