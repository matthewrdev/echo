import SceneKit

final class GroundGridVisibility: NSObject, SCNSceneRendererDelegate {
    func renderer(_ renderer: SCNSceneRenderer, updateAtTime time: TimeInterval) {
        guard let grid = renderer.scene?.rootNode.childNode(withName: "ground-grid", recursively: false),
            !grid.isHidden
        else { return }
        let centre = grid.worldPosition
        let projected = renderer.projectPoint(centre)
        let across = renderer.projectPoint(SCNVector3(centre.x + 1, centre.y, centre.z))
        let along = renderer.projectPoint(SCNVector3(centre.x, centre.y, centre.z + 1))
        let pixelsPerMetre = min(
            hypot(across.x - projected.x, across.y - projected.y),
            hypot(along.x - projected.x, along.y - projected.y))
        // Subpixel metre lines otherwise merge into a grey sheet. Fade them until
        // zoom reveals the cells; ten-metre reference lines remain faintly visible.
        grid.geometry?.firstMaterial?.transparency = opacity(pixels: pixelsPerMetre)
        grid.childNode(withName: "ground-grid-guides", recursively: false)?.geometry?.firstMaterial?.transparency =
            opacity(pixels: pixelsPerMetre * 10)
    }

    private func opacity(pixels: Double) -> CGFloat {
        let amount = min(1, max(0, (pixels - 1.5) / 2.5))
        return CGFloat(0.18 * amount * amount * (3 - 2 * amount))
    }
}
