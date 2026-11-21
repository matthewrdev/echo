import AppKit
import EchoRendering

struct GeoreferenceArtifactReader {
    func load(root: URL, run: RunData) throws -> GeoreferenceArtifact {
        guard let alignment = run.artifacts["alignment"] else {
            throw CLIError(message: "Complete alignment to view geographic placement.")
        }
        let json = ArtifactJSONReader()
        let reconstructionFile = root.appendingPathComponent(alignment).appendingPathComponent("scene.json")
        let metadata: ReconstructionMetadata = try json.read(reconstructionFile)
        guard metadata.isMetric, let origin = metadata.origin,
            origin.latitude.isFinite, origin.longitude.isFinite,
            abs(origin.latitude) <= 85, abs(origin.longitude) <= 180
        else {
            throw CLIError(
                message: "This run has no metric geographic placement. Add camera position hints and align again.")
        }
        let reconstruction = ReconstructionScene(
            frame: metadata.frame, origin: origin, cameras: metadata.cameras, points: [], unplaced: [], warnings: [])
        var surface: Surface?
        var uvs: [[Double]] = []
        var texture: NSImage?
        var source = "Aligned cameras"
        if let export = run.artifacts["export"] {
            let asset = try GlbAssetReader().read(
                root.appendingPathComponent(export).appendingPathComponent("cliff.glb"))
            surface = asset.surface
            uvs = asset.uvs
            texture = asset.image
            source = "Exported model"
        } else if let mesh = run.artifacts["mesh"] {
            var file = root.appendingPathComponent(mesh).appendingPathComponent("mesh.json")
            if let path = run.artifacts["texture"] {
                let folder = root.appendingPathComponent(path)
                let result: TextureArtifact = try json.read(folder.appendingPathComponent("texture.json"))
                if let meshFile = result.meshFile { file = folder.appendingPathComponent(meshFile) }
                uvs = result.uvs
                texture = NSImage(contentsOf: folder.appendingPathComponent("atlas.jpg"))
            }
            let savedSurface: Surface = try json.read(file)
            surface = savedSurface
            source = texture == nil ? "Saved mesh" : "Textured mesh"
        }
        try Task.checkCancellation()
        let overlay = try surface.map {
            try ModelMapProjector().render(surface: $0, uvs: uvs, texture: texture, origin: origin)
        }
        let scene = NativeSceneFactory().make(
            reconstruction: reconstruction, points: [], photos: run.snapshot.images, root: root,
            surface: surface, uvs: uvs, texture: texture)
        let cameras = MapCameraProjector().cameras(
            photos: run.snapshot.images, hints: run.snapshot.hints, reconstruction: reconstruction, aligned: true,
            distance: 20)
        return GeoreferenceArtifact(
            reconstruction: reconstruction,
            presentation: ArtifactPresentation(
                title: "Geographic view", detail: source, scene: scene, sceneIdentity: run.id + "geographic",
                cameraInspection: CameraInspectionData(
                    root: root, cameras: metadata.cameras, photos: run.snapshot.images),
                metricScale: true),
            overlay: overlay, cameras: cameras, surface: surface,
            bounds: SceneBounds(positions: surface?.vertices ?? metadata.cameras.map(\.centre)))
    }
}
