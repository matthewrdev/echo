import EchoDrone
import MapKit

final class DroneImportMapCoordinator: NSObject, MKMapViewDelegate {
    var select: (Set<URL>) -> Void = { _ in }
    private var annotations: [URL: DroneMapPoint] = [:]
    private var appearances: [URL: DroneMapAppearance] = [:]
    private var updating = false

    func update(map: ScrollablePhotoMap, photos: [DroneImportPhoto], appearances: [URL: DroneMapAppearance]) {
        let located = photos.filter { $0.coordinate != nil }
        let ids = Set(located.map(\.id))
        updating = true
        defer { updating = false }
        if Set(annotations.keys) != ids {
            let shouldFit = annotations.isEmpty
            map.removeAnnotations(Array(annotations.values))
            annotations.removeAll(keepingCapacity: true)
            for photo in located {
                if let coordinate = photo.coordinate {
                    annotations[photo.id] = DroneMapPoint(photo: photo, coordinate: coordinate)
                }
            }
            self.appearances = appearances
            map.addAnnotations(Array(annotations.values))
            if shouldFit { fit(map: map) }
        } else {
            let previous = self.appearances
            self.appearances = appearances
            for (id, point) in annotations where previous[id] != appearances[id] {
                if let view = map.view(for: point) { style(view, id: id) }
            }
        }

        for point in map.selectedAnnotations {
            if let point = point as? DroneMapPoint, appearances[point.id]?.selected != true {
                map.deselectAnnotation(point, animated: false)
            }
        }
        map.boxSelection = { [weak self, weak map] rect in
            guard let self, let map else { return }
            let boxed = annotations.values.filter { rect.contains(map.convert($0.coordinate, toPointTo: map)) }
            select(Set(boxed.map(\.id)))
        }
    }

    private func fit(map: MKMapView) {
        guard let first = annotations.values.first else { return }
        let rect = annotations.values.reduce(MKMapRect.null) { extent, annotation in
            let point = MKMapPoint(annotation.coordinate)
            return extent.union(MKMapRect(x: point.x, y: point.y, width: 1, height: 1))
        }
        let padding = max(
            max(rect.width, rect.height) * 0.15, 80 / MKMetersPerMapPointAtLatitude(first.coordinate.latitude))
        map.setVisibleMapRect(rect.insetBy(dx: -padding, dy: -padding), animated: false)
    }

    func mapView(_ mapView: MKMapView, viewFor annotation: MKAnnotation) -> MKAnnotationView? {
        guard let point = annotation as? DroneMapPoint else { return nil }
        let identifier = "drone-photo"
        let view =
            mapView.dequeueReusableAnnotationView(withIdentifier: identifier)
            ?? MKAnnotationView(annotation: point, reuseIdentifier: identifier)
        view.annotation = point
        view.frame.size = CGSize(width: 14, height: 14)
        view.wantsLayer = true
        view.collisionMode = .none
        view.displayPriority = .required
        view.toolTip = point.title
        style(view, id: point.id)
        return view
    }

    func mapView(_ mapView: MKMapView, didSelect view: MKAnnotationView) {
        guard !updating, let point = view.annotation as? DroneMapPoint else { return }
        select([point.id])
    }

    private func style(_ view: MKAnnotationView, id: URL) {
        guard let appearance = appearances[id] else { return }
        view.layer?.backgroundColor = appearance.color.cgColor
        view.layer?.cornerRadius = 7
        view.layer?.borderWidth = appearance.selected ? 3 : 1
        view.layer?.borderColor = NSColor.white.cgColor
        view.alphaValue = appearance.excluded && !appearance.selected ? 0.38 : 1
        view.zPriority = appearance.selected ? .max : .defaultUnselected
    }
}
