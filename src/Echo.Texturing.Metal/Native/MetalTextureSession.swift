import Foundation
import Metal

final class MetalTextureSession {
    let device: MTLDevice
    let queue: MTLCommandQueue
    let pipeline: MTLComputePipelineState
    let size: Int
    let occupancy: MTLBuffer
    let masks: MTLBuffer
    let result: MTLBuffer
    var spans: MTLBuffer?

    init(source: String, size: Int) throws {
        guard size > 0, size <= 8192 else { throw Self.failure("Invalid texture search size") }
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
            throw Self.failure("No Metal compute device is available")
        }
        self.device = device
        self.queue = queue
        self.size = size
        let library = try device.makeLibrary(source: source, options: nil)
        guard let function = library.makeFunction(name: "texture_placement") else {
            throw Self.failure("Missing texture_placement kernel")
        }
        pipeline = try device.makeComputePipelineState(function: function)
        guard
            let occupancy = device.makeBuffer(length: size * ((size + 31) / 32) * 4, options: .storageModeShared),
            let masks = device.makeBuffer(length: 16 * 4, options: .storageModeShared),
            let result = device.makeBuffer(length: 4, options: .storageModeShared)
        else { throw Self.failure("Could not allocate Metal texture buffers") }
        self.occupancy = occupancy
        self.masks = masks
        self.result = result
    }

    func find(
        occupancy: UnsafePointer<UInt32>, masks: UnsafePointer<UInt32>, spans: UnsafePointer<UInt32>,
        spanCount: Int, maximumBottom: Int
    ) throws -> UInt32 {
        guard spanCount > 0, maximumBottom > 0, maximumBottom <= size else {
            throw Self.failure("Invalid texture search inputs")
        }
        let spanBytes = spanCount * 3 * 4
        if self.spans == nil || self.spans!.length < spanBytes {
            self.spans = device.makeBuffer(length: spanBytes, options: .storageModeShared)
        }
        guard let spanBuffer = self.spans else { throw Self.failure("Could not allocate Metal island mask") }
        self.occupancy.contents().copyMemory(from: occupancy, byteCount: self.occupancy.length)
        self.masks.contents().copyMemory(from: masks, byteCount: self.masks.length)
        spanBuffer.contents().copyMemory(from: spans, byteCount: spanBytes)
        result.contents().assumingMemoryBound(to: UInt32.self).pointee = UInt32.max

        guard let command = queue.makeCommandBuffer(), let encoder = command.makeComputeCommandEncoder() else {
            throw Self.failure("Could not allocate Metal texture command")
        }
        encoder.setComputePipelineState(pipeline)
        for (index, buffer) in [self.occupancy, self.masks, spanBuffer, result].enumerated() {
            encoder.setBuffer(buffer, offset: 0, index: index)
        }
        // Four words preserve Metal uint3's 16-byte constant-buffer alignment.
        let counts = [UInt32(size), UInt32((size + 31) / 32), UInt32(maximumBottom), 0]
        counts.withUnsafeBytes { raw in
            encoder.setBytes(raw.baseAddress!, length: raw.count, index: 4)
        }
        encoder.dispatchThreads(
            MTLSize(width: size, height: maximumBottom, depth: 4),
            threadsPerThreadgroup: MTLSize(width: min(256, pipeline.maxTotalThreadsPerThreadgroup), height: 1, depth: 1)
        )
        encoder.endEncoding()
        command.commit()
        command.waitUntilCompleted()
        guard command.status == .completed else {
            throw command.error ?? Self.failure("Metal texture command did not complete")
        }
        return result.contents().assumingMemoryBound(to: UInt32.self).pointee
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.Texturing.Metal", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
