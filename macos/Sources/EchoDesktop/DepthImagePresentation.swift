import AppKit

struct DepthImagePresentation {
    let identity: String
    let overlay: NSImage
    let photograph: NSImage?
    let map: DepthMap
    var isMetric = false
}
