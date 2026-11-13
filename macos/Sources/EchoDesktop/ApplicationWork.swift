import Foundation

@MainActor
protocol ApplicationWork: AnyObject {
    var isWorking: Bool { get }
    func stopForTermination() async throws
}
