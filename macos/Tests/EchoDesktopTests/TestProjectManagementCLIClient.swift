import Foundation

@testable import EchoDesktop

final class TestProjectManagementCLIClient: CLIClient {
    private(set) var requests: [[String]] = []
    let response = Data(
        #"{"id":"saved-project","name":"Saved project","revision":1,"images":[],"settings":{},"hints":[]}"#.utf8)

    func execute(_ arguments: [String], progress: @escaping @Sendable (String) -> Void) async throws -> Data {
        requests.append(arguments)
        guard arguments.count == 4, arguments.suffix(2) == ["project", "info"] else {
            throw CLIError(message: "Saved artifacts cannot be read")
        }
        return response
    }
}
