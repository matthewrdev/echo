import AppKit
import SceneKit

final class SavedArtifactReader: ArtifactReader {
    private let scenes: NativeSceneFactory
    private let images: ArtifactImageRenderer
    private let glb: GlbAssetReader
    private let featureMatches: FeatureMatchIndexer
    private let depths: SavedDepthArtifactReader

    init(
        scenes: NativeSceneFactory, images: ArtifactImageRenderer, glb: GlbAssetReader,
        featureMatches: FeatureMatchIndexer, depths: SavedDepthArtifactReader
    ) {
        self.featureMatches = featureMatches
        self.scenes = scenes
        self.images = images
        self.glb = glb
        self.depths = depths
    }

    func units(root: URL, run: RunData, stage: PipelineStage) async throws -> [ArtifactUnit] {
        try await Task.detached(priority: .userInitiated) {
            try self.savedUnits(root: root, run: run, stage: stage)
        }.value
    }

    private func savedUnits(root: URL, run: RunData, stage: PipelineStage) throws -> [ArtifactUnit] {
        guard let relative = run.artifacts[stage.rawValue] ?? run.artifacts[stage.rawValue + "Preview"] else {
            return []
        }
        let folder = root.appendingPathComponent(relative)
        let photos = Dictionary(uniqueKeysWithValues: run.snapshot.images.map { ($0.id, $0) })
        func unit(_ id: String, file: URL, imageIDs: [String], detail: String = "") -> ArtifactUnit {
            let images = imageIDs.compactMap { photos[$0] }
            return ArtifactUnit(
                id: id, title: images.map(\.name).joined(separator: " ↔ "), file: file,
                imageIDs: imageIDs, thumbnails: images.map { root.appendingPathComponent($0.preview) }, detail: detail)
        }
        if stage == .features {
            let units: [String: String] = try read(folder.appendingPathComponent("units.json"))
            return units.map { id, path in
                unit(
                    id, file: root.appendingPathComponent(path).appendingPathComponent("features.json"), imageIDs: [id])
            }.sorted { $0.title < $1.title }
        }
        if stage == .matching {
            let paths: [String] = try read(folder.appendingPathComponent("units.json"))
            return try paths.map { path in
                let file = root.appendingPathComponent(path).appendingPathComponent("pair.json")
                let pair: ImagePair = try read(file)
                return unit(path, file: file, imageIDs: [pair.a, pair.b], detail: "\(pair.matches.count) candidates")
            }
        }
        if stage == .depth, let alignment = run.artifacts["alignment"] {
            if run.artifacts["depth"] == nil {
                let units: [String: String] = try read(folder.appendingPathComponent("units.json"))
                return units.map { id, path in
                    unit(
                        path, file: root.appendingPathComponent(path).appendingPathComponent("depth.json"),
                        imageIDs: [id], detail: "Depth + confidence")
                }.sorted { $0.title < $1.title }
            }
            let paths: [String] = try read(folder.appendingPathComponent("units.json"))
            let scene: ReconstructionMetadata = try read(
                root.appendingPathComponent(alignment).appendingPathComponent("scene.json"))
            // DepthStage writes checkpoints in reconstruction camera order.
            guard paths.count == scene.cameras.count else {
                throw CLIError(message: "Depth index does not match its alignment.")
            }
            return paths.enumerated().map { index, path in
                unit(
                    path, file: root.appendingPathComponent(path).appendingPathComponent("depth.json"),
                    imageIDs: [scene.cameras[index].imageId], detail: "Depth + confidence")
            }
        }
        return []
    }

    func load(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail
    )
        async throws -> ArtifactPresentation
    {
        let task = Task.detached(priority: .userInitiated) {
            try await self.loadSaved(
                root: root, run: run, stage: stage, unit: unit, mode: mode, slice: slice, cloudDetail: cloudDetail)
        }
        return try await withTaskCancellationHandler(operation: { try await task.value }, onCancel: { task.cancel() })
    }

    private func loadSaved(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail
    ) async throws -> ArtifactPresentation {
        guard let path = run.artifacts[stage.rawValue] ?? run.artifacts[stage.rawValue + "Preview"] else {
            throw CLIError(message: "This stage has no saved artifact yet.")
        }
        let folder = root.appendingPathComponent(path)
        let photos = run.snapshot.images
        func photo(_ id: String) throws -> Photo {
            guard let photo = photos.first(where: { $0.id == id }) else {
                throw CLIError(message: "Artifact references an unknown image.")
            }
            return photo
        }
        func unitFile() throws -> URL {
            guard let unit else { throw CLIError(message: "Select a saved image or pair.") }
            return unit.file
        }
        func metricScale() -> Bool {
            guard let alignment = run.artifacts["alignment"],
                let scene: ReconstructionMetadata = try? read(
                    root.appendingPathComponent(alignment).appendingPathComponent("scene.json"))
            else { return false }
            return scene.isMetric
        }
        switch stage {
        case .features:
            let features: FeatureSet = try read(unitFile())
            let photograph = try photo(features.imageId)
            guard let image = NSImage(contentsOf: root.appendingPathComponent(photograph.source)) else {
                throw CLIError(message: "Cannot load the feature photograph.")
            }
            let matches = try featureMatches.read(root: root, run: run, features: features)
            return ArtifactPresentation(
                title: stage.title,
                detail: "\(features.features.count) points · \((features.lines ?? []).count) line segments",
                features: FeaturePresentation(
                    photo: photograph, image: image, features: features.features,
                    matches: matches, matchingAvailable: run.artifacts["matching"] != nil,
                    lines: features.lines ?? [], hasLineData: features.lines != nil))
        case .matching:
            let pair: ImagePair = try read(unitFile())
            let first = try photo(pair.a)
            let second = try photo(pair.b)
            guard let left = NSImage(contentsOf: root.appendingPathComponent(first.source)),
                let right = NSImage(contentsOf: root.appendingPathComponent(second.source))
            else {
                throw CLIError(message: "Cannot load both matched photographs.")
            }
            return ArtifactPresentation(
                title: stage.title,
                detail: "\(pair.matches.count) point candidates · \((pair.lineMatches ?? []).count) line candidates",
                matches: MatchPresentation(
                    first: first, second: second, firstImage: left, secondImage: right, matches: pair.displayMatches,
                    diagnostics: pair.diagnostics))
        case .depth:
            return try await depths.load(root: root, run: run, unit: unit, mode: mode)
        case .fusion:
            let volume: FusedVolume = try read(folder.appendingPathComponent("volume.preview.json"))
            return ArtifactPresentation(
                title: stage.title,
                detail: "Sampled volume slice · voxel \(volume.voxelSize.formatted()) m · blue inside / orange outside",
                image: try images.volume(volume, axis: mode, slice: slice))
        case .texture:
            guard let image = NSImage(contentsOf: folder.appendingPathComponent("atlas.jpg")) else {
                throw CLIError(message: "Cannot open texture atlas.")
            }
            let artifact: TextureArtifact = try read(folder.appendingPathComponent("texture.json"))
            let detail =
                "\(artifact.size) × \(artifact.size) · \(artifact.chartCount.map { "\($0) surface patches" } ?? "legacy camera crops")"
            if mode == "Atlas" { return ArtifactPresentation(title: stage.title, detail: detail, image: image) }
            guard let meshPath = run.artifacts["mesh"] else { throw CLIError(message: "Texture has no mesh artifact.") }
            let surfaceFile =
                artifact.meshFile.map { folder.appendingPathComponent($0) }
                ?? root.appendingPathComponent(meshPath).appendingPathComponent("mesh.json")
            let surface: Surface = try read(surfaceFile)
            let scene = scenes.make(
                reconstruction: nil, points: [], photos: [], root: root, surface: surface,
                uvs: artifact.uvs, texture: image, wireframe: false)
            return ArtifactPresentation(
                title: stage.title, detail: detail, scene: scene, sceneIdentity: run.id + stage.rawValue,
                metricScale: metricScale())
        case .export:
            let asset = try glb.read(folder.appendingPathComponent("cliff.glb"))
            let scene = scenes.make(
                reconstruction: nil, points: [], photos: [], root: root, surface: asset.surface,
                uvs: asset.uvs, texture: mode == "Textured" ? asset.image : nil, wireframe: mode == "Wireframe")
            return ArtifactPresentation(
                title: stage.title, detail: "\(asset.surface.faces.count.formatted()) triangles · actual exported GLB",
                scene: scene, sceneIdentity: run.id + stage.rawValue, metricScale: metricScale())
        case .alignment, .cloud, .mesh:
            let alignmentPath = run.artifacts["alignment"] ?? run.artifacts["alignmentPreview"]
            let reconstruction: ReconstructionScene? = try alignmentPath.map {
                try read(root.appendingPathComponent($0).appendingPathComponent("scene.json"))
            }
            var points = reconstruction?.points ?? []
            var detail = "\(points.count.formatted()) sparse points · \(reconstruction?.cameras.count ?? 0) cameras"
            var cloudGeometry: SCNGeometry?
            var surface: Surface?
            var uvs: [[Double]] = []
            var texture: NSImage?
            if stage == .cloud {
                let report: DenseCloudReport = try read(folder.appendingPathComponent("cloud.json"))
                let threshold =
                    ["≥60%", "Support ≥60%"].contains(mode) ? 0.6 : ["≥40%", "Support ≥40%"].contains(mode) ? 0.4 : 0
                points = report.preview.filter { 1 - $0.error >= threshold }
                let partial = run.artifacts["cloud"] == nil
                var displayedCount = points.count
                if !partial {
                    let preview = try DenseCloudPreviewReader().read(
                        file: folder.appendingPathComponent("cloud.ply"), expectedCount: report.retainedSamples,
                        limit: cloudDetail.limit(for: report.retainedSamples), minimumSupport: threshold)
                    cloudGeometry = preview.geometry
                    displayedCount = preview.count
                    points = []
                }
                let label = partial ? (run.status == "running" ? "LIVE PREVIEW" : "PARTIAL PREVIEW") : "Completed cloud"
                let attributes = try FileManager.default.attributesOfItem(
                    atPath: folder.appendingPathComponent("cloud.json").path)
                let updated =
                    (attributes[.modificationDate] as? Date)?.formatted(date: .omitted, time: .standard) ?? "unknown"
                let density =
                    !partial && cloudDetail.limit(for: report.retainedSamples) >= report.retainedSamples
                    ? "all saved points" : "sampled points"
                detail =
                    "\(label) · \(report.retainedSamples.formatted()) accepted points · displaying \(displayedCount.formatted()) \(density) · updated \(updated)"
            }
            if stage == .mesh {
                surface = try read(folder.appendingPathComponent("mesh.json"))
                points = []
                detail = "\(surface!.faces.count.formatted()) triangles"
                if mode == "Textured", let texturePath = run.artifacts["texture"] {
                    let textureFolder = root.appendingPathComponent(texturePath)
                    let artifact: TextureArtifact = try read(textureFolder.appendingPathComponent("texture.json"))
                    if let meshFile = artifact.meshFile {
                        surface = try read(textureFolder.appendingPathComponent(meshFile))
                    }
                    uvs = artifact.uvs
                    texture = NSImage(contentsOf: textureFolder.appendingPathComponent("atlas.jpg"))
                }
            }
            let scene = scenes.make(
                reconstruction: reconstruction, points: points, photos: photos, root: root,
                surface: surface, uvs: uvs, texture: texture, wireframe: mode == "Wireframe",
                cloudGeometry: cloudGeometry)
            var inspection: SparseInspectionData?
            if stage == .alignment, let reconstruction {
                let featureFiles: [String: String] =
                    try run.artifacts["features"].map {
                        try read(root.appendingPathComponent($0).appendingPathComponent("units.json"))
                    } ?? [:]
                inspection = SparseInspectionData(
                    root: root, scene: reconstruction, photos: photos, featureFiles: featureFiles)
            }
            return ArtifactPresentation(
                title: stage.title, detail: detail, scene: scene, sceneIdentity: run.id + stage.rawValue,
                sparseInspection: inspection,
                metricScale: reconstruction?.origin != nil && !(reconstruction?.frame.contains("arbitrary") ?? true))
        }
    }

    private func read<T: Decodable>(_ file: URL) throws -> T {
        try JSONDecoder().decode(T.self, from: Data(contentsOf: file, options: .mappedIfSafe))
    }
}
