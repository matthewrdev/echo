import Foundation

struct WorkspaceRunResponse: Decodable {
    let id: String
    let status: String
    let target: String
    let snapshot: ProjectData?
    let artifacts: [String: String]
    let error: String?
    let progress: RunProgress?
    let storageVersion: Int?
    let snapshotToken: String?

    func data(cached: ProjectData?) throws -> RunData {
        guard let snapshot = snapshot ?? cached else { throw CocoaError(.fileReadCorruptFile) }
        return RunData(
            id: id, status: status, target: target, snapshot: snapshot, artifacts: artifacts,
            error: error, progress: progress, storageVersion: storageVersion, snapshotToken: snapshotToken)
    }
}
