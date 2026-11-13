import Foundation

enum PipelineStage: String, CaseIterable, Identifiable {
    case features, matching, alignment, depth, cloud, fusion, mesh, texture, export
    var id: String { rawValue }

    var title: String {
        switch self {
        case .features: return "Image features"
        case .matching: return "Matched images"
        case .alignment: return "Sparse cloud & cameras"
        case .depth: return "Depth maps"
        case .cloud: return "Dense point cloud"
        case .fusion: return "Fused volume"
        case .mesh: return "Surface mesh"
        case .texture: return "Texture atlas"
        case .export: return "Exported GLB"
        }
    }

    var shortTitle: String {
        switch self {
        case .features: return "Features"
        case .matching: return "Matching"
        case .alignment: return "Alignment"
        case .depth: return "Depth"
        case .cloud: return "Dense cloud"
        case .fusion: return "Fusion"
        case .mesh: return "Mesh"
        case .texture: return "Texture"
        case .export: return "GLB"
        }
    }

    var explanation: String {
        switch self {
        case .features:
            return
                "Inspect detected points and straight line segments. Squares identify line junctions; click a point or line to see its details and candidate matches."
        case .matching:
            return
                "Inspect candidate point and line correspondences between photographs. Select a marker or segment to compare both image patches. Point matches are geometrically checked during alignment; standalone line matches are not yet used by the solver."
        case .alignment:
            return
                "Matched points, GPS and camera orientation initialise poses. Triangulation and bundle adjustment refine cameras and the sparse cloud together."
        case .depth:
            return
                "Stereo compares neighbouring photographs to estimate distance per pixel. Dark regions have no supported depth."
        case .cloud:
            return
                "Depth samples that agree across neighbouring views become coloured 3D points. A sampled preview updates during construction; the complete PLY is saved at the end."
        case .fusion:
            return
                "Supported depth observations are integrated into a signed-distance volume before surface extraction."
        case .mesh:
            return "A connected surface is extracted from the fused volume and simplified to the triangle budget."
        case .texture:
            return
                "Inspect the texture on the reconstructed surface. Switch to Atlas to inspect packed surface patches and their padding."
        case .export:
            return
                "Inspect the actual exported GLB. The georeference sidecar retains the coordinate transform and camera data."
        }
    }
}
