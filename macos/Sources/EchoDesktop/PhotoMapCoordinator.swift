import MapKit

final class PhotoMapCoordinator: NSObject, MKMapViewDelegate {
    private let selection: (String?) -> Void
    private let palette = CameraTiltPalette()
    private var identity = ""
    private var signature = ""
    private var colors: [ObjectIdentifier: NSColor] = [:]
    private var cameraIDs: [ObjectIdentifier: String] = [:]
    private var cameraOverlays: [String: [MKOverlay]] = [:]
    private var selectedPhotoID: String?
    private var updating = false
    private var model: ModelMapOverlay?
    private var modelOpacity = 0.8

    init(selection: @escaping (String?) -> Void) { self.selection = selection }

    func updateModel(map: MKMapView, model: ModelMapOverlay?, opacity: Double) {
        if self.model !== model {
            if let previous = self.model { map.removeOverlay(previous) }
            self.model = model
            if let model {
                map.addOverlay(model, level: .aboveRoads)
                let rect = model.boundingMapRect
                map.setVisibleMapRect(rect.insetBy(dx: -rect.width * 0.2, dy: -rect.height * 0.2), animated: false)
            }
        }
        modelOpacity = opacity
        if let model { map.renderer(for: model)?.alpha = opacity }
    }

    func update(map: MKMapView, cameras: [MapCamera], identity: String, selected: String?) {
        let signature =
            cameras.map {
                "\($0.id):\($0.position.latitude):\($0.position.longitude):\($0.pitchDegrees ?? 0):\($0.corners.first?.latitude ?? 0)"
            }.joined()
        let selected = cameras.contains { $0.id == selected } ? selected : nil
        let camerasChanged = self.signature != signature || self.identity != identity
        guard camerasChanged || selectedPhotoID != selected else { return }
        let shouldFit = self.identity != identity
        let previousSelection = selectedPhotoID
        selectedPhotoID = selected
        self.identity = identity
        self.signature = signature
        updating = true
        defer { updating = false }
        if camerasChanged {
            rebuild(map: map, cameras: cameras, fit: shouldFit)
        } else {
            for id in [previousSelection, selected].compactMap({ $0 }) {
                guard let camera = cameras.first(where: { $0.id == id }) else { continue }
                let overlays = cameraOverlays.removeValue(forKey: id) ?? []
                map.removeOverlays(overlays)
                for overlay in overlays {
                    colors.removeValue(forKey: ObjectIdentifier(overlay))
                    cameraIDs.removeValue(forKey: ObjectIdentifier(overlay))
                }
                addFrustum(camera, map: map)
            }
        }
        updateEmphasis(map: map)
    }

    private func rebuild(map: MKMapView, cameras: [MapCamera], fit: Bool) {
        map.removeAnnotations(map.annotations)
        map.removeOverlays(map.overlays.filter { !($0 is ModelMapOverlay) })
        colors.removeAll()
        cameraIDs.removeAll()
        cameraOverlays.removeAll()
        var extent = model?.boundingMapRect ?? MKMapRect.null
        for camera in cameras {
            let annotation = PhotoMapAnnotation(camera: camera)
            map.addAnnotation(annotation)
            let origin = annotation.coordinate
            let location = MKMapPoint(origin)
            extent = extent.union(MKMapRect(x: location.x, y: location.y, width: 1, height: 1))
            if !camera.corners.isEmpty {
                let corners = camera.corners.map {
                    CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude)
                }
                extent = extent.union(MKPolygon(coordinates: corners, count: corners.count).boundingMapRect)
            }
            addFrustum(camera, map: map)
        }
        if fit, !extent.isNull {
            let minimum = 40 / MKMetersPerMapPointAtLatitude(cameras.first?.position.latitude ?? 0)
            let padding = max(minimum, max(extent.width, extent.height) * 0.15)
            map.setVisibleMapRect(extent.insetBy(dx: -padding, dy: -padding), animated: false)
        }
    }

    private func addFrustum(_ camera: MapCamera, map: MKMapView) {
        let corners = camera.displayCorners(selected: camera.id == selectedPhotoID).map {
            CLLocationCoordinate2D(latitude: $0.latitude, longitude: $0.longitude)
        }
        guard !corners.isEmpty else { return }
        let origin = CLLocationCoordinate2D(latitude: camera.position.latitude, longitude: camera.position.longitude)
        let color = palette.color(pitch: camera.pitchDegrees)
        add(MKPolygon(coordinates: corners, count: corners.count), color: color, cameraID: camera.id, map: map)
        for corner in corners {
            let ray = MKPolyline(coordinates: [origin, corner], count: 2)
            add(ray, color: color.withAlphaComponent(0.6), cameraID: camera.id, map: map)
        }
    }

    private func add(_ overlay: MKOverlay, color: NSColor, cameraID: String, map: MKMapView) {
        colors[ObjectIdentifier(overlay)] = color
        cameraIDs[ObjectIdentifier(overlay)] = cameraID
        cameraOverlays[cameraID, default: []].append(overlay)
        map.addOverlay(overlay)
    }

    private func updateEmphasis(map: MKMapView) {
        for annotation in map.annotations {
            guard let annotation = annotation as? PhotoMapAnnotation else { continue }
            if let view = map.view(for: annotation) {
                applyEmphasis(to: view, cameraID: annotation.camera.id)
            }
            if annotation.camera.id == selectedPhotoID {
                map.selectAnnotation(annotation, animated: false)
            } else {
                map.deselectAnnotation(annotation, animated: false)
            }
        }
        for overlay in map.overlays {
            guard let renderer = map.renderer(for: overlay) as? MKOverlayPathRenderer,
                let cameraID = cameraIDs[ObjectIdentifier(overlay)]
            else { continue }
            renderer.alpha = opacity(for: cameraID)
            renderer.lineWidth = cameraID == selectedPhotoID ? 3 : 1
            renderer.setNeedsDisplay()
        }
    }

    private func opacity(for cameraID: String) -> CGFloat {
        selectedPhotoID == nil || selectedPhotoID == cameraID ? 1 : 0.18
    }

    private func applyEmphasis(to view: MKAnnotationView, cameraID: String) {
        view.alphaValue = opacity(for: cameraID)
        if let marker = view as? MKMarkerAnnotationView {
            let highlighted = selectedPhotoID == cameraID
            marker.titleVisibility = selectedPhotoID == nil ? .adaptive : highlighted ? .visible : .hidden
            marker.subtitleVisibility = highlighted ? .visible : .hidden
        }
    }

    func mapView(_ mapView: MKMapView, rendererFor overlay: MKOverlay) -> MKOverlayRenderer {
        if let model = overlay as? ModelMapOverlay {
            let renderer = ModelMapOverlayRenderer(model: model)
            renderer.alpha = modelOpacity
            return renderer
        }
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
        if let cameraID = cameraIDs[ObjectIdentifier(overlay)] {
            renderer.lineWidth = cameraID == selectedPhotoID ? 3 : 1
            renderer.alpha = opacity(for: cameraID)
        }
        return renderer
    }

    func mapView(_ mapView: MKMapView, viewFor annotation: MKAnnotation) -> MKAnnotationView? {
        guard let annotation = annotation as? PhotoMapAnnotation else { return nil }
        let view = MKMarkerAnnotationView(annotation: annotation, reuseIdentifier: nil)
        view.markerTintColor = palette.color(pitch: annotation.camera.pitchDegrees)
        view.glyphImage = NSImage(systemSymbolName: "camera.fill", accessibilityDescription: "Camera")
        view.canShowCallout = true
        view.displayPriority = .required
        applyEmphasis(to: view, cameraID: annotation.camera.id)
        return view
    }

    func mapView(_ mapView: MKMapView, didSelect view: MKAnnotationView) {
        guard !updating, let annotation = view.annotation as? PhotoMapAnnotation else { return }
        selection(annotation.camera.id)
    }

    func mapView(_ mapView: MKMapView, didDeselect view: MKAnnotationView) {
        guard !updating, let annotation = view.annotation as? PhotoMapAnnotation else { return }
        // MapKit deselects the previous marker before selecting another one.
        // Only clear selection once that transition has settled on empty space.
        DispatchQueue.main.async { [weak self, weak mapView] in
            guard let self, let mapView, mapView.selectedAnnotations.isEmpty,
                self.selectedPhotoID == annotation.camera.id
            else { return }
            self.selection(nil)
        }
    }
}
