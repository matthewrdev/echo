import Foundation

struct RunData: Decodable, Identifiable {
    let id: String
    let status: String
    let target: String
    let snapshot: ProjectData
    let artifacts: [String: String]
    let error: String?
    let progress: RunProgress?
}
