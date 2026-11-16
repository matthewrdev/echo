import Foundation

struct TelemetrySample: Identifiable {
    let time: Date
    let resources: ResourceProgress

    var id: Date { time }
    var memoryGiB: Double { Double(resources.workingSetBytes) / 1_073_741_824 }
    var cacheGiB: Double { Double(resources.cacheResidentBytes) / 1_073_741_824 }
    var budgetGiB: Double { Double(resources.memoryBudgetBytes) / 1_073_741_824 }
}
