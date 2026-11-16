import Foundation

@MainActor
final class DepthStageQualityViewModel: ObservableObject {
    private let reader: DepthStageQualityReader
    private var generation = UUID()
    private var request: DepthStageQualityRequest?
    @Published private(set) var summary: DepthStageQualitySummary?
    @Published private(set) var loading = false
    @Published private(set) var error: String?

    init(reader: DepthStageQualityReader = DepthStageQualityReader()) {
        self.reader = reader
    }

    func load(_ request: DepthStageQualityRequest?) async {
        let generation = UUID()
        self.generation = generation
        let continuingPreview =
            self.request?.isPreview == true && request?.isPreview == true
            && self.request?.root == request?.root && self.request?.runID == request?.runID
            && self.request?.semanticsIndex == request?.semanticsIndex
        if !continuingPreview { summary = nil }
        self.request = request
        error = nil
        loading = request != nil
        guard let request else { return }
        do {
            let result = try await reader.load(request)
            try Task.checkCancellation()
            guard self.generation == generation else { return }
            summary = result
        } catch is CancellationError {
            guard self.generation == generation else { return }
        } catch {
            guard self.generation == generation else { return }
            summary = nil
            self.error = error.localizedDescription
        }
        loading = false
    }
}
