import Foundation
import Metal

final class MetalDepthSession {
    let device: MTLDevice
    let queue: MTLCommandQueue
    let pipeline: MTLComputePipelineState
    let views: MTLBuffer
    let gray: MTLBuffer
    let viewCount: Int

    init(source: String, views: UnsafePointer<Float>, viewCount: Int, gray: UnsafePointer<Float>, grayCount: Int)
        throws
    {
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
            throw Self.failure("No Metal compute device is available")
        }
        self.device = device
        self.queue = queue
        self.viewCount = viewCount
        let options = MTLCompileOptions()
        if #available(macOS 15.0, *) {
            options.mathMode = .safe
            options.mathFloatingPointFunctions = .precise
        } else {
            options.fastMathEnabled = false
        }
        let library = try device.makeLibrary(source: source, options: options)
        guard let function = library.makeFunction(name: "stereo_cost") else {
            throw Self.failure("Missing stereo_cost kernel")
        }
        pipeline = try device.makeComputePipelineState(function: function)
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
        guard
            let jobBuffer = device.makeBuffer(bytes: jobs, length: sampleCount * 32 * 4, options: .storageModeShared),
            let depthBuffer = device.makeBuffer(bytes: depths, length: costCount * 4, options: .storageModeShared),
            let costBuffer = device.makeBuffer(length: costCount * 4, options: .storageModeShared),
            let command = queue.makeCommandBuffer(), let encoder = command.makeComputeCommandEncoder()
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
        guard command.status == .completed else {
            throw command.error ?? Self.failure("Metal command did not complete")
        }
        output.update(from: costBuffer.contents().assumingMemoryBound(to: Float.self), count: costCount)
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "Echo.Depth.Metal", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }
}
