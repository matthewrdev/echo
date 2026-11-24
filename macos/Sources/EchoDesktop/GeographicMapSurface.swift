import EchoRendering
import MapKit
import SceneKit

struct GeographicMapSurface {
    func make(snapshot: MKMapSnapshotter.Snapshot, rect: MKMapRect, origin: GeoPosition, height: Double) -> SCNNode {
        let transform = GeographicTransform(origin: origin)
        var positions: [SCNVector3] = []
        var uvs: [CGPoint] = []
        var indices: [UInt32] = []
        let count = 16
        for row in 0...count {
            for column in 0...count {
                let point = MKMapPoint(
                    x: rect.minX + rect.width * Double(column) / Double(count),
                    y: rect.minY + rect.height * Double(row) / Double(count))
                let coordinate = point.coordinate
                let local = transform.local(
                    GeoPosition(
                        latitude: coordinate.latitude, longitude: coordinate.longitude, altitude: origin.altitude,
                        altitudeDatum: origin.altitudeDatum))
                positions.append(SCNVector3(local.x, height, local.z))
                let pixel = snapshot.point(for: coordinate)
                // MapKit's macOS snapshot points use AppKit's bottom-left image coordinates.
                uvs.append(CGPoint(x: pixel.x / snapshot.image.size.width, y: 1 - pixel.y / snapshot.image.size.height))
                if row < count, column < count {
                    let a = UInt32(row * (count + 1) + column)
                    let b = a + UInt32(count + 1)
                    indices += [a, b, a + 1, a + 1, b, b + 1]
                }
            }
        }
        let geometry = SCNGeometry(
            sources: [SCNGeometrySource(vertices: positions), SCNGeometrySource(textureCoordinates: uvs)],
            elements: [SCNGeometryElement(indices: indices, primitiveType: .triangles)])
        let material = SCNMaterial()
        material.diffuse.contents = snapshot.image
        material.lightingModel = .constant
        material.isDoubleSided = true
        geometry.materials = [material]
        let node = SCNNode(geometry: geometry)
        node.name = "geographic-map"
        return node
    }
}
