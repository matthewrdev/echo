import EchoRendering

struct GeographicProbeResponse: Encodable {
    let coordinate: GeoPosition
    let local: [Double]
    let accuracy: Double
    let surfaceIntersections: Int
    let placement: String
}
