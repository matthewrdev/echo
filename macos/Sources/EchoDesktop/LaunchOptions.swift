import Foundation

struct LaunchOptions {
    let cli: URL
    let project: String

    init(arguments: [String], bundle: Bundle) {
        func option(_ name: String) -> String? {
            guard let index = arguments.firstIndex(of: name), index + 1 < arguments.count else { return nil }
            return arguments[index + 1]
        }

        let bundled = bundle.bundleURL.appendingPathComponent("Contents/MacOS/cli/echo-photo")
        cli = option("--cli").map { URL(fileURLWithPath: $0) } ?? bundled
        project = option("--project") ?? ""
    }
}
