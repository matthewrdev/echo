import MapKit
import SwiftUI

struct NativePhotoMap: NSViewRepresentable {
    let cameras: [MapCamera]
    let identity: String
    let satellite: Bool
    @Binding var selected: String?
    var boxed: Set<String> = []
    var boxSelection: ((Set<String>) -> Void)? = nil
    var singleSelection: ((String?) -> Void)? = nil
    var modelOverlay: ModelMapOverlay? = nil
    var modelOpacity = 0.8
    var testLocation: GeoPosition? = nil
    var testAccuracy = 0.0
    var pickLocation: ((CLLocationCoordinate2D) -> Void)? = nil
    var gpsPolygons: [GpsPolygon] = []
    var gpsDraft: GpsPolygon? = nil

    func makeCoordinator() -> PhotoMapCoordinator {
        PhotoMapCoordinator(selection: singleSelection ?? { selected = $0 })
    }

    func makeNSView(context: Context) -> ScrollablePhotoMap {
        let map = ScrollablePhotoMap()
        map.isZoomEnabled = true
        map.isScrollEnabled = true
        map.isRotateEnabled = false
        map.isPitchEnabled = false
        map.showsCompass = true
        map.showsScale = true
        map.delegate = context.coordinator
        let gesture = NSClickGestureRecognizer(
            target: context.coordinator, action: #selector(PhotoMapCoordinator.pickTestLocation(_:)))
        gesture.isEnabled = pickLocation != nil
        context.coordinator.locationGesture = gesture
        map.addGestureRecognizer(gesture)
        return map
    }

    func updateNSView(_ map: ScrollablePhotoMap, context: Context) {
        map.mapType = satellite ? .satellite : .standard
        context.coordinator.pickLocation = pickLocation
        context.coordinator.locationGesture?.isEnabled = pickLocation != nil
        context.coordinator.selection = singleSelection ?? { selected = $0 }
        map.boxSelection = boxSelection.map { select in
            { [weak map] rect in
                guard let map else { return }
                let ids = cameras.compactMap { camera -> String? in
                    let coordinate = CLLocationCoordinate2D(
                        latitude: camera.position.latitude, longitude: camera.position.longitude)
                    return rect.contains(map.convert(coordinate, toPointTo: map)) ? camera.id : nil
                }
                select(Set(ids))
            }
        }
        context.coordinator.updateModel(map: map, model: modelOverlay, opacity: modelOpacity)
        context.coordinator.updateGpsPolygons(map: map, polygons: gpsPolygons, draft: gpsDraft)
        context.coordinator.update(map: map, cameras: cameras, identity: identity, selected: selected, boxed: boxed)
        context.coordinator.updateProbe(map: map, position: testLocation, accuracy: testAccuracy)
    }

    func sizeThatFits(_ proposal: ProposedViewSize, nsView: ScrollablePhotoMap, context: Context) -> CGSize? {
        CGSize(width: proposal.width ?? 600, height: proposal.height ?? 400)
    }
}
