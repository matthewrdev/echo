import Foundation

struct Inspection: Decodable {
    let projectRoot: String
    let project: ProjectData
    var run: RunData?
    let reconstruction: ReconstructionScene?
    let cloud: DenseCloudReport?
    var warnings: [String]? = nil
}
