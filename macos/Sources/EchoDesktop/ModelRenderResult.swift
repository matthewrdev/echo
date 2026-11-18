struct ModelRenderResult: Codable {
    let input: String
    let output: String
    let width: Int
    let height: Int
    let view: ModelRenderView
    let mode: String
    let vertices: Int
    let triangles: Int
}
