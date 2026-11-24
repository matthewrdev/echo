import Foundation
import Metal

final class ComputeSession {
    var gpuMilliseconds = 0.0
    let owner: ComputeDevice
    let budget: Int
    var buffers: [MTLBuffer?] = Array(repeating: nil, count: 8)
    private var readOnlySlots = Set<Int>()
    var lengths = Array(repeating: 0, count: 8)
    var command: MTLCommandBuffer?
    var dispatches = 0
    var usedSlots = Set<Int>()
    var submitted: [MTLCommandBuffer] = []
    var lastUse: [MTLCommandBuffer?] = Array(repeating: nil, count: 8)

    init(owner: ComputeDevice, budget: Int) {
        self.owner = owner
        self.budget = min(owner.budget, max(4, budget))
    }

    func finish() throws {
        var failure: Error?
        do { try submit() } catch { failure = error }
        if let last = submitted.last {
            do { try complete(through: last) } catch { failure = failure ?? error }
        }
        if let failure { throw failure }
    }

    func submit() throws {
        guard let pending = command else { return }
        // Two in-flight submissions bound memory and cancellation latency.
        var failure: Error?
        if submitted.count >= 2 {
            do { try complete(through: submitted[0]) } catch { failure = error }
        }
        command = nil
        dispatches = 0
        for slot in usedSlots { lastUse[slot] = pending }
        usedSlots.removeAll(keepingCapacity: true)
        pending.commit()
        submitted.append(pending)
        if let failure { throw failure }
    }

    private func complete(through target: MTLCommandBuffer) throws {
        var failure: Error?
        while !submitted.isEmpty {
            let pending = submitted.removeFirst()
            pending.waitUntilCompleted()
            if pending.gpuStartTime > 0, pending.gpuEndTime >= pending.gpuStartTime {
                gpuMilliseconds += (pending.gpuEndTime - pending.gpuStartTime) * 1000
            }
            for slot in lastUse.indices where lastUse[slot] === pending { lastUse[slot] = nil }
            if pending.status != .completed {
                failure = failure ?? pending.error ?? ComputeDevice.failure("Metal command did not complete")
            }
            if pending === target { break }
        }
        if let failure { throw failure }
    }

    private func waitForSlot(_ slot: Int) throws {
        if usedSlots.contains(slot) { try submit() }
        if let pending = lastUse[slot] { try complete(through: pending) }
    }

    func buffer(slot: Int, input: UnsafeRawPointer?, bytes: Int) throws {
        guard buffers.indices.contains(slot), bytes >= 0 else {
            throw ComputeDevice.failure("Invalid buffer slot or length")
        }
        try waitForSlot(slot)
        let wasReadOnly = readOnlySlots.contains(slot)
        if wasReadOnly || buffers[slot] == nil || buffers[slot]!.length < max(4, bytes) {
            let available = availableBytes(excluding: slot)
            guard max(4, bytes) <= available else {
                throw ComputeDevice.failure("Metal session memory budget exceeded")
            }
            discard(slot)
            buffers[slot] = try owner.acquire(bytes, limit: available)
        }
        lengths[slot] = bytes
        if let input, bytes > 0 { memcpy(buffers[slot]!.contents(), input, bytes) }
    }

    func descriptor(slot: Int, key: UInt64, input: UnsafeRawPointer, bytes: Int) throws -> Bool {
        guard buffers.indices.contains(slot), bytes > 0, bytes <= availableBytes(excluding: slot) else {
            throw ComputeDevice.failure("Invalid compute descriptor binding")
        }

        try waitForSlot(slot)
        let binding = try owner.descriptor(key: key, input: input, bytes: bytes, limit: budget)
        discard(slot)
        buffers[slot] = binding.buffer
        lengths[slot] = bytes
        readOnlySlots.insert(slot)
        return binding.reused
    }

    private func availableBytes(excluding slot: Int) -> Int {
        budget - buffers.enumerated().reduce(0) { $0 + ($1.offset == slot ? 0 : ($1.element?.length ?? 0)) }
    }

    private func discard(_ slot: Int) {
        let previous = buffers[slot]
        buffers[slot] = nil
        let wasReadOnly = readOnlySlots.remove(slot) != nil
        if let previous, !wasReadOnly { owner.recycle(previous) }
    }

    func write(slot: Int, offset: Int, input: UnsafeRawPointer?, bytes: Int) throws {
        guard buffers.indices.contains(slot), !readOnlySlots.contains(slot), offset >= 0, bytes >= 0,
            let buffer = buffers[slot], offset <= lengths[slot], bytes <= lengths[slot] - offset
        else {
            throw ComputeDevice.failure("Invalid compute upload range")
        }
        try waitForSlot(slot)
        if let input, bytes > 0 { memcpy(buffer.contents().advanced(by: offset), input, bytes) }
    }

    func dispatch(
        name: String, count: Int, parameters: UnsafePointer<Int32>?, parameterCount: Int,
        slots: UnsafeBufferPointer<Int32>? = nil
    ) throws {
        guard count > 0, parameterCount > 0, parameterCount <= 64, let parameters else {
            throw ComputeDevice.failure("Invalid compute dispatch")
        }
        if let slots {
            guard !slots.isEmpty, slots.count <= 8, slots.allSatisfy({ buffers.indices.contains(Int($0)) }) else {
                throw ComputeDevice.failure("Invalid compute buffer bindings")
            }
        }
        let pipeline = try owner.pipeline(name)
        let fixedGroups =
            name == "fusion_retained_counts" || name == "fusion_compact_results" || name == "descriptor_tiles"
        if fixedGroups && pipeline.maxTotalThreadsPerThreadgroup < 256 {
            throw ComputeDevice.failure("This compute kernel requires 256-thread groups")
        }
        if command == nil { command = owner.queue.makeCommandBuffer() }
        guard let command, let encoder = command.makeComputeCommandEncoder() else {
            throw ComputeDevice.failure("Could not create Metal command encoder")
        }
        encoder.setComputePipelineState(pipeline)
        if let slots {
            for (index, slot) in slots.enumerated() {
                encoder.setBuffer(buffers[Int(slot)], offset: 0, index: index)
                usedSlots.insert(Int(slot))
            }
        } else {
            for (index, buffer) in buffers.enumerated() {
                encoder.setBuffer(buffer, offset: 0, index: index)
                usedSlots.insert(index)
            }
        }
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
        for slot in buffers.indices { discard(slot) }
    }
}
