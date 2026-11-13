import AppKit

struct FeaturePresentation {
    let photo: Photo
    let image: NSImage
    let features: [ImageFeature]
    let matches: [FeatureMatchLink]
    let matchingAvailable: Bool
    var lines: [LineFeature] = []
    var hasLineData: Bool = true
}
