import Foundation

@MainActor
protocol QuitConfirmation {
    func confirmStopAndQuit() -> Bool
    func showFailure(_ error: Error)
}
