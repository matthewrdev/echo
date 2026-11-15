import Foundation

@MainActor
final class ArtifactViewModel: ObservableObject {
    private let reader: ArtifactReader
    private var generation = UUID()
    private var loadTask: Task<ArtifactPresentation, Error>?
    private var root: URL?
    private var run: RunData?
    private var requestedMatch: FeatureMatchLink?
    private var presentedMode = ""
    @Published var selectedMatchIndex: Int?
    private var stage = PipelineStage.alignment

    @Published var units: [ArtifactUnit] = []
    @Published var selectedUnit = ""
    @Published var mode = "Cameras"
    @Published var slice = 0.5
    @Published var pointPercentage = 100.0
    @Published var loading = false
    @Published var presentation: ArtifactPresentation?
    @Published var error: String?

    init(reader: ArtifactReader) { self.reader = reader }

    func photoFeatures(root: String, run: RunData, photoID: String) async throws -> FeaturePresentation? {
        let root = URL(fileURLWithPath: root)
        let units = try await reader.units(root: root, run: run, stage: .features)
        guard let unit = units.first(where: { $0.imageIDs.contains(photoID) }) else { return nil }
        return try await reader.load(root: root, run: run, stage: .features, unit: unit, mode: "", slice: 0).features
    }

    var modes: [String] {
        switch stage {
        case .alignment: return []
        case .depth: return ["Depth", "Confidence"]
        case .cloud: return ["All points", "≥40%", "≥60%"]
        case .fusion: return FusionViewMode.allCases.map(\.rawValue)
        case .texture: return ["Textured model", "Atlas"]
        case .mesh, .export: return MeshViewMode.allCases.map(\.rawValue)
        default: return []
        }
    }

    func configure(root: String, run: RunData?, stage: PipelineStage, preferredPhotoID: String? = nil) async {
        let sameSelection = self.root?.path == root && self.run?.id == run?.id && self.stage == stage
        let previousUnit = units.first { $0.id == selectedUnit }
        let previousMatching = self.run?.artifacts["matching"]
        let request = UUID()
        generation = request
        loadTask?.cancel()
        loading = false
        error = nil
        self.root = URL(fileURLWithPath: root)
        self.run = run
        self.stage = stage
        if !sameSelection {
            mode = modes.first ?? ""
            units = []
            selectedUnit = ""
            presentation = nil
        }
        guard let run else {
            error = "Start a processing run to create saved artifacts."
            return
        }
        do {
            let found = try await reader.units(root: self.root!, run: run, stage: stage)
            guard generation == request else { return }
            units = found
            if stage == .matching, let requestedMatch {
                selectedUnit = requestedMatch.unitID
                selectedMatchIndex = requestedMatch.matchIndex
                self.requestedMatch = nil
            } else if !sameSelection, stage == .features || stage == .depth,
                let preferredPhotoID, let unit = units.first(where: { $0.imageIDs.contains(preferredPhotoID) })
            {
                selectedUnit = unit.id
                selectedMatchIndex = nil
            } else if !units.contains(where: { $0.id == selectedUnit }) {
                selectedUnit = units.first?.id ?? ""
                selectedMatchIndex = nil
            }
            // New image checkpoints extend the browser without replacing the
            // photo, zoom or selected feature that the user is inspecting.
            let currentUnit = units.first { $0.id == selectedUnit }
            let unchangedFeature =
                stage == .features && presentation?.features?.photo.id == selectedUnit
                && previousMatching == run.artifacts["matching"]
            let unchangedDepth =
                stage == .depth && presentation?.depthImage?.identity == currentUnit?.file.path
                && presentedMode == mode
            if sameSelection, presentation != nil, previousUnit == currentUnit, unchangedFeature || unchangedDepth {
                return
            }
            await load()
        } catch { self.error = error.localizedDescription }
    }

    func requestMatch(_ link: FeatureMatchLink) { requestedMatch = link }

    func selectUnit(_ id: String) {
        selectedMatchIndex = nil
        selectedUnit = id
        Task { await load() }
    }

    func selectMode(_ value: String) {
        if stage == .fusion, let axis = FusionViewMode(selection: value).axis,
            let preferred = presentation?.fusion?.preferredSlices[axis]
        {
            slice = preferred
        }
        mode = value
        Task { await load() }
    }

    func cancelLoading() {
        generation = UUID()
        loadTask?.cancel()
        loadTask = nil
        loading = false
    }

    func load() async {
        guard let root, let run else { return }
        let request = UUID()
        generation = request
        loading = true
        error = nil
        loadTask?.cancel()
        let reader = self.reader
        let stage = self.stage
        let unit = units.first { $0.id == selectedUnit }
        let mode = self.mode
        let slice = self.slice
        let cloudDetail = CloudDisplayDetail.percentage(Int(pointPercentage.rounded()))
        let task = Task {
            try await reader.load(
                root: root, run: run, stage: stage, unit: unit, mode: mode, slice: slice, cloudDetail: cloudDetail)
        }
        loadTask = task
        do {
            let value = try await task.value
            guard generation == request else { return }
            presentation = value
            presentedMode = mode
        } catch is CancellationError {
            guard generation == request else { return }
        } catch {
            guard generation == request else { return }
            self.error = error.localizedDescription
            presentation = nil
        }
        loading = false
    }
}
