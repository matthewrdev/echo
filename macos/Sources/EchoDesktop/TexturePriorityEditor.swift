import SwiftUI

struct TexturePriorityEditor: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var model: TexturePriorityEditorModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Texture priority").font(.headline)
            if model.incompatible {
                Text("Saved painting belongs to another alignment. Clear it to paint this run.")
                    .font(.callout).foregroundStyle(.orange)
            }
            Picker("Interaction", selection: $model.paintingEnabled) {
                Text("Paint").tag(true)
                Text("Navigate").tag(false)
            }.pickerStyle(.segmented)
            Picker("Priority", selection: $model.brush.weight) {
                Text("High · 4×").tag(4.0)
                Text("Raised · 2×").tag(2.0)
                Text("Normal · erase").tag(1.0)
                Text("Reduced · ½×").tag(0.5)
                Text("Low · ¼×").tag(0.25)
            }
            Text("Brush radius · \(Int(model.brush.radiusPixels)) px").font(.caption)
            Slider(value: $model.brush.radiusPixels, in: 8...120, step: 1)
                .accessibilityLabel("Texture brush radius")
            Toggle("Show priority colours", isOn: $model.showHeatmap)
            HStack {
                Label("More detail", systemImage: "circle.fill").foregroundStyle(.orange)
                Label("Less detail", systemImage: "circle.fill").foregroundStyle(.blue)
            }.font(.caption)
            Text(
                "Drag to paint. Hold Alt/Option and drag to orbit; add Shift to pan. Release to return to painting. Scroll to zoom."
            )
            .font(.caption).foregroundStyle(.secondary)
            Text("The brush has a shallow depth and soft edge. Rotate the view to paint steep or hidden surfaces.")
                .font(.caption).foregroundStyle(.secondary)
            HStack {
                Button("Undo", systemImage: "arrow.uturn.backward", action: model.undo)
                    .disabled(model.undoHistory.isEmpty)
                Button("Redo", systemImage: "arrow.uturn.forward", action: model.redo)
                    .disabled(model.redoHistory.isEmpty)
            }
            Button("Clear painting", systemImage: "eraser", action: model.clear)
                .disabled(model.painting.strokes.isEmpty && !model.incompatible)
            Text("\(model.painting.strokes.count) brush strokes").font(.caption).foregroundStyle(.secondary)
            Spacer(minLength: 0)
            Text(
                "Priority shares the fixed texture budget between regions. Extra pixels preserve detail available in your photographs."
            )
            .font(.caption).foregroundStyle(.secondary)
            Text("Save, then process Texture again. Existing runs keep their original painting.")
                .font(.caption).foregroundStyle(.secondary)
            Button(workspace.busy ? "Saving…" : "Save texture priorities") {
                guard var settings = workspace.inspection?.project.settings else { return }
                settings.texturePriority = model.savedValue
                workspace.save(settings: settings)
            }
            .buttonStyle(.borderedProminent)
            .disabled(!workspace.canEdit || model.incompatible || !model.painting.isValid)
            if workspace.inspection?.project.settings.texturePriority == model.savedValue {
                Label("Saved", systemImage: "checkmark.circle").font(.caption).foregroundStyle(.green)
            } else {
                Text("Unsaved painting").font(.caption).foregroundStyle(.secondary)
            }
        }
    }
}
