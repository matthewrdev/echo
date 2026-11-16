import SwiftUI

struct FeatureViewer: View {
    let data: FeaturePresentation
    let openMatch: (FeatureMatchLink) -> Void
    @StateObject private var controller = CanvasViewportController()
    @State private var selected: Int?
    @AppStorage("features.overlayOpacity") private var overlayOpacity = 1.0
    @AppStorage("features.showsPoints") private var showsPoints = true
    @AppStorage("features.showsLines") private var showsLines = true
    @AppStorage("features.showsJunctions") private var showsJunctions = true

    private var overlay: FeatureOverlayOptions {
        FeatureOverlayOptions(
            opacity: overlayOpacity, showsPoints: showsPoints, showsLines: showsLines, showsJunctions: showsJunctions)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(
                "\(data.photo.name) · Yellow: points · Cyan: lines · Squares: junctions · Mint: matched · Orange: selected"
            )
            .font(.caption).foregroundStyle(.secondary)
            if !data.hasLineData {
                Text(
                    "This saved artifact predates line detection. Enable lines in Stage settings, then process Features."
                )
                .font(.caption).foregroundStyle(.orange)
            }
            CanvasControls(controller: controller)
            FeatureOverlayControls(
                opacity: $overlayOpacity, showsPoints: $showsPoints, showsLines: $showsLines,
                showsJunctions: $showsJunctions)
            HSplitView {
                CanvasViewport(
                    renderer: FeatureCanvasRenderer(
                        data: data, selected: selected, overlay: overlay, selection: { selected = $0 }),
                    controller: controller)
                if selected != nil {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 12) {
                            HStack {
                                Spacer()
                                Button("Close feature inspector", systemImage: "xmark") { selected = nil }
                                    .labelStyle(ViewerIconLabelStyle()).buttonStyle(.plain).help(
                                        "Close feature inspector")
                            }
                            if let selected, data.features.indices.contains(selected) {
                                let feature = data.features[selected]
                                let matches = data.matches.filter { $0.featureIndex == selected }
                                Text("Feature \(selected + 1)").font(.headline)
                                Text("Full-resolution pixel: \(feature.x.formatted()), \(feature.y.formatted())").font(
                                    .caption)
                                Text(
                                    "\(feature.kind == "junction" ? "Line junction" : feature.kind == "scale-space" ? "Scale-space point" : "Harris corner") · \(feature.descriptor.count)-value normalized descriptor"
                                )
                                .font(.caption).foregroundStyle(.secondary)
                                MatchPatchView(
                                    title: "Local image patch", image: data.image, photo: data.photo,
                                    pixel: [feature.x, feature.y], overlay: .point(kind: feature.kind ?? "corner")
                                )
                                .frame(height: 200)
                                Text("\(matches.count) candidate correspondences").font(.subheadline.bold())
                                if !data.matchingAvailable {
                                    Text("Run image matching to find correspondences for this feature.").font(.caption)
                                } else if matches.isEmpty {
                                    Text("No saved pair accepted a match for this keypoint.").font(.caption)
                                }
                                ForEach(matches) { match in
                                    VStack(alignment: .leading, spacing: 5) {
                                        Text(match.otherPhotoName).font(.caption.weight(.semibold)).lineLimit(2)
                                        Text(
                                            "Other pixel: \(match.otherPixel[0].formatted()), \(match.otherPixel[1].formatted())"
                                        )
                                        .font(.caption2).foregroundStyle(.secondary)
                                        if let score = match.score {
                                            Text(
                                                "Descriptor distance: \(score.formatted(.number.precision(.fractionLength(4))))"
                                            ).font(.caption2)
                                        }
                                        Button("Compare this match →") { openMatch(match) }
                                    }.padding(.vertical, 5)
                                    Divider()
                                }
                                DisclosureGroup("Descriptor values") {
                                    Text(
                                        feature.descriptor.map { $0.formatted(.number.precision(.fractionLength(3))) }
                                            .joined(separator: ", ")
                                    )
                                    .font(.system(.caption2, design: .monospaced)).textSelection(.enabled)
                                }
                            } else if let selected, data.lines.indices.contains(selected - data.features.count) {
                                LineFeatureInspector(
                                    data: data, index: selected - data.features.count, openMatch: openMatch)
                            } else {
                                Text("Click a point or line").font(.headline)
                                Text(
                                    "Inspect its pixel coordinates, local descriptor, and candidate matches in other photographs."
                                )
                                .font(.callout).foregroundStyle(.secondary)
                            }
                        }.padding(12).frame(maxWidth: .infinity, alignment: .leading)
                    }.frame(minWidth: 260, idealWidth: 290, maxWidth: 350)
                }
            }
        }
        .onChange(of: data.photo.id) { _, _ in selected = nil }
        .onChange(of: overlay) { _, options in
            if let selected, !options.shows(index: selected, in: data) { self.selected = nil }
        }
    }
}
