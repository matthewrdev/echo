import Foundation

@testable import EchoDesktop

@MainActor
final class TestQuitConfirmation: QuitConfirmation {
    var approved = false
    var prompts = 0
    var failures = 0

    func confirmStopAndQuit() -> Bool {
        prompts += 1
        return approved
    }

    func showFailure(_ error: Error) {
        failures += 1
    }
}
