import Foundation

private func details(_ text: String, _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32) {
    guard let output, capacity > 0 else { return }
    let bytes = Array(text.utf8.prefix(Int(capacity) - 1))
    for (index, byte) in bytes.enumerated() { output[index] = CChar(bitPattern: byte) }
    output[bytes.count] = 0
}

private func perform(_ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32, _ work: () throws -> Void) -> Int32 {
    autoreleasepool {
        do {
            try work()
            return 0
        } catch {
            details(error.localizedDescription, output, capacity)
            return 1
        }
    }
}

@_cdecl("echo_compute_create")
public func computeCreate(_ source: UnsafePointer<CChar>, _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32)
    -> UnsafeMutableRawPointer?
{
    autoreleasepool {
        do {
            let owner = try ComputeDevice(source: String(cString: source))
            details(owner.device.name, output, capacity)
            return Unmanaged.passRetained(owner).toOpaque()
        } catch {
            details(error.localizedDescription, output, capacity)
            return nil
        }
    }
}

@_cdecl("echo_compute_destroy")
public func computeDestroy(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<ComputeDevice>.fromOpaque(handle).release()
}

@_cdecl("echo_compute_open")
public func computeOpen(_ handle: UnsafeMutableRawPointer, _ budget: Int64) -> UnsafeMutableRawPointer {
    Unmanaged.passRetained(
        ComputeSession(owner: Unmanaged<ComputeDevice>.fromOpaque(handle).takeUnretainedValue(), budget: Int(budget))
    )
    .toOpaque()
}

@_cdecl("echo_compute_close")
public func computeClose(_ handle: UnsafeMutableRawPointer) {
    autoreleasepool { Unmanaged<ComputeSession>.fromOpaque(handle).release() }
}

@_cdecl("echo_compute_buffer")
public func computeBuffer(
    _ handle: UnsafeMutableRawPointer, _ slot: Int32, _ input: UnsafeRawPointer?, _ bytes: Int32,
    _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    perform(output, capacity) {
        try Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().buffer(
            slot: Int(slot), input: input, bytes: Int(bytes))
    }
}

@_cdecl("echo_compute_descriptor")
public func computeDescriptor(
    _ handle: UnsafeMutableRawPointer, _ slot: Int32, _ key: UInt64, _ input: UnsafeRawPointer, _ bytes: Int32,
    _ reused: UnsafeMutablePointer<Int32>, _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    perform(output, capacity) {
        let session = Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue()
        reused.pointee = try session.descriptor(slot: Int(slot), key: key, input: input, bytes: Int(bytes)) ? 1 : 0
    }
}

@_cdecl("echo_compute_write")
public func computeWrite(
    _ handle: UnsafeMutableRawPointer, _ slot: Int32, _ offset: Int32, _ input: UnsafeRawPointer?, _ bytes: Int32,
    _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    perform(output, capacity) {
        try Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().write(
            slot: Int(slot), offset: Int(offset), input: input, bytes: Int(bytes))
    }
}

@_cdecl("echo_compute_dispatch")
public func computeDispatch(
    _ handle: UnsafeMutableRawPointer, _ kernel: UnsafePointer<CChar>, _ count: Int32,
    _ parameters: UnsafePointer<Int32>?, _ parameterCount: Int32, _ output: UnsafeMutablePointer<CChar>?,
    _ capacity: Int32
) -> Int32 {
    perform(output, capacity) {
        try Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().dispatch(
            name: String(cString: kernel), count: Int(count), parameters: parameters,
            parameterCount: Int(parameterCount))
    }
}

@_cdecl("echo_compute_read")
public func computeRead(
    _ handle: UnsafeMutableRawPointer, _ slot: Int32, _ destination: UnsafeMutableRawPointer?, _ bytes: Int32,
    _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    perform(output, capacity) {
        try Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().read(
            slot: Int(slot), output: destination, bytes: Int(bytes))
    }
}

@_cdecl("echo_compute_dispatch_bound")
public func computeDispatchBound(
    _ handle: UnsafeMutableRawPointer, _ kernel: UnsafePointer<CChar>, _ count: Int32,
    _ parameters: UnsafePointer<Int32>?, _ parameterCount: Int32, _ slots: UnsafePointer<Int32>?,
    _ slotCount: Int32, _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32
) -> Int32 {
    perform(output, capacity) {
        guard let slots, slotCount > 0, slotCount <= 8 else {
            throw ComputeDevice.failure("Invalid compute buffer bindings")
        }
        try Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().dispatch(
            name: String(cString: kernel), count: Int(count), parameters: parameters,
            parameterCount: Int(parameterCount),
            slots: UnsafeBufferPointer(start: slots, count: Int(slotCount)))
    }
}

@_cdecl("echo_compute_submit")
public func computeSubmit(_ handle: UnsafeMutableRawPointer, _ output: UnsafeMutablePointer<CChar>?, _ capacity: Int32)
    -> Int32
{
    perform(output, capacity) {
        try Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().submit()
    }
}

@_cdecl("echo_compute_gpu_time")
public func computeGpuTime(_ handle: UnsafeMutableRawPointer) -> Double {
    Unmanaged<ComputeSession>.fromOpaque(handle).takeUnretainedValue().gpuMilliseconds
}

@_cdecl("echo_compute_close_timed")
public func computeCloseTimed(_ handle: UnsafeMutableRawPointer) -> Double {
    autoreleasepool {
        let session = Unmanaged<ComputeSession>.fromOpaque(handle).takeRetainedValue()
        try? session.finish()
        return session.gpuMilliseconds
    }
}
