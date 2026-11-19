import AppKit
import SceneKit

final class NativeSceneFactory {
    func make(
        reconstruction: ReconstructionScene?, points: [CloudPoint], photos: [Photo], root: URL,
        surface: Surface? = nil, uvs: [[Double]] = [], texture: NSImage? = nil,
        meshMode: MeshViewMode = .textured, photoPlanes: Bool = false, cloudGeometry: SCNGeometry? = nil
    ) -> SCNScene {
        let scene = SCNScene()
        scene.background.contents = NSColor.windowBackgroundColor

        if let cloudGeometry {
            let node = SCNNode(geometry: cloudGeometry)
            node.name = "artifact"
            scene.rootNode.addChildNode(node)
        }

        if !points.isEmpty {
            let vertices = points.map { vector($0.position) }
            let colors = points.flatMap { point -> [Float] in
                let color = Array(point.colour ?? Data([83, 210, 185]))
                return color.map { Float($0) / 255 } + [1]
            }
            let colorSource = SCNGeometrySource(
                data: colors.withUnsafeBufferPointer { Data(buffer: $0) },
                semantic: .color, vectorCount: vertices.count, usesFloatComponents: true,
                componentsPerVector: 4, bytesPerComponent: 4, dataOffset: 0, dataStride: 16
            )
            let indices = Array(0..<UInt32(vertices.count))
            let element = SCNGeometryElement(indices: indices, primitiveType: .point)
            element.pointSize = 3
            element.minimumPointScreenSpaceRadius = 1
            element.maximumPointScreenSpaceRadius = 4
            let geometry = SCNGeometry(
                sources: [SCNGeometrySource(vertices: vertices), colorSource], elements: [element])
            geometry.firstMaterial?.lightingModel = .constant
            let node = SCNNode(geometry: geometry)
            node.name = "artifact"
            scene.rootNode.addChildNode(node)
        }

        if let surface {
            let geometry = SurfaceGeometryBuilder().make(surface: surface, uvs: uvs, texture: texture, mode: meshMode)
            let node = SCNNode(geometry: geometry)
            node.name = "artifact"
            scene.rootNode.addChildNode(node)
        }

        for camera in reconstruction?.cameras ?? [] {
            guard let photo = photos.first(where: { $0.id == camera.imageId }) else { continue }
            let focal = camera.focalPixels > 0 ? camera.focalPixels : photo.focalPixels
            let depth = CameraFrustumDisplay.normalDepth
            let halfWidth = Double(photo.width) / focal * depth / 2
            let halfHeight = Double(photo.height) / focal * depth / 2
            let corners = [
                [-halfWidth, -halfHeight, depth], [halfWidth, -halfHeight, depth],
                [halfWidth, halfHeight, depth], [-halfWidth, halfHeight, depth],
            ].map { transform($0, camera: camera) }
            let vertices = [vector(camera.centre)] + corners
            let edges: [UInt32] = [0, 1, 0, 2, 0, 3, 0, 4, 1, 2, 2, 3, 3, 4, 4, 1]
            let geometry = SCNGeometry(
                sources: [SCNGeometrySource(vertices: vertices)],
                elements: [SCNGeometryElement(indices: edges, primitiveType: .line)])
            geometry.firstMaterial?.diffuse.contents = NSColor.systemOrange
            geometry.firstMaterial?.lightingModel = .constant
            let node = SCNNode(geometry: geometry)
            node.name = camera.imageId
            scene.rootNode.addChildNode(node)

            if photoPlanes, let image = NSImage(contentsOf: root.appendingPathComponent(photo.preview)) {
                let plane = SCNGeometry(
                    sources: [
                        SCNGeometrySource(vertices: corners),
                        SCNGeometrySource(textureCoordinates: [
                            CGPoint(x: 0, y: 0), CGPoint(x: 1, y: 0), CGPoint(x: 1, y: 1), CGPoint(x: 0, y: 1),
                        ]),
                    ],
                    elements: [SCNGeometryElement(indices: [UInt32(0), 1, 2, 0, 2, 3], primitiveType: .triangles)]
                )
                plane.firstMaterial?.diffuse.contents = image
                plane.firstMaterial?.isDoubleSided = true
                plane.firstMaterial?.lightingModel = .constant
                let imageNode = SCNNode(geometry: plane)
                imageNode.name = camera.imageId
                scene.rootNode.addChildNode(imageNode)
            }
        }

        let positions: [[Double]]
        if let cloudGeometry {
            let box = cloudGeometry.boundingBox
            positions = [[box.min.x, box.min.y, box.min.z], [box.max.x, box.max.y, box.max.z]]
        } else {
            positions = surface?.vertices ?? points.map(\.position)
        }
        let bounds = SceneBounds(positions: positions.isEmpty ? reconstruction?.cameras.map(\.centre) ?? [] : positions)
        let camera = SCNNode()
        camera.name = "viewer-camera"
        camera.camera = SCNCamera()
        let view = PhotographedView(bounds: bounds, cameras: surface == nil ? [] : reconstruction?.cameras ?? [])
        bounds.fit(camera: camera, size: CGSize(width: 800, height: 600), offset: view.offset, up: view.up)
        scene.rootNode.addChildNode(camera)
        scene.rootNode.addChildNode(GroundGridFactory().make(bounds: bounds))
        ColourGradePreview.apply(ColourGrade(), to: scene)
        return scene
    }

    private func vector(_ values: [Double]) -> SCNVector3 {
        SCNVector3(values[0], values[1], values[2])
    }

    private func transform(_ point: [Double], camera: CameraPose) -> SCNVector3 {
        let rotation = camera.rotation
        return SCNVector3(
            camera.centre[0] + rotation[0] * point[0] + rotation[1] * point[1] + rotation[2] * point[2],
            camera.centre[1] + rotation[3] * point[0] + rotation[4] * point[1] + rotation[5] * point[2],
            camera.centre[2] + rotation[6] * point[0] + rotation[7] * point[1] + rotation[8] * point[2]
        )
    }
}
