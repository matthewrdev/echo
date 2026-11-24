import Foundation

struct TiffEntry {
    let tag: UInt16
    let type: UInt16
    let count: UInt32
    let value: UInt32
}
