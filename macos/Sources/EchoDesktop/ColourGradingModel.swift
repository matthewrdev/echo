import Foundation

@MainActor
final class ColourGradingModel: ObservableObject {
    @Published var grade = ColourGrade() { didSet { updateHistogram() } }
    @Published var showOriginal = false { didSet { updateHistogram() } }
    @Published var selectingRegion = false
    @Published var selection: ExclusionRegion?
    @Published var selectionError: String?
    @Published private(set) var histogram: ColourHistogram?
    @Published private(set) var progress = 0.0
    @Published private(set) var scanning = false
    @Published private(set) var updating = false
    @Published private(set) var error: String?
    private var frequencies: ColourFrequencies?
    private var scanTask: Task<Void, Never>?
    private var histogramTask: Task<Void, Never>?
    private var generation = UUID()
    private var histogramGeneration = UUID()

    func load(_ saved: ColourGrade?) {
        selection = nil
        selectingRegion = false
        selectionError = nil
        grade = saved ?? ColourGrade()
        showOriginal = false
    }

    func select(_ region: ExclusionRegion) {
        selection = region
        selectingRegion = false
        selectionError = nil
    }

    func scan(folder: URL, regions: [ExclusionRegion], minimumSupport: Double) {
        cancel()
        let token = UUID()
        generation = token
        scanning = true
        progress = 0
        error = nil
        let worker = Task.detached(priority: .utility) {
            let report = try JSONDecoder().decode(
                DenseCloudReport.self, from: Data(contentsOf: folder.appendingPathComponent("cloud.json")))
            try Task.checkCancellation()
            return try CloudColourHistogramReader().read(
                file: folder.appendingPathComponent("cloud.ply"), expectedCount: report.retainedSamples,
                regions: regions,
                minimumSupport: minimumSupport
            ) { value in
                Task { @MainActor [weak self] in
                    guard let self, self.generation == token else { return }
                    self.progress = value
                }
            }
        }
        scanTask = Task { [weak self] in
            do {
                let result = try await withTaskCancellationHandler(
                    operation: { try await worker.value }, onCancel: { worker.cancel() })
                guard let self, !Task.isCancelled, self.generation == token else { return }
                self.frequencies = result
                self.scanning = false
                self.updateHistogram()
            } catch {
                guard let self, !Task.isCancelled, self.generation == token else { return }
                self.scanning = false
                self.error = error.localizedDescription
            }
        }
    }

    func cancel() {
        generation = UUID()
        histogramGeneration = UUID()
        scanTask?.cancel()
        histogramTask?.cancel()
        scanTask = nil
        histogramTask = nil
        frequencies = nil
        histogram = nil
        scanning = false
        updating = false
        selectingRegion = false
    }

    private func updateHistogram() {
        histogramTask?.cancel()
        histogramGeneration = UUID()
        guard let frequencies else { return }
        let token = histogramGeneration
        let grade = showOriginal ? ColourGrade() : grade
        updating = true
        histogramTask = Task { [weak self] in
            do {
                // Coalesce slider events, then re-bin the exact RGB counts without reading the cloud again.
                try await Task.sleep(for: .milliseconds(120))
                let worker = Task.detached(priority: .utility) { try frequencies.histogram(grade: grade) }
                let result = try await withTaskCancellationHandler(
                    operation: { try await worker.value }, onCancel: { worker.cancel() })
                guard let self, !Task.isCancelled, self.histogramGeneration == token else { return }
                self.histogram = result
                self.updating = false
            } catch {
                guard let self, !Task.isCancelled, self.histogramGeneration == token else { return }
                self.error = error.localizedDescription
                self.updating = false
            }
        }
    }
}
