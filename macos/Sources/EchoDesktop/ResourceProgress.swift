import Foundation

struct ResourceProgress: Decodable {
    let sampledAt: String?
    let workingSetBytes: Int64
    let peakWorkingSetBytes: Int64
    let managedBytes: Int64
    let memoryBudgetBytes: Int64
    let cacheResidentBytes: Int64
    let cachePeakBytes: Int64
    let diskReadBytes: Int64
    let diskWrittenBytes: Int64
    let pageEvictions: Int64
    let scratchBytes: Int64

    var summary: String {
        let gib = 1_073_741_824.0
        return String(
            format: "RAM %.2f / %.2f GiB · peak %.2f GiB · cache %.2f GiB · paged read/write %.2f / %.2f GiB",
            Double(workingSetBytes) / gib, Double(memoryBudgetBytes) / gib,
            Double(peakWorkingSetBytes) / gib, Double(cacheResidentBytes) / gib,
            Double(diskReadBytes) / gib, Double(diskWrittenBytes) / gib)
    }
}
