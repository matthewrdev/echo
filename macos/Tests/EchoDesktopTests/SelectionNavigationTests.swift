import SwiftUI
import XCTest

@testable import EchoDesktop

final class SelectionNavigationTests: XCTestCase {
    private let navigation = SelectionNavigation()

    func testListFollowsVisibleOrderAndStopsAtEnds() {
        let rows = [["third"], ["first"], ["last"]]
        XCTAssertEqual(navigation.destination(in: rows, from: "third", key: .downArrow), "first")
        XCTAssertEqual(navigation.destination(in: rows, from: "first", key: .upArrow), "third")
        XCTAssertEqual(navigation.destination(in: rows, from: "third", key: .upArrow), "third")
        XCTAssertEqual(navigation.destination(in: rows, from: "last", key: .downArrow), "last")
        XCTAssertEqual(navigation.destination(in: rows, from: "filtered-out", key: .downArrow), "third")
        XCTAssertEqual(navigation.destination(in: rows, from: nil, key: .downArrow), "third")
        XCTAssertNil(navigation.destination(in: [] as [[String]], from: nil, key: .downArrow))
    }

    func testGridMovesByRowsAndAcrossPartialRows() {
        let rows = [["a", "b", "c"], ["d", "e"], ["f", "g", "h"]]
        XCTAssertEqual(navigation.destination(in: rows, from: "b", key: .downArrow), "e")
        XCTAssertEqual(navigation.destination(in: rows, from: "c", key: .downArrow), "e")
        XCTAssertEqual(navigation.destination(in: rows, from: "h", key: .upArrow), "e")
        XCTAssertEqual(navigation.destination(in: rows, from: "e", key: .downArrow), "g")
        XCTAssertEqual(navigation.destination(in: rows, from: "c", key: .rightArrow), "d")
        XCTAssertEqual(navigation.destination(in: rows, from: "f", key: .leftArrow), "e")
        XCTAssertEqual(navigation.destination(in: rows, from: "a", key: .leftArrow), "a")
        XCTAssertEqual(navigation.destination(in: rows, from: "h", key: .rightArrow), "h")
    }

    func testSamePhotoInMultipleGroupsHasDistinctNavigationPositions() {
        let first = PhotoTileIdentifier(group: "First", photoID: "shared")
        let second = PhotoTileIdentifier(group: "Second", photoID: "shared")
        let last = PhotoTileIdentifier(group: "Second", photoID: "last")
        XCTAssertEqual(navigation.destination(in: [[first], [second, last]], from: second, key: .rightArrow), last)
        XCTAssertEqual(navigation.destination(in: [[first], [second, last]], from: second, key: .upArrow), first)
    }
}
