import Foundation
import SwiftUI

@MainActor
final class TelemetryHistory: ObservableObject {
    @Published private(set) var samples: [TelemetrySample] = []
    private var identity = ""

    func record(root: String, runID: String?, resources: ResourceProgress?) {
        let nextIdentity = runID.map { root + "/" + $0 } ?? ""
        if identity != nextIdentity {
            identity = nextIdentity
            samples = []
        }
        guard !identity.isEmpty, let resources, let time = resources.sampledDate else { return }
        if let last = samples.last, time <= last.time { return }
        samples.append(TelemetrySample(time: time, resources: resources))
        let cutoff = time.addingTimeInterval(-600)
        samples.removeAll { $0.time < cutoff }
        if samples.count > 240 { samples.removeFirst(samples.count - 240) }
    }
}
