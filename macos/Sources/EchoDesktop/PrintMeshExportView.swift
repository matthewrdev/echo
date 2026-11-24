import SwiftUI

struct PrintMeshExportView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var sizeMillimetres = 150.0

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Form {
                TextField("Longest side (mm)", value: $sizeMillimetres, format: .number)
            }
            .formStyle(.columns)

            Text(
                "Echo scales the saved closed mesh to this size and places it on the build plane. The 3MF contains the mesh geometry."
            )
            .font(.callout).foregroundStyle(.secondary)

            Spacer(minLength: 0)
            HStack {
                Spacer()
                Button("Choose Export Folder…") {
                    let options = ["--size-mm", String(sizeMillimetres)]
                    workspace.sheet = nil
                    Task { @MainActor in
                        await Task.yield()
                        workspace.exportArtifact(group: "mesh", format: "3mf", stage: .mesh, options: options)
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(!sizeMillimetres.isFinite || sizeMillimetres <= 0)
            }
        }
        .padding(20)
    }
}
