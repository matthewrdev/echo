import Foundation

final class ProcessCLIClient: CLIClient {
    private let executable: URL

    init(executable: URL) {
        self.executable = executable
    }

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let executable = executable
        return try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                do {
                    let process = Process()
                    if executable.pathExtension == "dll" {
                        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
                        process.arguments = ["dotnet", executable.path] + arguments
                    } else {
                        process.executableURL = executable
                        process.arguments = arguments
                    }

                    let standardOutput = Pipe()
                    let standardError = Pipe()
                    process.standardOutput = standardOutput
                    process.standardError = standardError
                    let output = PipeCapture(handle: standardOutput.fileHandleForReading, progress: progress)
                    let errors = PipeCapture(handle: standardError.fileHandleForReading, progress: progress)
                    try process.run()

                    let readers = DispatchGroup()
                    for capture in [output, errors] {
                        readers.enter()
                        DispatchQueue.global().async {
                            capture.readToEnd()
                            readers.leave()
                        }
                    }
                    process.waitUntilExit()
                    readers.wait()

                    guard process.terminationStatus == 0 else {
                        let raw = String(decoding: errors.data, as: UTF8.self)
                        let lastLine = raw.split(separator: "\n").last.map(String.init) ?? raw
                        let record = lastLine.data(using: .utf8).flatMap {
                            try? JSONSerialization.jsonObject(with: $0) as? [String: Any]
                        }
                        let detail = record?["error"] as? String ?? raw
                        throw CLIError(
                            message: detail.isEmpty ? "CLI exited with code \(process.terminationStatus)." : detail)
                    }
                    continuation.resume(returning: output.data)
                } catch {
                    continuation.resume(throwing: error)
                }
            }
        }
    }
}
