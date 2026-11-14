import Foundation
import stdlib_h
import xlocale

// Scoped to Data.withUnsafeBytes: numeric arrays are read directly from the
// mapped UTF-8 buffer into Float storage, without strings or boxed numbers.
struct DepthMapJSONParser {
    let bytes: UnsafeBufferPointer<UInt8>
    let locale: locale_t
    private var position = 0

    init(bytes: UnsafeBufferPointer<UInt8>, locale: locale_t) {
        self.bytes = bytes
        self.locale = locale
    }

    mutating func read() throws -> DepthMap {
        var imageID: String?
        var width: Int?
        var height: Int?
        var depth: [Float]?
        var confidence: [Float]?
        try expect(123)
        if !consume(125) {
            repeat {
                try Task.checkCancellation()
                let key = try string()
                try expect(58)
                switch key {
                case "imageId":
                    guard imageID == nil else { throw invalid() }
                    imageID = try string()
                case "width":
                    guard width == nil else { throw invalid() }
                    width = try integer()
                case "height":
                    guard height == nil else { throw invalid() }
                    height = try integer()
                case "depth":
                    guard depth == nil else { throw invalid() }
                    depth = try samples(capacity: capacity(width: width, height: height))
                case "confidence":
                    guard confidence == nil else { throw invalid() }
                    confidence = try samples(capacity: capacity(width: width, height: height))
                default:
                    try skipValue(nesting: 0)
                }
                if consume(125) { break }
                try expect(44)
            } while true
        }
        whitespace()
        guard position == bytes.count, let imageID, let width, let height, let depth, let confidence else {
            throw invalid()
        }
        let size = width.multipliedReportingOverflow(by: height)
        let validDimensions = width > 0 && height > 0 && !size.overflow
        guard validDimensions, size.partialValue <= Int.max / 4,
            depth.count == size.partialValue, confidence.count == depth.count
        else { throw CLIError(message: "Depth image dimensions do not match its samples.") }
        try Task.checkCancellation()
        return DepthMap(imageId: imageID, width: width, height: height, depth: depth, confidence: confidence)
    }

    private func capacity(width: Int?, height: Int?) -> Int {
        guard let width, let height, width > 0, height > 0 else { return 0 }
        let size = width.multipliedReportingOverflow(by: height)
        return !size.overflow && size.partialValue <= bytes.count ? size.partialValue : 0
    }

    private mutating func samples(capacity: Int) throws -> [Float] {
        try expect(91)
        var result: [Float] = []
        result.reserveCapacity(capacity)
        if consume(93) { return result }
        repeat {
            if result.count % 65536 == 0 { try Task.checkCancellation() }
            let range = try number()
            // The validated JSON number must have a delimiter inside the buffer.
            // strtof_l therefore stops before its boundary, even for truncated input.
            guard range.upperBound < bytes.count else { throw invalid() }
            switch bytes[range.upperBound] {
            case 9, 10, 13, 32, 44, 93: break
            default: throw invalid()
            }
            let start = UnsafeRawPointer(bytes.baseAddress! + range.lowerBound).assumingMemoryBound(to: CChar.self)
            let value = strtof_l(start, nil, locale)
            guard value.isFinite else { throw invalid() }
            result.append(value)
            if consume(93) { return result }
            try expect(44)
        } while true
    }

    private mutating func integer() throws -> Int {
        let range = try number()
        return try JSONDecoder().decode(Int.self, from: Data(bytes[range]))
    }

    private mutating func string() throws -> String {
        whitespace()
        let start = position
        try expect(34)
        while position < bytes.count {
            let byte = bytes[position]
            position += 1
            if byte == 34 {
                return try JSONDecoder().decode(String.self, from: Data(bytes[start..<position]))
            }
            if byte == 92 {
                guard position < bytes.count else { throw invalid() }
                position += 1
            }
        }
        throw invalid()
    }

    private mutating func number() throws -> Range<Int> {
        whitespace()
        let start = position
        if position < bytes.count, bytes[position] == 45 { position += 1 }
        guard position < bytes.count else { throw invalid() }
        if bytes[position] == 48 {
            position += 1
        } else {
            guard digit(), bytes[position] != 48 else { throw invalid() }
            while digit() { position += 1 }
        }
        if position < bytes.count, bytes[position] == 46 {
            position += 1
            guard digit() else { throw invalid() }
            while digit() { position += 1 }
        }
        if position < bytes.count, bytes[position] == 101 || bytes[position] == 69 {
            position += 1
            if position < bytes.count, bytes[position] == 43 || bytes[position] == 45 { position += 1 }
            guard digit() else { throw invalid() }
            while digit() { position += 1 }
        }
        return start..<position
    }

    private func digit() -> Bool {
        guard position < bytes.count else { return false }
        return bytes[position] >= 48 && bytes[position] <= 57
    }

    private mutating func skipValue(nesting: Int) throws {
        try Task.checkCancellation()
        guard nesting < 128 else { throw invalid() }
        whitespace()
        guard position < bytes.count else { throw invalid() }
        switch bytes[position] {
        case 34:
            _ = try string()
        case 123:
            position += 1
            if consume(125) { return }
            repeat {
                _ = try string()
                try expect(58)
                try skipValue(nesting: nesting + 1)
                if consume(125) { return }
                try expect(44)
            } while true
        case 91:
            position += 1
            if consume(93) { return }
            repeat {
                try skipValue(nesting: nesting + 1)
                if consume(93) { return }
                try expect(44)
            } while true
        case 116: try literal([116, 114, 117, 101])
        case 102: try literal([102, 97, 108, 115, 101])
        case 110: try literal([110, 117, 108, 108])
        default: _ = try number()
        }
    }

    private mutating func literal(_ value: [UInt8]) throws {
        for byte in value {
            guard position < bytes.count, bytes[position] == byte else { throw invalid() }
            position += 1
        }
    }

    private mutating func whitespace() {
        while position < bytes.count {
            switch bytes[position] {
            case 9, 10, 13, 32: position += 1
            default: return
            }
        }
    }

    private mutating func consume(_ byte: UInt8) -> Bool {
        whitespace()
        guard position < bytes.count, bytes[position] == byte else { return false }
        position += 1
        return true
    }

    private mutating func expect(_ byte: UInt8) throws {
        guard consume(byte) else { throw invalid() }
    }

    private func invalid() -> CLIError { CLIError(message: "Invalid saved depth map near byte \(position).") }
}
