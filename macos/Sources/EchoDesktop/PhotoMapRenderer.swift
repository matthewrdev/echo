import AppKit

final class PhotoMapRenderer: CanvasRenderer {
    let identity: String
    let size = CGSize(width: 1200, height: 800)
    private let cameras: [MapCamera]
    private let selected: String?
    private let highlightedIDs: Set<String>
    private let selection: (String?) -> Void
    private let latitude: Double
    private let longitude: Double
    private let bounds: CGRect

    init(
        identity: String, cameras: [MapCamera], selected: String?, boxed: Set<String> = [],
        selection: @escaping (String?) -> Void
    ) {
        self.identity = identity
        self.cameras = cameras
        self.selected = selected
        highlightedIDs = Set(cameras.lazy.map(\.id)).intersection(boxed.union(selected.map { [$0] } ?? []))
        self.selection = selection
        latitude = cameras.first?.position.latitude ?? 0
        longitude = cameras.first?.position.longitude ?? 0
        let lat = latitude
        let lon = longitude
        let metresPerLatitude = Double.pi / 180 * 6_378_137.0
        let metresPerLongitude = metresPerLatitude * cos(lat * Double.pi / 180)
        let locations: [GeoPosition] = cameras.flatMap { [$0.position] + $0.corners }
        let points: [CGPoint] = locations.map { position in
            let east = (position.longitude - lon) * metresPerLongitude
            let south = (lat - position.latitude) * metresPerLatitude
            return CGPoint(x: east, y: south)
        }
        let minX = points.map(\.x).min() ?? -10
        let maxX = points.map(\.x).max() ?? 10
        let minY = points.map(\.y).min() ?? -10
        let maxY = points.map(\.y).max() ?? 10
        bounds = CGRect(x: minX, y: minY, width: max(20, maxX - minX), height: max(20, maxY - minY))
    }

    private var metresToPixels: CGFloat { min((size.width - 100) / bounds.width, (size.height - 100) / bounds.height) }

    private func point(_ position: GeoPosition) -> CGPoint {
        let east = (position.longitude - longitude) * .pi / 180 * 6_378_137 * cos(latitude * .pi / 180)
        let south = (latitude - position.latitude) * .pi / 180 * 6_378_137
        return CGPoint(
            x: size.width / 2 + (east - bounds.midX) * metresToPixels,
            y: size.height / 2 + (south - bounds.midY) * metresToPixels)
    }

    func draw(in context: CGContext, scale: CGFloat) {
        context.setStrokeColor(NSColor.separatorColor.withAlphaComponent(0.35).cgColor)
        context.setLineWidth(0.6 / scale)
        for x in stride(from: 0.0, through: size.width, by: 80) {
            context.move(to: CGPoint(x: x, y: 0))
            context.addLine(to: CGPoint(x: x, y: size.height))
        }
        for y in stride(from: 0.0, through: size.height, by: 80) {
            context.move(to: CGPoint(x: 0, y: y))
            context.addLine(to: CGPoint(x: size.width, y: y))
        }
        context.strokePath()
        // Draw the selected camera last so it remains legible among overlapping frustums.
        for camera in cameras.sorted(by: {
            (highlightedIDs.contains($0.id) ? 1 : 0) < (highlightedIDs.contains($1.id) ? 1 : 0)
        }) {
            context.saveGState()
            let origin = point(camera.position)
            let highlighted = highlightedIDs.contains(camera.id)
            context.setAlpha(highlightedIDs.isEmpty || highlighted ? 1 : 0.18)
            let color = CameraTiltPalette().color(pitch: camera.pitchDegrees)
            let corners = camera.displayCorners(selected: camera.id == selected).map(point)
            if !corners.isEmpty {
                context.setStrokeColor(color.withAlphaComponent(highlighted ? 1 : 0.42).cgColor)
                context.setFillColor(color.withAlphaComponent(highlighted ? 0.22 : 0.055).cgColor)
                context.setLineWidth((highlighted ? 2 : 1) / scale)
                context.beginPath()
                context.move(to: corners[0])
                for corner in corners.dropFirst() { context.addLine(to: corner) }
                context.closePath()
                context.drawPath(using: .fillStroke)
                for corner in corners {
                    context.move(to: origin)
                    context.addLine(to: corner)
                }
                context.strokePath()
            }
            let radius: CGFloat = (highlighted ? 5 : 3.5) / scale
            context.setFillColor(color.cgColor)
            context.fillEllipse(
                in: CGRect(x: origin.x - radius, y: origin.y - radius, width: radius * 2, height: radius * 2))
            context.restoreGState()
        }
        let scaleMetres = max(1, pow(10, floor(log10(Double(bounds.width) / 5))))
        let length = scaleMetres * metresToPixels
        context.setStrokeColor(NSColor.labelColor.cgColor)
        context.setLineWidth(2 / scale)
        context.move(to: CGPoint(x: 30, y: size.height - 30))
        context.addLine(to: CGPoint(x: 30 + length, y: size.height - 30))
        context.strokePath()
        ("\(Int(scaleMetres)) m" as NSString).draw(
            at: CGPoint(x: 30, y: size.height - 55),
            withAttributes: [.font: NSFont.systemFont(ofSize: 14), .foregroundColor: NSColor.labelColor])
    }

    func select(at position: CGPoint, tolerance: CGFloat) {
        let closest = cameras.min { a, b in
            hypot(point(a.position).x - position.x, point(a.position).y - position.y)
                < hypot(point(b.position).x - position.x, point(b.position).y - position.y)
        }
        if let closest,
            hypot(point(closest.position).x - position.x, point(closest.position).y - position.y) <= tolerance
        {
            selection(closest.id)
        } else {
            selection(nil)
        }
    }

    func cameraIDs(in rect: CGRect) -> Set<String> {
        Set(cameras.lazy.filter { rect.contains(self.point($0.position)) }.map(\.id))
    }
}
