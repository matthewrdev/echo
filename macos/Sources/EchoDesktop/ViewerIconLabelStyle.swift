import SwiftUI

struct ViewerIconLabelStyle: LabelStyle {
    var selected = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.icon
            .modifier(ViewerIconFrame(selected: selected))
            .accessibilityRepresentation { configuration.title }
    }
}
