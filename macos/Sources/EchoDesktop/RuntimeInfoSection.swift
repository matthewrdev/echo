import Foundation

struct RuntimeInfoSection: Identifiable {
    let title: String
    let fields: [RuntimeInfoField]
    var id: String { title }
}
