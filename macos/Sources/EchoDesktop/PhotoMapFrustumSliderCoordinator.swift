import AppKit
import SwiftUI

@MainActor
final class PhotoMapFrustumSliderCoordinator: NSObject {
    var distance: Binding<Double>

    init(distance: Binding<Double>) {
        self.distance = distance
    }

    @objc func changeDistance(_ slider: NSSlider) {
        distance.wrappedValue = slider.doubleValue
    }
}
