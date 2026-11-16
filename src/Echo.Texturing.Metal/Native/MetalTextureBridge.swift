import Foundation

private func writeTextureDetails(_ text: String, to output: UnsafeMutablePointer<CChar>?, capacity: Int32) {
    guard let output, capacity > 0 else { return }
    let bytes = Array(text.utf8.prefix(Int(capacity) - 1))
    for (index, byte) in bytes.enumerated() {
        output[index] = CChar(bitPattern: byte)
    }
    output[bytes.count] = 0
}

@_cdecl("echo_texture_metal_create")
public func echoTextureMetalCreate(
    _ source: UnsafePointer<CChar>, _ size: Int32, _ details: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> UnsafeMutableRawPointer? {
    autoreleasepool {
        do {
            let session = try MetalTextureSession(source: String(cString: source), size: Int(size))
            writeTextureDetails(session.device.name, to: details, capacity: capacity)
            return Unmanaged.passRetained(session).toOpaque()
        } catch {
            writeTextureDetails(error.localizedDescription, to: details, capacity: capacity)
            return nil
        }
    }
}

@_cdecl("echo_texture_metal_find")
public func echoTextureMetalFind(
    _ handle: UnsafeMutableRawPointer, _ occupancy: UnsafePointer<UInt32>, _ firstRow: Int32, _ rowCount: Int32,
    _ masks: UnsafePointer<UInt32>,
    _ spans: UnsafePointer<UInt32>, _ spanCount: Int32, _ minimumBottom: Int32, _ maximumBottom: Int32,
    _ output: UnsafeMutablePointer<UInt32>, _ details: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    autoreleasepool {
        do {
            let session = Unmanaged<MetalTextureSession>.fromOpaque(handle).takeUnretainedValue()
            output.pointee = try session.find(
                occupancy: occupancy, firstRow: Int(firstRow), rowCount: Int(rowCount), masks: masks, spans: spans,
                spanCount: Int(spanCount),
                minimumBottom: Int(minimumBottom), maximumBottom: Int(maximumBottom))
            return 0
        } catch {
            writeTextureDetails(error.localizedDescription, to: details, capacity: capacity)
            return 1
        }
    }
}

@_cdecl("echo_texture_metal_destroy")
public func echoTextureMetalDestroy(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<MetalTextureSession>.fromOpaque(handle).release()
}

@_cdecl("echo_texture_metal_gpu_time")
public func metalTextureGpuTime(_ handle: UnsafeMutableRawPointer) -> Double {
    Unmanaged<MetalTextureSession>.fromOpaque(handle).takeUnretainedValue().gpuMilliseconds
}
