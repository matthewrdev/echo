import AppKit

struct DepthImagePresentation {
    let identity: String
    let overlay: NSImage
    let photograph: NSImage?
    let map: DepthMap
    let quality: DepthQualitySummary
    let isMetric: Bool

    init(
        identity: String, overlay: NSImage, photograph: NSImage?, map: DepthMap, isMetric: Bool = false,
        quality: DepthQualitySummary? = nil
    ) {
        self.identity = identity
        self.overlay = overlay
        self.photograph = photograph
        self.map = map
        self.quality = quality ?? DepthQualitySummary(map: map)
        self.isMetric = isMetric
    }
}
