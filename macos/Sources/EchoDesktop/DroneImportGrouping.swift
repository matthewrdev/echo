import CoreLocation

struct DroneImportGrouping {
    private let siteRadius = 250.0
    private let flightGap = 20.0 * 60

    func suggest(_ photos: [DroneImportPhoto]) -> DroneImportSuggestions {
        var suggestions = DroneImportSuggestions()
        var centres: [CLLocation] = []

        for photo in photos {
            guard let coordinate = photo.coordinate else { continue }
            let location = CLLocation(latitude: coordinate.latitude, longitude: coordinate.longitude)
            var closest: Int?
            var distance = siteRadius
            for (index, centre) in centres.enumerated() {
                let candidate = location.distance(from: centre)
                if candidate <= distance {
                    distance = candidate
                    closest = index
                }
            }
            if let closest {
                suggestions.assignments[photo.id] = suggestions.groups[closest].id
            } else {
                let group = DroneImportGroup(name: "Site \(centres.count + 1)")
                centres.append(location)
                suggestions.groups.append(group)
                suggestions.assignments[photo.id] = group.id
            }
        }
        return suggestions
    }

    func entries(for photos: [DroneImportPhoto]) -> [DroneImportEntry] {
        var flight = 1
        var previous: Date?
        return photos.map { photo in
            guard let date = photo.capturedAt else {
                return DroneImportEntry(path: photo.id.path, set: "Capture time unavailable")
            }
            if let previous, date.timeIntervalSince(previous) > flightGap { flight += 1 }
            previous = date
            return DroneImportEntry(path: photo.id.path, set: "Flight \(flight)")
        }
    }
}
