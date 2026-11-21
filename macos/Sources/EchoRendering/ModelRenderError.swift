import Foundation

struct ModelRenderError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}
