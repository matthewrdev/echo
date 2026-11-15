import SwiftUI

struct RenameProjectView: View {
    let project: RecentProject
    @ObservedObject var workspace: WorkspaceViewModel
    @Environment(\.dismiss) private var dismiss
    @State private var name: String
    @State private var saving = false
    @State private var error: String?
    @FocusState private var focused: Bool

    init(project: RecentProject, workspace: WorkspaceViewModel) {
        self.project = project
        self.workspace = workspace
        _name = State(initialValue: project.name)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Rename Project").font(.title2.bold())
            TextField("Project name", text: $name).focused($focused)
                .onSubmit { save() }
            Text("The project folder will stay at \(project.path)")
                .font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
            if let error { Text(error).foregroundStyle(.red).font(.callout) }
            HStack {
                if saving { ProgressView().controlSize(.small) }
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction).disabled(saving)
                Button("Rename") { save() }.keyboardShortcut(.defaultAction).disabled(!canSave)
            }
        }.padding(24).frame(width: 420).interactiveDismissDisabled(saving)
            .onAppear { focused = true }
    }

    private var canSave: Bool { !saving && !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private func save() {
        guard canSave else { return }
        saving = true
        error = nil
        Task {
            do {
                try await workspace.renameRecent(project, name: name.trimmingCharacters(in: .whitespacesAndNewlines))
                dismiss()
            } catch {
                self.error = error.localizedDescription
                saving = false
            }
        }
    }
}
