import AppKit

@MainActor
final class EchoApplicationDelegate: NSObject, NSApplicationDelegate {
    var termination: ApplicationTerminationCoordinator?
    var isImportingDrone: (() -> Bool)?

    func applicationWillFinishLaunching(_ notification: Notification) {
        // scripts/desktop runs the executable outside an app bundle. Such processes
        // otherwise remain background-only and cannot receive normal keyboard focus.
        NSApplication.shared.setActivationPolicy(.regular)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        if Bundle.main.bundleURL.pathExtension != "app" {
            NSApplication.shared.activate()
        }
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if isImportingDrone?() == true {
            let alert = NSAlert()
            alert.messageText = "Drone Import is working"
            alert.informativeText = "Wait for the current operation to finish before quitting Echo."
            alert.runModal()
            return .terminateCancel
        }
        return termination?.requestTermination { allowed in
            sender.reply(toApplicationShouldTerminate: allowed)
        } ?? .terminateNow
    }
}
