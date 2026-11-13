import SwiftUI

struct ProjectEditConfirmation: ViewModifier {
    @ObservedObject var workspace: WorkspaceViewModel
    let active: Bool

    func body(content: Content) -> some View {
        content.confirmationDialog(
            workspace.pendingChange?.title ?? "Change project?",
            isPresented: Binding(
                get: { active && workspace.pendingChange != nil },
                set: { if !$0 { workspace.pendingChange = nil } }), titleVisibility: .visible
        ) {
            Button(workspace.pendingChange?.actionTitle ?? "Apply change") { workspace.applyPendingEdit() }
            Button("Cancel", role: .cancel) { workspace.pendingChange = nil }
        } message: {
            Text(workspace.pendingChange?.message ?? "")
        }
    }
}
