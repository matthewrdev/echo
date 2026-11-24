import SwiftUI

struct ArtifactExportMenu: View {
    @ObservedObject var workspace: WorkspaceViewModel

    var body: some View {
        Menu {
            if let run = workspace.displayedRun {
                let hasMesh = run.artifacts["mesh"] != nil
                let hasTexture = run.artifacts["texture"] != nil
                let hasGLB = run.artifacts["export"] != nil
                let hasCloud = run.artifacts["cloud"] != nil
                let glbSource: PipelineStage = hasGLB ? .export : .texture

                Menu("Mesh") {
                    option("GLB", group: "mesh", format: "glb", stage: glbSource, available: hasGLB || hasTexture)
                    option("glTF", group: "mesh", format: "gltf", stage: glbSource, available: hasGLB || hasTexture)
                    option("OBJ", group: "mesh", format: "obj", stage: .texture, available: hasTexture)
                    option("STL", group: "mesh", format: "stl", stage: .mesh, available: hasMesh)
                    option("COLLADA (DAE)", group: "mesh", format: "dae", stage: .texture, available: hasTexture)
                    option("FBX", group: "mesh", format: "fbx", stage: .texture, available: hasTexture)
                    option("USDZ", group: "mesh", format: "usdz", stage: .texture, available: hasTexture)
                    option("PLY (triangle mesh)", group: "mesh", format: "ply", stage: .mesh, available: hasMesh)
                    Button("3MF print export…") { workspace.sheet = .printExport }
                        .disabled(!hasMesh)
                }
                .disabled(!hasMesh && !hasTexture && !hasGLB)

                Menu("Point cloud") {
                    option("PLY", group: "cloud", format: "ply", stage: .cloud)
                    option("PTS", group: "cloud", format: "pts", stage: .cloud)
                    option("XYZ", group: "cloud", format: "xyz", stage: .cloud)
                    option("DXF", group: "cloud", format: "dxf", stage: .cloud)
                    option("LAS (local)", group: "cloud", format: "las", stage: .cloud)
                    option("LAS (georeferenced)", group: "cloud", format: "geo-las", stage: .cloud)
                    option("E57", group: "cloud", format: "e57", stage: .cloud)
                    Button("Survey export (LAS, LAZ, COPC)…") { workspace.sheet = .surveyExport }
                }
                .disabled(!hasCloud)

                Divider()
                Button("Images from saved run…") { workspace.exportArtifact(group: "images") }
                    .disabled(!run.snapshot.images.contains { $0.enabled })
                Button("Geographic data…") { workspace.sheet = .georeference }
                    .disabled(workspace.geographicRun?.artifacts["alignment"] == nil)
            }
        } label: {
            Label("Export saved run", systemImage: "square.and.arrow.up")
        }
        .disabled(!workspace.canEdit || !hasExportableArtifact)
        .accessibilityLabel("Export saved run")
        .help("Export a mesh, point cloud, or geographic data from the displayed run")
    }

    private var hasExportableArtifact: Bool {
        guard let run = workspace.displayedRun else { return false }
        return run.snapshot.images.contains { $0.enabled }
            || ["mesh", "texture", "export", "cloud", "alignment"].contains { run.artifacts[$0] != nil }
    }

    private func option(_ title: String, group: String, format: String, stage: PipelineStage, available: Bool = true)
        -> some View
    {
        Button(title) { workspace.exportArtifact(group: group, format: format, stage: stage) }
            .disabled(!available)
    }
}
