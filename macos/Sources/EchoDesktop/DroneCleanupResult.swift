import Foundation

struct DroneCleanupResult: Sendable {
    var removed: Set<URL> = []
    var failures: [String] = []
}
