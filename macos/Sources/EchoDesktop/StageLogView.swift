import SwiftUI

struct StageLogView: View {
    @ObservedObject var workspace: WorkspaceViewModel
    @State private var expanded = true
    @State private var scope = ActivityLogScope.stage
    @State private var followLatest = true
    @State private var showBackgroundQueries = false

    private var entries: [RunLogEntry] {
        workspace.logs.filter { $0.stage == workspace.selectedStage.rawValue || $0.stage == "run" }
    }
    private var commands: [CLICommandInvocation] {
        workspace.commands.filter { showBackgroundQueries || !$0.isBackgroundQuery }
    }
    private var latestCommand: CLICommandInvocation? {
        workspace.commands.last { !$0.isBackgroundQuery }
    }
    private var lastID: String? {
        scope == .stage ? entries.last.map { "event-\($0.id)" } : commands.last.map { "command-\($0.id)" }
    }

    var body: some View {
        DisclosureGroup(isExpanded: $expanded) {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Picker("Log history", selection: $scope) {
                        Text("Stage activity").tag(ActivityLogScope.stage)
                        Text("CLI commands").tag(ActivityLogScope.commands)
                    }.pickerStyle(.segmented).labelsHidden().frame(width: 270)
                    if scope == .commands {
                        Toggle("Background queries", isOn: $showBackgroundQueries).toggleStyle(.checkbox)
                    }
                    Spacer()
                    Toggle("Follow latest", isOn: $followLatest).toggleStyle(.checkbox)
                }
                if scope == .stage, let latestCommand {
                    CLICommandRow(invocation: latestCommand, title: "Latest CLI command")
                }
                if let error = workspace.commandLogError {
                    Text("CLI history is available for this session, but could not be saved: \(error)")
                        .foregroundStyle(.orange)
                }
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 5) {
                            if scope == .stage {
                                if entries.isEmpty {
                                    Text(
                                        "\(workspace.stageStatus(workspace.selectedStage)). Messages will appear here when this stage runs."
                                    )
                                    .foregroundStyle(.secondary)
                                }
                                ForEach(entries) { entry in
                                    HStack(alignment: .top, spacing: 12) {
                                        Text(entry.time).monospacedDigit().foregroundStyle(.secondary).fixedSize()
                                        Text(entry.message).textSelection(.enabled)
                                    }.frame(maxWidth: .infinity, alignment: .leading).id("event-\(entry.id)")
                                }
                            } else {
                                if commands.isEmpty {
                                    Text("CLI commands for this project appear here as Echo launches them.")
                                        .foregroundStyle(.secondary)
                                }
                                ForEach(commands) { command in
                                    CLICommandRow(invocation: command).id("command-\(command.id)")
                                }
                            }
                        }
                        .padding(.vertical, 6)
                        .background(LogScrollPositionObserver(followLatest: $followLatest))
                    }
                    .frame(height: 160)
                    .onAppear { if let lastID { proxy.scrollTo(lastID, anchor: .bottom) } }
                    .onChange(of: lastID) { _, id in
                        if followLatest, let id { proxy.scrollTo(id, anchor: .bottom) }
                    }
                    .onChange(of: followLatest) { _, follows in
                        if follows, let lastID { proxy.scrollTo(lastID, anchor: .bottom) }
                    }
                }
            }.font(.caption).padding(.top, 6)
        } label: {
            Text("\(workspace.selectedStage.shortTitle) activity · full history").font(.caption.weight(.semibold))
        }
        .padding(.horizontal, 16).padding(.vertical, 8)
    }
}
