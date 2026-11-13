import Foundation

struct ProjectData: Decodable {
    let id: String
    let name: String
    let revision: Int
    let images: [Photo]
    let settings: ProcessingSettings
    let hints: [ImageHint]
    var stageRevisions: [String: String]? = nil
}
