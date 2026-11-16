import SwiftUI

struct ArtifactStatusIcon: View {
    let symbol: String
    let color: Color
    let isProcessing: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        TimelineView(.animation(minimumInterval: 1.0 / 30, paused: !isProcessing || reduceMotion)) { timeline in
            let turns = timeline.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 2.4) / 2.4
            Image(systemName: symbol)
                .font(.caption).foregroundStyle(color)
                .rotationEffect(.degrees(isProcessing && !reduceMotion ? turns * 360 : 0))
                .frame(width: 16, height: 16)
        }
        .accessibilityHidden(true)
    }
}
