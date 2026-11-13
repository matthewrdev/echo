import Foundation

@testable import EchoDesktop

@MainActor
final class TestApplicationWork: ApplicationWork {
    var isWorking = true
    var stopCount = 0
    var started: (() -> Void)?
    private var continuation: CheckedContinuation<Void, Error>?

    func stopForTermination() async throws {
        stopCount += 1
        try await withCheckedThrowingContinuation { continuation in
            self.continuation = continuation
            started?()
        }
    }

    func finish(error: Error? = nil) {
        if let error {
            continuation?.resume(throwing: error)
        } else {
            isWorking = false
            continuation?.resume()
        }
        continuation = nil
    }
}
