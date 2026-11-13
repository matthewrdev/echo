import AppKit

@MainActor
final class NativeQuitConfirmation: QuitConfirmation {
    func makeAlert() -> NSAlert {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Processing is still running"
        alert.informativeText =
            "Stop processing and quit Echo? Completed checkpoints will be kept so you can resume later. The current unfinished image or stage may need to run again. Echo will wait for processing to stop, or for an import or save to finish, before closing."
        alert.addButton(withTitle: "Keep Processing").keyEquivalent = "\r"
        alert.addButton(withTitle: "Stop and Quit").keyEquivalent = ""
        return alert
    }

    func confirmStopAndQuit() -> Bool {
        makeAlert().runModal() == .alertSecondButtonReturn
    }

    func showFailure(_ error: Error) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Echo could not stop processing safely"
        alert.informativeText = "Echo will stay open. \(error.localizedDescription)"
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }
}
