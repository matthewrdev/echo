import Foundation

struct DroneSourceError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}
