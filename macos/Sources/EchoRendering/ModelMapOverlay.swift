import AppKit
import MapKit

package final class ModelMapOverlay: NSObject, MKOverlay {
    package let image: NSImage
    package let boundingMapRect: MKMapRect
    package var coordinate: CLLocationCoordinate2D {
        MKMapPoint(x: boundingMapRect.midX, y: boundingMapRect.midY).coordinate
    }

    package init(image: NSImage, rect: MKMapRect) {
        self.image = image
        boundingMapRect = rect
    }
}
