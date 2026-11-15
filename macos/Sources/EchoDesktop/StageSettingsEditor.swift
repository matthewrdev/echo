import SwiftUI

struct StageSettingsEditor: View {
    let stage: PipelineStage
    @Binding var settings: ProcessingSettings
    let imageCount: Int

    var body: some View {
        Form {
            switch stage {
            case .features:
                Toggle("Scale and rotation invariance", isOn: $settings.scaleInvariantFeatures)
                Text(
                    "Connects photos taken at different distances and angles. For cross-pass recovery, try at least 2000 px and 4000 features; this budget also limits the second alignment pass."
                )
                .font(.caption).foregroundStyle(.secondary)
                Toggle("Detect line segments and junctions", isOn: $settings.detectLines)
                IntegerSettingSlider(title: "Maximum line segments", value: $settings.lineLimit, range: 1...5000)
                    .disabled(!settings.detectLines)
                IntegerSettingSlider(
                    title: "Minimum line length (px)", value: $settings.lineMinimumLength, range: 8...1000
                )
                .disabled(!settings.detectLines)
                DecimalSettingSlider(
                    title: "Minimum edge contrast", value: $settings.lineGradientThreshold,
                    range: 0.001...0.2, step: 0.001
                ).disabled(!settings.detectLines)
                Toggle("Recover straight sections of curved edges", isOn: $settings.lineSplitRegions)
                    .disabled(!settings.detectLines)
                Text(
                    "Lower contrast detects fainter edges and more noise. Length is measured at the analysis resolution."
                )
                .font(.caption).foregroundStyle(.secondary)
                IntegerSettingSlider(title: "Analysis width (px)", value: $settings.featureWidth, range: 64...8192)
                IntegerSettingSlider(
                    title: "Maximum features per photo", value: $settings.featureLimit, range: 32...20000)
                Text(
                    "Corners and line junctions support alignment; line segments are for inspection only. Larger feature limits increase matching work quadratically."
                ).font(.callout).foregroundStyle(.secondary)
            case .matching:
                Toggle("Compare every image pair", isOn: $settings.matchAllPairs)
                IntegerSettingSlider(
                    title: "Overlapping views per photo", value: $settings.frustumPairNeighbours, range: 0...100
                )
                .disabled(settings.matchAllPairs)
                DecimalSettingSlider(
                    title: "View search distance (m)", value: $settings.pairSearchDistance,
                    range: 1...5000, step: 1
                ).disabled(settings.matchAllPairs)
                Text(
                    "Uses GPS, elevation, direction and field of view, reserving half the shortlist for different elevations or tilts. Missing metadata falls back to GPS and sequence neighbours."
                )
                .font(.caption).foregroundStyle(.secondary)
                IntegerSettingSlider(title: "Following images to compare", value: $settings.pairWindow, range: 1...100)
                    .disabled(
                        settings.matchAllPairs)
                IntegerSettingSlider(
                    title: "Nearest GPS neighbours", value: $settings.gpsPairNeighbours, range: 0...100
                )
                .disabled(settings.matchAllPairs)
                Text(
                    "All pairs: \((imageCount * max(0, imageCount - 1) / 2).formatted()) comparisons. Overlap and tie hints always add their pairs."
                ).font(.caption).foregroundStyle(.secondary)
                DecimalSettingSlider(
                    title: "Uniqueness ratio", value: $settings.matchRatioThreshold, range: 0.01...1, step: 0.01)
                DecimalSettingSlider(
                    title: "Maximum squared descriptor distance", value: $settings.matchDistanceThreshold,
                    range: 0.01...4, step: 0.01)
                Text(
                    "Higher thresholds accept more matches, including false ones. Both use squared distances between normalized descriptors. Matches must be mutual nearest neighbours; alignment then checks geometry."
                ).font(.callout).foregroundStyle(.secondary)
            case .alignment:
                Toggle("Guided second pass", isOn: $settings.guidedAlignmentPass)
                Text(
                    "Finds and verifies additional matches using image geometry, then refines cameras and points. Each pair is checkpointed."
                )
                .font(.caption).foregroundStyle(.secondary)
                IntegerSettingSlider(
                    title: "Bundle-adjustment iterations", value: $settings.bundleIterations, range: 1...500)
                Text(
                    "More iterations allow further refinement; the solve stops early on convergence. Extra iterations cannot repair incorrect matches or missing overlap."
                ).foregroundStyle(.secondary)
            case .depth:
                Picker("Compute backend", selection: $settings.depthBackend) {
                    Text("Automatic (prefer Metal)").tag("auto")
                    Text("Metal").tag("metal")
                    Text("CPU").tag("cpu")
                    Text("Metal with CPU validation").tag("metal-validated")
                }
                if settings.depthBackend == "auto" || settings.depthBackend == "metal" {
                    Text(
                        "Uses the GPU for faster depth processing, with CPU fallback if Metal is unavailable. Small numerical differences from CPU processing are expected."
                    ).font(.caption).foregroundStyle(.secondary)
                } else if settings.depthBackend == "metal-validated" {
                    Text(
                        "Checks every Metal search against the CPU and corrects any difference. This validation mode adds work; it is not a speed mode. Saved maps remain reusable; reset Depth to process them with Metal."
                    ).font(.caption).foregroundStyle(.secondary)
                }
                IntegerSettingSlider(title: "Concurrent photos", value: $settings.depthConcurrency, range: 1...16)
                IntegerSettingSlider(
                    title: "Total CPU workers (0 = automatic)", value: $settings.depthWorkerThreads, range: 0...256)
                Toggle(
                    "Automatic memory budget",
                    isOn: Binding(
                        get: { settings.depthMemoryBudgetMb == 0 },
                        set: { settings.depthMemoryBudgetMb = $0 ? 0 : 4096 }))
                if settings.depthMemoryBudgetMb != 0 {
                    IntegerSettingSlider(
                        title: "Memory budget (MB)", value: $settings.depthMemoryBudgetMb,
                        range: 256...32768)
                }
                Text(
                    "Automatic uses 25% of available memory, up to 32 GB, calculated at processing time and shown in the activity log."
                )
                .font(.caption).foregroundStyle(.secondary)
                Text(
                    "Photos share CPU workers. Concurrency falls when estimated memory exceeds the budget; this is not a hard memory limit. These execution settings preserve saved maps."
                )
                .font(.caption).foregroundStyle(.secondary)
                IntegerSettingSlider(title: "Map width (px)", value: $settings.depthWidth, range: 32...8192)
                IntegerSettingSlider(
                    title: "Initial search samples", value: $settings.depthPlanes, range: 8...2048)
                IntegerSettingSlider(
                    title: "Refinement samples per level", value: $settings.depthRefinementSamples, range: 3...128)
                IntegerSettingSlider(title: "Stereo neighbours", value: $settings.depthNeighbours, range: 1...32)
                Toggle("Recover missing cliff detail", isOn: $settings.recoverCliffDepth)
                Text(
                    "Searches small rock gaps using finer source photographs and additional views. Requires two independent depth confirmations; existing samples stay fixed. The first semantic pass downloads a 228 MB model, then runs locally."
                )
                .font(.caption).foregroundStyle(.secondary)
                Toggle("Automatic depth range", isOn: $settings.depthRangeAutomatic)
                if settings.depthRangeAutomatic {
                    Text(
                        "Uses aligned scene points with a margin per photo, falling back to camera bounds. The range appears in the activity log."
                    )
                    .font(.caption).foregroundStyle(.secondary)
                } else {
                    TextField("Nearest distance (m)", value: $settings.depthNear, format: .number.grouping(.never))
                        .textFieldStyle(.roundedBorder)
                    TextField("Farthest distance (m)", value: $settings.depthFar, format: .number.grouping(.never))
                        .textFieldStyle(.roundedBorder)
                }
                Text(
                    "For 4K photos, use 2048 px or source width for finer detail; 1024 px suits previews. Images are never upscaled. Refinement samples apply at each higher-resolution level. Distances require a metric reconstruction frame."
                ).foregroundStyle(.secondary)
            case .cloud:
                IntegerSettingSlider(
                    title: "CPU workers (0 = automatic)", value: $settings.cloudWorkerThreads, range: 0...256)
                IntegerSettingSlider(
                    title: "Rows per batch", value: $settings.cloudBatchRows, range: 1...128)
                Text(
                    "Rows run in parallel and merge in image order. Worker buffers use up to 64 MB, plus memory for depth maps and the cloud. These execution settings preserve saved results."
                ).font(.caption).foregroundStyle(.secondary)
                IntegerSettingSlider(
                    title: "Neighbours to check", value: $settings.cloudNeighbours, range: 1...64)
                IntegerSettingSlider(
                    title: "Minimum supporting neighbours", value: $settings.cloudMinimumSupport,
                    range: 1...max(1, settings.cloudNeighbours))
                DecimalSettingSlider(
                    title: "Relative depth tolerance", value: $settings.cloudDepthTolerance, range: 0.001...0.1,
                    step: 0.001)
                Text(
                    "0.01 means 1% depth agreement, with a two-pixel projected footprint as the minimum tolerance. Source resolution comes from Depth maps. Viewer support filters affect only the preview."
                ).foregroundStyle(.secondary)
                DecimalSettingSlider(
                    title: "Round-trip tolerance (depth-map px)",
                    value: $settings.cloudReprojectionTolerancePixels,
                    range: 0.1...10, step: 0.1)
                DecimalSettingSlider(
                    title: "Minimum viewing-ray angle (°)", value: $settings.cloudMinimumParallaxDegrees,
                    range: 0...10, step: 0.1)
                Text(
                    "Support must agree in depth and project back to the source pixel. Require two or more neighbours to reduce floating noise, at the risk of losing thin or sparsely captured surfaces. These checks cannot repair camera alignment."
                )
                .font(.caption).foregroundStyle(.secondary)
            case .fusion:
                FusionSettingsEditor(settings: $settings)
                Picker("Trim islands before meshing", selection: $settings.fusionIslandMode) {
                    Text("Off").tag(IslandTrimMode.off)
                    Text("Remove small regions").tag(IslandTrimMode.small)
                    Text("Keep main region only").tag(IslandTrimMode.main)
                }
                if settings.fusionIslandMode == .small {
                    DecimalSettingSlider(
                        title: "Minimum region size (% of main region)",
                        value: $settings.fusionIslandMinimumSizePercent,
                        range: 0...100, step: 0.01)
                }
                Text(
                    "Removes disconnected observed volume before mesh extraction and hole closing. Region size uses the number of fusion samples. Main region only can also remove real detached objects. Nearby fragments joined by the fusion band may remain; Mesh island trimming can clean up later."
                ).font(.caption).foregroundStyle(.secondary)
            case .mesh:
                Toggle("Optimise vegetation", isOn: $settings.optimiseVegetation)
                if settings.optimiseVegetation {
                    DecimalSettingSlider(
                        title: "Vegetation mesh tolerance multiplier",
                        value: $settings.vegetationMeshToleranceMultiplier, range: 1...4, step: 0.25)
                    Text(
                        "Simplifies vegetation confirmed by multiple views. Rock, uncertain regions and vegetation boundaries keep their detail. Also enables a separate vegetation texture budget. The first pass downloads a 228 MB model; photographs stay local."
                    )
                    .font(.caption).foregroundStyle(.secondary)
                }
                IntegerSettingSlider(title: "Triangle budget", value: $settings.faceLimit, range: 1...10_000_000)
                Text(
                    "Reduces the reconstructed surface while preserving shape and boundaries. Flat areas use fewer triangles; complex areas retain more. Changing this budget reuses fusion. Topology may prevent reaching very low budgets."
                ).foregroundStyle(.secondary)
                Picker("Island trimming", selection: $settings.meshIslandMode) {
                    Text("Off").tag(IslandTrimMode.off)
                    Text("Remove small islands").tag(IslandTrimMode.small)
                    Text("Keep main surface only").tag(IslandTrimMode.main)
                }
                if settings.meshIslandMode == .small {
                    DecimalSettingSlider(
                        title: "Minimum island area (% of main surface)",
                        value: $settings.meshIslandMinimumAreaPercent, range: 0...100, step: 0.01)
                }
                Text(
                    "Runs after hole closing. Small islands are measured by surface area, independent of triangle density. Main surface only removes every disconnected patch, including detached rocks. Connected protrusions are retained. Changing this setting rebuilds Mesh, Texture and Export."
                ).font(.caption).foregroundStyle(.secondary)
            case .texture:
                if settings.optimiseVegetation {
                    DecimalSettingSlider(
                        title: "Vegetation texture scale", value: $settings.vegetationTextureScale, range: 0.25...1,
                        step: 0.05)
                    Text(
                        "Linear texel density relative to rock. 0.65 uses about 42% as many texels per surface area, leaving more atlas space for rock. Uncertain faces retain full quality. Layouts that waste space on extra padding are skipped."
                    )
                    .font(.caption).foregroundStyle(.secondary)
                }
                Picker("Atlas size (px)", selection: $settings.textureSize) {
                    ForEach([256, 512, 1024, 2048, 4096, 8192], id: \.self) { Text("\($0)").tag($0) }
                }.pickerStyle(.segmented)
                IntegerSettingSlider(
                    title: "Maximum triangles per UV chart", value: $settings.textureChartFaces, range: 16...8192)
                DecimalSettingSlider(
                    title: "Maximum chart normal angle (°)", value: $settings.textureChartAngle, range: 10...80,
                    step: 1)
                IntegerSettingSlider(title: "Island padding (px)", value: $settings.textureGutter, range: 1...16)
                IntegerSettingSlider(title: "JPEG quality", value: $settings.textureJpegQuality, range: 25...100)
                Toggle("Reduce sky colour bleed", isOn: $settings.textureSuppressSky)
                if settings.textureSuppressSky {
                    IntegerSettingSlider(
                        title: "Skyline safety margin (source px)", value: $settings.textureSkyMarginPixels,
                        range: 1...64)
                }
                Text(
                    "Detects smooth sky-coloured regions touching the top of a photo, then samples slightly inside the foreground near that edge. Start with 8 px. Larger margins can soften edge detail. This is a conservative heuristic; pale objects can be mistaken for sky and large projection errors need a geometry fix. Only Texture and Export are rebuilt."
                ).font(.caption).foregroundStyle(.secondary)
                Text(
                    "The atlas is square. Larger charts and wider angles reduce seams but can increase distortion. More pixels preserve detail; higher JPEG quality reduces compression loss and increases file size."
                ).foregroundStyle(.secondary)
            case .export:
                Toggle("Validate saved GLB", isOn: $settings.exportValidate)
                Text(
                    "Packages the mesh, texture and georeference data. Mesh and Texture settings control detail. Validation reads back the saved file to check its structure."
                ).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .onChange(of: settings.cloudNeighbours) { _, count in
            settings.cloudMinimumSupport = min(settings.cloudMinimumSupport, count)
        }
    }
}
