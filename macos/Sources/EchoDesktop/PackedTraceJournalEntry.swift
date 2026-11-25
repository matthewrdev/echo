struct PackedTraceJournalEntry {
    let inode: UInt64?
    var offset: UInt64 = 0
    var spans: [PerformanceSpan] = []
}
