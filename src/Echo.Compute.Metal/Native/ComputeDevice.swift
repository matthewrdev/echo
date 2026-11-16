import Foundation
import Metal

final class ComputeDevice {
    let device: MTLDevice
    let queue: MTLCommandQueue
    let library: MTLLibrary
    var pipelines: [String: MTLComputePipelineState] = [:]
    var spare: [MTLBuffer] = []
    let budget = 512 * 1024 * 1024

    init(source: String) throws {
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
            throw Self.failure("No Metal compute device is available")
        }
        self.device = device
        self.queue = queue
        let options = MTLCompileOptions()
        if #available(macOS 15.0, *) {
            options.mathMode = .safe
            options.mathFloatingPointFunctions = .precise
        } else {
            options.fastMathEnabled = false
        }
        library = try device.makeLibrary(source: source, options: options)
    }

    func pipeline(_ name: String) throws -> MTLComputePipelineState {
        if let pipeline = pipelines[name] { return pipeline }
        guard let function = library.makeFunction(name: name) else {
            throw Self.failure("Missing compute kernel: \(name)")
        }
        let pipeline = try device.makeComputePipelineState(function: function)
        pipelines[name] = pipeline
        return pipeline
    }

    func acquire(_ size: Int, limit: Int) throws -> MTLBuffer {
        if let index = spare.firstIndex(where: { $0.length >= size && $0.length <= min(limit, max(4096, size * 2)) }) {
            return spare.remove(at: index)
        }
        guard let buffer = device.makeBuffer(length: max(4, size), options: .storageModeShared) else {
            throw Self.failure("Could not allocate Metal compute storage")
        }
        return buffer
    }

    func recycle(_ buffer: MTLBuffer) {
        // Bound idle retention independently of the active session budget.
        if spare.reduce(0, { $0 + $1.length }) + buffer.length <= 64 * 1024 * 1024 {
            spare.append(buffer)
        }
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.Compute.Metal", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
