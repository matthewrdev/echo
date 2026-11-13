import Foundation

struct AppBuildInfo: Decodable {
    let commit: String
    let dirty: Bool
    let builtAt: String
    let swiftCompiler: String
    let targetRuntime: String
    let processorDeployment: String
    let signing: String

    init?(bundle: Bundle) {
        guard let url = bundle.url(forResource: "BuildInfo", withExtension: "json"),
            let data = try? Data(contentsOf: url),
            let info = try? JSONDecoder().decode(Self.self, from: data)
        else { return nil }
        self = info
    }
}
