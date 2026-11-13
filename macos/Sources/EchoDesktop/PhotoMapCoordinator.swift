import MapKit

final class PhotoMapCoordinator: NSObject, MKMapViewDelegate {
    private let selection: (String) -> Void
    private let palette = CameraTiltPalette()
    private var identity = ""
    private var signature = ""
    private var colors: [ObjectIdentifier: NSColor] = [:]
    private var widths: [ObjectIdentifier: CGFloat] = [:]
    private var updating = false

    init(selection: @escaping (String) -> Void) { self.selection = selection }

    func update(map: MKMapView, cameras: [MapCamera], identity: String, selected: String?, showAll: Bool) {
        let signature =
            cameras.map {
                "\($0.id):\($0.position.latitude):\($0.position.longitude):\($0.pitchDegrees ?? 0):\($0.corners.first?.latitude ?? 0)"
            }.joined() + (selected ?? "") + String(showAll)
        guard self.signature != signature || self.identity != identity else { return }
        let shouldFit = self.identity != identity
        self.identity = identity
        self.signature = signature
        updating = true
        map.removeAnnotations(map.annotations)
        map.removeOverlays(map.overlays)
        colors.removeAll()
        widths.removeAll()
        var extent = MKMapRect.null
        for camera in cameras {
            let annotation = PhotoMapAnnotation(camera: camera)
            map.addAnnotation(annotation)
            let origin = annotation.coordinate
            let location = MKMapPoint(origin)
            extent = extent.union(MKMapRect(x: location.x, y: location.y, width: 1, height: 1))
            let color = palette.color(pitch: camera.pitchDegrees)
            if !camera.corners.isEmpty && (showAll || camera.id == selected) {
                let corners = camera.corners.map {
                    CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude)
                }
                let polygon = MKPolygon(coordinates: corners, count: corners.count)
                add(polygon, color: color, selected: camera.id == selected, map: map)
                extent = extent.union(polygon.boundingMapRect)
                for corner in corners {
                    let ray = MKPolyline(coordinates: [origin, corner], count: 2)
                    add(ray, color: color.withAlphaComponent(0.6), selected: camera.id == selected, map: map)
                }
            }
            if camera.id == selected { map.selectAnnotation(annotation, animated: false) }
        }
        if shouldFit, !extent.isNull {
            let minimum = 40 / MKMetersPerMapPointAtLatitude(cameras.first?.position.latitude ?? 0)
            let padding = max(minimum, max(extent.width, extent.height) * 0.15)
            map.setVisibleMapRect(extent.insetBy(dx: -padding, dy: -padding), animated: false)
        }
        updating = false
    }

    private func add(_ overlay: MKOverlay, color: NSColor, selected: Bool, map: MKMapView) {
        colors[ObjectIdentifier(overlay)] = color
        widths[ObjectIdentifier(overlay)] = selected ? 3 : 1
        map.addOverlay(overlay)
    }

    func mapView(_ mapView: MKMapView, rendererFor overlay: MKOverlay) -> MKOverlayRenderer {
        let renderer: MKOverlayPathRenderer
        if let polygon = overlay as? MKPolygon {
            let polygonRenderer = MKPolygonRenderer(polygon: polygon)
            polygonRenderer.fillColor = colors[ObjectIdentifier(overlay)]?.withAlphaComponent(0.1)
            renderer = polygonRenderer
        } else if let line = overlay as? MKPolyline {
            renderer = MKPolylineRenderer(polyline: line)
        } else {
            return MKOverlayRenderer(overlay: overlay)
        }
        renderer.strokeColor = colors[ObjectIdentifier(overlay)]
        renderer.lineWidth = widths[ObjectIdentifier(overlay)] ?? 1
        return renderer
    }

    func mapView(_ mapView: MKMapView, viewFor annotation: MKAnnotation) -> MKAnnotationView? {
        guard let annotation = annotation as? PhotoMapAnnotation else { return nil }
        let view = MKMarkerAnnotationView(annotation: annotation, reuseIdentifier: nil)
        view.markerTintColor = palette.color(pitch: annotation.camera.pitchDegrees)
        view.glyphImage = NSImage(systemSymbolName: "camera.fill", accessibilityDescription: "Camera")
        view.canShowCallout = true
        view.displayPriority = .required
        return view
    }

    func mapView(_ mapView: MKMapView, didSelect view: MKAnnotationView) {
        guard !updating, let annotation = view.annotation as? PhotoMapAnnotation else { return }
        selection(annotation.camera.id)
    }
}
