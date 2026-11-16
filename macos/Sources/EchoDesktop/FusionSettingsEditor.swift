import SwiftUI

struct FusionSettingsEditor: View {
    @Binding var settings: ProcessingSettings

    var body: some View {
        Picker(
            "Target voxel size",
            selection: Binding(
                get: { FusionVoxelPreset(metres: settings.fusionVoxelSize) },
                set: { settings.fusionVoxelSize = $0.metres ?? 0.25 })
        ) {
            ForEach(FusionVoxelPreset.allCases) { preset in
                Text(preset.title).tag(preset)
            }
        }
        if settings.fusionVoxelSize == 0 {
            DecimalSettingSlider(
                title: "Fusion detail multiplier", value: $settings.fusionDetailMultiplier, range: 0.25...16,
                step: 0.25)
        } else {
            HStack {
                Text("Target voxel size (cm)")
                TextField(
                    "Target voxel size (cm)",
                    value: Binding(
                        get: { settings.fusionVoxelSize * 100 },
                        set: { settings.fusionVoxelSize = $0 / 100 }),
                    format: .number.grouping(.never)
                ).textFieldStyle(.roundedBorder).frame(width: 120)
            }
        }
        if settings.fusionVoxelSize == 0 {
            Text(
                "Auto balances image detail against the fusion voxel budget. The selected spacing is reported before integration."
            )
            .font(.caption).foregroundStyle(.secondary)
        } else {
            Toggle("Allow coarser voxels if the budget is exceeded", isOn: $settings.fusionAllowCoarserResolution)
            Text(
                settings.fusionAllowCoarserResolution
                    ? "Fusion may use larger voxels to fit the budget. Requested and actual sizes are reported."
                    : "Uses the exact target size. Smaller voxels capture more detail and require more processing and storage."
            ).font(.caption).foregroundStyle(.secondary)
        }
        MemoryBudgetPicker(title: "Fusion memory", megabytes: $settings.fusionMemoryBudgetMb, exponents: 6...18)
        Text(
            "Automatic uses 25% of available memory. Depth maps, the point cloud and other applications also need memory."
        )
        .font(.caption).foregroundStyle(.secondary)
    }
}
