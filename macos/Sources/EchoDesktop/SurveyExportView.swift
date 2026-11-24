import SwiftUI

struct SurveyExportView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var format = "las"
    @State private var coordinates: SurveyExportCoordinates = .local
    @State private var epsg = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Form {
                Picker("Format", selection: $format) {
                    Text("LAS").tag("las")
                    Text("LAZ").tag("laz")
                    Text("COPC (.copc.laz)").tag("copc")
                }
                Picker("Coordinates", selection: $coordinates) {
                    ForEach(SurveyExportCoordinates.allCases) { option in
                        Text(option.rawValue).tag(option)
                    }
                }
                if coordinates == .projected {
                    TextField("EPSG code", text: $epsg, prompt: Text("7856"))
                }
            }
            .formStyle(.columns)

            if coordinates != .local {
                Text(
                    "Requires a saved alignment with a WGS84 ellipsoidal height. Projected exports use the selected horizontal CRS and retain ellipsoidal heights in metres."
                )
                .font(.callout).foregroundStyle(.secondary)
            }

            if format != "las" || coordinates == .projected {
                Text("Uses a locally installed PDAL.")
                    .font(.callout).foregroundStyle(.secondary)
            }

            Spacer(minLength: 0)
            HStack {
                Spacer()
                Button("Choose Export Folder…") {
                    let options = crs.map { ["--crs", $0] } ?? []
                    workspace.sheet = nil
                    Task { @MainActor in
                        await Task.yield()
                        workspace.exportArtifact(group: "cloud", format: format, stage: .cloud, options: options)
                    }
                }
                .keyboardShortcut(.defaultAction)
                .disabled(coordinates == .projected && crs == nil)
            }
        }
        .padding(20)
    }

    private var crs: String? {
        switch coordinates {
        case .local: return nil
        case .earthCentred: return "EPSG:4978"
        case .projected:
            let value = epsg.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
            let code = value.hasPrefix("EPSG:") ? String(value.dropFirst(5)) : value
            guard let number = UInt32(code), number > 0, number <= Int32.max, number != 4978 else { return nil }
            return "EPSG:\(number)"
        }
    }
}
