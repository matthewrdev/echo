import SwiftUI

struct GradedCloudWorkspace: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @ObservedObject var textureModel: TexturePriorityEditorModel
    let presentation: ArtifactPresentation
    let openFeatures: (String) -> Void
    @ObservedObject var controller: SceneViewportController
    @Binding var showGround: Bool
    @Binding var editing: Bool
    @Binding var editingTexture: Bool
    @ObservedObject var grading: ColourGradingModel
    let editingColour: Bool
    let minimumSupport: Double

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            ExclusionWorkspaceView(
                workspace: workspace, textureModel: textureModel, presentation: presentation,
                openFeatures: openFeatures, controller: controller, showGround: $showGround,
                editing: $editing, editingTexture: $editingTexture,
                colourGrade: workspace.selectedStage == .cloud && !grading.showOriginal
                    ? grading.grade.savedValue : nil,
                histogramBrush: editingColour && grading.selectingRegion
                    ? CropBrushConfiguration(radius: 14, mode: .include) : nil,
                histogramSelection: editingColour ? grading.selection : nil,
                finishHistogramStroke: grading.select,
                histogramStrokeError: { grading.selectionError = $0 }
            )
            if editingColour && workspace.selectedStage == .cloud {
                Divider()
                ColourGradingEditor(workspace: workspace, model: grading, minimumSupport: minimumSupport)
                    .frame(width: 300)
            }
        }
    }
}
