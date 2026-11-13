import SwiftUI

struct ViewerIconFrame: ViewModifier {
    var selected = false

    func body(content: Content) -> some View {
        content
            .font(.system(size: 14, weight: .medium))
            .foregroundStyle(selected ? Color.white : Color.primary)
            .frame(width: 28, height: 28)
            .background(
                selected ? Color.accentColor : Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 5)
            )
            .contentShape(Rectangle())
    }
}
