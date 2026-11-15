import AppKit
import XCTest

@testable import EchoDesktop

@MainActor
final class ApplicationActivationTests: XCTestCase {
    func testStandaloneLaunchPromotesBackgroundApplicationToForeground() {
        let app = NSApplication.shared
        let originalPolicy = app.activationPolicy()
        defer { app.setActivationPolicy(originalPolicy) }
        app.setActivationPolicy(.prohibited)
        XCTAssertEqual(app.activationPolicy(), .prohibited)

        let delegate = EchoApplicationDelegate()
        delegate.applicationWillFinishLaunching(Notification(name: NSApplication.willFinishLaunchingNotification))

        XCTAssertEqual(app.activationPolicy(), .regular, "A standalone Echo window must be able to take keyboard focus")
    }
}
