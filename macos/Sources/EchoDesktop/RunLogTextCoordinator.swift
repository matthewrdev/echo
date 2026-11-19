import AppKit

@MainActor
final class RunLogTextCoordinator {
    let scrolling = LogScrollPositionCoordinator { _ in }
    private var entries: [RunLogEntry] = []
    private var following = false
    private var scrollScheduled = false

    func update(text: NSTextView, entries: [RunLogEntry], followLatest: Bool) {
        let changed = self.entries != entries
        let resumeFollowing = followLatest && !following
        following = followLatest
        guard changed || resumeFollowing, let storage = text.textStorage else { return }
        if changed {
            let appending = entries.starts(with: self.entries)
            let added = appending ? entries.dropFirst(self.entries.count) : entries[...]
            let content = NSMutableAttributedString(string: "")
            let paragraph = NSMutableParagraphStyle()
            paragraph.paragraphSpacing = 5
            let font = NSFont.monospacedSystemFont(ofSize: NSFont.smallSystemFontSize, weight: .regular)
            for entry in added {
                content.append(
                    NSAttributedString(
                        string: entry.time + "  ",
                        attributes: [.font: font, .foregroundColor: NSColor.secondaryLabelColor]))
                content.append(
                    NSAttributedString(
                        string: entry.message + "\n", attributes: [.font: font, .foregroundColor: NSColor.labelColor]))
            }
            content.addAttribute(.paragraphStyle, value: paragraph, range: NSRange(location: 0, length: content.length))
            if appending {
                storage.append(content)
            } else {
                storage.setAttributedString(content)
                text.setSelectedRange(NSRange(location: 0, length: 0))
            }
            self.entries = entries
        }
        if followLatest, !scrollScheduled {
            // SwiftUI can resize the scroll view after updateNSView. Follow only
            // after that layout, otherwise the first history opens at its start.
            scrollScheduled = true
            DispatchQueue.main.async { [weak self, weak text] in
                guard let self else { return }
                self.scrollScheduled = false
                guard self.following, let text else { return }
                text.scrollRangeToVisible(NSRange(location: text.textStorage?.length ?? 0, length: 0))
            }
        }
    }
}
