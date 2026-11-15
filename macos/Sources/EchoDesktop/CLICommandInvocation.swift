import Foundation

struct CLICommandInvocation: Codable, Identifiable, Sendable {
    let id: UUID
    let timestamp: Date
    let executable: String
    let arguments: [String]
    let projectRoot: String?
    let isBackgroundQuery: Bool

    init(executable: URL, arguments: [String]) {
        id = UUID()
        timestamp = Date()
        self.executable = executable.pathExtension == "dll" ? "/usr/bin/env" : executable.path
        self.arguments = executable.pathExtension == "dll" ? ["dotnet", executable.path] + arguments : arguments
        var words = arguments
        if let index = words.firstIndex(of: "--project"), index + 1 < words.count {
            projectRoot = URL(fileURLWithPath: words[index + 1]).standardizedFileURL.path
            words.removeSubrange(index...(index + 1))
        } else if words.starts(with: ["project", "init"]), words.count > 2 {
            projectRoot = URL(fileURLWithPath: words[2]).standardizedFileURL.path
        } else {
            projectRoot = nil
        }
        isBackgroundQuery =
            words.first == "inspect" || words.first == "runtime-info"
            || words.starts(with: ["run", "list"]) || words.starts(with: ["run", "status"])
            || words.starts(with: ["images", "metadata"]) || words.starts(with: ["images", "payload"])
    }

    var command: String {
        ([executable] + arguments).map { word in
            let safe =
                !word.isEmpty
                && word.utf8.allSatisfy { byte in
                    (48...57).contains(byte) || (65...90).contains(byte) || (97...122).contains(byte)
                        || "_@%+=:,./-".utf8.contains(byte)
                }
            return safe ? word : "'" + word.replacingOccurrences(of: "'", with: "'\"'\"'") + "'"
        }.joined(separator: " ")
    }
}
