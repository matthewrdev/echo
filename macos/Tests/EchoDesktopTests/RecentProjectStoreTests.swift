import Foundation
import XCTest

@testable import EchoDesktop

final class RecentProjectStoreTests: XCTestCase {
    func testReopeningMovesProjectToFrontAndPersistsUpdatedName() throws {
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        _ = store.record(path: "/private/tmp/first.echo", name: "First")
        _ = store.record(path: "/private/tmp/second.echo", name: "Second")
        _ = store.record(path: "/private/tmp/first.echo", name: "Renamed")

        let reopened = UserDefaultsRecentProjectStore(defaults: defaults).load()
        XCTAssertEqual(reopened.map(\.name), ["Renamed", "Second"])
        XCTAssertEqual(reopened.first?.path, "/private/tmp/first.echo")
        XCTAssertEqual(store.remove(path: "/private/tmp/first.echo").map(\.name), ["Second"])
    }

    func testRecentListKeepsOnlyTwelveNewestProjects() throws {
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        for number in 0..<15 {
            _ = store.record(path: "/private/tmp/project-\(number).echo", name: "Project \(number)")
        }

        let projects = store.load()
        XCTAssertEqual(projects.count, 12)
        XCTAssertEqual(projects.first?.name, "Project 14")
        XCTAssertEqual(projects.last?.name, "Project 3")
    }
}
