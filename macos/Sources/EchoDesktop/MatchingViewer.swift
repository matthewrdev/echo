import SwiftUI

struct MatchingViewer: View {
    let data: MatchPresentation
    @Binding var selected: Int?
    @StateObject private var controller = CanvasViewportController()
    private var summary: String {
        let verified = data.matches.filter { $0.geometricInlier == true }.count
        let rejected = data.matches.filter { $0.geometricInlier == false }.count
        let unverified = data.matches.filter { $0.geometricInlier == nil && !$0.manual }.count
        return
            "All \(data.matches.count) candidates · \(verified) passed geometry · \(rejected) rejected (grey) · \(unverified) unverified"
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text(summary)
                    .font(.caption).foregroundStyle(.secondary)
                Spacer()
                if let diagnostics = data.diagnostics {
                    Text("Matching details").font(.caption).foregroundStyle(.secondary)
                        .help(
                            "Keypoints A/B: \(diagnostics.featuresA)/\(diagnostics.featuresB)\nAccepted: \(diagnostics.accepted)\nRejected: \(diagnostics.rejectedNonMutual) non-mutual, \(diagnostics.rejectedAmbiguous) ambiguous, \(diagnostics.rejectedDistance) too different"
                        )
                }
            }
            CanvasControls(controller: controller)
            CanvasViewport(
                renderer: MatchCanvasRenderer(
                    data: data, selected: selected, selection: { selected = $0 }),
                controller: controller
            )
            .frame(minHeight: 50, maxHeight: .infinity)
            if let selected, data.matches.indices.contains(selected) {
                Divider()
                HStack(alignment: .top, spacing: 16) {
                    MatchPatchView(
                        title: "A · \(data.first.name)", image: data.firstImage, photo: data.first,
                        pixel: data.matches[selected].a,
                        overlay: FeaturePatchOverlay(line: data.matches[selected].lineA))
                    Image(systemName: "arrow.right").padding(.top, 80).foregroundStyle(.secondary)
                    MatchPatchView(
                        title: "B · \(data.second.name)", image: data.secondImage, photo: data.second,
                        pixel: data.matches[selected].b,
                        overlay: FeaturePatchOverlay(line: data.matches[selected].lineB))
                    VStack(alignment: .leading, spacing: 10) {
                        HStack {
                            Text("Correspondence \(selected + 1)").font(.headline)
                            Spacer()
                            Button("Close match inspector", systemImage: "xmark") { self.selected = nil }
                                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help("Close match inspector")
                        }
                        HStack {
                            Button("Previous", systemImage: "chevron.left") { self.selected = max(0, selected - 1) }
                                .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain)
                            Text("\(selected + 1) of \(data.matches.count)").font(.caption)
                            Button("Next", systemImage: "chevron.right") {
                                self.selected = min(data.matches.count - 1, selected + 1)
                            }.labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain)
                        }
                        let match = data.matches[selected]
                        Text(match.verificationLabel)
                            .font(.caption).fontWeight(.semibold)
                            .foregroundStyle(match.geometricInlier == false ? .red : .primary)
                            .fixedSize(horizontal: false, vertical: true)
                        if match.manual {
                            Text("Manual tie point").font(.caption).foregroundStyle(.orange)
                        } else {
                            if let score = match.score {
                                Text("Descriptor distance\n\(score.formatted(.number.precision(.fractionLength(4))))")
                                    .font(.caption).textSelection(.enabled)
                            }
                            Text(
                                "Mutual nearest neighbour; ratio and distance filters passed. Lower distance means more similar, not a higher probability."
                            )
                            .font(.caption2).foregroundStyle(.secondary)
                        }
                        Text("Coordinates refer to full-resolution, orientation-normalized photographs.")
                            .font(.caption2).foregroundStyle(.secondary)
                    }.frame(width: 220)
                }.frame(minHeight: 230, idealHeight: 250, maxHeight: 280)
            }
        }
    }
}
