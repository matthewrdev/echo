import Foundation

struct RuntimeInfoField: Identifiable {
    let name: String
    let value: String
    var id: String { name }
}
