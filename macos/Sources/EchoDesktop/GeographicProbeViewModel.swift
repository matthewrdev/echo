import EchoRendering
import MapKit
import SceneKit
import SwiftUI

@MainActor
final class GeographicProbeViewModel: ObservableObject {
    @Published var latitude = ""
    @Published var longitude = ""
    @Published var altitude = ""
    @Published var accuracy = "5"
    @Published var mapLocation: GeoPosition?
    @Published var mapAccuracy = 0.0
    @Published var result: GeographicProbeResult?
    @Published var error: String?

    func show(in artifact: GeoreferenceArtifact) {
        mapLocation = nil
        mapAccuracy = 0
        do {
            guard let origin = artifact.reconstruction.origin,
                let surface = artifact.surface
            else { throw CLIError(message: "Complete Mesh to test a GPS location on the model.") }
            let input = try GeographicProbeInput(
                latitude: latitude, longitude: longitude, altitude: altitude, accuracy: accuracy)
            mapLocation = GeoPosition(latitude: input.latitude, longitude: input.longitude, altitude: input.altitude)
            mapAccuracy = input.accuracy
            result = try GeographicProbeResolver().resolve(
                input, origin: origin, bounds: artifact.bounds, surface: surface)
            error = nil
        } catch {
            result = nil
            self.error = error.localizedDescription
        }
        updateOverlay(in: artifact)
    }

    func pickMap(_ coordinate: CLLocationCoordinate2D, in artifact: GeoreferenceArtifact) {
        latitude = String(format: "%.8f", coordinate.latitude)
        longitude = String(format: "%.8f", coordinate.longitude)
        altitude = ""
        show(in: artifact)
    }

    func pickModel(_ point: SCNVector3, in artifact: GeoreferenceArtifact) {
        guard let origin = artifact.reconstruction.origin else { return }
        let coordinate = GeographicTransform(origin: origin).geographic([point.x, point.y, point.z])
        latitude = String(format: "%.8f", coordinate.latitude)
        longitude = String(format: "%.8f", coordinate.longitude)
        altitude = ""
        mapLocation = coordinate
        mapAccuracy = 0
        do {
            let input = try GeographicProbeInput(
                latitude: latitude, longitude: longitude, altitude: "", accuracy: accuracy)
            mapAccuracy = input.accuracy
            result = GeographicProbeResult(
                coordinate: coordinate, local: point, accuracy: input.accuracy, surfaceIntersections: 1,
                placement: "Picked model surface")
            error = nil
        } catch {
            result = nil
            self.error = error.localizedDescription
        }
        updateOverlay(in: artifact)
    }

    func updateOverlay(in artifact: GeoreferenceArtifact) {
        if let scene = artifact.presentation.scene { GeographicProbeOverlay().update(scene: scene, result: result) }
    }

    func clear(in artifact: GeoreferenceArtifact?) {
        result = nil
        mapLocation = nil
        mapAccuracy = 0
        error = nil
        if let artifact { updateOverlay(in: artifact) }
    }

    func copyCoordinate() {
        guard let result else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(
            String(format: "%.8f, %.8f", result.coordinate.latitude, result.coordinate.longitude), forType: .string)
    }
}
