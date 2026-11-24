import Foundation

final class ProcessCLIClient: CLIClient {
    private let executable: URL
    private let commandLog: CLICommandLog?

    init(executable: URL, commandLog: CLICommandLog? = nil) {
        self.executable = executable
        self.commandLog = commandLog
    }

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let invocation = CLICommandInvocation(executable: executable, arguments: arguments)
        await commandLog?.record(invocation)
        return try await execute(invocation, progress: progress)
    }

    func executeUnlogged(_ arguments: [String]) async throws -> Data {
        try await execute(CLICommandInvocation(executable: executable, arguments: arguments), progress: nil)
    }

    private func execute(_ invocation: CLICommandInvocation, progress: (@Sendable (String) -> Void)?)
        async throws
        -> Data
    {
        try await withCheckedThrowingContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                do {
                    let process = Process()
                    process.executableURL = URL(fileURLWithPath: invocation.executable)
                    process.arguments = invocation.arguments

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

                    if process.terminationStatus == CLIExitCode.cancelled.rawValue {
                        throw CancellationError()
                    }
                    guard process.terminationStatus == CLIExitCode.success.rawValue else {
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
