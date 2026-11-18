import Foundation

struct ModelRenderRequest: Codable {
    let input: String
    let output: String
    let width: Int
    let height: Int
    let view: ModelRenderView
    let mode: String

    func validate() throws {
        guard (1...8192).contains(width), (1...8192).contains(height), width * height <= 16_777_216 else {
            throw CLIError(message: "Render dimensions must be 1–8192 per side, at most 16,777,216 pixels total.")
        }
        guard meshMode != nil else { throw CLIError(message: "Unknown render mesh mode: \(mode).") }
        guard URL(fileURLWithPath: input).pathExtension.lowercased() == "glb",
            FileManager.default.fileExists(atPath: input)
        else { throw CLIError(message: "Render input must be an existing Echo-exported GLB.") }
        guard URL(fileURLWithPath: output).pathExtension.lowercased() == "png" else {
            throw CLIError(message: "Render output must be a PNG file.")
        }
        guard !FileManager.default.fileExists(atPath: output) else {
            throw CLIError(message: "Render output already exists.")
        }
    }

    var meshMode: MeshViewMode? {
        MeshViewMode.allCases.first { $0.rawValue.lowercased() == mode }
    }
}
