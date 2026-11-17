import SceneKit
import SwiftUI

struct CropPointCountView: View {
    let scene: SCNScene?
    let regions: [ExclusionRegion]
    @State private var counts: CropPointCounts?

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            if !regions.allSatisfy(\.isValid) {
                Text("Correct the region values to count points.")
            } else if let counts {
                HStack {
                    Label("Included: \(counts.included.formatted())", systemImage: "checkmark.circle")
                        .foregroundStyle(.green)
                    Spacer()
                }
                Label("Excluded: \(counts.excluded.formatted())", systemImage: "minus.circle")
                    .foregroundStyle(.red)
            } else {
                ProgressView("Counting points…").controlSize(.small)
            }
        }
        .font(.caption).monospacedDigit()
        .frame(maxWidth: .infinity, minHeight: 38, alignment: .topLeading)
        .help("Counts for the points or vertices in the loaded preview.")
        .accessibilityElement(children: .combine)
        .task(id: regions) { await update() }
    }

    @MainActor
    private func update() async {
        counts = nil
        guard regions.allSatisfy(\.isValid) else { return }
        do {
            try await Task.sleep(for: .milliseconds(120))
            let counter = CropPointCounter(scene: scene)
            let snapshot = regions
            let worker = Task.detached(priority: .userInitiated) { try counter.count(regions: snapshot) }
            let result = try await withTaskCancellationHandler {
                try await worker.value
            } onCancel: {
                worker.cancel()
            }
            try Task.checkCancellation()
            counts = result
        } catch is CancellationError {
            // A newer region edit owns the count now.
        } catch {
            counts = nil
        }
    }
}
