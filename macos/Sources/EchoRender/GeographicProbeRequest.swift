import EchoRendering

struct GeographicProbeRequest: Decodable {
    let source: GeographicModelSource
    let latitude: String
    let longitude: String
    let altitude: String?
    let accuracy: String

    func resolve() throws -> GeographicProbeResponse {
        let input = try GeographicProbeInput(
            latitude: latitude, longitude: longitude, altitude: altitude ?? "", accuracy: accuracy)
        let surface = try GeographicModelReader().read(source).surface
        guard !surface.faces.isEmpty else {
            throw ModelRenderError(message: "A saved mesh is required to test a GPS coordinate.")
        }
        let result = try GeographicProbeResolver().resolve(
            input, origin: source.origin, bounds: SceneBounds(positions: surface.vertices), surface: surface)
        return GeographicProbeResponse(
            coordinate: result.coordinate, local: [result.local.x, result.local.y, result.local.z],
            accuracy: result.accuracy, surfaceIntersections: result.surfaceIntersections, placement: result.placement)
    }
}
