import Foundation

struct RunLogCursor {
    var offset: UInt64 = 0
    var pending = Data()
    var entries: [RunLogEntry] = []
}
