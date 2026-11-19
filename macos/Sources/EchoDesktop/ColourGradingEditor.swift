import SwiftUI

struct ColourGradingEditor: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var model: ColourGradingModel
    let minimumSupport: Double
    @State private var scope = "included"
    @State private var logarithmic = false

    private var regions: [ExclusionRegion] { workspace.inspection?.project.settings.exclusionRegions ?? [] }
    private var histogramRegions: [ExclusionRegion] {
        if scope == "all" { return [] }
        if scope == "included" { return regions }
        if scope == "selection" { return model.selection.map { [$0] } ?? [] }
        return regions.filter { $0.mode == .exclude || $0.id == scope }
    }
    private var scanKey: String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = .sortedKeys
        let encoded = (try? encoder.encode(histogramRegions)).map { String(decoding: $0, as: UTF8.self) } ?? ""
        return workspace.root + (workspace.displayedRun?.artifacts["cloud"] ?? "") + String(minimumSupport) + encoded
    }
    private var saved: Bool {
        (workspace.inspection?.project.settings.colourGrade ?? ColourGrade()) == model.grade
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Text("Colour grading").font(.headline)
                Toggle("Show original colours", isOn: $model.showOriginal)
                    .accessibilityIdentifier("colourGrade.original")
                Picker("Histogram region", selection: $scope) {
                    Text("Included points").tag("included")
                    Text("Whole cloud").tag("all")
                    if model.selection != nil { Text("Painted selection").tag("selection") }
                    ForEach(
                        Array(regions.enumerated()).filter { $0.element.enabled && $0.element.mode == .include },
                        id: \.element.id
                    ) { index, region in
                        Text("Include region \(index + 1)").tag(region.id)
                    }
                }
                Button(
                    model.selectingRegion ? "Cancel selection" : "Paint histogram region",
                    systemImage: "paintbrush.pointed"
                ) {
                    model.selectingRegion.toggle()
                    model.selectionError = nil
                }
                if model.selectingRegion {
                    Text(
                        "Paint around the region to measure. The outline extends through the cloud. Option-drag orbits."
                    )
                    .font(.caption).foregroundStyle(.secondary)
                }
                if let error = model.selectionError { Text(error).font(.caption).foregroundStyle(.red) }
                Text("Uses every saved point passing Depth agreement, regardless of display detail.")
                    .font(.caption).foregroundStyle(.secondary)
                if model.scanning {
                    ProgressView("Reading full cloud…", value: model.progress)
                    Button("Cancel histogram") { model.cancel() }
                } else if let histogram = model.histogram {
                    ColourHistogramView(histogram: histogram, logarithmic: logarithmic)
                    Text("\(histogram.count.formatted()) points · \(model.showOriginal ? "original" : "graded")")
                        .font(.caption).foregroundStyle(.secondary)
                    if histogram.count > 0 {
                        Text(
                            "Channel clipping · black \(percentage(histogram.clippedBlack, histogram.count)) · white \(percentage(histogram.clippedWhite, histogram.count))"
                        )
                        .font(.caption).foregroundStyle(.secondary)
                    } else {
                        Text("No points match this region and depth-agreement filter.").font(.caption)
                    }
                    Toggle("Logarithmic histogram", isOn: $logarithmic).font(.caption)
                } else {
                    Text("Histograms use the completed cloud, including points beyond the display limit.")
                        .font(.caption).foregroundStyle(.secondary)
                    Button("Calculate histogram") { startScan() }
                        .disabled(workspace.displayedRun?.artifacts["cloud"] == nil)
                }
                if model.updating { ProgressView("Updating histogram…").controlSize(.small) }
                if let error = model.error { Text(error).font(.caption).foregroundStyle(.red) }
                Divider()
                control("Exposure (EV)", value: $model.grade.exposure, range: -4...4)
                control("Temperature", value: $model.grade.temperature, range: -1...1)
                control("Tint", value: $model.grade.tint, range: -1...1)
                control("Black point", value: $model.grade.blackPoint, range: 0...0.49)
                control("White point", value: $model.grade.whitePoint, range: 0.51...1)
                control("Contrast", value: $model.grade.contrast, range: 0.25...2)
                control("Shadows", value: $model.grade.shadows, range: -1...1)
                control("Highlights", value: $model.grade.highlights, range: -1...1)
                control("Saturation", value: $model.grade.saturation, range: 0...2)
                control("Vibrance", value: $model.grade.vibrance, range: -1...1)
                HStack {
                    Button("Reset") {
                        model.grade = ColourGrade()
                        model.showOriginal = false
                    }
                    Button("Revert to saved") { model.load(workspace.inspection?.project.settings.colourGrade) }
                        .disabled(saved)
                }
                Text(
                    "The grade applies to the whole scan. Painted histogram selections are temporary and do not change your crop or processing settings."
                )
                .font(.caption).foregroundStyle(.secondary)
                Text(
                    "Save, then process Texture and Export. Textures use the original photographs with seam balancing and colour enhancement before this grade. Source colours stay intact."
                )
                .font(.caption).foregroundStyle(.secondary)
            }.padding(.trailing, 8)
        }
        .safeAreaInset(edge: .bottom, spacing: 0) { saveControls }
        .task(id: scanKey) { startScan() }
        .onChange(of: model.selection) { _, region in
            if region != nil { scope = "selection" } else if scope == "selection" { scope = "included" }
        }
        .onChange(of: regions) { _, values in
            if scope != "all" && scope != "included" && scope != "selection"
                && !values.contains(where: { $0.id == scope && $0.enabled && $0.mode == .include })
            {
                scope = "included"
            }
        }
        .onDisappear {
            model.cancel()
            model.selection = nil
            model.selectionError = nil
        }
    }

    private var saveControls: some View {
        VStack(alignment: .leading, spacing: 6) {
            Button(workspace.busy ? "Saving…" : "Save colour grade") {
                guard var settings = workspace.inspection?.project.settings else { return }
                settings.colourGrade = model.grade.savedValue
                workspace.save(settings: settings)
            }
            .buttonStyle(.borderedProminent)
            .disabled(!workspace.canEdit || !model.grade.isValid || saved)
            .accessibilityIdentifier("colourGrade.save")
            Label(saved ? "Saved" : "Unsaved grade", systemImage: saved ? "checkmark.circle" : "pencil.circle")
                .font(.caption).foregroundStyle(saved ? .green : .secondary)
        }.padding(.top, 10).frame(maxWidth: .infinity, alignment: .leading).background(.background)
    }

    private func startScan() {
        guard let path = workspace.displayedRun?.artifacts["cloud"] else {
            model.cancel()
            return
        }
        let folder = URL(fileURLWithPath: workspace.root).appendingPathComponent(path)
        model.scan(folder: folder, regions: histogramRegions, minimumSupport: minimumSupport)
    }

    private func control(_ title: String, value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        VStack(spacing: 3) {
            HStack {
                Text(title)
                Spacer()
                Text(value.wrappedValue.formatted(.number.precision(.fractionLength(2)))).monospacedDigit()
            }.font(.caption)
            Slider(value: value, in: range).accessibilityLabel(title)
        }
    }

    private func percentage(_ value: UInt64, _ count: UInt64) -> String {
        (Double(value) / Double(count)).formatted(.percent.precision(.fractionLength(1)))
    }
}
