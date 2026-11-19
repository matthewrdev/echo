import Foundation

struct MeshTopologySummary: Decodable {
    let mainHullFaces: Int
    let boundaryEdges: Int
    let nonManifoldEdges: Int
    let windingConflicts: Int
    let degenerateFaces: Int

    var closed: Bool {
        let valid = nonManifoldEdges == 0 && windingConflicts == 0 && degenerateFaces == 0
        return mainHullFaces > 0 && boundaryEdges == 0 && valid
    }

    var detail: String {
        if closed { return "Closed main hull · 0 open edges" }
        if mainHullFaces == 0 { return "Main hull is empty" }
        let defects = nonManifoldEdges + windingConflicts + degenerateFaces
        let boundary = "\(boundaryEdges.formatted()) open edges"
        return defects > 0 ? "\(boundary) · \(defects.formatted()) topology defects" : boundary
    }
}
