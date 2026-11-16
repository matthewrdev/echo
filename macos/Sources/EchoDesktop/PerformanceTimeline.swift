import Charts
import SwiftUI

struct PerformanceTimeline: View {
    let spans: [PerformanceSpan]
    let duration: Double
    @State private var page = 0

    private var pages: Int { max(1, (spans.count + 299) / 300) }
    private var visible: [PerformanceSpan] { Array(spans.dropFirst(min(page, pages - 1) * 300).prefix(300)) }

    var body: some View {
        ScrollView(.vertical) {
            Chart(visible) { span in
                BarMark(
                    xStart: .value("Start (s)", span.startMilliseconds / 1000),
                    xEnd: .value("End (s)", (span.startMilliseconds + max(0.1, span.durationMilliseconds)) / 1000),
                    y: .value("Step", "\(span.id) · \(span.name)")
                )
                .foregroundStyle(by: .value("Backend", span.backend.rawValue.uppercased()))
                .opacity(span.status == .reused ? 0.4 : 0.9)
                .cornerRadius(2)
            }
            .chartXScale(domain: 0...max(0.001, duration / 1000))
            .chartXAxisLabel("Seconds since this attempt started")
            .chartForegroundStyleScale(["CPU": Color.blue, "METAL": Color.purple, "MIXED": Color.teal])
            .frame(height: max(100, CGFloat(visible.count) * 25 + 60))
            .padding(.horizontal, 8)
        }
        .frame(minHeight: 140, idealHeight: 250, maxHeight: 300)
        if pages > 1 {
            HStack {
                Button("Earlier") { page = max(0, page - 1) }.disabled(page == 0)
                Text("Intervals · page \(min(page, pages - 1) + 1) of \(pages)").font(.caption)
                Button("Later") { page = min(pages - 1, page + 1) }.disabled(page >= pages - 1)
                Button("Latest") { page = pages - 1 }
            }
        }
    }
}
