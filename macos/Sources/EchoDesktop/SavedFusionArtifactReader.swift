import AppKit

actor SavedFusionArtifactReader {
    private let json: ArtifactJSONReader
    private let scenes: NativeSceneFactory
    private var cached: CachedFusionArtifact?

    init(json: ArtifactJSONReader, scenes: NativeSceneFactory) {
        self.json = json
        self.scenes = scenes
    }

    func load(
        folder: URL, mode: String, slice: Double, metricScale: Bool,
        sceneIdentity: String? = nil, running: Bool = false
    ) throws -> ArtifactPresentation {
        try Task.checkCancellation()
        let previewFile = folder.appendingPathComponent("preview.json")
        if FileManager.default.fileExists(atPath: previewFile.path) {
            let preview: FusionPreview = try json.read(previewFile)
            let volume = preview.volume
            let points = volume.samples.map { sample in
                CloudPoint(
                    position: [
                        Double(sample.x) * volume.voxelSize, Double(sample.y) * volume.voxelSize,
                        Double(sample.z) * volume.voxelSize,
                    ], colour: nil, error: 0)
            }
            let scene = scenes.make(reconstruction: nil, points: points, photos: [], root: folder)
            let label = running ? "LIVE PREVIEW" : "PARTIAL PREVIEW"
            return ArtifactPresentation(
                title: "Fusion in progress",
                detail:
                    "\(label) · \(preview.totalSamples.formatted()) integrated samples · showing \(points.count.formatted()) sampled voxels. Surface and slices appear when fusion completes.",
                scene: scene, sceneIdentity: sceneIdentity ?? folder.path, metricScale: metricScale)
        }
        let file = folder.appendingPathComponent("volume.json")
        let version = ArtifactFileVersion(file: file)
        if cached?.version != version {
            cached = nil
            let volume: FusedVolume = try json.read(file)
            cached = CachedFusionArtifact(
                version: version, volume: volume, bounds: try FusionVolumeBounds(volume: volume))
        }
        guard var artifact = cached else { throw CLIError(message: "Could not open the fusion volume.") }
        let selection = FusionViewMode(selection: mode)
        var info = FusionPresentationInfo(
            mode: selection, samples: artifact.volume.samples.count,
            voxelSize: artifact.volume.voxelSize, metricScale: metricScale)
        info.preferredSlices = artifact.bounds.preferredSlices
        if let axis = selection.axis {
            let rendered = try FusionSliceRenderer().render(
                volume: artifact.volume, bounds: artifact.bounds, axis: axis, fraction: slice)
            info.slicePosition = Double(rendered.index) * artifact.volume.voxelSize
            info.sliceSamples = rendered.count
            let horizontal = axis == "X" ? "Z" : "X"
            let vertical = axis == "Y" ? "Z" : "Y"
            return ArtifactPresentation(
                title: "Fusion slice", detail: "\(horizontal) horizontal · \(vertical) vertical · full saved slice",
                image: rendered.image, metricScale: metricScale, fusion: info)
        }
        if artifact.surface == nil {
            artifact.surface = try FusionSurfaceBuilder().build(artifact.volume)
            cached = artifact
        }
        guard let surface = artifact.surface, surface.triangleCount > 0 else {
            throw CLIError(
                message:
                    "No connected surface crosses this volume. Use Slice X, Y or Z to inspect the saved observations.")
        }
        try Task.checkCancellation()
        let builder = FusionSceneBuilder()
        let geometry = builder.geometry(surface: surface, showSupport: selection == .support)
        let scene = scenes.make(reconstruction: nil, points: [], photos: [], root: folder, cloudGeometry: geometry)
        builder.light(scene)
        return ArtifactPresentation(
            title: "Fused surface",
            detail: "\(surface.triangleCount.formatted()) preview triangles · unobserved areas remain open",
            scene: scene, sceneIdentity: sceneIdentity ?? file.path, metricScale: metricScale, fusion: info)
    }
}
