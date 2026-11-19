import AppKit
import SceneKit

final class SavedArtifactReader: ArtifactReader {
    private let scenes: NativeSceneFactory
    private let images: ArtifactImageRenderer
    private let glb: GlbAssetReader
    private let featureMatches: FeatureMatchIndexer
    private let json: ArtifactJSONReader
    private let depths: SavedDepthArtifactReader
    private let fusion: SavedFusionArtifactReader
    private let matchingIndex = MatchingIndexReader()

    init(
        scenes: NativeSceneFactory, images: ArtifactImageRenderer, glb: GlbAssetReader,
        featureMatches: FeatureMatchIndexer, depths: SavedDepthArtifactReader, fusion: SavedFusionArtifactReader,
        json: ArtifactJSONReader
    ) {
        self.featureMatches = featureMatches
        self.scenes = scenes
        self.images = images
        self.glb = glb
        self.depths = depths
        self.fusion = fusion
        self.json = json
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
            let entries = try matchingIndex.read(folder.appendingPathComponent("pairs.ndjson"))
            return entries.map { entry in
                let file = root.appendingPathComponent(entry.artifact).appendingPathComponent("pair.json")
                return unit(
                    entry.artifact, file: file, imageIDs: [entry.a, entry.b], detail: "\(entry.pointCount) candidates")
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
        cloudDetail: CloudDisplayDetail = .automatic, cloudMinimumSupport: Double = 0,
        sparseOptions: SparseDisplayOptions = SparseDisplayOptions()
    )
        async throws -> ArtifactPresentation
    {
        let task = Task.detached(priority: .userInitiated) {
            try await self.loadSaved(
                root: root, run: run, stage: stage, unit: unit, mode: mode, slice: slice,
                cloudDetail: cloudDetail, cloudMinimumSupport: cloudMinimumSupport, sparseOptions: sparseOptions)
        }
        return try await withTaskCancellationHandler(operation: { try await task.value }, onCancel: { task.cancel() })
    }

    private func loadSaved(
        root: URL, run: RunData, stage: PipelineStage, unit: ArtifactUnit?, mode: String, slice: Double,
        cloudDetail: CloudDisplayDetail, cloudMinimumSupport: Double, sparseOptions: SparseDisplayOptions
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
            return try await fusion.load(
                folder: folder, mode: mode, slice: slice, metricScale: metricScale(),
                sceneIdentity: run.id + stage.rawValue, running: run.status == "running")
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
            let reconstruction = try cameraScene(root: root, run: run)
            let scene = scenes.make(
                reconstruction: reconstruction, points: [], photos: photos, root: root, surface: surface,
                uvs: artifact.uvs, texture: image)
            return ArtifactPresentation(
                title: stage.title, detail: detail, scene: scene, sceneIdentity: run.id + stage.rawValue,
                cameraInspection: CameraInspectionData(
                    root: root, cameras: reconstruction?.cameras ?? [], photos: photos),
                metricScale: metricScale())
        case .export:
            let asset = try glb.read(folder.appendingPathComponent("cliff.glb"))
            let reconstruction = try cameraScene(root: root, run: run)
            let scene = scenes.make(
                reconstruction: reconstruction, points: [], photos: photos, root: root, surface: asset.surface,
                uvs: asset.uvs, texture: asset.image, meshMode: MeshViewMode(rawValue: mode) ?? .textured)
            var detail = "\(asset.surface.faces.count.formatted()) triangles · actual exported GLB"
            let report: MeshArtifactReport? = try? read(folder.appendingPathComponent("cliff.report.json"))
            if let topologyDetail = report?.detail { detail += " · \(topologyDetail)" }
            return ArtifactPresentation(
                title: stage.title, detail: detail,
                scene: scene, sceneIdentity: run.id + stage.rawValue,
                cameraInspection: CameraInspectionData(
                    root: root, cameras: reconstruction?.cameras ?? [], photos: photos),
                metricScale: metricScale())
        case .alignment, .cloud, .mesh:
            let alignmentPath = run.artifacts["alignment"] ?? run.artifacts["alignmentPreview"]
            var totalSparsePoints: Int?
            let reconstruction: ReconstructionScene? = try alignmentPath.map {
                let directory = root.appendingPathComponent($0)
                let binaryPreview = directory.appendingPathComponent("preview.bin")
                let previewFile =
                    FileManager.default.fileExists(atPath: binaryPreview.path)
                    ? binaryPreview : directory.appendingPathComponent("preview.json")
                if FileManager.default.fileExists(atPath: previewFile.path) {
                    let preview: AlignmentPreview = try read(previewFile)
                    totalSparsePoints = preview.totalPoints
                    return preview.scene
                }
                let file = directory.appendingPathComponent("scene.json")
                if stage == .alignment { return try read(file) }
                let metadata: ReconstructionMetadata = try read(file)
                return ReconstructionScene(
                    frame: metadata.frame, origin: metadata.origin, cameras: metadata.cameras,
                    points: [], unplaced: [], warnings: [])
            }
            var points = reconstruction?.points ?? []
            var detail = "\(points.count.formatted()) sparse points · \(reconstruction?.cameras.count ?? 0) cameras"
            if stage == .alignment {
                let available = points.count
                let preview = SparsePointPreview(points: points, options: sparseOptions)
                points = preview.points
                let partial = run.artifacts["alignment"] == nil
                let label = partial ? (run.status == "running" ? "LIVE PREVIEW" : "PARTIAL PREVIEW") : "Sparse cloud"
                let source = partial ? "preview" : "saved"
                detail =
                    "\(label) · displaying \(points.count.formatted()) of \(available.formatted()) \(source) points"
                if partial {
                    let total = totalSparsePoints ?? available
                    let coverage = available < total ? "provisional sample" : "provisional alignment"
                    detail += " · \(total.formatted()) reconstructed · \(coverage)"
                }
                if sparseOptions.maximumReprojectionError != nil {
                    detail += " · \(preview.eligibleCount.formatted()) pass error filter"
                }
                detail += " · \(reconstruction?.cameras.count ?? 0) cameras"
            }
            var cloudGeometry: SCNGeometry?
            var surface: Surface?
            var uvs: [[Double]] = []
            var texture: NSImage?
            if stage == .cloud {
                let report: DenseCloudReport = try read(folder.appendingPathComponent("cloud.json"))
                points = report.preview.filter { Float(1 - $0.error) >= Float(cloudMinimumSupport) }
                let partial = run.artifacts["cloud"] == nil
                var displayedCount = points.count
                if !partial {
                    let preview = try DenseCloudPreviewReader().read(
                        file: folder.appendingPathComponent("cloud.ply"), expectedCount: report.retainedSamples,
                        limit: cloudDetail.limit(for: report.retainedSamples), minimumSupport: cloudMinimumSupport)
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
                    !partial && displayedCount == report.retainedSamples
                    ? "all saved points" : "sampled points"
                detail =
                    "\(label) · \(report.retainedSamples.formatted()) accepted points · displaying \(displayedCount.formatted()) \(density) · updated \(updated)"
            }
            if stage == .mesh {
                let savedSurface: Surface = try read(folder.appendingPathComponent("mesh.json"))
                surface = savedSurface
                points = []
                detail = "\(savedSurface.faces.count.formatted()) triangles"
                let report: MeshArtifactReport? = try? read(folder.appendingPathComponent("mesh.report.json"))
                if let topologyDetail = report?.detail { detail += " · \(topologyDetail)" }
                if mode == "Textured", let texturePath = run.artifacts["texture"] {
                    let textureFolder = root.appendingPathComponent(texturePath)
                    let artifact: TextureArtifact = try read(textureFolder.appendingPathComponent("texture.json"))
                    if let meshFile = artifact.meshFile {
                        let texturedSurface: Surface = try read(textureFolder.appendingPathComponent(meshFile))
                        surface = texturedSurface
                    }
                    uvs = artifact.uvs
                    texture = NSImage(contentsOf: textureFolder.appendingPathComponent("atlas.jpg"))
                }
            }
            let scene = scenes.make(
                reconstruction: reconstruction, points: points, photos: photos, root: root,
                surface: surface, uvs: uvs, texture: texture, meshMode: MeshViewMode(rawValue: mode) ?? .textured,
                cloudGeometry: cloudGeometry)
            var inspection: SparseInspectionData?
            if stage == .alignment, let reconstruction {
                let featureFiles: [String: String] =
                    try run.artifacts["features"].map {
                        try read(root.appendingPathComponent($0).appendingPathComponent("units.json"))
                    } ?? [:]
                inspection = SparseInspectionData(
                    root: root, scene: reconstruction, photos: photos, featureFiles: featureFiles, json: json,
                    visiblePoints: points)
            }
            return ArtifactPresentation(
                title: stage.title, detail: detail, scene: scene, sceneIdentity: run.id + stage.rawValue,
                sparseInspection: inspection,
                cameraInspection: stage == .cloud || stage == .mesh
                    ? CameraInspectionData(root: root, cameras: reconstruction?.cameras ?? [], photos: photos) : nil,
                metricScale: reconstruction?.origin != nil && !(reconstruction?.frame.contains("arbitrary") ?? true))
        }
    }

    private func cameraScene(root: URL, run: RunData) throws -> ReconstructionScene? {
        guard let alignment = run.artifacts["alignment"] else { return nil }
        let metadata: ReconstructionMetadata = try read(
            root.appendingPathComponent(alignment).appendingPathComponent("scene.json"))
        return ReconstructionScene(
            frame: metadata.frame, origin: metadata.origin, cameras: metadata.cameras,
            points: [], unplaced: [], warnings: [])
    }

    private func read<T: Decodable>(_ file: URL) throws -> T {
        try json.read(file)
    }
}
