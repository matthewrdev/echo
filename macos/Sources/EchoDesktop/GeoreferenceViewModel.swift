import MapKit
import SwiftUI

@MainActor
final class GeoreferenceViewModel: ObservableObject {
    @Published var artifact: GeoreferenceArtifact?
    @Published var loading = false
    @Published var error: String?
    @Published var mapLoading = false
    @Published var mapError: String?
    @Published var mapHeight = 0.0
    @Published var exporting = false
    @Published var exportMessage: String?
    private var loadTask: Task<GeoreferenceArtifact, Error>?
    private var snapshotter: MKMapSnapshotter?
    private var generation = UUID()

    func load(root: String, run: RunData?) async {
        cancel()
        let request = UUID()
        generation = request
        artifact = nil
        error = nil
        mapError = nil
        exportMessage = nil
        guard let run else {
            error = "Complete alignment to view geographic placement."
            return
        }
        loading = true
        let task = Task.detached(priority: .userInitiated) {
            try GeoreferenceArtifactReader().load(root: URL(fileURLWithPath: root), run: run)
        }
        loadTask = task
        do {
            let value = try await withTaskCancellationHandler {
                try await task.value
            } onCancel: {
                task.cancel()
            }
            guard request == generation, !Task.isCancelled else { return }
            artifact = value
            mapHeight = value.bounds.minimum.y - max(1, value.bounds.span * 0.01)
        } catch is CancellationError {
        } catch {
            guard request == generation else { return }
            self.error = error.localizedDescription
        }
        if request == generation { loading = false }
    }

    func loadMap() async {
        guard let artifact, let origin = artifact.reconstruction.origin, let scene = artifact.presentation.scene,
            scene.rootNode.childNode(withName: "geographic-map", recursively: false) == nil, !mapLoading
        else { return }
        let request = generation
        var rect = artifact.overlay?.boundingMapRect ?? MKMapRect.null
        if rect.isNull {
            for camera in artifact.cameras {
                let point = MKMapPoint(
                    CLLocationCoordinate2D(latitude: camera.position.latitude, longitude: camera.position.longitude))
                rect = rect.union(MKMapRect(x: point.x, y: point.y, width: 1, height: 1))
            }
        }
        guard !rect.isNull else { return }
        let span = max(100 / MKMetersPerMapPointAtLatitude(origin.latitude), max(rect.width, rect.height) * 1.6)
        rect = MKMapRect(x: rect.midX - span / 2, y: rect.midY - span / 2, width: span, height: span)
        let options = MKMapSnapshotter.Options()
        options.mapRect = rect
        options.size = CGSize(width: 1024, height: 1024)
        options.mapType = .satellite
        options.showsBuildings = false
        options.pointOfInterestFilter = .excludingAll
        let snapshotter = MKMapSnapshotter(options: options)
        self.snapshotter = snapshotter
        mapLoading = true
        mapError = nil
        defer {
            if request == generation {
                mapLoading = false
                self.snapshotter = nil
            }
        }
        do {
            let snapshot = try await withTaskCancellationHandler {
                try await snapshotter.start()
            } onCancel: {
                snapshotter.cancel()
            }
            guard request == generation, !Task.isCancelled else { return }
            let height = artifact.bounds.minimum.y - max(1, artifact.bounds.span * 0.01)
            scene.rootNode.addChildNode(
                GeographicMapSurface().make(snapshot: snapshot, rect: rect, origin: origin, height: height))
            updateMapHeight()
        } catch {
            guard request == generation, !Task.isCancelled else { return }
            mapError =
                "Apple Maps imagery could not be loaded. The model is still available. \(error.localizedDescription)"
        }
    }

    func updateMapHeight() {
        guard mapHeight.isFinite else { return }
        // The surface was created at its initial display height; move it by the difference.
        if let artifact,
            let node = artifact.presentation.scene?.rootNode.childNode(withName: "geographic-map", recursively: false)
        {
            node.position.y = mapHeight - (artifact.bounds.minimum.y - max(1, artifact.bounds.span * 0.01))
        }
    }

    func cancel() {
        generation = UUID()
        loadTask?.cancel()
        loadTask = nil
        snapshotter?.cancel()
        snapshotter = nil
        loading = false
        mapLoading = false
    }
}
