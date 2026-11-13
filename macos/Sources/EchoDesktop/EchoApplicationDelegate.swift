import AppKit

@MainActor
final class EchoApplicationDelegate: NSObject, NSApplicationDelegate {
    var termination: ApplicationTerminationCoordinator?

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        termination?.requestTermination { allowed in
            sender.reply(toApplicationShouldTerminate: allowed)
        } ?? .terminateNow
    }
}
