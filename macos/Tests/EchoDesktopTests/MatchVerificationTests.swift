import Foundation
import Testing

@testable import EchoDesktop

struct MatchVerificationTests {
    @Test func distinguishesRejectedAndUnverifiedCandidates() throws {
        let decoder = JSONDecoder()
        let legacy = try decoder.decode(ImageMatch.self, from: Data(#"{"a":[1,2],"b":[3,4],"manual":false}"#.utf8))
        #expect(legacy.geometricInlier == nil)
        #expect(legacy.verificationLabel.contains("Unverified point"))
        let rejected = try decoder.decode(
            ImageMatch.self, from: Data(#"{"a":[1,2],"b":[3,4],"manual":false,"geometricInlier":false}"#.utf8))
        #expect(rejected.verificationLabel.contains("Rejected"))
        #expect(rejected.verificationLabel.contains("excluded from alignment"))
        let passed = try decoder.decode(
            ImageMatch.self, from: Data(#"{"a":[1,2],"b":[3,4],"manual":false,"geometricInlier":true}"#.utf8))
        #expect(passed.verificationLabel.contains("Passed pair geometry"))
        #expect(passed.verificationLabel.contains("3D consistency checked during alignment"))
        let line = ImageMatch(a: [1, 2], b: [3, 4], score: 0.1, manual: false, kind: "line")
        #expect(line.verificationLabel.contains("Unverified line"))
        #expect(line.verificationLabel.contains("excluded from alignment"))
    }
}
