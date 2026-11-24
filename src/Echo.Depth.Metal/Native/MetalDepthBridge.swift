import Foundation

private func writeDetails(_ text: String, to output: UnsafeMutablePointer<CChar>?, capacity: Int32) {
    guard let output, capacity > 0 else { return }
    let bytes = Array(text.utf8.prefix(Int(capacity) - 1))
    for (index, byte) in bytes.enumerated() {
        output[index] = CChar(bitPattern: byte)
    }
    output[bytes.count] = 0
}

@_cdecl("echo_metal_compile")
public func echoMetalCompile(
    _ source: UnsafePointer<CChar>, _ details: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> UnsafeMutableRawPointer? {
    autoreleasepool {
        do {
            let program = try MetalDepthPipeline(source: String(cString: source))
            return Unmanaged.passRetained(program).toOpaque()
        } catch {
            writeDetails(error.localizedDescription, to: details, capacity: capacity)
            return nil
        }
    }
}

@_cdecl("echo_metal_destroy_pipeline")
public func echoMetalDestroyPipeline(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<MetalDepthPipeline>.fromOpaque(handle).release()
}

@_cdecl("echo_metal_create_session")
public func echoMetalCreate(
    _ program: UnsafeMutableRawPointer, _ views: UnsafePointer<Float>, _ viewCount: Int32,
    _ gray: UnsafePointer<Float>, _ grayCount: Int32, _ details: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> UnsafeMutableRawPointer? {
    autoreleasepool {
        do {
            guard viewCount > 0, grayCount > 0 else {
                throw MetalDepthSession.failure("Empty Metal view inputs")
            }
            let session = try MetalDepthSession(
                program: Unmanaged<MetalDepthPipeline>.fromOpaque(program).takeUnretainedValue(),
                views: views, viewCount: Int(viewCount), gray: gray,
                grayCount: Int(grayCount))
            writeDetails(session.device.name, to: details, capacity: capacity)
            return Unmanaged.passRetained(session).toOpaque()
        } catch {
            writeDetails(error.localizedDescription, to: details, capacity: capacity)
            return nil
        }
    }
}

@_cdecl("echo_metal_calculate")
public func echoMetalCalculate(
    _ handle: UnsafeMutableRawPointer, _ jobs: UnsafePointer<Float>, _ sampleCount: Int32,
    _ depths: UnsafePointer<Float>, _ planeCount: Int32, _ output: UnsafeMutablePointer<Float>,
    _ details: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    autoreleasepool {
        do {
            guard sampleCount > 0, planeCount > 0 else {
                throw MetalDepthSession.failure("Empty Metal search batch")
            }
            let session = Unmanaged<MetalDepthSession>.fromOpaque(handle).takeUnretainedValue()
            try session.calculate(
                jobs: jobs, sampleCount: Int(sampleCount), depths: depths, planeCount: Int(planeCount), output: output)
            return 0
        } catch {
            writeDetails(error.localizedDescription, to: details, capacity: capacity)
            return 1
        }
    }
}

@_cdecl("echo_metal_destroy")
public func echoMetalDestroy(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<MetalDepthSession>.fromOpaque(handle).release()
}

@_cdecl("echo_metal_gpu_time")
public func metalDepthGpuTime(_ handle: UnsafeMutableRawPointer) -> Double {
    Unmanaged<MetalDepthSession>.fromOpaque(handle).takeUnretainedValue().gpuMilliseconds
}
