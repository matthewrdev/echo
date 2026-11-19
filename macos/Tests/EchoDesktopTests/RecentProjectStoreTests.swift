import Foundation
import XCTest

@testable import EchoDesktop

final class RecentProjectStoreTests: XCTestCase {
    func testRenamingPreservesRecencyAndFolderPath() throws {
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        _ = store.record(path: "/private/tmp/first.echo", name: "First")
        let original = try XCTUnwrap(store.load().first)
        _ = store.record(path: "/private/tmp/second.echo", name: "Second")
        _ = store.rename(path: original.path, name: "Renamed")

        let saved = UserDefaultsRecentProjectStore(defaults: defaults).load()
        XCTAssertEqual(saved.map(\.name), ["Second", "Renamed"])
        XCTAssertEqual(saved.last?.lastOpened, original.lastOpened)
        XCTAssertEqual(saved.last?.path, original.path)
    }

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

    func testRecentListRetainsAllProjectsAcrossReloadAndReopen() throws {
        let suite = "EchoTests.\(UUID().uuidString)"
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let store = UserDefaultsRecentProjectStore(defaults: defaults)
        for number in 0..<30 {
            _ = store.record(path: "/private/tmp/project-\(number).echo", name: "Project \(number)")
        }

        let reopened = UserDefaultsRecentProjectStore(defaults: defaults)
        let projects = reopened.load()
        XCTAssertEqual(projects.count, 30)
        XCTAssertEqual(projects.first?.name, "Project 29")
        XCTAssertEqual(projects.last?.name, "Project 0")
        let reordered = reopened.record(path: "/private/tmp/project-0.echo", name: "Project 0")
        XCTAssertEqual(reordered.count, 30)
        XCTAssertEqual(reordered.first?.name, "Project 0")
        XCTAssertEqual(Set(reordered.map(\.path)).count, 30)
    }
}
