import Foundation
import simd

// WGS84 tangent frame, identical to the C# CameraGeometry/GeographicCoordinates transform.
struct GeographicTransform {
    let origin: GeoPosition

    func geographic(_ local: [Double]) -> GeoPosition {
        let latitude = origin.latitude * .pi / 180
        let longitude = origin.longitude * .pi / 180
        let east = SIMD3(-sin(longitude), cos(longitude), 0)
        let up = SIMD3(cos(latitude) * cos(longitude), cos(latitude) * sin(longitude), sin(latitude))
        let south = SIMD3(sin(latitude) * cos(longitude), sin(latitude) * sin(longitude), -cos(latitude))
        let value = ecef(origin) + east * local[0] + up * local[1] + south * local[2]
        let horizontal = hypot(value.x, value.y)
        let eccentricity = 6.69437999014e-3
        var lat = atan2(value.z, horizontal * (1 - eccentricity))
        for _ in 0..<10 {
            let normal = 6_378_137 / sqrt(1 - eccentricity * pow(sin(lat), 2))
            lat = atan2(value.z + eccentricity * normal * sin(lat), horizontal)
        }
        let normal = 6_378_137 / sqrt(1 - eccentricity * pow(sin(lat), 2))
        return GeoPosition(
            latitude: lat * 180 / .pi, longitude: atan2(value.y, value.x) * 180 / .pi,
            altitude: origin.altitude.map { _ in horizontal / cos(lat) - normal },
            altitudeDatum: origin.altitudeDatum, uncertaintyMetres: origin.uncertaintyMetres)
    }

    func local(_ position: GeoPosition) -> SIMD3<Double> {
        let delta = ecef(position) - ecef(origin)
        let lat = origin.latitude * .pi / 180
        let lon = origin.longitude * .pi / 180
        return SIMD3(
            -sin(lon) * delta.x + cos(lon) * delta.y,
            cos(lat) * cos(lon) * delta.x + cos(lat) * sin(lon) * delta.y + sin(lat) * delta.z,
            sin(lat) * cos(lon) * delta.x + sin(lat) * sin(lon) * delta.y - cos(lat) * delta.z)
    }

    private func ecef(_ position: GeoPosition) -> SIMD3<Double> {
        let lat = position.latitude * .pi / 180
        let lon = position.longitude * .pi / 180
        let eccentricity = 6.69437999014e-3
        let normal = 6_378_137 / sqrt(1 - eccentricity * pow(sin(lat), 2))
        let altitude = position.altitude ?? 0
        return SIMD3(
            (normal + altitude) * cos(lat) * cos(lon), (normal + altitude) * cos(lat) * sin(lon),
            (normal * (1 - eccentricity) + altitude) * sin(lat))
    }
}
