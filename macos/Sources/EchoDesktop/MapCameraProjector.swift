import Foundation

struct MapCameraProjector {
    func cameras(
        photos: [Photo], hints: [ImageHint], reconstruction: ReconstructionScene?,
        aligned: Bool, distance: Double
    ) -> [MapCamera] {
        photos.compactMap { photo in
            let hint = hints.last { $0.enabled && $0.type == "camera" && $0.imageId == photo.id }
            let pose = aligned ? reconstruction?.cameras.first { $0.imageId == photo.id } : nil
            let position: GeoPosition?
            let rotation: [Double]?
            let focal: Double
            let source: String
            if let pose, let origin = reconstruction?.origin, pose.centre.count == 3 {
                position = offset(origin, east: pose.centre[0], up: pose.centre[1], south: pose.centre[2])
                rotation = pose.rotation
                focal = pose.focalPixels > 0 ? pose.focalPixels : photo.focalPixels
                source = "Aligned camera"
            } else {
                position = hint?.position ?? photo.gps
                rotation = (hint?.attitude ?? photo.attitude).map(matrix)
                focal = photo.focalPixels
                source =
                    aligned
                    ? "Capture GPS (no aligned pose)"
                    : hint == nil ? photo.attitude?.source ?? "Capture GPS / gimbal" : "Capture data with camera hint"
            }
            guard let position else { return nil }
            var corners: [GeoPosition] = []
            if let rotation, rotation.count == 9, focal > 0 {
                let right = Double(photo.width) / (2 * focal) * distance
                let down = Double(photo.height) / (2 * focal) * distance
                for pixel in [[-right, -down], [right, -down], [right, down], [-right, down]] {
                    let east = rotation[0] * pixel[0] + rotation[1] * pixel[1] + rotation[2] * distance
                    let up = rotation[3] * pixel[0] + rotation[4] * pixel[1] + rotation[5] * distance
                    let south = rotation[6] * pixel[0] + rotation[7] * pixel[1] + rotation[8] * distance
                    corners.append(offset(position, east: east, up: up, south: south))
                }
            }
            let pitch = rotation.flatMap { values -> Double? in
                guard values.count == 9 else { return nil }
                return asin(min(1, max(-1, values[5]))) * 180 / .pi
            }
            return MapCamera(photo: photo, position: position, corners: corners, source: source, pitchDegrees: pitch)
        }
    }

    func offset(_ origin: GeoPosition, east: Double, up: Double, south: Double) -> GeoPosition {
        GeographicTransform(origin: origin).geographic([east, up, south])
    }

    private func matrix(_ attitude: Attitude) -> [Double] {
        let yaw = attitude.yaw * .pi / 180
        let pitch = attitude.pitch * .pi / 180
        let roll = attitude.roll * .pi / 180
        let forward = [sin(yaw) * cos(pitch), sin(pitch), -cos(yaw) * cos(pitch)]
        let right = [cos(yaw), 0, sin(yaw)]
        let down = [forward[1] * right[2], forward[2] * right[0] - forward[0] * right[2], -forward[1] * right[0]]
        let rolledRight = (0..<3).map { right[$0] * cos(roll) + down[$0] * sin(roll) }
        let rolledDown = (0..<3).map { down[$0] * cos(roll) - right[$0] * sin(roll) }
        return [
            rolledRight[0], rolledDown[0], forward[0], rolledRight[1], rolledDown[1], forward[1], rolledRight[2],
            rolledDown[2], forward[2],
        ]
    }
}
