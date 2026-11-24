import Foundation
import Metal

final class MetalDepthSession {
    var gpuMilliseconds = 0.0
    let device: MTLDevice
    let queue: MTLCommandQueue
    let pipeline: MTLComputePipelineState
    let views: MTLBuffer
    let gray: MTLBuffer
    let viewCount: Int
    private var jobs: MTLBuffer?
    private var depths: MTLBuffer?
    private var costs: MTLBuffer?

    init(
        program: MetalDepthPipeline, views: UnsafePointer<Float>, viewCount: Int, gray: UnsafePointer<Float>,
        grayCount: Int
    )
        throws
    {
        let device = program.device
        guard let queue = device.makeCommandQueue() else {
            throw Self.failure("Could not create Metal depth command queue")
        }
        self.device = device
        self.queue = queue
        self.viewCount = viewCount
        pipeline = program.pipeline
        guard
            let viewBuffer = device.makeBuffer(bytes: views, length: viewCount * 24 * 4, options: .storageModeShared),
            let grayBuffer = device.makeBuffer(bytes: gray, length: grayCount * 4, options: .storageModeShared)
        else {
            throw Self.failure("Could not allocate Metal image buffers")
        }
        self.views = viewBuffer
        self.gray = grayBuffer
    }

    func calculate(
        jobs: UnsafePointer<Float>, sampleCount: Int, depths: UnsafePointer<Float>, planeCount: Int,
        output: UnsafeMutablePointer<Float>
    ) throws {
        let costCount = sampleCount * planeCount
        let jobBuffer = try reusable(&self.jobs, bytes: sampleCount * 32 * 4)
        let depthBuffer = try reusable(&self.depths, bytes: costCount * 4)
        let costBuffer = try reusable(&self.costs, bytes: costCount * 4)
        memcpy(jobBuffer.contents(), jobs, sampleCount * 32 * 4)
        memcpy(depthBuffer.contents(), depths, costCount * 4)
        guard let command = queue.makeCommandBuffer(), let encoder = command.makeComputeCommandEncoder()
        else {
            throw Self.failure("Could not allocate Metal batch buffers or command")
        }
        encoder.setComputePipelineState(pipeline)
        for (index, buffer) in [views, gray, jobBuffer, depthBuffer, costBuffer].enumerated() {
            encoder.setBuffer(buffer, offset: 0, index: index)
        }
        let counts = [UInt32(sampleCount), UInt32(planeCount), UInt32(viewCount)]
        counts.withUnsafeBytes { raw in
            encoder.setBytes(raw.baseAddress!, length: raw.count, index: 5)
        }
        encoder.dispatchThreads(
            MTLSize(width: costCount, height: 1, depth: 1),
            threadsPerThreadgroup: MTLSize(width: min(256, pipeline.maxTotalThreadsPerThreadgroup), height: 1, depth: 1)
        )
        encoder.endEncoding()
        command.commit()
        command.waitUntilCompleted()
        if command.gpuStartTime > 0, command.gpuEndTime >= command.gpuStartTime {
            gpuMilliseconds += (command.gpuEndTime - command.gpuStartTime) * 1000
        }
        guard command.status == .completed else {
            throw command.error ?? Self.failure("Metal command did not complete")
        }
        output.update(from: costBuffer.contents().assumingMemoryBound(to: Float.self), count: costCount)
    }

    private func reusable(_ buffer: inout MTLBuffer?, bytes: Int) throws -> MTLBuffer {
        if let buffer, buffer.length >= bytes { return buffer }
        guard let replacement = device.makeBuffer(length: bytes, options: .storageModeShared) else {
            throw Self.failure("Could not allocate Metal batch storage")
        }
        buffer = replacement
        return replacement
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.Depth.Metal", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
