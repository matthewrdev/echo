import Foundation

actor PackedTraceReader: PerformanceTraceReading {
    private let codec = PackedTraceCodec()
    private var summaries: [URL: PackedTraceSummaryEntry] = [:]
    private var journals: [URL: PackedTraceJournalEntry] = [:]

    func list(root: String) throws -> [PerformanceTraceSummary] {
        let runs = URL(fileURLWithPath: root).appendingPathComponent("runs", isDirectory: true)
        var current: [URL: PackedTraceSummaryEntry] = [:]
        for run in try children(runs) where validIdentifier(run.lastPathComponent) {
            let traces = run.appendingPathComponent("traces", isDirectory: true)
            for file in try children(traces) where file.lastPathComponent.hasSuffix(".trace.bin") {
                try Task.checkCancellation()
                guard let stamp = WorkspaceFileStamp(file: file) else { continue }
                if let cached = summaries[file], cached.stamp == stamp {
                    current[file] = cached
                } else {
                    let handle = try FileHandle(forReadingFrom: file)
                    defer { try? handle.close() }
                    var cursor = BinaryJournalCursor(try handle.read(upToCount: 65_536) ?? Data())
                    current[file] = PackedTraceSummaryEntry(stamp: stamp, summary: try codec.summary(&cursor))
                }
            }
        }
        summaries = current
        return current.values.map(\.summary).sorted { $0.startedAt > $1.startedAt }
    }

    func document(root: String, run: String, attempt: String) throws -> PerformanceTraceDocument {
        guard validIdentifier(run), validIdentifier(attempt) else { throw codec.malformed }
        let directory = URL(fileURLWithPath: root).appendingPathComponent("runs/\(run)/traces", isDirectory: true)
        let file = directory.appendingPathComponent("\(attempt).trace.bin")
        var header = BinaryJournalCursor(try Data(contentsOf: file))
        let summary = try codec.summary(&header)
        let committed = try header.wideInteger()
        let active = try header.array(codec.span)
        let statistics = try header.array(codec.statistic)
        let measurements = try header.array(codec.measurement)
        guard header.offset == header.data.count, committed >= 4 else { throw codec.malformed }
        let journal = directory.appendingPathComponent("\(attempt).trace.spans.bin")
        let spans = try completedSpans(journal, committed: committed)
        return PerformanceTraceDocument(
            schemaVersion: 1, summary: summary, spans: merge(spans, active.sorted { $0.id < $1.id }),
            statistics: statistics, measurements: measurements)
    }

    private func completedSpans(_ journal: URL, committed: UInt64) throws -> [PerformanceSpan] {
        guard let stamp = WorkspaceFileStamp(file: journal), let size = stamp.size, size >= committed else {
            throw codec.malformed
        }
        var entry = journals.removeValue(forKey: journal)
        if entry?.inode != stamp.inode || (entry?.offset ?? 0) > committed {
            entry = nil
        }
        var saved = entry ?? PackedTraceJournalEntry(inode: stamp.inode)
        if saved.offset < committed {
            let handle = try FileHandle(forReadingFrom: journal)
            defer { try? handle.close() }
            if saved.offset == 0 {
                var magic = BinaryJournalCursor(try handle.read(upToCount: 4) ?? Data())
                guard try magic.integer() == 0x3153_5445 else { throw codec.malformed }
                saved.offset = 4
            }
            try handle.seek(toOffset: saved.offset)
            var pending = Data()
            var added: [PerformanceSpan] = []
            var unread = committed - saved.offset
            while unread > 0 {
                try Task.checkCancellation()
                let bytes = try handle.read(upToCount: Int(min(unread, 1024 * 1024))) ?? Data()
                guard !bytes.isEmpty else { throw codec.malformed }
                unread -= UInt64(bytes.count)
                pending.append(bytes)
                var cursor = BinaryJournalCursor(pending)
                while let frame = try cursor.frame() {
                    var record = BinaryJournalCursor(frame)
                    added.append(try codec.span(&record))
                    guard record.offset == frame.count else { throw codec.malformed }
                }
                pending = pending.subdata(in: cursor.offset..<pending.count)
            }
            guard pending.isEmpty else { throw codec.malformed }
            saved.spans = merge(saved.spans, added.sorted { $0.id < $1.id })
            saved.offset = committed
        }
        if journals.count >= 4 { journals.removeAll() }
        journals[journal] = saved
        return saved.spans
    }

    private func merge(_ existing: [PerformanceSpan], _ added: [PerformanceSpan]) -> [PerformanceSpan] {
        guard let first = added.first else { return existing }
        guard let last = existing.last, last.id >= first.id else { return existing + added }
        var spans: [PerformanceSpan] = []
        spans.reserveCapacity(existing.count + added.count)
        var oldIndex = 0
        var newIndex = 0
        while oldIndex < existing.count && newIndex < added.count {
            if existing[oldIndex].id <= added[newIndex].id {
                spans.append(existing[oldIndex])
                oldIndex += 1
            } else {
                spans.append(added[newIndex])
                newIndex += 1
            }
        }
        spans.append(contentsOf: existing[oldIndex...])
        spans.append(contentsOf: added[newIndex...])
        return spans
    }

    private func children(_ directory: URL) throws -> [URL] {
        do {
            return try FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)
        } catch let error as CocoaError where [.fileReadNoSuchFile, .fileNoSuchFile].contains(error.code) {
            return []
        }
    }

    private func validIdentifier(_ value: String) -> Bool {
        value.utf8.count == 32
            && value.utf8.allSatisfy { (48...57).contains($0) || (65...70).contains($0) || (97...102).contains($0) }
    }
}
