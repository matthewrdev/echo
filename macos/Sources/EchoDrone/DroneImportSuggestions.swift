import Foundation

package struct DroneImportSuggestions {
    package init() {}

    package var groups: [DroneImportGroup] = []
    package var assignments: [URL: UUID] = [:]
}
