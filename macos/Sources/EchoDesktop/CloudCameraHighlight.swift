import SceneKit

final class CloudCameraHighlight {
    private weak var artifact: SCNNode?
    private var original: SCNGeometry?
    private var foreground: SCNGeometry?
    private var context: SCNNode?

    func show(_ frustum: CameraFrustum, on node: SCNNode) {
        if artifact !== node {
            clear()
            guard let geometry = node.geometry else { return }
            artifact = node
            original = geometry
            foreground = filteredGeometry(geometry, background: false)
            node.geometry = foreground
            let context = SCNNode(geometry: filteredGeometry(geometry, background: true))
            context.name = "cloud-context"
            context.renderingOrder = 1
            node.addChildNode(context)
            self.context = context
        }
        for geometry in [foreground, context?.geometry].compactMap({ $0 }) {
            geometry.setValue(NSValue(scnVector4: frustum.plane(frustum.right)), forKey: "echoCameraRight")
            geometry.setValue(NSValue(scnVector4: frustum.plane(frustum.down)), forKey: "echoCameraDown")
            geometry.setValue(NSValue(scnVector4: frustum.plane(frustum.forward)), forKey: "echoCameraForward")
            geometry.setValue(
                NSValue(point: NSPoint(x: frustum.halfWidth, y: frustum.halfHeight)), forKey: "echoCameraExtent")
        }
    }

    func clear() {
        if let original { artifact?.geometry = original }
        context?.removeFromParentNode()
        artifact = nil
        original = nil
        foreground = nil
        context = nil
    }

    private func filteredGeometry(_ source: SCNGeometry, background: Bool) -> SCNGeometry {
        // Both passes share the saved vertex sources; camera changes only update uniforms.
        let geometry = SCNGeometry(sources: source.sources, elements: source.elements)
        let material = (source.firstMaterial?.copy() as? SCNMaterial) ?? SCNMaterial()
        material.writesToDepthBuffer = !background
        material.readsFromDepthBuffer = true
        geometry.materials = [material]
        let normalization = source.shaderModifiers?[.geometry] ?? ""
        geometry.shaderModifiers = [
            .geometry: """
            #pragma arguments
            float4 echoCameraRight;
            float4 echoCameraDown;
            float4 echoCameraForward;
            float2 echoCameraExtent;
            #pragma varyings
            float echoInsideCamera;
            #pragma body
            \(normalization)
            float4 worldPoint = scn_node.modelTransform * _geometry.position;
            float depth = dot(echoCameraForward, worldPoint);
            bool inside = depth > 1e-8
                && abs(dot(echoCameraRight, worldPoint)) <= depth * echoCameraExtent.x
                && abs(dot(echoCameraDown, worldPoint)) <= depth * echoCameraExtent.y;
            out.echoInsideCamera = inside ? 1.0 : 0.0;
            """,
            .fragment: background
                ? """
                #pragma transparent
                #pragma body
                if (in.echoInsideCamera > 0.5) { discard_fragment(); }
                _output.color *= 0.1;
                """
                : """
                #pragma opaque
                #pragma body
                if (in.echoInsideCamera < 0.5) { discard_fragment(); }
                """,
        ]
        return geometry
    }
}
