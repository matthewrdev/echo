import Foundation
import Metal

final class MetalTextureSession {
    var gpuMilliseconds = 0.0
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
        occupancy: UnsafePointer<UInt32>, firstRow: Int, rowCount: Int, masks: UnsafePointer<UInt32>,
        spans: UnsafePointer<UInt32>,
        spanCount: Int, minimumBottom: Int, maximumBottom: Int
    ) throws -> UInt32 {
        guard spanCount > 0, minimumBottom > 0, minimumBottom <= maximumBottom, maximumBottom <= size,
            firstRow >= 0, rowCount >= 0, firstRow <= size, rowCount <= size - firstRow
        else {
            throw Self.failure("Invalid texture search inputs")
        }
        let spanBytes = spanCount * 3 * 4
        if self.spans == nil || self.spans!.length < spanBytes {
            self.spans = device.makeBuffer(length: spanBytes, options: .storageModeShared)
        }
        guard let spanBuffer = self.spans else { throw Self.failure("Could not allocate Metal island mask") }
        let wordsPerRow = (size + 31) / 32
        if rowCount > 0 {
            self.occupancy.contents().advanced(by: firstRow * wordsPerRow * 4).copyMemory(
                from: occupancy.advanced(by: firstRow * wordsPerRow), byteCount: rowCount * wordsPerRow * 4)
        }
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
        // Search only this band of bottom edges; occupancy survives subsequent bands.
        let counts = [UInt32(size), UInt32((size + 31) / 32), UInt32(maximumBottom), UInt32(minimumBottom)]
        counts.withUnsafeBytes { raw in
            encoder.setBytes(raw.baseAddress!, length: raw.count, index: 4)
        }
        encoder.dispatchThreads(
            MTLSize(width: size, height: maximumBottom - minimumBottom + 1, depth: 4),
            threadsPerThreadgroup: MTLSize(width: min(256, pipeline.maxTotalThreadsPerThreadgroup), height: 1, depth: 1)
        )
        encoder.endEncoding()
        command.commit()
        command.waitUntilCompleted()
        if command.gpuStartTime > 0, command.gpuEndTime >= command.gpuStartTime {
            gpuMilliseconds += (command.gpuEndTime - command.gpuStartTime) * 1000
        }
        guard command.status == .completed else {
            throw command.error ?? Self.failure("Metal texture command did not complete")
        }
        return result.contents().assumingMemoryBound(to: UInt32.self).pointee
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.Texturing.Metal", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
