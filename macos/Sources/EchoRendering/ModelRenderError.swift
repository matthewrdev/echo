import Foundation

package struct ModelRenderError: LocalizedError {
    let message: String
    package init(message: String) { self.message = message }

    package var errorDescription: String? { message }
}
