import MapKit
import SwiftUI

struct NativePhotoMap: NSViewRepresentable {
    let cameras: [MapCamera]
    let identity: String
    let satellite: Bool
    let showAll: Bool
    @Binding var selected: String?

    func makeCoordinator() -> PhotoMapCoordinator { PhotoMapCoordinator(selection: { selected = $0 }) }

    func makeNSView(context: Context) -> ScrollablePhotoMap {
        let map = ScrollablePhotoMap()
        map.isZoomEnabled = true
        map.isScrollEnabled = true
        map.isRotateEnabled = false
        map.isPitchEnabled = false
        map.showsCompass = true
        map.showsScale = true
        map.delegate = context.coordinator
        return map
    }

    func updateNSView(_ map: ScrollablePhotoMap, context: Context) {
        map.mapType = satellite ? .satellite : .standard
        context.coordinator.update(map: map, cameras: cameras, identity: identity, selected: selected, showAll: showAll)
    }

    func sizeThatFits(_ proposal: ProposedViewSize, nsView: ScrollablePhotoMap, context: Context) -> CGSize? {
        CGSize(width: proposal.width ?? 600, height: proposal.height ?? 400)
    }
}
