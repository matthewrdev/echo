import SceneKit

@MainActor
final class TexturePriorityOverlay {
    private weak var artifact: SCNNode?
    private var strokes: [TexturePriorityStroke] = []
    private var task: Task<Void, Never>?
    private var overlay: SCNNode?

    func update(scene: SCNScene?, strokes: [TexturePriorityStroke]) {
        let node = scene?.rootNode.childNode(withName: "artifact", recursively: false)
        guard artifact !== node || self.strokes != strokes else { return }
        let changedArtifact = artifact !== node
        task?.cancel()
        self.strokes = strokes
        artifact = node
        if changedArtifact || strokes.isEmpty {
            overlay?.removeFromParentNode()
            overlay = nil
        }
        guard !strokes.isEmpty, let node, let scene, let source = CropPointSource(node: node) else { return }
        task = Task { [weak self] in
            do {
                try await Task.sleep(for: .milliseconds(100))
                let worker = Task.detached(priority: .userInitiated) {
                    try TexturePriorityPreview(source: source, strokes: strokes)
                }
                let preview = try await withTaskCancellationHandler {
                    try await worker.value
                } onCancel: {
                    worker.cancel()
                }
                try Task.checkCancellation()
                guard let self else { return }
                self.overlay?.removeFromParentNode()
                let overlay = self.makeNode(preview)
                scene.rootNode.addChildNode(overlay)
                self.overlay = overlay
            } catch {
                // Superseded previews are cancelled while the brush is moving.
            }
        }
    }

    private func makeNode(_ preview: TexturePriorityPreview) -> SCNNode {
        let vertices = SCNGeometrySource(
            data: preview.positions.withUnsafeBufferPointer { Data(buffer: $0) }, semantic: .vertex,
            vectorCount: preview.positions.count, usesFloatComponents: true, componentsPerVector: 3,
            bytesPerComponent: 4, dataOffset: 0, dataStride: MemoryLayout<SIMD3<Float>>.stride)
        let colours = SCNGeometrySource(
            data: preview.colours.withUnsafeBufferPointer { Data(buffer: $0) }, semantic: .color,
            vectorCount: preview.colours.count, usesFloatComponents: true, componentsPerVector: 4,
            bytesPerComponent: 4, dataOffset: 0, dataStride: MemoryLayout<SIMD4<Float>>.stride)
        let element = SCNGeometryElement(
            data: nil, primitiveType: .point, primitiveCount: preview.positions.count, bytesPerIndex: 0)
        element.pointSize = 5
        element.minimumPointScreenSpaceRadius = 2
        element.maximumPointScreenSpaceRadius = 5
        let geometry = SCNGeometry(sources: [vertices, colours], elements: [element])
        geometry.firstMaterial?.lightingModel = .constant
        geometry.firstMaterial?.writesToDepthBuffer = false
        geometry.firstMaterial?.readsFromDepthBuffer = true
        let node = SCNNode(geometry: geometry)
        node.name = "texture-priority-preview"
        node.renderingOrder = 5
        return node
    }
}
