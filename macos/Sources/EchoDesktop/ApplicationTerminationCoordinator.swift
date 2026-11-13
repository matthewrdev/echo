import AppKit

@MainActor
final class ApplicationTerminationCoordinator {
    private let work: ApplicationWork
    private let confirmation: QuitConfirmation
    private var waitingToQuit = false

    init(work: ApplicationWork, confirmation: QuitConfirmation) {
        self.work = work
        self.confirmation = confirmation
    }

    func requestTermination(reply: @escaping (Bool) -> Void) -> NSApplication.TerminateReply {
        if waitingToQuit { return .terminateLater }
        guard work.isWorking else { return .terminateNow }
        guard confirmation.confirmStopAndQuit() else { return .terminateCancel }
        waitingToQuit = true
        Task {
            do {
                try await work.stopForTermination()
                waitingToQuit = false
                reply(true)
            } catch {
                waitingToQuit = false
                confirmation.showFailure(error)
                reply(false)
            }
        }
        return .terminateLater
    }
}
