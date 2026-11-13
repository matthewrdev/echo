import SwiftUI

struct StageSettingsEditor: View {
    let stage: PipelineStage
    @Binding var settings: ProcessingSettings
    let imageCount: Int

    var body: some View {
        Form {
            switch stage {
            case .features:
                Toggle("Detect line segments and junctions", isOn: $settings.detectLines)
                IntegerSettingSlider(title: "Maximum line segments", value: $settings.lineLimit, range: 1...5000)
                    .disabled(!settings.detectLines)
                IntegerSettingSlider(
                    title: "Minimum line length (analysis pixels)", value: $settings.lineMinimumLength, range: 8...1000
                )
                .disabled(!settings.detectLines)
                IntegerSettingSlider(title: "Analysis width (pixels)", value: $settings.featureWidth, range: 64...8192)
                IntegerSettingSlider(
                    title: "Maximum features per photo", value: $settings.featureLimit, range: 32...20000)
                Text(
                    "Corners and line junctions supply point correspondences for alignment. Line segments are detected and matched separately for inspection; the solver does not yet optimise 3D line constraints. This detects straight edges, not semantic shapes or objects. Larger feature limits increase matching work quadratically."
                ).font(.callout).foregroundStyle(.secondary)
            case .matching:
                Toggle("Compare every image pair", isOn: $settings.matchAllPairs)
                IntegerSettingSlider(title: "Following images to compare", value: $settings.pairWindow, range: 1...100)
                    .disabled(
                        settings.matchAllPairs)
                IntegerSettingSlider(
                    title: "Nearest GPS neighbours", value: $settings.gpsPairNeighbours, range: 0...100
                )
                .disabled(settings.matchAllPairs)
                Text(
                    "All pairs means \((imageCount * max(0, imageCount - 1) / 2).formatted()) comparisons for this project. Overlap and manual tie hints always add their pairs."
                ).font(.caption).foregroundStyle(.secondary)
                DecimalSettingSlider(
                    title: "Uniqueness ratio", value: $settings.matchRatioThreshold, range: 0.01...1, step: 0.01)
                DecimalSettingSlider(
                    title: "Maximum squared descriptor distance", value: $settings.matchDistanceThreshold,
                    range: 0.01...4, step: 0.01)
                Text(
                    "Higher thresholds accept more candidates, including more false matches. Matches must still be mutual nearest neighbours. These thresholds use squared distances between normalized descriptors. Alignment checks geometry afterwards."
                ).font(.callout).foregroundStyle(.secondary)
            case .alignment:
                IntegerSettingSlider(
                    title: "Bundle-adjustment iterations", value: $settings.bundleIterations, range: 1...500)
                Text(
                    "More iterations allow further camera and point refinement. The solve may stop early after convergence; more iterations cannot repair incorrect correspondences or missing overlap."
                ).foregroundStyle(.secondary)
            case .depth:
                IntegerSettingSlider(title: "Concurrent photos", value: $settings.depthConcurrency, range: 1...16)
                IntegerSettingSlider(
                    title: "Total CPU workers (0 = automatic)", value: $settings.depthWorkerThreads, range: 0...256)
                IntegerSettingSlider(
                    title: "Scheduling memory budget (MB)", value: $settings.depthMemoryBudgetMb, range: 256...32768)
                Text(
                    "Photos share the CPU budget. Concurrency is reduced when estimated image memory exceeds the budget; this is not a hard process-memory limit. Execution settings do not invalidate saved maps."
                )
                .font(.caption).foregroundStyle(.secondary)
                IntegerSettingSlider(title: "Depth-map width (pixels)", value: $settings.depthWidth, range: 32...8192)
                IntegerSettingSlider(
                    title: "Initial depth search samples", value: $settings.depthPlanes, range: 8...2048)
                IntegerSettingSlider(
                    title: "Refinement samples per level", value: $settings.depthRefinementSamples, range: 3...128)
                IntegerSettingSlider(title: "Stereo neighbours", value: $settings.depthNeighbours, range: 1...32)
                DecimalSettingSlider(
                    title: "Nearest distance (metres)", value: $settings.depthNear, range: 0.1...1000, step: 0.1)
                DecimalSettingSlider(
                    title: "Farthest distance (metres)", value: $settings.depthFar, range: 1...2000, step: 1)
                Text(
                    "1024 pixels is a preview resolution for 4K photos. Use 2048 or the source width to retain smaller surface details; images are never upscaled. Initial samples search the depth range, and refinement samples refine each higher-resolution level. Tighten the range around the photographed surface to spend those samples usefully. Distances are metric only when the reconstruction has a metric frame."
                ).foregroundStyle(.secondary)
            case .cloud:
                IntegerSettingSlider(
                    title: "Neighbouring views to check", value: $settings.cloudNeighbours, range: 1...64)
                IntegerSettingSlider(
                    title: "Minimum supporting neighbours", value: $settings.cloudMinimumSupport,
                    range: 1...max(1, settings.cloudNeighbours))
                DecimalSettingSlider(
                    title: "Relative depth tolerance", value: $settings.cloudDepthTolerance, range: 0.001...0.1,
                    step: 0.001)
                Text(
                    "0.01 means 1% depth agreement; a two-pixel projected footprint is the minimum tolerance. More required supporting views remove isolated noise but can remove thin or sparsely photographed surfaces. Source depth resolution is set in Depth maps. Viewer support buttons only filter the preview."
                ).foregroundStyle(.secondary)
            case .fusion:
                DecimalSettingSlider(
                    title: "Detail budget multiplier", value: $settings.fusionDetailMultiplier, range: 0.25...16,
                    step: 0.25)
                Text(
                    "Higher values use a finer initial voxel grid and allow more occupied cells before coarsening. This costs memory and fusion time. The mesh triangle budget still limits the final surface; this setting does not add observed detail."
                ).foregroundStyle(.secondary)
            case .mesh:
                IntegerSettingSlider(title: "Triangle budget", value: $settings.faceLimit, range: 1...10_000_000)
                Text(
                    "Surface extraction and simplification aim below this ceiling. Increasing it can preserve more recovered detail, but cannot fill unobserved surfaces. It also informs the automatic fusion grid."
                ).foregroundStyle(.secondary)
            case .texture:
                Picker("Atlas size", selection: $settings.textureSize) {
                    ForEach([256, 512, 1024, 2048, 4096, 8192], id: \.self) { Text("\($0)").tag($0) }
                }.pickerStyle(.segmented)
                IntegerSettingSlider(
                    title: "Maximum triangles per UV chart", value: $settings.textureChartFaces, range: 16...8192)
                DecimalSettingSlider(
                    title: "Maximum chart normal angle (degrees)", value: $settings.textureChartAngle, range: 10...80,
                    step: 1)
                IntegerSettingSlider(title: "Island padding (pixels)", value: $settings.textureGutter, range: 1...16)
                IntegerSettingSlider(title: "JPEG quality", value: $settings.textureJpegQuality, range: 25...100)
                Text(
                    "The atlas is square. Larger charts and a wider normal angle can reduce seams, at the cost of more UV distortion. More pixels preserve photographic detail across the whole surface. Higher JPEG quality reduces compression loss and increases file size."
                ).foregroundStyle(.secondary)
            case .export:
                Toggle("Validate the GLB structure after writing", isOn: $settings.exportValidate)
                Text(
                    "Export packages the saved mesh and texture into GLB and writes georeference data. It does not reconstruct additional detail. Mesh and Texture settings control output detail; validation adds a read-back check of the saved file."
                ).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onChange(of: settings.cloudNeighbours) { _, count in
            settings.cloudMinimumSupport = min(settings.cloudMinimumSupport, count)
        }
    }
}
