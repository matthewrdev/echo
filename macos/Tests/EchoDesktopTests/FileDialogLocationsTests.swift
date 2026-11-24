import Foundation
import XCTest

@testable import EchoDesktop

final class FileDialogLocationsTests: XCTestCase {
    func testProjectAndPhotoDialogsRememberSeparateLocations() throws {
        let suite = UUID().uuidString
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let locations = FileDialogLocations(defaults: defaults)
        let projects = URL.homeDirectory
        let photos = URL.temporaryDirectory
        let project = projects.appending(path: "New.echo", directoryHint: .isDirectory)

        locations.remember(project, for: .newProject)
        XCTAssertEqual(locations.startingDirectory(for: .importPhotos, fallback: photos).path, photos.path)

        locations.remember(photos, for: .importPhotos)
        let reopened = FileDialogLocations(defaults: try XCTUnwrap(UserDefaults(suiteName: suite)))
        XCTAssertEqual(reopened.startingDirectory(for: .newProject).path, projects.path)
        XCTAssertEqual(reopened.startingDirectory(for: .importPhotos).path, photos.path)
        XCTAssertEqual(reopened.startingDirectory(for: .openProject, fallback: photos).path, photos.path)
    }

    func testMissingDirectoryUsesFallback() throws {
        let suite = UUID().uuidString
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let locations = FileDialogLocations(defaults: defaults)
        let fallback = URL.temporaryDirectory
        let missing = fallback.appending(path: UUID().uuidString, directoryHint: .isDirectory)

        locations.remember(missing, for: .droneImportDestination)

        XCTAssertEqual(
            locations.startingDirectory(for: .droneImportDestination, fallback: fallback).path, fallback.path)
    }
}
