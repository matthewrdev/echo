package struct ModelRenderResult: Codable {
    package let input: String
    package let output: String
    package let width: Int
    package let height: Int
    package let view: ModelRenderView
    package let mode: String
    package let vertices: Int
    package let triangles: Int
}
