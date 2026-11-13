import Foundation

struct ArtifactUnit: Identifiable, Hashable {
    let id: String
    let title: String
    let file: URL
    var imageIDs: [String] = []
    var thumbnails: [URL] = []
    var detail: String = ""
}
