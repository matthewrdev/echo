import Foundation
import Metal

@main
struct MetalDepthPrototype {
    static func main() throws {
        let args = CommandLine.arguments
        guard args.count == 4 else {
            throw failure("Usage: metal-depth FIXTURE_DIRECTORY SHADER_FILE ITERATIONS")
        }
        let root = URL(fileURLWithPath: args[1], isDirectory: true)
        let fixture = try JSONDecoder().decode(
            MetalDepthFixtureInfo.self, from: Data(contentsOf: root.appendingPathComponent("fixture.json")))
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
            throw failure("No Metal compute device is available")
        }
        let setupStart = ProcessInfo.processInfo.systemUptime
        let options = MTLCompileOptions()
        if #available(macOS 15.0, *) {
            options.mathMode = .safe
            options.mathFloatingPointFunctions = .precise
        } else {
            options.fastMathEnabled = false
        }
        let source = try String(contentsOfFile: args[2], encoding: .utf8)
        let library = try device.makeLibrary(source: source, options: options)
        guard let function = library.makeFunction(name: "stereo_cost") else {
            throw failure("Missing stereo_cost kernel")
        }
        let pipeline = try device.makeComputePipelineState(function: function)
        let setupSeconds = ProcessInfo.processInfo.systemUptime - setupStart
        let uploadStart = ProcessInfo.processInfo.systemUptime
        let inputs = try ["views.bin", "gray.bin", "jobs.bin", "depths.bin"].map { name in
            let bytes = try Data(contentsOf: root.appendingPathComponent(name))
            return try bytes.withUnsafeBytes { raw -> MTLBuffer in
                guard
                    let buffer = device.makeBuffer(
                        bytes: raw.baseAddress!, length: raw.count, options: .storageModeShared)
                else {
                    throw failure("Could not allocate \(name)")
                }
                return buffer
            }
        }
        let costCount = fixture.pixelCount * fixture.planeCount
        let outputBytes = costCount * MemoryLayout<Float>.stride
        guard let output = device.makeBuffer(length: outputBytes, options: .storageModeShared) else {
            throw failure("Could not allocate cost output")
        }
        let uploadSeconds = ProcessInfo.processInfo.systemUptime - uploadStart
        let counts = [UInt32(fixture.pixelCount), UInt32(fixture.planeCount), UInt32(fixture.viewCount)]
        var gpuSeconds: [Double] = []
        var wallSeconds: [Double] = []
        var previous: Data?
        var identical = true
        for _ in 0..<(Int(args[3]) ?? 5) {
            let start = ProcessInfo.processInfo.systemUptime
            guard let command = queue.makeCommandBuffer(), let encoder = command.makeComputeCommandEncoder() else {
                throw failure("Could not create compute command")
            }
            encoder.setComputePipelineState(pipeline)
            for (index, buffer) in inputs.enumerated() {
                encoder.setBuffer(buffer, offset: 0, index: index)
            }
            encoder.setBuffer(output, offset: 0, index: 4)
            counts.withUnsafeBytes { raw in
                encoder.setBytes(raw.baseAddress!, length: raw.count, index: 5)
            }
            let groupSize = min(256, pipeline.maxTotalThreadsPerThreadgroup)
            encoder.dispatchThreads(
                MTLSize(width: costCount, height: 1, depth: 1),
                threadsPerThreadgroup: MTLSize(width: groupSize, height: 1, depth: 1))
            encoder.endEncoding()
            command.commit()
            command.waitUntilCompleted()
            guard command.status == .completed else {
                throw command.error ?? failure("Metal command did not complete")
            }
            wallSeconds.append(ProcessInfo.processInfo.systemUptime - start)
            gpuSeconds.append(command.gpuEndTime - command.gpuStartTime)
            let data = Data(bytes: output.contents(), count: outputBytes)
            if let previous {
                identical = identical && data == previous
            }
            previous = data
        }
        try previous?.write(to: root.appendingPathComponent("gpu-costs.bin"))
        let metrics = MetalDepthMetrics(
            device: device.name, pixelCount: fixture.pixelCount, planeCount: fixture.planeCount,
            viewCount: fixture.viewCount, pipelineSetupSeconds: setupSeconds, bufferUploadSeconds: uploadSeconds,
            gpuSeconds: gpuSeconds, dispatchWallSeconds: wallSeconds, repeatOutputsIdentical: identical)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let encoded = try encoder.encode(metrics)
        try encoded.write(to: root.appendingPathComponent("gpu-metrics.json"))
        print(String(decoding: encoded, as: UTF8.self))
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.MetalDepthPrototype", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
