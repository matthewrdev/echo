import Foundation

struct WorkspaceInspectionResponse: Decodable {
    let projectRoot: String
    let project: ProjectData?
    let run: WorkspaceRunResponse?
    let reconstruction: ReconstructionScene?
    let cloud: DenseCloudReport?
    let warnings: [String]?

    func inspection(cached: Inspection?) throws -> Inspection {
        guard let project = project ?? cached?.project else {
            throw CocoaError(.fileReadCorruptFile)
        }
        return Inspection(
            projectRoot: projectRoot, project: project, run: try run?.data(cached: cached?.run?.snapshot),
            reconstruction: reconstruction, cloud: cloud, warnings: warnings)
    }
}
