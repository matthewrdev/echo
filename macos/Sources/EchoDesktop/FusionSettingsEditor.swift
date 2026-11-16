import SwiftUI

struct FusionSettingsEditor: View {
    @Binding var settings: ProcessingSettings

    var body: some View {
        MemoryBudgetPicker(title: "Fusion memory", megabytes: $settings.fusionMemoryBudgetMb, exponents: 6...18)
        Text(
            "Automatic uses 25% of available memory. A custom budget can exceed 32 GiB. This estimates fusion working memory; depth maps, the point cloud and other applications need additional memory."
        ).font(.caption).foregroundStyle(.secondary)
        Toggle(
            "Automatic voxel size",
            isOn: Binding(
                get: { settings.fusionVoxelSize == 0 },
                set: { settings.fusionVoxelSize = $0 ? 0 : 0.1 }))
        if settings.fusionVoxelSize == 0 {
            DecimalSettingSlider(
                title: "Fusion detail multiplier", value: $settings.fusionDetailMultiplier, range: 0.25...16,
                step: 0.25)
        } else {
            HStack {
                Text("Voxel size (cm)")
                TextField(
                    "Voxel size (cm)",
                    value: Binding(
                        get: { settings.fusionVoxelSize * 100 },
                        set: { settings.fusionVoxelSize = $0 / 100 }),
                    format: .number.grouping(.never)
                ).textFieldStyle(.roundedBorder).frame(width: 120)
            }
        }
        Toggle("Allow coarser voxels if the budget is exceeded", isOn: $settings.fusionAllowCoarserResolution)
        Text(
            settings.fusionAllowCoarserResolution
                ? "Fusion may reduce detail to fit the budget. Every reduction is logged with the requested and actual voxel sizes."
                : "Preserves the requested resolution. If it cannot fit, processing stops with an explanation so you can raise the budget or change voxel size."
        ).font(.caption).foregroundStyle(.secondary)
    }
}
