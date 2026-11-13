import Foundation

@MainActor
final class ArtifactViewModel: ObservableObject {
    private let reader: ArtifactReader
    private var generation = UUID()
    private var root: URL?
    private var run: RunData?
    private var requestedMatch: FeatureMatchLink?
    @Published var selectedMatchIndex: Int?
    private var stage = PipelineStage.alignment

    @Published var units: [ArtifactUnit] = []
    @Published var selectedUnit = ""
    @Published var mode = "Cameras"
    @Published var slice = 0.5
    @Published var loading = false
    @Published var presentation: ArtifactPresentation?
    @Published var error: String?

    init(reader: ArtifactReader) { self.reader = reader }

    var modes: [String] {
        switch stage {
        case .alignment: return ["Cameras", "Photo planes"]
        case .depth: return ["Depth", "Confidence"]
        case .cloud: return ["All support", "Support ≥40%", "Support ≥60%"]
        case .fusion: return ["X", "Y", "Z"]
        case .texture: return ["Textured model", "Atlas"]
        case .mesh, .export: return ["Textured", "Solid", "Wireframe"]
        default: return []
        }
    }

    func configure(root: String, run: RunData?, stage: PipelineStage, preferredPhotoID: String? = nil) async {
        let sameSelection = self.root?.path == root && self.run?.id == run?.id && self.stage == stage
        let request = UUID()
        generation = request
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
        mode = value
        Task { await load() }
    }

    func load() async {
        guard let root, let run else { return }
        let request = UUID()
        generation = request
        loading = true
        error = nil
        do {
            let value = try await reader.load(
                root: root, run: run, stage: stage,
                unit: units.first { $0.id == selectedUnit }, mode: mode, slice: slice)
            guard generation == request else { return }
            presentation = value
        } catch {
            guard generation == request else { return }
            self.error = error.localizedDescription
            presentation = nil
        }
        loading = false
    }
}
