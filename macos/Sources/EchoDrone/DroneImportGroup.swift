import Foundation

package struct DroneImportGroup: Identifiable, Sendable {
    package let id = UUID()
    package var name: String

    package init(name: String) {
        self.name = name
    }
}
