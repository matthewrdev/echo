import Foundation
import Metal

final class MetalDepthPipeline {
    let device: MTLDevice
    let pipeline: MTLComputePipelineState

    init(source: String) throws {
        guard let device = MTLCreateSystemDefaultDevice() else {
            throw MetalDepthSession.failure("No Metal compute device is available")
        }
        self.device = device
        let options = MTLCompileOptions()
        if #available(macOS 15.0, *) {
            options.mathMode = .safe
            options.mathFloatingPointFunctions = .precise
        } else {
            options.fastMathEnabled = false
        }
        let library = try device.makeLibrary(source: source, options: options)
        guard let function = library.makeFunction(name: "stereo_cost") else {
            throw MetalDepthSession.failure("Missing stereo_cost kernel")
        }
        pipeline = try device.makeComputePipelineState(function: function)
    }
}
