import EchoDrone
import MapKit
import SwiftUI

struct DroneImportMap: NSViewRepresentable {
    let photos: [DroneImportPhoto]
    let appearances: [URL: DroneMapAppearance]
    @Binding var selection: Set<URL>

    func makeCoordinator() -> DroneImportMapCoordinator { DroneImportMapCoordinator() }

    func makeNSView(context: Context) -> ScrollablePhotoMap {
        let map = ScrollablePhotoMap()
        map.mapType = .satellite
        map.showsScale = true
        map.isRotateEnabled = false
        map.isPitchEnabled = false
        map.delegate = context.coordinator
        return map
    }

    func updateNSView(_ map: ScrollablePhotoMap, context: Context) {
        context.coordinator.select = { selection = $0 }
        context.coordinator.update(map: map, photos: photos, appearances: appearances)
    }
}
