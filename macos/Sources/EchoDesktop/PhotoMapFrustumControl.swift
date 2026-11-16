import SwiftUI

struct PhotoMapFrustumControl: View {
    @Binding var distance: Double

    var body: some View {
        VStack(spacing: 6) {
            Image(systemName: "pyramid")
                .font(.system(size: 14, weight: .medium))
                .frame(width: 28, height: 22)
                .accessibilityHidden(true)
            PhotoMapFrustumSlider(distance: $distance)
                .frame(width: 24, height: 92)
            Text("\(Int(distance.rounded())) m")
                .font(.system(size: 10)).monospacedDigit()
                .accessibilityHidden(true)
        }
        .frame(width: 28).padding(6)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 8))
        .help(
            "Frustum length: adjust the camera viewing cones. This is a display scale, not measured ground coverage."
        )
    }
}
