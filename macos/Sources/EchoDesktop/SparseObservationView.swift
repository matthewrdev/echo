import SwiftUI

struct SparseObservationView: View {
    let data: SparseInspectionData
    let observation: SparseObservation
    let openFeatures: (String) -> Void
    @State private var image: NSImage?
    @State private var feature: ImageFeature?
    @State private var error: String?
    @State private var loading = false
    @StateObject private var controller = CanvasViewportController()

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let photo = data.photos[observation.imageID] {
                Text(photo.name).font(.headline).lineLimit(2).help(photo.name).textSelection(.enabled)
                if let residual = observation.error {
                    Text("Saved reprojection error: \(residual.formatted(.number.precision(.fractionLength(2)))) px")
                        .font(.caption).textSelection(.enabled)
                }
                if let feature {
                    Text("\(feature.kind ?? "point") feature · \(feature.descriptor.count)-value descriptor")
                        .font(.caption).foregroundStyle(.secondary)
                }
                if let image {
                    CanvasControls(controller: controller)
                    CanvasViewport(
                        renderer: SparseObservationRenderer(image: image, photo: photo, pixel: observation.pixel),
                        controller: controller
                    )
                    .frame(minHeight: 100, maxHeight: .infinity)
                    MatchPatchView(title: "Observed feature", image: image, photo: photo, pixel: observation.pixel)
                        .frame(height: 180)
                    Button("Explore all features in this photo", systemImage: "viewfinder") { openFeatures(photo.id) }
                } else if loading {
                    ProgressView("Opening source photo…").frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    Text(error ?? "Source photo is unavailable.").foregroundStyle(.secondary)
                }
            } else {
                Text("This saved observation references a missing photo.").foregroundStyle(.secondary)
            }
        }
        .task(id: observation.imageID + observation.pixel.description) {
            image = nil
            feature = nil
            error = nil
            loading = true
            guard let photo = data.photos[observation.imageID] else {
                loading = false
                return
            }
            let root = data.root
            let loaded = await Task.detached(priority: .userInitiated) {
                NSImage(contentsOf: root.appendingPathComponent(photo.source))
            }.value
            guard !Task.isCancelled else { return }
            image = loaded
            loading = false
            if loaded == nil { error = "Cannot open \(photo.name)." }
            if let path = data.featureFiles[photo.id] {
                let pixel = observation.pixel
                let found = await Task.detached(priority: .userInitiated) {
                    guard
                        let bytes = try? Data(
                            contentsOf: root.appendingPathComponent(path).appendingPathComponent("features.json")),
                        let set = try? JSONDecoder().decode(FeatureSet.self, from: bytes)
                    else { return Optional<ImageFeature>.none }
                    return set.features.first { abs($0.x - pixel[0]) < 0.1 && abs($0.y - pixel[1]) < 0.1 }
                }.value
                guard !Task.isCancelled else { return }
                feature = found
            }
        }
    }
}
