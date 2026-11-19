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
                        set: { settings.fusionVoxelSize = FusionVoxelPreset.clampedMetres(centimetres: $0) }),
                    format: .number.grouping(.never)
                ).textFieldStyle(.roundedBorder).frame(width: 120)
            }
            Text("Minimum: 2.5 cm").font(.caption).foregroundStyle(.secondary)
        }
        if FusionVoxelPreset.needsDetailWarning(metres: settings.fusionVoxelSize) {
            Label(
                "Fine voxels can substantially increase fusion time and disk usage. Use 10 cm or 50 cm for a faster first pass.",
                systemImage: "exclamationmark.triangle"
            ).font(.caption).foregroundStyle(.orange)
        }
        if settings.fusionVoxelSize == 0 {
            Text(
                "Auto balances image detail against the fusion voxel budget, with a minimum of 2.5 cm. The selected spacing is reported before integration."
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
            "Automatic allows 87.5% of the machine’s RAM, retaining system headroom. Memory grows as needed."
        )
        .font(.caption).foregroundStyle(.secondary)
    }
}
