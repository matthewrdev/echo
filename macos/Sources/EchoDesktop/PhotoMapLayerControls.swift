import SwiftUI

struct PhotoMapLayerControls: View {
    @Binding var layer: PhotoMapLayer

    var body: some View {
        VStack(spacing: 6) {
            ForEach(PhotoMapLayer.allCases) { option in
                Button(option.rawValue, systemImage: option.symbol) { layer = option }
                    .labelStyle(ViewerIconLabelStyle(selected: layer == option))
                    .accessibilityAddTraits(layer == option ? .isSelected : [])
                    .help(option.help)
            }
        }
        .buttonStyle(.plain).padding(6)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
        .accessibilityElement(children: .contain).accessibilityLabel("Map style")
    }
}
