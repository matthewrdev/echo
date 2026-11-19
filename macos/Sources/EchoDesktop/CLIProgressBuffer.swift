import Foundation

final class CLIProgressBuffer: @unchecked Sendable {
    private let lock = NSLock()
    private var pending: [String] = []
    private var scheduled = false
    private let deliver: @MainActor @Sendable ([String]) -> Void

    init(deliver: @escaping @MainActor @Sendable ([String]) -> Void) {
        self.deliver = deliver
    }

    func append(_ line: String) {
        lock.lock()
        pending.append(line)
        let needsDelivery = !scheduled
        scheduled = true
        lock.unlock()
        guard needsDelivery else { return }
        Task { @MainActor in
            try? await Task.sleep(for: .milliseconds(250))
            flush()
        }
    }

    @MainActor
    func flush() {
        let lines = drain()
        if !lines.isEmpty { deliver(lines) }
    }

    private func drain() -> [String] {
        lock.lock()
        defer { lock.unlock() }
        let lines = pending
        pending = []
        scheduled = false
        return lines
    }
}
