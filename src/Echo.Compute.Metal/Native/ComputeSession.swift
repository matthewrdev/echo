import Foundation
import Metal

final class ComputeSession {
    let owner: ComputeDevice
    let budget: Int
    var buffers: [MTLBuffer?] = Array(repeating: nil, count: 8)
    var lengths = Array(repeating: 0, count: 8)
    var command: MTLCommandBuffer?
    var dispatches = 0

    init(owner: ComputeDevice, budget: Int) {
        self.owner = owner
        self.budget = min(owner.budget, max(4, budget))
    }

    func finish() throws {
        guard let pending = command else { return }
        command = nil
        dispatches = 0
        pending.commit()
        pending.waitUntilCompleted()
        guard pending.status == .completed else {
            throw pending.error ?? ComputeDevice.failure("Metal command did not complete")
        }
    }

    func buffer(slot: Int, input: UnsafeRawPointer?, bytes: Int) throws {
        guard buffers.indices.contains(slot), bytes >= 0 else {
            throw ComputeDevice.failure("Invalid buffer slot or length")
        }
        try finish()
        if buffers[slot] == nil || buffers[slot]!.length < max(4, bytes) {
            let allocated = buffers.enumerated().reduce(0) { $0 + ($1.offset == slot ? 0 : ($1.element?.length ?? 0)) }
            guard max(4, bytes) <= budget - allocated else {
                throw ComputeDevice.failure("Metal session memory budget exceeded")
            }
            if let previous = buffers[slot] { owner.recycle(previous) }
            buffers[slot] = nil
            buffers[slot] = try owner.acquire(bytes, limit: budget - allocated)
        }
        lengths[slot] = bytes
        if let input, bytes > 0 { memcpy(buffers[slot]!.contents(), input, bytes) }
    }

    func write(slot: Int, offset: Int, input: UnsafeRawPointer?, bytes: Int) throws {
        guard buffers.indices.contains(slot), offset >= 0, bytes >= 0,
            let buffer = buffers[slot], offset <= lengths[slot], bytes <= lengths[slot] - offset
        else {
            throw ComputeDevice.failure("Invalid compute upload range")
        }
        try finish()
        if let input, bytes > 0 { memcpy(buffer.contents().advanced(by: offset), input, bytes) }
    }

    func dispatch(name: String, count: Int, parameters: UnsafePointer<Int32>?, parameterCount: Int) throws {
        guard count > 0, parameterCount > 0, parameterCount <= 64, let parameters else {
            throw ComputeDevice.failure("Invalid compute dispatch")
        }
        let pipeline = try owner.pipeline(name)
        if command == nil { command = owner.queue.makeCommandBuffer() }
        guard let command, let encoder = command.makeComputeCommandEncoder() else {
            throw ComputeDevice.failure("Could not create Metal command encoder")
        }
        encoder.setComputePipelineState(pipeline)
        for (index, buffer) in buffers.enumerated() { encoder.setBuffer(buffer, offset: 0, index: index) }
        encoder.setBytes(parameters, length: parameterCount * 4, index: 8)
        encoder.dispatchThreads(
            MTLSize(width: count, height: 1, depth: 1),
            threadsPerThreadgroup: MTLSize(width: min(256, pipeline.maxTotalThreadsPerThreadgroup), height: 1, depth: 1)
        )
        encoder.endEncoding()
        dispatches += 1
        // Keep cancellation latency bounded for iterative solvers.
        if dispatches >= 32 { try finish() }
    }

    func read(slot: Int, output: UnsafeMutableRawPointer?, bytes: Int) throws {
        guard buffers.indices.contains(slot), bytes >= 0, let buffer = buffers[slot], bytes <= lengths[slot] else {
            throw ComputeDevice.failure("Invalid compute read")
        }
        try finish()
        if let output, bytes > 0 { memcpy(output, buffer.contents(), bytes) }
    }

    deinit {
        try? finish()
        for buffer in buffers.compactMap({ $0 }) { owner.recycle(buffer) }
    }
}
