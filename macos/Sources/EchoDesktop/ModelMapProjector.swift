import MapKit
import Metal
import SceneKit

struct ModelMapProjector {
    func render(surface: Surface, uvs: [[Double]], texture: NSImage?, origin: GeoPosition, size: Int = 1536) throws
        -> ModelMapOverlay
    {
        guard !surface.vertices.isEmpty, !surface.faces.isEmpty,
            let device = MTLCreateSystemDefaultDevice()
        else { throw CLIError(message: "A saved mesh and Metal rendering are required for the model overlay.") }
        let transform = GeographicTransform(origin: origin)
        var points: [MKMapPoint] = []
        var extent = MKMapRect.null
        for vertex in surface.vertices {
            guard vertex.count == 3, vertex.allSatisfy(\.isFinite) else {
                throw CLIError(message: "The model contains an invalid coordinate.")
            }
            let position = transform.geographic(vertex)
            let point = MKMapPoint(CLLocationCoordinate2D(latitude: position.latitude, longitude: position.longitude))
            points.append(point)
            extent = extent.union(MKMapRect(x: point.x, y: point.y, width: 0, height: 0))
        }
        guard extent.width < MKMapRect.world.width / 2 else {
            throw CLIError(message: "Models crossing the antimeridian need to be split before map display.")
        }
        let minimum = 10 / MKMetersPerMapPointAtLatitude(origin.latitude)
        let span = max(minimum, max(extent.width, extent.height) * 1.1)
        let rect = MKMapRect(x: extent.midX - span / 2, y: extent.midY - span / 2, width: span, height: span)
        let metres = MKMetersPerMapPointAtLatitude(origin.latitude)
        let vertices = points.enumerated().map { index, point in
            [(point.x - rect.midX) * metres, surface.vertices[index][1], (point.y - rect.midY) * metres]
        }
        let projected = Surface(vertices: vertices, faces: surface.faces, sourceImages: [])
        let scene = SCNScene()
        scene.background.contents = NSColor.clear
        let geometry = SurfaceGeometryBuilder().make(surface: projected, uvs: uvs, texture: texture, mode: .textured)
        scene.rootNode.addChildNode(SCNNode(geometry: geometry))
        let bounds = SceneBounds(positions: vertices)
        let camera = SCNNode()
        let lens = SCNCamera()
        lens.usesOrthographicProjection = true
        lens.orthographicScale = span * metres / 2
        lens.zNear = 0.01
        lens.zFar = max(1000, bounds.span * 10)
        camera.camera = lens
        camera.position = SCNVector3(0, bounds.maximum.y + max(10, bounds.span), 0)
        camera.look(at: SCNVector3(0, bounds.minimum.y, 0), up: SCNVector3(0, 0, -1), localFront: SCNVector3(0, 0, -1))
        scene.rootNode.addChildNode(camera)
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        let image = renderer.snapshot(
            atTime: 0, with: CGSize(width: size, height: size), antialiasingMode: .multisampling4X)
        return ModelMapOverlay(image: image, rect: rect)
    }
}
