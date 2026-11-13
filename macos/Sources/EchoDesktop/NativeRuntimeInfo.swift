import AppKit
import Darwin
import Metal

final class NativeRuntimeInfo {
    private let bundle: Bundle
    private let launchedAt = Date()
    private let processorURL: URL

    init(bundle: Bundle, processorURL: URL) {
        self.bundle = bundle
        self.processorURL = processorURL
    }

    func read() -> [RuntimeInfoSection] {
        let process = ProcessInfo.processInfo
        #if DEBUG
            let configuration = "Debug"
        #else
            let configuration = "Release"
        #endif
        #if arch(arm64)
            let architecture = "arm64"
        #elseif arch(x86_64)
            let architecture = "x86_64"
        #else
            let architecture = "Unknown"
        #endif
        let build = AppBuildInfo(bundle: bundle)
        let gpu = MTLCreateSystemDefaultDevice()
        var application = [
            RuntimeInfoField(
                name: "Version",
                value: bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
                    ?? "Development build"),
            RuntimeInfoField(
                name: "Build number",
                value: bundle.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "Unknown"),
            RuntimeInfoField(name: "Native build", value: configuration),
            RuntimeInfoField(name: "Architecture", value: architecture),
            RuntimeInfoField(name: "Rosetta", value: number("sysctl.proc_translated") == 1 ? "Translated" : "No"),
            RuntimeInfoField(name: "Interface", value: "SwiftUI / AppKit · SceneKit / Metal"),
            RuntimeInfoField(name: "Bundle", value: bundle.bundleIdentifier ?? "Unbundled"),
            RuntimeInfoField(name: "Location", value: bundle.bundleURL.path),
            RuntimeInfoField(name: "Processor path", value: processorURL.path),
        ]
        if let build {
            application += [
                RuntimeInfoField(
                    name: "Source commit", value: build.commit + (build.dirty ? " · uncommitted changes" : " · clean")),
                RuntimeInfoField(name: "Built at", value: build.builtAt),
                RuntimeInfoField(name: "Swift compiler", value: build.swiftCompiler),
                RuntimeInfoField(
                    name: "Processor package", value: build.targetRuntime + " · " + build.processorDeployment),
                RuntimeInfoField(name: "Signing", value: build.signing),
            ]
        } else {
            application.append(RuntimeInfoField(name: "Build provenance", value: "Not recorded in this bundle"))
        }
        var hardware = [
            RuntimeInfoField(name: "macOS", value: process.operatingSystemVersionString),
            RuntimeInfoField(
                name: "Minimum macOS",
                value: bundle.object(forInfoDictionaryKey: "LSMinimumSystemVersion") as? String ?? "14.0"),
            RuntimeInfoField(name: "Model", value: string("hw.model") ?? "Unknown"),
            RuntimeInfoField(name: "CPU", value: string("machdep.cpu.brand_string") ?? architecture),
            RuntimeInfoField(
                name: "CPU cores",
                value:
                    "\(number("hw.physicalcpu") ?? 0) physical · \(process.processorCount) logical · \(process.activeProcessorCount) active"
            ),
            RuntimeInfoField(name: "System memory", value: bytes(process.physicalMemory)),
            RuntimeInfoField(name: "Thermal state", value: thermalState(process.thermalState)),
            RuntimeInfoField(name: "Low power mode", value: process.isLowPowerModeEnabled ? "Enabled" : "Disabled"),
            RuntimeInfoField(name: "System uptime", value: duration(process.systemUptime)),
        ]
        if let gpu {
            hardware += [
                RuntimeInfoField(name: "Viewer GPU", value: gpu.name),
                RuntimeInfoField(name: "Unified memory", value: gpu.hasUnifiedMemory ? "Yes" : "No"),
                RuntimeInfoField(name: "GPU working-set guidance", value: bytes(gpu.recommendedMaxWorkingSetSize)),
            ]
        }
        return [
            RuntimeInfoSection(title: "Application & build", fields: application),
            RuntimeInfoSection(title: "This Mac", fields: hardware),
            RuntimeInfoSection(
                title: "Desktop process",
                fields: [
                    RuntimeInfoField(name: "Process ID", value: String(process.processIdentifier)),
                    RuntimeInfoField(name: "Session uptime", value: duration(Date().timeIntervalSince(launchedAt))),
                    RuntimeInfoField(name: "Memory footprint", value: memoryFootprint().map(bytes) ?? "Unavailable"),
                    RuntimeInfoField(name: "Locale", value: Locale.current.identifier),
                    RuntimeInfoField(name: "Time zone", value: TimeZone.current.identifier),
                ]),
        ]
    }

    private func string(_ name: String) -> String? {
        var length = 0
        guard sysctlbyname(name, nil, &length, nil, 0) == 0, length > 0 else { return nil }
        var buffer = [CChar](repeating: 0, count: length)
        guard sysctlbyname(name, &buffer, &length, nil, 0) == 0 else { return nil }
        return String(cString: buffer)
    }

    private func number(_ name: String) -> UInt64? {
        var value: UInt64 = 0
        var length = MemoryLayout<UInt64>.size
        guard sysctlbyname(name, &value, &length, nil, 0) == 0 else { return nil }
        return value
    }

    private func memoryFootprint() -> UInt64? {
        var info = task_vm_info_data_t()
        let capacity = MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<integer_t>.size
        var count = mach_msg_type_number_t(capacity)
        let result = withUnsafeMutablePointer(to: &info) { pointer in
            pointer.withMemoryRebound(to: integer_t.self, capacity: capacity) {
                task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count)
            }
        }
        return result == KERN_SUCCESS ? info.phys_footprint : nil
    }

    private func bytes(_ value: UInt64) -> String {
        ByteCountFormatter.string(fromByteCount: Int64(clamping: value), countStyle: .memory)
    }

    private func duration(_ value: TimeInterval) -> String {
        Duration.seconds(max(0, value)).formatted(
            .units(allowed: [.days, .hours, .minutes, .seconds], width: .abbreviated))
    }

    private func thermalState(_ state: ProcessInfo.ThermalState) -> String {
        switch state {
        case .nominal: return "Nominal"
        case .fair: return "Fair"
        case .serious: return "Serious"
        case .critical: return "Critical"
        @unknown default: return "Unknown"
        }
    }
}
