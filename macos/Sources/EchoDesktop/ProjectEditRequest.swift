import Foundation

struct ProjectEditRequest: Identifiable {
    let id = UUID()
    let title: String
    let message: String
    let apply: () -> Void
    var actionTitle = "Apply change"
}
