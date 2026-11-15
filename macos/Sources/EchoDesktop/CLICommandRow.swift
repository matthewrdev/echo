import SwiftUI

struct CLICommandRow: View {
    let invocation: CLICommandInvocation
    var title = "CLI command"

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            HStack {
                Label(title, systemImage: "terminal").fontWeight(.semibold)
                Text(invocation.timestamp, format: .dateTime.hour().minute().second()).foregroundStyle(.secondary)
                Spacer()
            }
            Text("$ " + invocation.command)
                .font(.system(.caption, design: .monospaced)).textSelection(.enabled)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(8)
        .background(Color.accentColor.opacity(0.08), in: RoundedRectangle(cornerRadius: 5))
        .overlay(alignment: .leading) { Rectangle().fill(Color.accentColor).frame(width: 3) }
        .accessibilityIdentifier("cli-command-\(invocation.id)")
    }
}
