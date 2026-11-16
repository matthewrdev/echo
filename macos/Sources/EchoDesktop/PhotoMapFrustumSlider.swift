import AppKit
import SwiftUI

struct PhotoMapFrustumSlider: NSViewRepresentable {
    @Binding var distance: Double

    func makeCoordinator() -> PhotoMapFrustumSliderCoordinator {
        PhotoMapFrustumSliderCoordinator(distance: $distance)
    }

    func makeNSView(context: Context) -> NSSlider {
        let slider = NSSlider(
            value: distance, minValue: 3, maxValue: 80, target: context.coordinator,
            action: #selector(PhotoMapFrustumSliderCoordinator.changeDistance(_:)))
        slider.isVertical = true
        slider.isContinuous = true
        slider.controlSize = .small
        slider.setAccessibilityLabel("Frustum length")
        return slider
    }

    func updateNSView(_ slider: NSSlider, context: Context) {
        context.coordinator.distance = $distance
        slider.doubleValue = distance
        slider.isEnabled = context.environment.isEnabled
        slider.setAccessibilityValueDescription("\(Int(distance.rounded())) metres")
    }
}
