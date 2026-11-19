import Foundation

@testable import EchoDesktop

actor TestPhotoImportCLIClient: CLIClient {
    private var progress: (@Sendable (String) -> Void)?
    private var completion: CheckedContinuation<Data, Error>?
    var importing: Bool { completion != nil }

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        let command = Array(arguments.dropFirst(2))
        if command.prefix(2) == ["images", "import"] {
            self.progress = progress
            progress(#"{"type":"imageImportStarted","count":0,"completed":0,"total":540,"skipped":0}"#)
            return try await withCheckedThrowingContinuation { completion = $0 }
        }
        if command == ["run", "list"] { return Data("[]".utf8) }
        if command.first == "inspect" {
            return Data(
                #"{"projectRoot":"/tmp/import-test.echo","project":{"id":"import-test","name":"Import test","revision":1,"images":[],"settings":{},"hints":[]}}"#
                    .utf8)
        }
        throw CLIError(message: "Unexpected test command: \(command)")
    }

    func emit(_ line: String) { progress?(line) }

    func finish(failing: Bool = false) {
        if failing {
            completion?.resume(throwing: CLIError(message: "Unreadable photo"))
        } else {
            completion?.resume(returning: Data("{}".utf8))
        }
        completion = nil
        progress = nil
    }
}
