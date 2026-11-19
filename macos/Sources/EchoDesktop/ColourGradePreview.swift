import SceneKit

// Uniform-only updates: slider movement never copies or recolours the cloud's vertex buffers.
enum ColourGradePreview {
    static func apply(_ grade: ColourGrade?, to scene: SCNScene?) {
        guard let artifact = scene?.rootNode.childNode(withName: "artifact", recursively: false),
            artifact.geometry?.elements.contains(where: { $0.primitiveType == .point }) == true
        else { return }
        let value = grade?.isValid == true ? grade! : ColourGrade()
        for node in [artifact] + artifact.childNodes.filter({ $0.name == "cloud-context" }) {
            for material in node.geometry?.materials ?? [] {
                if grade == nil && material.shaderModifiers?[.surface] != shader { continue }
                if material.shaderModifiers?[.surface] != shader {
                    var modifiers = material.shaderModifiers ?? [:]
                    modifiers[.surface] = shader
                    material.shaderModifiers = modifiers
                }
                let gains = value.gains
                material.setValue(
                    NSValue(scnVector4: SCNVector4(gains.x, gains.y, gains.z, value.isIdentity ? 0 : 1)),
                    forKey: "echoGradeBalance")
                material.setValue(
                    NSValue(scnVector4: SCNVector4(value.blackPoint, value.whitePoint, value.contrast, value.shadows)),
                    forKey: "echoGradeTone")
                material.setValue(
                    NSValue(scnVector3: SCNVector3(value.highlights, value.saturation, value.vibrance)),
                    forKey: "echoGradeColour")
            }
        }
    }

    static let shader = """
        #pragma arguments
        float4 echoGradeBalance;
        float4 echoGradeTone;
        float3 echoGradeColour;
        #pragma body
        if (echoGradeBalance.w > 0.5) {
            float3 c = clamp(_surface.diffuse.rgb, 0.0, 1.0);
            float3 linear = mix(c / 12.92, pow((c + 0.055) / 1.055, float3(2.4)), step(float3(0.04045), c));
            linear *= echoGradeBalance.xyz;
            c = mix(linear * 12.92, 1.055 * pow(linear, float3(1.0 / 2.4)) - 0.055, step(float3(0.0031308), linear));
            c = clamp(((c - echoGradeTone.x) / (echoGradeTone.y - echoGradeTone.x) - 0.5) * echoGradeTone.z + 0.5, 0.0, 1.0);
            float light = dot(c, float3(0.2126, 0.7152, 0.0722));
            float lift = light * (1.0 - light) * (echoGradeTone.w * (1.0 - light) + echoGradeColour.x * light);
            float chroma = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
            float gain = echoGradeColour.y * (1.0 + echoGradeColour.z * (1.0 - chroma));
            _surface.diffuse.rgb = clamp(light + lift + (c - light) * gain, 0.0, 1.0);
        }
        // Vertex RGB bytes are photographic sRGB. SceneKit's render target performs
        // the final sRGB encoding, so hand it linear light rather than encoding twice.
        float3 encoded = clamp(_surface.diffuse.rgb, 0.0, 1.0);
        _surface.diffuse.rgb = mix(encoded / 12.92, pow((encoded + 0.055) / 1.055, float3(2.4)), step(float3(0.04045), encoded));
        """
}
