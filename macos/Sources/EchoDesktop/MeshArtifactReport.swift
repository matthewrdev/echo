struct MeshArtifactReport: Decodable {
    let topology: MeshTopologySummary?
    let closeHull: Bool?

    var detail: String? {
        guard let topology else { return nil }
        return topology.detail + (closeHull == true ? " · includes inferred surfaces" : "")
    }
}
