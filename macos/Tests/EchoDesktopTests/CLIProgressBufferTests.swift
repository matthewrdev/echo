import Foundation
import XCTest

@testable import EchoDesktop

final class CLIProgressBufferTests: XCTestCase {
    @MainActor
    func testBurstIsDeliveredOnceWithoutLosingLinesAndFinalFlushDoesNotRepeat() async throws {
        var batches: [[String]] = []
        let buffer = CLIProgressBuffer { batches.append($0) }
        let lines = (0..<10_000).map { "Event \($0)" }
        for line in lines { buffer.append(line) }
        XCTAssertTrue(batches.isEmpty, "A progress burst must not trigger a workspace update per event")
        buffer.flush()
        XCTAssertEqual(batches, [lines])
        try await Task.sleep(for: .milliseconds(300))
        XCTAssertEqual(batches.count, 1, "The pending timer must not repeat a final flush")

        let delivered = expectation(description: "Updates arrive while processing continues")
        let streaming = CLIProgressBuffer { lines in
            XCTAssertEqual(lines, ["Still processing"])
            delivered.fulfill()
        }
        streaming.append("Still processing")
        await fulfillment(of: [delivered], timeout: 2)
    }

    @MainActor
    func testConcurrentOutputAndErrorStreamsKeepEveryLineInStreamOrder() async {
        var received: [String] = []
        let buffer = CLIProgressBuffer { received.append(contentsOf: $0) }
        await Task.detached {
            DispatchQueue.concurrentPerform(iterations: 4) { stream in
                for index in 0..<1000 { buffer.append("\(stream):\(index)") }
            }
        }.value
        buffer.flush()
        XCTAssertEqual(received.count, 4000)
        XCTAssertEqual(Set(received).count, 4000)
        for stream in 0..<4 {
            XCTAssertEqual(received.filter { $0.hasPrefix("\(stream):") }, (0..<1000).map { "\(stream):\($0)" })
        }
    }
}
