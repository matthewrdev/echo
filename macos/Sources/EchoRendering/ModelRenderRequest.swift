import Foundation

package struct ModelRenderRequest: Codable {
    package let input: String
    package let output: String
    package let width: Int
    package let height: Int
    package let view: ModelRenderView
    package let mode: String

    package func validate() throws {
        guard (1...8192).contains(width), (1...8192).contains(height), width * height <= 16_777_216 else {
            throw ModelRenderError(
                message: "Render dimensions must be 1–8192 per side, at most 16,777,216 pixels total.")
        }
        guard meshMode != nil else { throw ModelRenderError(message: "Unknown render mesh mode: \(mode).") }
        guard URL(fileURLWithPath: input).pathExtension.lowercased() == "glb",
            FileManager.default.fileExists(atPath: input)
        else { throw ModelRenderError(message: "Render input must be an existing Echo-exported GLB.") }
        guard URL(fileURLWithPath: output).pathExtension.lowercased() == "png" else {
            throw ModelRenderError(message: "Render output must be a PNG file.")
        }
        guard !FileManager.default.fileExists(atPath: output) else {
            throw ModelRenderError(message: "Render output already exists.")
        }
    }

    package var meshMode: MeshViewMode? {
        MeshViewMode.allCases.first { $0.rawValue.lowercased() == mode }
    }

    package init(input: String, output: String, width: Int, height: Int, view: ModelRenderView, mode: String) {
        self.input = input
        self.output = output
        self.width = width
        self.height = height
        self.view = view
        self.mode = mode
    }
}
