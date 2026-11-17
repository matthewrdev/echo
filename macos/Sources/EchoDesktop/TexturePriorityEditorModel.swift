import SwiftUI

@MainActor
final class TexturePriorityEditorModel: ObservableObject {
    @Published var painting = TexturePriorityPainting(alignmentArtifact: "")
    @Published var brush = TextureBrushConfiguration()
    @Published var paintingEnabled = true
    @Published var showHeatmap = true
    @Published private(set) var undoHistory: [TexturePriorityPainting] = []
    @Published private(set) var redoHistory: [TexturePriorityPainting] = []
    private(set) var alignment = ""
    private(set) var loaded = false

    var incompatible: Bool { painting.alignmentArtifact != alignment }
    var savedValue: TexturePriorityPainting? { painting.strokes.isEmpty ? nil : painting }

    func load(_ painting: TexturePriorityPainting?, alignment: String) {
        self.alignment = alignment
        self.painting = painting ?? TexturePriorityPainting(alignmentArtifact: alignment)
        undoHistory = []
        redoHistory = []
        loaded = true
    }

    func append(_ stroke: TexturePriorityStroke) {
        guard !incompatible, !stroke.samples.isEmpty, stroke.isValid else { return }
        remember()
        painting.strokes.append(stroke)
    }

    func followRun(_ saved: TexturePriorityPainting?, alignment: String) {
        let emptyDraft = painting.strokes.isEmpty && undoHistory.isEmpty && redoHistory.isEmpty
        if !loaded || emptyDraft {
            load(saved, alignment: alignment)
        } else {
            // Run changes do not discard a draft. A genuinely different alignment
            // marks it incompatible while keeping the strokes available.
            self.alignment = alignment
        }
    }

    func clear() {
        remember()
        painting = TexturePriorityPainting(alignmentArtifact: alignment)
    }

    func undo() {
        guard let previous = undoHistory.popLast() else { return }
        redoHistory.append(painting)
        painting = previous
    }

    func redo() {
        guard let next = redoHistory.popLast() else { return }
        undoHistory.append(painting)
        painting = next
    }

    private func remember() {
        undoHistory.append(painting)
        redoHistory = []
    }
}
