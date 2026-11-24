import SceneKit
import simd

package struct GeographicProbeResolver {
    package init() {}

    package func resolve(_ input: GeographicProbeInput, origin: GeoPosition, bounds: SceneBounds, surface: Surface)
        throws
        -> GeographicProbeResult
    {
        let transform = GeographicTransform(origin: origin)
        if let altitude = input.altitude {
            guard origin.altitude != nil, let datum = origin.altitudeDatum,
                !datum.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, datum.lowercased() != "unknown"
            else {
                throw ModelRenderError(
                    message: "The model's altitude reference is unknown. Leave altitude blank to test on the surface.")
            }
            let coordinate = GeoPosition(
                latitude: input.latitude, longitude: input.longitude, altitude: altitude, altitudeDatum: datum)
            let local = transform.local(coordinate)
            guard Float(local.x).isFinite, Float(local.y).isFinite, Float(local.z).isFinite else {
                throw ModelRenderError(message: "This altitude is outside the supported geographic range.")
            }
            let inside =
                local.x >= bounds.minimum.x && local.x <= bounds.maximum.x
                && local.y >= bounds.minimum.y && local.y <= bounds.maximum.y
                && local.z >= bounds.minimum.z && local.z <= bounds.maximum.z
            return GeographicProbeResult(
                coordinate: coordinate, local: SCNVector3(local.x, local.y, local.z), accuracy: input.accuracy,
                surfaceIntersections: 0,
                placement: inside ? "Entered altitude · \(datum)" : "Outside model bounds · \(datum)")
        }
        // Use the geographic vertical, not a local X/Z approximation: height also
        // moves X/Z slightly away from the tangent-frame origin.
        let margin = max(10, bounds.span * 2)
        func endpoint(_ height: Double) -> SIMD3<Double> {
            let point = transform.local(
                GeoPosition(
                    latitude: input.latitude, longitude: input.longitude, altitude: (origin.altitude ?? 0) + height))
            return point
        }
        let surfaces = SurfaceSegmentIntersector().intersections(
            surface: surface, from: endpoint(bounds.maximum.y + margin), to: endpoint(bounds.minimum.y - margin))
        guard let first = surfaces.first else {
            throw ModelRenderError(
                message:
                    "No model surface intersects this GPS coordinate. Pick a point over the model, or enter an altitude in its reference."
            )
        }
        let local = SCNVector3(first.x, first.y, first.z)
        let coordinate = transform.geographic([local.x, local.y, local.z])
        return GeographicProbeResult(
            coordinate: coordinate, local: local, accuracy: input.accuracy, surfaceIntersections: surfaces.count,
            placement: surfaces.count > 1
                ? "Topmost of \(surfaces.count) surface intersections · altitude from model"
                : "On model surface · altitude from model")
    }
}
