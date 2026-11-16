import SwiftUI

struct GeoreferenceModal: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var expanded = false

    var body: some View {
        GeometryReader { bounds in
            ZStack {
                Color.black.opacity(0.18)
                GeoreferenceView(workspace: workspace, expanded: $expanded)
                    .frame(
                        width: expanded ? bounds.size.width : min(1100, bounds.size.width - 32),
                        height: expanded ? bounds.size.height : min(740, bounds.size.height - 32)
                    )
                    .background(.background)
                    .clipShape(RoundedRectangle(cornerRadius: expanded ? 0 : 12))
                    .shadow(color: .black.opacity(expanded ? 0 : 0.2), radius: 20)
            }
            .frame(width: bounds.size.width, height: bounds.size.height)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Georeference viewer")
    }
}
