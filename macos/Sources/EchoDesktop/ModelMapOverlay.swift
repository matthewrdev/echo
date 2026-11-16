import AppKit
import MapKit

final class ModelMapOverlay: NSObject, MKOverlay {
    let image: NSImage
    let boundingMapRect: MKMapRect
    var coordinate: CLLocationCoordinate2D {
        MKMapPoint(x: boundingMapRect.midX, y: boundingMapRect.midY).coordinate
    }

    init(image: NSImage, rect: MKMapRect) {
        self.image = image
        boundingMapRect = rect
    }
}
