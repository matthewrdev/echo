import Foundation
import Metal

final class ComputeDevice {
    let device: MTLDevice
    let queue: MTLCommandQueue
    let library: MTLLibrary
    private let lock = NSLock()
    private var descriptorBuffers: [UInt64: MTLBuffer] = [:]
    private var descriptorOrder: [UInt64] = []
    private var descriptorBytes = 0
    private var pipelines: [String: MTLComputePipelineState] = [:]
    private var spare: [MTLBuffer] = []
    var budget: Int { Int(min(device.recommendedMaxWorkingSetSize, ProcessInfo.processInfo.physicalMemory / 2)) }

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
        lock.lock()
        defer { lock.unlock() }
        if let pipeline = pipelines[name] { return pipeline }
        guard let function = library.makeFunction(name: name) else {
            throw Self.failure("Missing compute kernel: \(name)")
        }
        let pipeline = try device.makeComputePipelineState(function: function)
        pipelines[name] = pipeline
        return pipeline
    }

    func acquire(_ size: Int, limit: Int) throws -> MTLBuffer {
        lock.lock()
        defer { lock.unlock() }
        // Reusing an oversized allocation can consume space reserved for later
        // slots and make a valid plan fail depending on previous workloads.
        if let index = spare.firstIndex(where: { $0.length == max(4, size) && $0.length <= limit }) {
            return spare.remove(at: index)
        }
        guard let buffer = device.makeBuffer(length: max(4, size), options: .storageModeShared) else {
            throw Self.failure("Could not allocate Metal compute storage")
        }
        return buffer
    }

    func recycle(_ buffer: MTLBuffer) {
        lock.lock()
        defer { lock.unlock() }
        // Bound idle retention independently of the active session budget.
        if spare.reduce(0, { $0 + $1.length }) + buffer.length <= 64 * 1024 * 1024 {
            spare.append(buffer)
        }
    }

    func descriptor(key: UInt64, input: UnsafeRawPointer, bytes: Int, limit: Int) throws -> ComputeDescriptorBinding {
        lock.lock()
        defer { lock.unlock() }

        let capacity = min(32 * 1024 * 1024, limit / 4)
        if let cached = descriptorBuffers[key], cached.length == bytes,
            memcmp(cached.contents(), input, bytes) == 0
        {
            if let position = descriptorOrder.firstIndex(of: key) { descriptorOrder.remove(at: position) }
            descriptorOrder.append(key)
            return ComputeDescriptorBinding(buffer: cached, reused: true)
        }

        guard let buffer = device.makeBuffer(bytes: input, length: bytes, options: .storageModeShared) else {
            throw Self.failure("Could not allocate Metal descriptor storage")
        }
        if let previous = descriptorBuffers.removeValue(forKey: key) {
            descriptorBytes -= previous.length
            descriptorOrder.removeAll { $0 == key }
        }
        while descriptorBytes + bytes > capacity, !descriptorOrder.isEmpty {
            let oldest = descriptorOrder.removeFirst()
            if let discarded = descriptorBuffers.removeValue(forKey: oldest) { descriptorBytes -= discarded.length }
        }
        if bytes <= capacity {
            descriptorBuffers[key] = buffer
            descriptorOrder.append(key)
            descriptorBytes += bytes
        }
        return ComputeDescriptorBinding(buffer: buffer, reused: false)
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.Compute.Metal", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
