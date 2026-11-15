import AppKit
import Metal
import SceneKit

actor ProjectThumbnailLoader {
    private let scenes: NativeSceneFactory
    private let json: ArtifactJSONReader
    private let glb: GlbAssetReader
    private var cache: [String: ProjectThumbnailCacheEntry] = [:]

    init(scenes: NativeSceneFactory, json: ArtifactJSONReader, glb: GlbAssetReader) {
        self.scenes = scenes
        self.json = json
        self.glb = glb
    }

    func load(root: URL) -> NSImage? {
        guard !Task.isCancelled else { return nil }
        let folders =
            (try? FileManager.default.contentsOfDirectory(
                at: root.appendingPathComponent("runs"), includingPropertiesForKeys: nil)) ?? []
        let runs = folders.compactMap { folder -> ProjectMeshRun? in
            try? json.read(folder.appendingPathComponent("run.json"))
        }.sorted { $0.created > $1.created }

        for run in runs {
            for stage in ["export", "texture", "mesh"] {
                guard !Task.isCancelled else { return nil }
                guard let relative = run.artifacts[stage], let folder = artifactFolder(root: root, relative: relative)
                else { continue }
                do {
                    return try thumbnail(root: root, folder: folder, stage: stage, run: run)
                } catch {
                    // A removed or incomplete result must not hide an earlier saved mesh.
                    continue
                }
            }
        }
        cache[root.path] = nil
        return nil
    }

    private func artifactFolder(root: URL, relative: String) -> URL? {
        let canonical = root.resolvingSymlinksInPath()
        let folder = canonical.appendingPathComponent(relative).resolvingSymlinksInPath()
        return folder.path.hasPrefix(canonical.path + "/") ? folder : nil
    }

    private func thumbnail(root: URL, folder: URL, stage: String, run: ProjectMeshRun) throws -> NSImage {
        var files: [URL]
        var textureArtifact: TextureArtifact?
        switch stage {
        case "export": files = [folder.appendingPathComponent("cliff.glb")]
        case "texture":
            let descriptor = folder.appendingPathComponent("texture.json")
            let artifact: TextureArtifact = try json.read(descriptor)
            textureArtifact = artifact
            let mesh: URL?
            if let relative = artifact.meshFile {
                let candidate = folder.appendingPathComponent(relative).resolvingSymlinksInPath()
                mesh = candidate.path.hasPrefix(root.resolvingSymlinksInPath().path + "/") ? candidate : nil
            } else {
                mesh = run.artifacts["mesh"].flatMap { artifactFolder(root: root, relative: $0) }?
                    .appendingPathComponent("mesh.json")
            }
            guard let mesh else { throw CLIError(message: "Texture has no saved mesh.") }
            files = [mesh, descriptor, folder.appendingPathComponent("atlas.jpg")]
        default: files = [folder.appendingPathComponent("mesh.json")]
        }
        let versions = files.map { ArtifactFileVersion(file: $0) }
        if let entry = cache[root.path], entry.versions == versions { return entry.image }
        let surface: Surface
        var uvs: [[Double]] = []
        var texture: NSImage?
        if stage == "export" {
            let asset = try glb.read(files[0])
            surface = asset.surface
            uvs = asset.uvs
            texture = asset.image
        } else {
            surface = try json.read(files[0])
            if let textureArtifact {
                uvs = textureArtifact.uvs
                guard let image = NSImage(contentsOf: files[2]) else {
                    throw CLIError(message: "Cannot open saved texture.")
                }
                texture = image
            }
        }
        guard !surface.faces.isEmpty, surface.vertices.allSatisfy({ $0.count == 3 && $0.allSatisfy(\.isFinite) }),
            surface.faces.allSatisfy({ $0.count == 3 && $0.allSatisfy { surface.vertices.indices.contains($0) } })
        else { throw CLIError(message: "No valid mesh to preview.") }
        if !uvs.allSatisfy({ $0.count == 2 && $0.allSatisfy(\.isFinite) }) { uvs = [] }
        try Task.checkCancellation()
        let image = try render(root: root, surface: surface, uvs: uvs, texture: texture)
        if cache.count >= 12 { cache.removeAll() }
        cache[root.path] = ProjectThumbnailCacheEntry(versions: versions, image: image)
        return image
    }

    private func render(root: URL, surface: Surface, uvs: [[Double]], texture: NSImage?) throws -> NSImage {
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw CLIError(message: "Mesh thumbnails require Metal.")
        }
        let scene = scenes.make(
            reconstruction: nil, points: [], photos: [], root: root, surface: surface, uvs: uvs, texture: texture)
        scene.background.contents = NSColor(calibratedWhite: 0.12, alpha: 1)
        guard let camera = scene.rootNode.childNode(withName: "viewer-camera", recursively: false) else {
            throw CLIError(message: "Mesh preview has no camera.")
        }
        let size = CGSize(width: 240, height: 180)
        SceneBounds(positions: surface.vertices).fit(camera: camera, size: size)
        let renderer = SCNRenderer(device: device, options: nil)
        renderer.scene = scene
        renderer.pointOfView = camera
        return renderer.snapshot(atTime: 0, with: size, antialiasingMode: .multisampling4X)
    }
}
