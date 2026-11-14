import Foundation

struct ProcessorRuntimeInfo: Decodable {
    let schemaVersion: Int
    let framework: String
    let runtimeVersion: String
    let runtimeIdentifier: String
    let processArchitecture: String
    let operatingSystemArchitecture: String
    let buildConfiguration: String
    let version: String
    let executable: String
    let dynamicCodeSupported: Bool
    let dynamicCodeCompiled: Bool
    let serverGC: Bool
    let gcLatency: String
    let gcAvailableMemoryBytes: Int64
    let processorCount: Int
    let is64BitProcess: Bool
    let vectorAccelerated: Bool
    let vectorFloatLanes: Int
    let armAdvSimd: Bool
    let x86Avx2: Bool
    let metadataEngine: String?
    let assemblies: [String: String]

    var sections: [RuntimeInfoSection] {
        let execution = dynamicCodeCompiled ? "JIT enabled" : "Ahead of time / interpreter (no JIT)"
        let instructionSets = [armAdvSimd ? "ARM AdvSIMD" : nil, x86Avx2 ? "x86 AVX2" : nil].compactMap { $0 }.joined(
            separator: ", ")
        return [
            RuntimeInfoSection(
                title: "Processing engine",
                fields: [
                    RuntimeInfoField(name: "Build", value: buildConfiguration),
                    RuntimeInfoField(name: "Runtime", value: framework + " · " + runtimeIdentifier),
                    RuntimeInfoField(name: "Runtime version", value: runtimeVersion),
                    RuntimeInfoField(name: "Code generation", value: execution),
                    RuntimeInfoField(name: "Dynamic code", value: dynamicCodeSupported ? "Supported" : "Unavailable"),
                    RuntimeInfoField(
                        name: "Architecture", value: processArchitecture + (is64BitProcess ? " · 64 bit" : " · 32 bit")),
                    RuntimeInfoField(name: "Available processors", value: String(processorCount)),
                    RuntimeInfoField(
                        name: "Garbage collector", value: (serverGC ? "Server" : "Workstation") + " · " + gcLatency),
                    RuntimeInfoField(
                        name: "GC memory budget",
                        value: ByteCountFormatter.string(fromByteCount: gcAvailableMemoryBytes, countStyle: .memory)),
                    RuntimeInfoField(
                        name: "SIMD",
                        value: vectorAccelerated
                            ? "Hardware accelerated · \(vectorFloatLanes) Float32 lanes" : "Software"),
                    RuntimeInfoField(
                        name: "Instruction sets", value: instructionSets.isEmpty ? "Baseline" : instructionSets),
                    RuntimeInfoField(name: "Engine version", value: version),
                    RuntimeInfoField(name: "Executable", value: executable),
                    RuntimeInfoField(name: "Metadata reader", value: metadataEngine ?? "Unknown"),
                ]),
            RuntimeInfoSection(
                title: "Processor assemblies",
                fields: assemblies.keys.sorted().map {
                    RuntimeInfoField(name: $0, value: assemblies[$0] ?? "Unknown")
                }),
        ]
    }
}
